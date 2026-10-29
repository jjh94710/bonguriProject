using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The solver operates on a mathematical copy. Only articulation drive targets move the real arm.
[DefaultExecutionOrder(-1000), DisallowMultipleComponent]
public class ParcelArmIK : MonoBehaviour
{
    public Transform tcp;
    public BoxCollider targetBox;
    public GripperController gripper;
    [Min(.02f)] public float approachHeight = .12f;
    [Min(.01f)] public float liftHeight = .10f;
    [Range(1f, 60f)] public float degreesPerSecond = 20f;
    public float positionTolerance = .008f;
    public float angleTolerance = 4f;
    [SerializeField] string status = "Play 후 이동 버튼을 누르세요.";
    public string Status => status;
    public bool Moving { get; private set; }
    public Vector3 GoalPosition { get; private set; }
    public Quaternion GoalRotation { get; private set; }
    public float PositionError => tcp == null ? 0 : Vector3.Distance(tcp.position, GoalPosition);
    ArmKinematics model;
    float[] solution;
    float started;
    readonly List<Behaviour> suspended = new List<Behaviour>();

    void Awake()
    {
        // The importer's keyboard controller also writes xDrive and must not compete with IK.
        foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
            if (component != null && component.enabled &&
                (component.GetType().FullName == "Unity.Robotics.UrdfImporter.Control.Controller" ||
                 component.GetType().FullName == "Unity.Robotics.UrdfImporter.Control.JointControl"))
            { suspended.Add(component); component.enabled = false; }
    }
    IEnumerator Start()
    {
        yield return new WaitForFixedUpdate();
        try
        {
            model = new ArmKinematics(transform, tcp);
            Hold();
            status = "준비 완료 · 박스 위로 이동을 누르세요.";
        }
        catch (Exception e) { status = e.Message; Debug.LogError("ParcelArmIK: " + e.Message, this); }
    }
    void OnDisable()
    {
        if (Application.isPlaying && model != null) Hold();
        foreach (var component in suspended) if (component != null) component.enabled = true;
        suspended.Clear();
    }
    public void ApproachBox() { RequestBox(approachHeight); }
    public void DescendToBox() { RequestBox(0); }
    void RequestBox(float height)
    {
        if (GetBoxGoal(height, out var position, out var desired)) MoveTo(position, desired);
    }
    public bool GetBoxGoal(float height, out Vector3 position, out Quaternion desired)
    {
        position = Vector3.zero; desired = Quaternion.identity;
        if (targetBox == null) { status = "Target Box에 박스를 지정하세요."; return false; }
        var bounds = targetBox.bounds;
        Vector3 closing = Vector3.ProjectOnPlane(targetBox.transform.right, Vector3.up).normalized;
        Vector3 other = Vector3.ProjectOnPlane(targetBox.transform.forward, Vector3.up).normalized;
        float x = targetBox.transform.TransformVector(Vector3.right * targetBox.size.x).magnitude;
        float z = targetBox.transform.TransformVector(Vector3.forward * targetBox.size.z).magnitude;
        if (z < x && other.sqrMagnitude > .9f) closing = other;
        if (closing.sqrMagnitude < .9f) closing = Vector3.right;
        var gripBase = GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "gripper_base_link");
        if (tcp == null || gripBase == null) { status = "TCP / gripper_base_link를 찾을 수 없습니다."; return false; }
        Vector3 localApproach = tcp.InverseTransformDirection(tcp.position - gripBase.position).normalized;
        Vector3 localClosing = tcp.InverseTransformDirection(gripBase.right).normalized;
        Quaternion localFrame = Quaternion.LookRotation(Vector3.Cross(localClosing, localApproach), localApproach);
        desired = Quaternion.LookRotation(Vector3.Cross(closing, Vector3.down), Vector3.down) * Quaternion.Inverse(localFrame);
        // Above the box first; grasp height is the collider centre, not the label or mesh pivot.
        position = bounds.center + Vector3.up * (height > 0 ? bounds.extents.y + height : 0);
        return true;
    }
    public void Lift()
    { if (tcp != null) MoveTo(tcp.position + Vector3.up * liftHeight, tcp.rotation); }
    public void OpenGripper() { if (gripper != null) gripper.Open(); }
    public void CloseGripper() { if (gripper != null) gripper.Close(); }
    public bool MoveTo(Vector3 position, Quaternion rotation)
    {
        if (!Application.isPlaying) { status = "Play 모드에서 실행하세요."; return false; }
        if (Moving) Stop();
        try
        {
            model = new ArmKinematics(transform, tcp);
            var current = model.CurrentAngles();
            if (!model.Solve(position, rotation, current, out solution, out float error, out float angle))
            { status = $"도달 가능한 해를 찾지 못함 · 위치 {error * 1000:F1}mm / 각도 {angle:F1}°"; return false; }
            GoalPosition = position; GoalRotation = rotation;
            Moving = true; started = Time.time;
            status = "이동 중 · 접촉하면 정지를 누르세요.";
            return true;
        }
        catch (Exception e) { status = e.Message; Debug.LogError("ParcelArmIK: " + e.Message, this); return false; }
    }
    void FixedUpdate()
    {
        if (!Moving || model == null) return;
        for (int i = 0; i < model.Joints.Length; i++)
        {
            var joint = model.Joints[i]; var drive = joint.xDrive;
            drive.stiffness = Mathf.Max(drive.stiffness, 100000f);
            drive.damping = Mathf.Max(drive.damping, 10000f);
            drive.forceLimit = Mathf.Max(drive.forceLimit, 1000f);
            drive.targetVelocity = 0;
            drive.target = Mathf.MoveTowards(drive.target, solution[i], degreesPerSecond * Time.fixedDeltaTime);
            joint.xDrive = drive;
        }
        if (PositionError < positionTolerance && Quaternion.Angle(tcp.rotation, GoalRotation) < angleTolerance)
        { Moving = false; status = "목표 도착 · 손가락을 닫거나 다음 동작을 선택하세요."; }
        else if (Time.time - started > 30)
        { Stop(); status = "이동 중단 · 30초 내 도착하지 못함. 충돌/관절 설정을 확인하세요."; }
    }
    public void Stop() { Hold(); status = "정지 · 현재 관절 각도를 유지합니다."; }
    void Hold()
    {
        Moving = false;
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
    void OnDrawGizmosSelected()
    {
        if (targetBox != null) { Gizmos.color = Color.yellow; Gizmos.DrawWireCube(targetBox.bounds.center, targetBox.bounds.size); }
        if (Moving && tcp != null) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(GoalPosition, .015f); Gizmos.DrawLine(tcp.position, GoalPosition); }
    }
}

public sealed class ArmKinematics
{
    public ArticulationBody[] Joints { get; }
    readonly Transform parent;
    readonly Node[] nodes;
    readonly float[] reference, lower, upper;
    struct Node { public Vector3 position, scale, anchor; public Quaternion rotation, anchorRotation; public int joint; }
    public ArmKinematics(Transform root, Transform tcp)
    {
        if (tcp == null || !tcp.IsChildOf(root)) throw new Exception("로봇 아래의 tcp를 지정하세요.");
        var chain = new List<Transform>();
        for (var t = tcp; t != root; t = t.parent) chain.Add(t);
        chain.Add(root); chain.Reverse(); parent = root.parent;
        Joints = chain.Select(t => t.GetComponent<ArticulationBody>())
            .Where(b => b != null && !b.isRoot && b.jointType == ArticulationJointType.RevoluteJoint).ToArray();
        if (Joints.Length != 6) throw new Exception("6개의 회전 관절이 필요합니다. 발견: " + Joints.Length);
        reference = CurrentAngles(); lower = new float[6]; upper = new float[6]; nodes = new Node[chain.Count];
        for (int i = 0; i < 6; i++)
        {
            var d = Joints[i].xDrive;
            lower[i] = Joints[i].twistLock == ArticulationDofLock.FreeMotion ? -360 : d.lowerLimit;
            upper[i] = Joints[i].twistLock == ArticulationDofLock.FreeMotion ? 360 : d.upperLimit;
        }
        for (int i = 0; i < chain.Count; i++)
        {
            var t = chain[i]; int j = Array.FindIndex(Joints, b => b.transform == t);
            nodes[i] = new Node { position = t.localPosition, rotation = t.localRotation, scale = t.localScale, joint = j,
                anchor = j < 0 ? Vector3.zero : Vector3.Scale(Joints[j].anchorPosition, t.localScale),
                anchorRotation = j < 0 ? Quaternion.identity : Joints[j].anchorRotation };
        }
    }
    public float[] CurrentAngles()
    {
        var result = new float[Joints.Length];
        for (int i = 0; i < result.Length; i++)
        {
            var b = Joints[i];
            if (Application.isPlaying && b.dofCount > 0) result[i] = b.jointPosition[0] * Mathf.Rad2Deg;
            else
            {
                var relative = Quaternion.Inverse(b.parentAnchorRotation) * b.transform.localRotation * b.anchorRotation;
                result[i] = Mathf.DeltaAngle(0, 2 * Mathf.Atan2(relative.x, relative.w) * Mathf.Rad2Deg);
            }
        }
        return result;
    }
    public void Forward(float[] q, out Vector3 position, out Quaternion rotation)
    {
        var matrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
        rotation = parent != null ? parent.rotation : Quaternion.identity;
        foreach (var n in nodes)
        {
            var r = n.rotation; var p = n.position;
            if (n.joint >= 0)
            {
                r = n.rotation * n.anchorRotation * Quaternion.AngleAxis(q[n.joint] - reference[n.joint], Vector3.right) * Quaternion.Inverse(n.anchorRotation);
                p += n.rotation * n.anchor - r * n.anchor;
            }
            matrix *= Matrix4x4.TRS(p, r, n.scale); rotation *= r;
        }
        position = matrix.MultiplyPoint3x4(Vector3.zero);
    }
    static Vector3 RotationError(Quaternion target, Quaternion actual)
    {
        var delta = target * Quaternion.Inverse(actual);
        if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        return axis.sqrMagnitude < .1f || float.IsNaN(axis.x) ? Vector3.zero : axis * (Mathf.DeltaAngle(0, angle) * Mathf.Deg2Rad);
    }
    public bool Solve(Vector3 target, Quaternion targetRotation, float[] seed, out float[] best, out float positionError, out float angleError)
    {
        best = (float[])seed.Clone(); float bestCost = float.PositiveInfinity;
        const float weight = .16f, epsilon = .25f;
        for (int attempt = 0; attempt < 9; attempt++)
        {
            var q = (float[])seed.Clone();
            if (attempt > 0) for (int j = 0; j < 6; j++) q[j] = Mathf.Clamp(q[j] + Mathf.Sin(attempt * 5.37f + j * 2.41f) * (attempt < 4 ? 35 : 95), lower[j], upper[j]);
            for (int iteration = 0; iteration < 180; iteration++)
            {
                Forward(q, out var p, out var r);
                Vector3 dp = target - p, dr = RotationError(targetRotation, r);
                float cost = dp.sqrMagnitude + weight * weight * dr.sqrMagnitude;
                if (cost < bestCost) { bestCost = cost; Array.Copy(q, best, 6); }
                if (dp.magnitude < .003f && dr.magnitude < 2 * Mathf.Deg2Rad) break;
                var jacobian = new float[6, 6];
                for (int j = 0; j < 6; j++)
                {
                    q[j] += epsilon; Forward(q, out var pp, out var rr); q[j] -= epsilon;
                    var jp = (pp - p) / (epsilon * Mathf.Deg2Rad);
                    var jr = RotationError(rr, r) * weight / (epsilon * Mathf.Deg2Rad);
                    jacobian[0, j] = jp.x; jacobian[1, j] = jp.y; jacobian[2, j] = jp.z;
                    jacobian[3, j] = jr.x; jacobian[4, j] = jr.y; jacobian[5, j] = jr.z;
                }
                float[] error = { dp.x, dp.y, dp.z, dr.x * weight, dr.y * weight, dr.z * weight };
                var normal = new float[6, 7];
                for (int row = 0; row < 6; row++)
                {
                    for (int col = 0; col < 6; col++)
                        for (int k = 0; k < 6; k++) normal[row, col] += jacobian[k, row] * jacobian[k, col];
                    normal[row, row] += .0004f;
                    for (int k = 0; k < 6; k++) normal[row, 6] += jacobian[k, row] * error[k];
                }
                if (!Eliminate(normal)) break;
                float largest = 0; for (int j = 0; j < 6; j++) largest = Mathf.Max(largest, Mathf.Abs(normal[j, 6] * Mathf.Rad2Deg));
                float stepScale = largest > 7 ? 7 / largest : 1;
                for (int j = 0; j < 6; j++) q[j] = Mathf.Clamp(q[j] + normal[j, 6] * Mathf.Rad2Deg * stepScale, lower[j], upper[j]);
            }
            Forward(best, out var bp, out var br);
            if (Vector3.Distance(bp, target) < .005f && Quaternion.Angle(br, targetRotation) < 3) break;
        }
        Forward(best, out var finalP, out var finalR);
        positionError = Vector3.Distance(finalP, target); angleError = Quaternion.Angle(finalR, targetRotation);
        return positionError < .005f && angleError < 3;
    }
    static bool Eliminate(float[,] a)
    {
        for (int k = 0; k < 6; k++)
        {
            int pivot = k; for (int i = k + 1; i < 6; i++) if (Mathf.Abs(a[i, k]) > Mathf.Abs(a[pivot, k])) pivot = i;
            if (Mathf.Abs(a[pivot, k]) < 1e-10f) return false;
            for (int j = k; j < 7; j++) { float v = a[k, j]; a[k, j] = a[pivot, j]; a[pivot, j] = v; }
            float divisor = a[k, k]; for (int j = k; j < 7; j++) a[k, j] /= divisor;
            for (int i = 0; i < 6; i++) if (i != k)
            { float factor = a[i, k]; for (int j = k; j < 7; j++) a[i, j] -= factor * a[k, j]; }
        }
        return true;
    }
}
