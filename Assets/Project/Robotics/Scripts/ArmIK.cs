using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Owns IK, path sampling and articulation drive execution. No parcel task sequence lives here.
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class ArmIK : MonoBehaviour
{
    public Transform tcp;
    BoxCollider targetBox;
    float degreesPerSecond = 20, metresPerSecond = .06f, positionTolerance = .008f, angleTolerance = 4, boxClearance = .008f;
    bool compensateRobotGravity = true;
    public bool Moving { get; private set; }
    public Vector3 GoalPosition { get; private set; }
    public Quaternion GoalRotation { get; private set; }
    public float PositionError => tcp == null ? 0 : Vector3.Distance(tcp.position, GoalPosition);
    public event Action<string> StatusChanged;
    public string Status { get; private set; }
    string status { get => Status; set { Status = value; StatusChanged?.Invoke(value); } }
    internal ArmKinematics Model;
    ArmKinematics model { get => Model; set => Model = value; }
    internal readonly List<float[]> Route = new List<float[]>();
    List<float[]> route => Route;
    internal bool ProtectBox;
    bool protectBox { get => ProtectBox; set => ProtectBox = value; }
    internal string PathFailure;
    string pathFailure { get => PathFailure; set => PathFailure = value; }
    internal bool PrecisePlacement;
    bool precisePlacement => PrecisePlacement;
    float[] solution;
    float started;
    int routeIndex;
    Vector3 plannedBoxPosition;
    Quaternion plannedBoxRotation;
    ArticulationBody[] bodies;
    readonly List<Behaviour> suspended = new List<Behaviour>();
    public void Configure(Transform tool, BoxCollider obstacle, float angularSpeed, float linearSpeed, float positionError, float angleError, float clearance, bool gravity)
    { tcp = tool; targetBox = obstacle; degreesPerSecond = angularSpeed; metresPerSecond = linearSpeed; positionTolerance = positionError; angleTolerance = angleError; boxClearance = clearance; compensateRobotGravity = gravity; }
    public void Initialize(float[] initialJointAngles)
    {
        // The importer's keyboard controller also writes xDrive and must not compete with IK.
        foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
            if (component != null && component.enabled &&
                (component.GetType().FullName == "Unity.Robotics.UrdfImporter.Control.Controller" ||
                 component.GetType().FullName == "Unity.Robotics.UrdfImporter.Control.JointControl" || component.GetType().FullName == "JointControl"))
            { suspended.Add(component); component.enabled = false; }
        bodies = GetComponentsInChildren<ArticulationBody>();
        if (initialJointAngles != null && initialJointAngles.Length == 6 && tcp != null)
        {
            var initialModel = new ArmKinematics(transform, tcp);
            for (int i = 0; i < 6; i++)
            {
                var joint = initialModel.Joints[i];
                joint.jointPosition = new ArticulationReducedSpace(initialJointAngles[i] * Mathf.Deg2Rad);
                joint.jointVelocity = new ArticulationReducedSpace(0);
                var drive = joint.xDrive; drive.target = initialJointAngles[i]; joint.xDrive = drive;
            }
        }
    }
    public bool MoveTo(Vector3 position, Quaternion rotation)
    {
        if (!Application.isPlaying) { status = "Play 모드에서 실행하세요."; return false; }
        if (Moving) StopMotion();
        try
        {
            model = new ArmKinematics(transform, tcp);
            var current = model.CurrentAngles();
            route.Clear(); protectBox = false;
            if (!AppendCartesian(ref current, position, rotation, false))
            { route.Clear(); status = "연속적인 직선 이동 경로를 찾지 못했습니다. " + pathFailure; return false; }
            BeginRoute(position, rotation);
            return true;
        }
        catch (Exception e) { status = e.Message; Debug.LogError("CorrectionSequence: " + e.Message, this); return false; }
    }
    internal bool AppendCartesian(ref float[] seed, Vector3 endPosition, Quaternion endRotation, bool checkBox)
    {
        model.Forward(seed, out var startPosition, out var startRotation);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Vector3.Distance(startPosition, endPosition) / .012f, Quaternion.Angle(startRotation, endRotation) / 4)));
        for (int n = 1; n <= steps; n++)
        {
            float t = (float)n / steps;
            if (!model.Solve(Vector3.Lerp(startPosition, endPosition, t), Quaternion.Slerp(startRotation, endRotation, t), seed,
                out var next, out float error, out float angle, 5f)) { pathFailure = $"중간 자세 IK 오차 {error * 1000:F1}mm / {angle:F1}도"; return false; }
            float largest = 0; for (int j = 0; j < 6; j++) largest = Mathf.Max(largest, Mathf.Abs(next[j] - seed[j]));
            // A discontinuous IK branch is never an acceptable Cartesian segment.
            if (largest > 28) { pathFailure = "중간 자세에서 관절이 크게 꺾입니다."; return false; }
            if (!AppendJointEdge(seed, next, checkBox)) return false;
            seed = next;
        }
        return true;
    }
    internal bool AppendJointEdge(float[] from, float[] to, bool checkBox)
    {
        float largest = 0;
        for (int j = 0; j < 6; j++) largest = Mathf.Max(largest, Mathf.Abs(to[j] - from[j]));
        int samples = Mathf.Max(1, Mathf.CeilToInt(largest));
        for (int s = 1; s <= samples; s++)
        {
            var q = new float[6];
            for (int j = 0; j < 6; j++) q[j] = Mathf.Lerp(from[j], to[j], (float)s / samples);
            if (checkBox && !model.IsBoxClear(q, targetBox, boxClearance)) { pathFailure = "중간 경로가 박스의 여유 공간을 침범합니다."; return false; }
            route.Add(q);
        }
        return true;
    }
    internal void BeginRoute(Vector3 position, Quaternion rotation)
    {
        GoalPosition = position; GoalRotation = rotation;
        routeIndex = 0; solution = route[0]; Moving = true; started = Time.time;
        if (targetBox != null) { plannedBoxPosition = targetBox.transform.position; plannedBoxRotation = targetBox.transform.rotation; }
        status = "상승·회전·접근 경로 이동 중";
    }
    void FixedUpdate()
    {
        if (compensateRobotGravity && bodies != null)
            foreach (var body in bodies) if (body != null && body.useGravity && !body.immovable) body.AddForce(-Physics.gravity * body.mass);
        if (!Moving || model == null) return;
        if (protectBox && (targetBox == null || Vector3.Distance(targetBox.transform.position, plannedBoxPosition) > .01f || Quaternion.Angle(targetBox.transform.rotation, plannedBoxRotation) > 6))
        { StopMotion(); status = "박스가 움직여 경로를 중단했습니다. 다시 계획하세요."; return; }
        if (protectBox && !model.IsBoxClear(model.CurrentAngles(), targetBox, boxClearance * .4f))
        { StopMotion(); status = "박스 접근 여유가 부족하여 정지했습니다."; return; }
        float largestStep = 0, trackingError = 0;
        var actual = model.CurrentAngles();
        var commanded = new float[6];
        for (int i = 0; i < 6; i++)
        {
            commanded[i] = model.Joints[i].xDrive.target;
            largestStep = Mathf.Max(largestStep, Mathf.Abs(solution[i] - commanded[i]));
            trackingError = Mathf.Max(trackingError, Mathf.Abs(actual[i] - commanded[i]));
        }
        model.Forward(commanded, out var commandPosition, out _);
        model.Forward(solution, out var nextPosition, out _);
        float fraction = 1;
        if (largestStep > .0001f) fraction = Mathf.Min(fraction, degreesPerSecond * Time.fixedDeltaTime / largestStep);
        float distance = Vector3.Distance(commandPosition, nextPosition);
        if (distance > .00001f) fraction = Mathf.Min(fraction, metresPerSecond * Time.fixedDeltaTime / distance);
        if (trackingError > 3) fraction = 0;
        for (int i = 0; i < model.Joints.Length; i++)
        {
            var joint = model.Joints[i]; var drive = joint.xDrive;
            drive.stiffness = Mathf.Max(drive.stiffness, 100000f);
            drive.damping = Mathf.Max(drive.damping, 10000f);
            drive.forceLimit = Mathf.Max(drive.forceLimit, 1000f);
            drive.targetVelocity = 0;
            drive.target = Mathf.Lerp(commanded[i], solution[i], fraction);
            joint.xDrive = drive;
        }
        if (fraction >= .999f && trackingError < 2.8f && routeIndex < route.Count - 1)
        { routeIndex++; solution = route[routeIndex]; }
        if (routeIndex == route.Count - 1 && PositionError < (precisePlacement ? .002f : positionTolerance) && Quaternion.Angle(tcp.rotation, GoalRotation) < (precisePlacement ? 2f : angleTolerance))
        { Moving = false; status = "목표 도착 · 손가락을 닫거나 다음 동작을 선택하세요."; }
        else if (Time.time - started > 60)
        { StopMotion(); status = "이동 중단 · 60초 내 도착하지 못함. 충돌/관절 설정을 확인하세요."; }
    }
    public void Hold()
    {
        Moving = false;
        route.Clear();
        if (model == null) return;
        var q = model.CurrentAngles();
        for (int i = 0; i < q.Length; i++)
        {
            var drive = model.Joints[i].xDrive;
            drive.target = q[i]; drive.targetVelocity = 0;
            drive.stiffness = Mathf.Max(drive.stiffness, 100000f);
            drive.damping = Mathf.Max(drive.damping, 10000f);
            drive.forceLimit = Mathf.Max(drive.forceLimit, 1000f);
            model.Joints[i].xDrive = drive;
        }
    }
    public void StopMotion() { Hold(); status = "정지 · 현재 관절 각도를 유지합니다."; }
    internal void RestoreControllers()
    { foreach (var component in suspended) if (component != null) component.enabled = true; suspended.Clear(); }
    void OnDisable() { if (Application.isPlaying && model != null) Hold(); RestoreControllers(); }
}
