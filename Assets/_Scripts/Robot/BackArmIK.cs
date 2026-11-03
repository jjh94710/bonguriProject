using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Owns IK, path sampling and articulation drive execution. No parcel task sequence lives here.
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public sealed class BackArmIK : MonoBehaviour
{
    public Transform tcp;
    [Min(0)] public float raisedElbowClearance;
    public float minimumWristPlane = -10000f;
    [Min(1)] public float driveStiffness = 100000f, driveDamping = 10000f, driveForceLimit = 1000f;
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
    public bool HighPrecision { get => PrecisePlacement; set => PrecisePlacement = value; }
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
        if (tcp != null)
        {
            var initialModel = new ArmKinematics(transform, tcp);
            bool hasSavedPose = initialJointAngles != null && initialJointAngles.Length == 6;
            for (int i = 0; i < 6; i++)
            {
                var joint = initialModel.Joints[i];
                // Do not overwrite articulation reduced-space state during Awake.
                // Unity 6 can corrupt the native PhysX articulation descriptor when
                // several trees are rewritten before their first simulation step.
                // If the scene has no saved ready pose, hold the pose authored in the
                // scene instead of letting the prefab's zero-degree drives fold the arm.
                float currentDegrees = joint.jointPosition.dofCount > 0
                    ? joint.jointPosition[0] * Mathf.Rad2Deg
                    : joint.xDrive.target;
                var drive = joint.xDrive;
                drive.target = hasSavedPose ? initialJointAngles[i] : currentDegrees;
                joint.xDrive = drive;
            }
        }
    }
    public bool MoveToJointAngles(float[] angles, BoxCollider obstacle = null)
    {
        if (!Application.isPlaying || Moving || angles == null || angles.Length != 6) return false;
        model = new ArmKinematics(transform, tcp); route.Clear(); targetBox=obstacle; protectBox=obstacle!=null;
        if (!AppendJointEdge(model.CurrentAngles(), angles, protectBox)) { route.Clear(); status="복귀 경로 실패 · "+pathFailure; return false; }
        model.Forward(angles, out var p, out var r); BeginRoute(p, r); return true;
    }
    public bool MoveToJointPath(Vector3 position, Quaternion rotation, BoxCollider obstacle = null)
    {
        if (!Application.isPlaying || Moving) return false;
        model = new ArmKinematics(transform, tcp); var current = model.CurrentAngles();
        route.Clear(); targetBox = obstacle; protectBox = obstacle != null;
        if (!model.Solve(position, rotation, current, out var answer, out _, out _, precisePlacement ? 1.5f : 3f, q => (raisedElbowClearance <= 0 || model.HasRaisedElbow(q, position.y + raisedElbowClearance)) && model.WristAbovePlane(q, minimumWristPlane) && (!protectBox || model.IsBoxClear(q, targetBox, boxClearance)), precisePlacement ? .0015f : positionTolerance)
            || !AppendJointEdge(current, answer, protectBox))
        { route.Clear(); status = "관절 경로를 찾지 못했습니다. " + pathFailure; return false; }
        BeginRoute(position, rotation); return true;
    }
    public bool PlanSuctionApproach(Vector3 high, Vector3 standOff, Vector3 contact, Quaternion rotation, BoxCollider obstacle, bool preview = false)
    {
        if ((!Application.isPlaying && !preview) || Moving) return false;
        model = new ArmKinematics(transform, tcp); targetBox = obstacle; protectBox = true; route.Clear();
        var current = model.CurrentAngles();
        bool acceptable(float[] q) => model.HasRaisedElbow(q, contact.y + raisedElbowClearance) && model.WristAbovePlane(q, minimumWristPlane);
        // Solve the constrained contact pose first, then withdraw mathematically to find the same elbow branch.
        if (!model.Solve(contact, rotation, current, out var contactPose, out float error, out float angle, 1.5f, acceptable, .0015f))
        { status = "흡착 접촉 자세 불가 · " + (error * 1000).ToString("F1") + "mm / " + angle.ToString("F1") + "도"; return false; }
        var seed = (float[])contactPose.Clone(); route.Add((float[])seed.Clone());
        if (!AppendCartesian(ref seed, standOff, rotation, false) || !AppendCartesian(ref seed, high, rotation, true))
        { route.Clear(); status = "흡착 직선 접근 경로 불가 · " + pathFailure; return false; }
        var reverse = route.Select(q => (float[])q.Clone()).Reverse().ToArray(); route.Clear();
        if (!AppendJointEdge(current, seed, true)) { route.Clear(); status = "흡착 대기 위치 접근 불가 · " + pathFailure; return false; }
        foreach (var q in reverse) { AppendJointEdge(seed, q, false); seed = q; }
        protectBox = false; if (!preview) BeginRoute(contact, rotation); return true;
    }
    readonly List<float[]> preparedSuctionLift = new List<float[]>();
    readonly List<float[]> preparedSuctionTurn = new List<float[]>();
    readonly List<float[]> preparedSuctionTransfer = new List<float[]>();
    float[] preparedTransferStart;
    float[] preparedLiftStart, preparedTurnStart;
    // Keep the successful joint paths so execution does not select a different IK branch later.
    public bool PreviewSuctionRotation(BoxCollider box, Quaternion desiredBox, float liftHeight, float rotationRise, float stepDegrees, Vector3 rotationOffset = default)
    {
        preparedSuctionLift.Clear(); preparedSuctionTurn.Clear(); preparedSuctionTransfer.Clear();
        preparedTransferStart = null;
        preparedLiftStart = preparedTurnStart = null;
        if (Moving || model == null || route.Count == 0 || box == null) return false;
        var approach = route.Select(q => (float[])q.Clone()).ToArray();
        try
        {
            var seed = (float[])approach[approach.Length - 1].Clone();
            var liftStart = (float[])seed.Clone();
            model.Forward(seed, out var contact, out var contactRotation);
            route.Clear();
            if (!AppendSuctionTransit(ref seed, contact + Vector3.up * liftHeight, contactRotation, true))
            { status = "흡착 후 상승 경로 불가 · " + pathFailure; return false; }
            var lift = route.Select(q => (float[])q.Clone()).ToArray();
            var transferStart = (float[])seed.Clone();
            route.Clear();
            // Translate the held parcel without rotating before selecting its turn path.
            if (!AppendSuctionTransit(ref seed, contact + Vector3.up * liftHeight + rotationOffset, contactRotation, true))
            { status = "회전 대기 위치 이동 경로 불가 · " + pathFailure; return false; }
            var transfer = route.Select(q => (float[])q.Clone()).ToArray();
            var turnStart = (float[])seed.Clone();
            route.Clear();
            Vector3 centre = box.bounds.center + Vector3.up * liftHeight + rotationOffset;
            Vector3 offset = contact - box.bounds.center;
            Quaternion delta = desiredBox * Quaternion.Inverse(box.transform.rotation);
            int count = Mathf.Max(1, Mathf.CeilToInt(Quaternion.Angle(Quaternion.identity, delta) / Mathf.Clamp(stepDegrees, 5f, 30f)));
            for (int n = 1; n <= count; n++)
            {
                float t = (float)n / count;
                var turn = Quaternion.Slerp(Quaternion.identity, delta, t);
                if (!AppendSuctionTransit(ref seed, centre + Vector3.up * rotationRise * t + turn * offset, turn * contactRotation, n == count))
                { status = "흡착 후 회전 경로 불가 (" + n + "/" + count + ") · " + pathFailure; return false; }
            }
            preparedSuctionTransfer.AddRange(transfer); preparedTransferStart = transferStart;
            preparedSuctionLift.AddRange(lift);
            preparedSuctionTurn.AddRange(route.Select(q => (float[])q.Clone()));
            preparedLiftStart = liftStart; preparedTurnStart = turnStart;
            return true;
        }
        finally { route.Clear(); route.AddRange(approach); }
    }
    public bool StartPreparedSuctionLift() => StartPreparedSuctionSegment(preparedSuctionLift, preparedLiftStart);
    public bool StartPreparedSuctionTransfer() => StartPreparedSuctionSegment(preparedSuctionTransfer, preparedTransferStart);
    public bool StartPreparedSuctionTurn() => StartPreparedSuctionSegment(preparedSuctionTurn, preparedTurnStart);
    bool StartPreparedSuctionSegment(List<float[]> segment, float[] expectedStart)
    {
        if (!Application.isPlaying || Moving || segment.Count == 0 || expectedStart == null)
        { status = "사전 확인한 흡착 이동 경로가 없습니다."; return false; }
        model = new ArmKinematics(transform, tcp);
        var actual = model.CurrentAngles();
        for (int j = 0; j < actual.Length; j++)
            if (Mathf.Abs(actual[j] - expectedStart[j]) > 5f)
            { status = "사전 경로 시작 자세에서 관절이 벗어나 중단했습니다."; return false; }
        route.Clear(); route.AddRange(segment.Select(q => (float[])q.Clone())); protectBox = false;
        model.Forward(route[route.Count - 1], out var position, out var rotation);
        BeginRoute(position, rotation);
        return true;
    }
    public bool StartPreparedSuctionApproach(Vector3 contact, Quaternion rotation)
    {
        if (!Application.isPlaying || Moving || route.Count == 0) return false;
        protectBox = false;
        BeginRoute(contact, rotation);
        return true;
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
    // Only X/Y suction previews use this retry. Keep the final pose precise,
    // but allow a small tracking error while turning in free space.
    bool AppendSuctionTransit(ref float[] seed, Vector3 endPosition, Quaternion endRotation, bool finalPose, int depth = 0)
    {
        int routeStart = route.Count;
        var original = (float[])seed.Clone();
        float tolerance = finalPose ? .0015f : .003f;
        if (AppendCartesian(ref seed, endPosition, endRotation, false, tolerance, 2f)) return true;
        route.RemoveRange(routeStart, route.Count - routeStart);
        seed = (float[])original.Clone();
        if (depth >= 3) return false;
        model.Forward(seed, out var startPosition, out var startRotation);
        if (AppendSuctionTransit(ref seed, Vector3.Lerp(startPosition, endPosition, .5f),
                Quaternion.Slerp(startRotation, endRotation, .5f), false, depth + 1)
            && AppendSuctionTransit(ref seed, endPosition, endRotation, finalPose, depth + 1)) return true;
        // Never leave half of a failed candidate in the executable route.
        route.RemoveRange(routeStart, route.Count - routeStart);
        seed = original;
        return false;
    }
    internal bool AppendCartesian(ref float[] seed, Vector3 endPosition, Quaternion endRotation, bool checkBox, float transitTolerance = -1f, float rotationStep = 4f)
    {
        model.Forward(seed, out var startPosition, out var startRotation);
        int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(Vector3.Distance(startPosition, endPosition) / .012f, Quaternion.Angle(startRotation, endRotation) / rotationStep)));
        for (int n = 1; n <= steps; n++)
        {
            float t = (float)n / steps;
            var previous = seed;
            bool continuous(float[] q)
            {
                for (int j = 0; j < q.Length; j++) if (Mathf.Abs(q[j] - previous[j]) > 28f) return false;
                return (raisedElbowClearance <= 0 || model.HasRaisedElbow(q, Mathf.Lerp(startPosition.y, endPosition.y, t) + raisedElbowClearance)) && model.WristAbovePlane(q, minimumWristPlane);
            }
            if (!model.Solve(Vector3.Lerp(startPosition, endPosition, t), Quaternion.Slerp(startRotation, endRotation, t), seed,
                out var next, out float error, out float angle, precisePlacement ? 1.5f : 5f, continuous, transitTolerance > 0 ? (n == steps ? transitTolerance : .003f) : (precisePlacement ? .0015f : positionTolerance))) { pathFailure = $"중간 자세 IK 오차 {error * 1000:F1}mm / {angle:F1}도"; return false; }
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
            drive.stiffness = driveStiffness;
            drive.damping = driveDamping;
            drive.forceLimit = driveForceLimit;
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
            drive.stiffness = driveStiffness;
            drive.damping = driveDamping;
            drive.forceLimit = driveForceLimit;
            model.Joints[i].xDrive = drive;
        }
    }
    public void StopMotion() { Hold(); status = "정지 · 현재 관절 각도를 유지합니다."; }
    internal void RestoreControllers()
    { foreach (var component in suspended) if (component != null) component.enabled = true; suspended.Clear(); }
    void OnDisable() { if (Application.isPlaying && model != null) Hold(); RestoreControllers(); }
}
