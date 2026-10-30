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
    [Min(.05f)] public float travelClearance = .20f;
    [Min(.001f)] public float boxClearance = .008f;
    [Min(.005f)] public float metresPerSecond = .06f;
    [Tooltip("Compensate the robot links' own weight. Parcel gravity remains active.")]
    public bool compensateRobotGravity = true;
    [SerializeField, Tooltip("Start angles in degrees: shoulder, upperarm, forearm, wrist1, wrist2, wrist3")] float[] initialJointAngles;
    [SerializeField] string status = "Play 후 이동 버튼을 누르세요.";
    public string Status => status;
    public bool Moving { get; private set; }
    public Vector3 GoalPosition { get; private set; }
    public Quaternion GoalRotation { get; private set; }
    public float PositionError => tcp == null ? 0 : Vector3.Distance(tcp.position, GoalPosition);
    ArmKinematics model;
    float[] solution;
    float started;
    readonly List<float[]> route = new List<float[]>();
    int routeIndex;
    Vector3 plannedBoxPosition;
    Quaternion plannedBoxRotation;
    bool protectBox;
    string pathFailure;
    ArticulationBody[] bodies;
    public void SetInitialJointAngles(float[] values) { initialJointAngles = (float[])values.Clone(); }
    readonly List<Behaviour> suspended = new List<Behaviour>();

    void Awake()
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
    public void ApproachBox()
    {
        if (!Application.isPlaying) { status = "Play 모드에서 실행하세요."; return; }
        if (!GetBoxGoal(approachHeight, out var position, out var desired)) return;
        if (Moving) Stop();
        try
        {
            model = new ArmKinematics(transform, tcp);
            var current = model.CurrentAngles();
            route.Clear(); protectBox = true;
            if (!model.IsBoxClear(current, targetBox, boxClearance))
            { status = "현재 팔과 박스가 너무 가깝거나 겹쳐 있습니다. 시작 위치를 분리한 뒤 다시 시도하세요."; return; }
            bool found = false;
            // Lift without folding over the parcel, turn above it, translate, then lower vertically.
            foreach (float extra in new[] { 0f, travelClearance, travelClearance * .65f, travelClearance * 1.4f })
            {
                route.Clear(); var seed = (float[])current.Clone();
                float high = Mathf.Max(tcp.position.y, position.y) + extra;
                Vector3 aboveStart = new Vector3(tcp.position.x, high, tcp.position.z);
                Vector3 aboveBox = new Vector3(position.x, high, position.z);
                if (AppendCartesian(ref seed, aboveStart, tcp.rotation, true) &&
                    AppendCartesian(ref seed, aboveStart, desired, true) &&
                    AppendCartesian(ref seed, aboveBox, desired, true) &&
                    AppendCartesian(ref seed, position, desired, true)) { found = true; break; }
            }
            if (!found && model.Solve(position, desired, current, out var direct, out _, out _, 5f, q => model.IsBoxClear(q, targetBox, boxClearance)))
            {
                // A shorter joint path is allowed only when every sampled arm pose clears the parcel.
                route.Clear(); found = AppendJointEdge(current, direct, true);
            }
            if (!found)
            { route.Clear(); status = "박스와 충돌하지 않는 경로를 찾지 못했습니다. " + pathFailure; return; }
            BeginRoute(position, desired);
        }
        catch (Exception e) { route.Clear(); status = e.Message; Debug.LogError("ParcelArmIK: " + e.Message, this); }
    }
    public void DescendToBox()
    {
        if (!Application.isPlaying) { status = "Play 모드에서 실행하세요."; return; }
        if (!GetBoxGoal(0, out var position, out var desired)) return;
        if (Vector2.Distance(new Vector2(tcp.position.x, tcp.position.z), new Vector2(position.x, position.z)) > .015f ||
            Quaternion.Angle(tcp.rotation, desired) > 6)
        { status = "먼저 박스 위로 이동해서 방향과 중심을 맞추세요."; return; }
        MoveTo(position, desired);
    }
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
            route.Clear(); protectBox = false;
            if (!AppendCartesian(ref current, position, rotation, false))
            { route.Clear(); status = "연속적인 직선 이동 경로를 찾지 못했습니다."; return false; }
            BeginRoute(position, rotation);
            return true;
        }
        catch (Exception e) { status = e.Message; Debug.LogError("ParcelArmIK: " + e.Message, this); return false; }
    }
    bool AppendCartesian(ref float[] seed, Vector3 endPosition, Quaternion endRotation, bool checkBox)
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
    bool AppendJointEdge(float[] from, float[] to, bool checkBox)
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
    void BeginRoute(Vector3 position, Quaternion rotation)
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
        { Stop(); status = "박스가 움직여 경로를 중단했습니다. 다시 계획하세요."; return; }
        if (protectBox && !model.IsBoxClear(model.CurrentAngles(), targetBox, boxClearance * .4f))
        { Stop(); status = "박스 접근 여유가 부족하여 정지했습니다."; return; }
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
        if (routeIndex == route.Count - 1 && PositionError < positionTolerance && Quaternion.Angle(tcp.rotation, GoalRotation) < angleTolerance)
        { Moving = false; status = "목표 도착 · 손가락을 닫거나 다음 동작을 선택하세요."; }
        else if (Time.time - started > 60)
        { Stop(); status = "이동 중단 · 60초 내 도착하지 못함. 충돌/관절 설정을 확인하세요."; }
    }
    public void Stop() { Hold(); status = "정지 · 현재 관절 각도를 유지합니다."; }
    void Hold()
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
    readonly Part[] parts;
    readonly Matrix4x4[] world;
    static BoxCollider clearanceProbe;
    struct Part { public int node; public Matrix4x4 relative; public Vector3 center, size; public Collider collider; }
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
        world = new Matrix4x4[nodes.Length];
        var geometry = new List<Part>();
        foreach (var collider in root.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger) continue;
            Bounds bounds;
            if (collider is BoxCollider box) bounds = new Bounds(box.center, box.size);
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) bounds = mesh.sharedMesh.bounds;
            else continue;
            int index = -1;
            for (int i = chain.Count - 1; i >= 0; i--)
                if (collider.transform == chain[i] || collider.transform.IsChildOf(chain[i])) { index = i; break; }
            if (index >= 0) geometry.Add(new Part { node = index, relative = chain[index].worldToLocalMatrix * collider.transform.localToWorldMatrix, center = bounds.center, size = bounds.size, collider = collider });
        }
        parts = geometry.ToArray();
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
    public void ApplyEditorPose(float[] q)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Editor pose only");
        for (int j = 0; j < Joints.Length; j++)
        {
            var body = Joints[j]; var n = nodes.First(v => v.joint == j);
            var rotation = n.rotation * n.anchorRotation * Quaternion.AngleAxis(q[j] - reference[j], Vector3.right) * Quaternion.Inverse(n.anchorRotation);
            body.transform.localRotation = rotation;
            body.transform.localPosition = n.position + n.rotation * n.anchor - rotation * n.anchor;
            var drive = body.xDrive; drive.target = q[j]; drive.targetVelocity = 0; body.xDrive = drive;
        }
    }
    public void Forward(float[] q, out Vector3 position, out Quaternion rotation)
    {
        var matrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
        rotation = parent != null ? parent.rotation : Quaternion.identity;
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            var r = n.rotation; var p = n.position;
            if (n.joint >= 0)
            {
                r = n.rotation * n.anchorRotation * Quaternion.AngleAxis(q[n.joint] - reference[n.joint], Vector3.right) * Quaternion.Inverse(n.anchorRotation);
                p += n.rotation * n.anchor - r * n.anchor;
            }
            matrix *= Matrix4x4.TRS(p, r, n.scale); rotation *= r;
            world[i] = matrix;
        }
        position = matrix.MultiplyPoint3x4(Vector3.zero);
    }
    public bool IsBoxClear(float[] q, BoxCollider box, float margin)
    {
        if (box == null) return false;
        if (clearanceProbe == null)
        {
            var probeObject = new GameObject("IK_ClearanceQuery_Only") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            probeObject.transform.position = Vector3.one * -10000;
            clearanceProbe = probeObject.AddComponent<BoxCollider>(); clearanceProbe.isTrigger = true;
        }
        var scale = box.transform.lossyScale;
        clearanceProbe.transform.localScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        clearanceProbe.center = box.center;
        clearanceProbe.size = box.size + new Vector3(2 * margin / Mathf.Abs(scale.x), 2 * margin / Mathf.Abs(scale.y), 2 * margin / Mathf.Abs(scale.z));
        Forward(q, out _, out _);
        var boxMatrix = box.transform.localToWorldMatrix;
        Vector3 boxCenter = boxMatrix.MultiplyPoint3x4(box.center);
        Vector3[] bAxes = { boxMatrix.MultiplyVector(Vector3.right), boxMatrix.MultiplyVector(Vector3.up), boxMatrix.MultiplyVector(Vector3.forward) };
        Vector3 be = box.size * .5f;
        for (int i = 0; i < 3; i++) { be[i] = be[i] * bAxes[i].magnitude + margin; bAxes[i].Normalize(); }
        foreach (var part in parts)
        {
            var matrix = world[part.node] * part.relative;
            Vector3 center = matrix.MultiplyPoint3x4(part.center);
            Vector3[] axes = { matrix.MultiplyVector(Vector3.right), matrix.MultiplyVector(Vector3.up), matrix.MultiplyVector(Vector3.forward) };
            Vector3 extents = part.size * .5f;
            for (int i = 0; i < 3; i++) { extents[i] *= axes[i].magnitude; axes[i].Normalize(); }
            if (Overlap(center, axes, extents, boxCenter, bAxes, be) &&
                Physics.ComputePenetration(clearanceProbe, box.transform.position, box.transform.rotation,
                    part.collider, matrix.MultiplyPoint3x4(Vector3.zero), matrix.rotation, out _, out _)) return false;
        }
        return true;
    }
    public bool IsElbowRaised(float[] q)
    {
        Forward(q, out _, out _);
        int shoulder = Array.FindIndex(nodes, n => n.joint == 1);
        int elbow = Array.FindIndex(nodes, n => n.joint == 2);
        Vector3 segment = world[elbow].MultiplyPoint3x4(Vector3.zero) - world[shoulder].MultiplyPoint3x4(Vector3.zero);
        return segment.y > .20f && Vector3.Dot(segment.normalized, Vector3.up) > .65f;
    }
    // Separating-axis test of conservative collider bounds; checks every arm link, not only TCP.
    static bool Overlap(Vector3 a, Vector3[] axes, Vector3 ae, Vector3 b, Vector3[] other, Vector3 be)
    {
        var difference = b - a;
        for (int i = 0; i < 15; i++)
        {
            Vector3 axis = i < 3 ? axes[i] : i < 6 ? other[i - 3] : Vector3.Cross(axes[(i - 6) / 3], other[(i - 6) % 3]);
            if (axis.sqrMagnitude < 1e-10f) continue;
            float radius = 0;
            for (int j = 0; j < 3; j++) radius += ae[j] * Mathf.Abs(Vector3.Dot(axes[j], axis)) + be[j] * Mathf.Abs(Vector3.Dot(other[j], axis));
            if (Mathf.Abs(Vector3.Dot(difference, axis)) > radius + .000001f) return false;
        }
        return true;
    }
    static Vector3 RotationError(Quaternion target, Quaternion actual)
    {
        var delta = target * Quaternion.Inverse(actual);
        if (delta.w < 0) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        return axis.sqrMagnitude < .1f || float.IsNaN(axis.x) ? Vector3.zero : axis * (Mathf.DeltaAngle(0, angle) * Mathf.Deg2Rad);
    }
    public bool Solve(Vector3 target, Quaternion targetRotation, float[] seed, out float[] best, out float positionError, out float angleError, float allowedAngle = 3f, Func<float[], bool> acceptable = null)
    {
        best = (float[])seed.Clone(); float bestCost = float.PositiveInfinity;
        const float weight = .25f, epsilon = .25f;
        for (int attempt = 0; attempt < 9; attempt++)
        {
            var q = (float[])seed.Clone();
            if (attempt > 0) for (int j = 0; j < 6; j++) q[j] = Mathf.Clamp(q[j] + Mathf.Sin(attempt * 5.37f + j * 2.41f) * (attempt < 4 ? 35 : 95), lower[j], upper[j]);
            for (int iteration = 0; iteration < 260; iteration++)
            {
                Forward(q, out var p, out var r);
                Vector3 dp = target - p, dr = RotationError(targetRotation, r);
                float cost = dp.sqrMagnitude + weight * weight * dr.sqrMagnitude;
                if (cost < bestCost && (acceptable == null || acceptable(q))) { bestCost = cost; Array.Copy(q, best, 6); }
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
                    normal[row, row] += .000025f;
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
        return positionError < .005f && angleError < allowedAngle;
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
