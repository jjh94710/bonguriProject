using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The solver operates on a mathematical copy. Only articulation drive targets move the real arm.
[DefaultExecutionOrder(-1000), DisallowMultipleComponent, RequireComponent(typeof(ArmIK))]
public class CorrectionSequence : MonoBehaviour
{
    public enum ParcelFace { [InspectorName("앞면 (+Z)")] Front, [InspectorName("뒷면 (-Z)")] Back,
        [InspectorName("왼쪽 (-X)")] Left, [InspectorName("오른쪽 (+X)")] Right, [InspectorName("밑면 (-Y)")] Bottom }
    [Tooltip("처음 벨트에 놓인 상태의 월드 축 기준. Scene 화살표가 선택한 실제 면을 표시합니다.")]
    public ParcelFace faceToRaise = ParcelFace.Back;
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
    [Tooltip("Approach the parcel horizontally and turn it with the wrist above it.")] public bool sideGrasp = true;
    [Range(-45f, 45f)] public float graspTiltDegrees = 0f;
    [Range(0f, .04f)] public float parcelGripCloseAmount = .025f;
    [SerializeField, Tooltip("Start angles in degrees: shoulder, upperarm, forearm, wrist1, wrist2, wrist3")] float[] initialJointAngles;
    [SerializeField] string status = "Play 후 이동 버튼을 누르세요.";
    public string Status => status;
    [SerializeField] ArmIK armIK;
    public ArmIK Arm => Motion;
    ArmIK Motion
    {
        get
        {
            if (armIK == null) armIK = GetComponent<ArmIK>();
            if (armIK == null && Application.isPlaying) armIK = gameObject.AddComponent<ArmIK>();
            if (armIK == null) throw new InvalidOperationException("ArmIK component is missing.");
            armIK.Configure(tcp, targetBox, degreesPerSecond, metresPerSecond, positionTolerance, angleTolerance, boxClearance, compensateRobotGravity);
            return armIK;
        }
    }
    public bool Moving => armIK != null && armIK.Moving;
    public Vector3 GoalPosition => armIK == null ? Vector3.zero : armIK.GoalPosition;
    public Quaternion GoalRotation => armIK == null ? Quaternion.identity : armIK.GoalRotation;
    public float PositionError => Motion.PositionError;
    ArmKinematics model { get => Motion.Model; set => Motion.Model = value; }
    List<float[]> route => Motion.Route;
    bool protectBox { get => Motion.ProtectBox; set => Motion.ProtectBox = value; }
    string pathFailure => Motion.PathFailure;
    bool AppendCartesian(ref float[] seed, Vector3 position, Quaternion rotation, bool checkBox) => Motion.AppendCartesian(ref seed, position, rotation, checkBox);
    bool AppendJointEdge(float[] from, float[] to, bool checkBox) => Motion.AppendJointEdge(from, to, checkBox);
    void BeginRoute(Vector3 position, Quaternion rotation) => Motion.BeginRoute(position, rotation);
    void Hold() => Motion.Hold();
    public bool MoveTo(Vector3 position, Quaternion rotation) => Motion.MoveTo(position, rotation);
    Coroutine parcelTurn;
    public string CycleStep { get; private set; }
    public int CurrentRotationStage { get; private set; }
    public int CompletedQuarterTurns { get; private set; }
    public Vector3 StageFaceNormal => targetBox == null ? Vector3.up : targetBox.transform.TransformDirection(stageFaceLocal);
    Coroutine faceCycle;
    public bool SequenceRunning => parcelTurn != null || faceCycle != null;
    Vector3 selectedFaceLocal, stageFaceLocal;
    bool faceSelectionBound, runningSelectedCycle;
    ParcelFace boundFace;
    BoxCollider boundBox;
    public Vector3 SelectedFaceNormal => targetBox == null ? Vector3.up :
        (faceSelectionBound && boundBox == targetBox && boundFace == faceToRaise ? targetBox.transform.TransformDirection(selectedFaceLocal) : WorldFace(faceToRaise));
    static Vector3 WorldFace(ParcelFace face)
    {
        switch (face) { case ParcelFace.Front: return Vector3.forward; case ParcelFace.Back: return Vector3.back;
            case ParcelFace.Left: return Vector3.left; case ParcelFace.Right: return Vector3.right; default: return Vector3.down; }
    }
    Vector3 NearestLocalFace(Vector3 worldNormal)
    {
        Vector3[] axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        return axes.OrderByDescending(v => Vector3.Dot(targetBox.transform.TransformDirection(v), worldNormal)).First();
    }
    void BindSelectedFace()
    {
        selectedFaceLocal = NearestLocalFace(WorldFace(faceToRaise));
        boundFace = faceToRaise; boundBox = targetBox; faceSelectionBound = true;
    }
    Quaternion selectedStageTurn;
    void PrepareSelectedStage()
    {
        Vector3 normal = targetBox.transform.TransformDirection(selectedFaceLocal).normalized;
        if (Vector3.Dot(normal, Vector3.up) < -.8f)
        {
            // Only a bottom-face flip needs an intermediate quarter turn and regrasp.
            stageFaceLocal = NearestLocalFace(Vector3.forward);
            selectedStageTurn = Quaternion.FromToRotation(targetBox.transform.TransformDirection(stageFaceLocal), Vector3.up);
        }
        else
        {
            // Keep the originally selected physical face: -Z -> +Y is a tip, never a yaw.
            stageFaceLocal = selectedFaceLocal;
            selectedStageTurn = Quaternion.FromToRotation(normal, Vector3.up);
        }
    }
    public void RaiseSelectedFaceAndPlace()
    {
        if (!Application.isPlaying || SequenceRunning || Moving || targetBox == null || gripper == null) return;
        placementAnchor = targetBox.bounds.center;
        BindSelectedFace(); CurrentRotationStage = 0; CompletedQuarterTurns = 0; runningSelectedCycle = true; cancellingTurn = false;
        faceCycle = StartCoroutine(SelectedFaceRoutine());
    }
    IEnumerator SelectedFaceRoutine()
    {
        yield return null;
        for (int stage = 0; stage < 2 && !cancellingTurn; stage++)
        {
            if (Vector3.Dot(SelectedFaceNormal, Vector3.up) > .985f) break;
            CurrentRotationStage = stage + 1;
            PrepareSelectedStage();
            OpenGripper(); yield return new WaitForSeconds(.4f);
            CycleStep = "옆면 접근"; ApproachBox();
            while (Moving && !cancellingTurn) yield return null;
            if (cancellingTurn || PositionError > positionTolerance || !status.StartsWith("목표 도착")) break;
            CycleStep = "잡기 위치"; DescendToBox();
            while (Moving && !cancellingTurn) yield return null;
            if (cancellingTurn || PositionError > positionTolerance || !status.StartsWith("목표 도착")) break;
            if (Mathf.Abs(Vector3.Dot(JawClosingDirection(), Vector3.up)) > .08f || JawHeightDifference() > .008f)
            { status = "집기 중단 · 두 손가락의 높이가 맞지 않습니다."; break; }
            float start = Time.time, gripTarget = parcelGripCloseAmount;
            while (Time.time - start < 3f && !cancellingTurn)
            { gripper.closeAmount = Mathf.Lerp(0, gripTarget, (Time.time - start) / 3f); yield return null; }
            yield return new WaitForSeconds(.8f);
            float bottom = targetBox.bounds.min.y;
            CycleStep = "들어 올리기"; Lift(); while (Moving && !cancellingTurn) yield return null;
            if (cancellingTurn || targetBox.bounds.min.y - bottom < .03f) { status = "선택 면 교정 중단 · 박스를 들어 올리지 못했습니다."; break; }
            CycleStep = "회전"; TurnHeldParcel90(); while ((Moving || parcelTurn != null) && !cancellingTurn) yield return null;
            if (cancellingTurn || !status.StartsWith("선택 회전 완료")) break;
            CycleStep = "내려놓기"; PlaceTurnedParcel(); while ((Moving || parcelTurn != null) && !cancellingTurn) yield return null;
            if (cancellingTurn || !status.StartsWith("벨트 놓기 완료")) break;
            yield return new WaitForSeconds(.5f);
            CompletedQuarterTurns = stage + 1;
            if (Vector3.Dot(SelectedFaceNormal, Vector3.up) <= .985f)
            {
                CycleStep = "준비 자세 복귀";
                if (!ReturnToReady()) break;
                while (Moving && !cancellingTurn) yield return null;
                if (cancellingTurn || !status.StartsWith("목표 도착")) break;
            }
        }
        if (!cancellingTurn && Vector3.Dot(SelectedFaceNormal, Vector3.up) > .985f && status.StartsWith("벨트 놓기 완료"))
            status = "선택 면 위로 놓기 완료 · " + faceToRaise;
        runningSelectedCycle = false; faceCycle = null;
    }
    public bool ReturnToReady()
    {
        if (initialJointAngles == null || initialJointAngles.Length != 6) { status = "준비 자세 각도가 없습니다."; return false; }
        model = new ArmKinematics(transform, tcp);
        precisePlacement = false; protectBox = true;
        var current = model.CurrentAngles(); bool found = false;
        foreach (float height in new[] { .12f, .08f, .04f })
        {
            route.Clear(); var seed = (float[])current.Clone();
            if (AppendCartesian(ref seed, tcp.position + Vector3.up * height, tcp.rotation, true)
                && AppendJointEdge(seed, initialJointAngles, true)) { found = true; break; }
        }
        if (!found) { route.Clear(); status = "재집기 복귀 경로를 찾지 못했습니다. " + pathFailure; return false; }
        model.Forward(initialJointAngles, out var position, out var rotation);
        BeginRoute(position, rotation); return true;
    }
    public Collider placementSurface;
    bool cancellingTurn;
    public void SetInitialJointAngles(float[] values) { initialJointAngles = (float[])values.Clone(); }


    void Awake()
    {
        Motion.StatusChanged -= OnMotionStatus;
        Motion.StatusChanged += OnMotionStatus;
        Motion.Initialize(initialJointAngles);
    }
    void OnMotionStatus(string value) { status = value; }
    IEnumerator Start()
    {
        faceCycle = null; parcelTurn = null; runningSelectedCycle = false; cancellingTurn = false;
        yield return new WaitForFixedUpdate();
        try
        {
            model = new ArmKinematics(transform, tcp);
            Hold();
            status = sideGrasp ? "준비 완료 · 박스 옆으로 이동을 누르세요." : "준비 완료 · 박스 위로 이동을 누르세요.";
        }
        catch (Exception e) { status = e.Message; Debug.LogError("CorrectionSequence: " + e.Message, this); }
    }
    void OnDisable()
    {
        StopAllCoroutines(); faceCycle = null; parcelTurn = null; runningSelectedCycle = false; cancellingTurn = true;
        if (Application.isPlaying && model != null) Hold();
        if (armIK != null) { armIK.RestoreControllers(); armIK.StatusChanged -= OnMotionStatus; }
    }
    float sideClosingSign = 1f, sideStandOff = .15f;
    public void ApproachBox()
    {
        // A symmetric parallel gripper can approach with either wrist roll.
        // Keep the chosen option for the subsequent straight grasp movement.
        foreach (float sign in new[] { 1f, -1f })
            foreach (float distance in new[] { .15f, .10f, .07f })
            {
                sideClosingSign = sign; sideStandOff = distance;
                TryApproachBox();
                if (Moving || !Application.isPlaying || targetBox == null) return;
                if (!status.StartsWith("박스와 충돌하지 않는 경로")) return;
            }
    }
    void TryApproachBox()
    {
        if (!Application.isPlaying) { status = "Play 모드에서 실행하세요."; return; }
        if (!runningSelectedCycle && targetBox != null) { placementAnchor = targetBox.bounds.center; BindSelectedFace(); PrepareSelectedStage(); }
        if (!GetBoxGoal(approachHeight, out var position, out var desired)) return;
        restingBoxRotation = targetBox.transform.rotation;
        hasTurnedBoxGoal = false; precisePlacement = false;
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
        catch (Exception e) { route.Clear(); status = e.Message; Debug.LogError("CorrectionSequence: " + e.Message, this); }
    }
    public void DescendToBox()
    {
        if (!Application.isPlaying) { status = "Play 모드에서 실행하세요."; return; }
        if (!GetBoxGoal(0, out var position, out var desired)) return;
        if (sideGrasp)
        {
            if (!GetBoxGoal(approachHeight, out var sidePosition, out var sideRotation)) return;
            if (Vector3.Distance(tcp.position, sidePosition) > .015f || Quaternion.Angle(tcp.rotation, sideRotation) > 6)
            { status = "먼저 박스 옆 대기 위치로 이동하세요."; return; }
            MoveTo(position, desired); return;
        }
        if (Vector2.Distance(new Vector2(tcp.position.x, tcp.position.z), new Vector2(position.x, position.z)) > .015f ||
            Quaternion.Angle(tcp.rotation, desired) > 6)
        { status = "먼저 박스 위로 이동해서 방향과 중심을 맞추세요."; return; }
        MoveTo(position, desired);
    }
    void RequestBox(float height)
    {
        if (GetBoxGoal(height, out var position, out var desired)) MoveTo(position, desired);
    }
    // URDF prismatic joints can have rotated anchors. The mesh/root X axis is not the jaw axis.
    public Vector3 JawClosingDirection()
    {
        var finger = GetComponentsInChildren<ArticulationBody>(true).FirstOrDefault(t => t.name == "finger_left_link");
        if (finger == null) throw new InvalidOperationException("finger_left_link 관절을 찾지 못했습니다.");
        return finger.transform.TransformDirection(finger.anchorRotation * Vector3.right).normalized;
    }
    public float JawHeightDifference()
    {
        var fingers = GetComponentsInChildren<ArticulationBody>(true)
            .Where(t => t.name == "finger_left_link" || t.name == "finger_right_link").ToArray();
        if (fingers.Length != 2) return float.PositiveInfinity;
        Vector3[] centres = new Vector3[2];
        for (int i = 0; i < 2; i++)
        {
            var colliders = fingers[i].GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToArray();
            if (colliders.Length == 0) return float.PositiveInfinity;
            var bounds = colliders[0].bounds;
            foreach (var c in colliders.Skip(1)) bounds.Encapsulate(c.bounds);
            centres[i] = bounds.center;
        }
        return Mathf.Abs(centres[0].y - centres[1].y);
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
        Vector3 localClosing = tcp.InverseTransformDirection(JawClosingDirection()).normalized;
        Quaternion localFrame = Quaternion.LookRotation(Vector3.Cross(localClosing, localApproach), localApproach);
        if (sideGrasp)
        {
            // Use a horizontal box edge as the jaw closing axis; choose the near side of that edge.
            var axes = new[] { targetBox.transform.right, targetBox.transform.up, targetBox.transform.forward };
            int best = -1; float narrowest = float.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(Vector3.Dot(axes[i].normalized, Vector3.up)) > .15f) continue;
                float width = targetBox.size[i] * targetBox.transform.lossyScale[i];
                if (width < narrowest) { narrowest = width; best = i; }
            }
            if (best < 0) { status = "박스를 벨트에 평평하게 놓은 뒤 옆면 집기를 시작하세요."; return false; }
            Vector3 selectedNormal = faceSelectionBound ? targetBox.transform.TransformDirection(stageFaceLocal) : WorldFace(faceToRaise);
            if (selectedNormal.sqrMagnitude < .5f || Mathf.Abs(selectedNormal.y) > .8f)
                selectedNormal = Vector3.ProjectOnPlane(transform.position - bounds.center, Vector3.up).normalized;
            Vector3 approach = -Vector3.ProjectOnPlane(selectedNormal, Vector3.up).normalized;
            closing = Vector3.Cross(approach, Vector3.up).normalized * sideClosingSign;
            approach = (approach * .8660254f + Vector3.down * .5f).normalized;
            desired = Quaternion.LookRotation(Vector3.Cross(closing, approach), approach) * Quaternion.Inverse(localFrame);
            float extent = Mathf.Abs(approach.x) * bounds.extents.x + Mathf.Abs(approach.z) * bounds.extents.z;
            position = bounds.center + Vector3.up * .005f;
            if (height > 0) position -= approach * (extent + Mathf.Min(height, sideStandOff));
        }
        else
        {
            desired = Quaternion.LookRotation(Vector3.Cross(closing, Vector3.down), Vector3.down) * Quaternion.Inverse(localFrame);
            desired = Quaternion.AngleAxis(graspTiltDegrees, closing) * desired;
            position = bounds.center + Vector3.up * (height > 0 ? bounds.extents.y + height : 0);
        }
        return true;
    }
    public void Lift()
    { if (tcp != null) MoveTo(tcp.position + Vector3.up * liftHeight, tcp.rotation); }
    public void TurnHeldParcel90()
    {
        if (!Application.isPlaying || Moving || parcelTurn != null) return;
        if (targetBox == null || gripper == null || gripper.closeAmount <= .001f)
        { status = "먼저 박스를 잡고 손가락을 닫으세요."; return; }
        cancellingTurn = false;
        parcelTurn = StartCoroutine(TurnParcelRoutine());
    }
    Vector3 releaseDirection;
    Quaternion expectedTurnedBox;
    bool hasTurnedBoxGoal;
    Quaternion restingBoxRotation;
    Vector3 placementAnchor;
    bool precisePlacement { get => Motion.PrecisePlacement; set => Motion.PrecisePlacement = value; }
    public Quaternion ExpectedTurnedBox => expectedTurnedBox;
    bool FindBelt(out float surfaceY)
    {
        surfaceY = float.NegativeInfinity;
        var bounds = targetBox.bounds;
        foreach (var hit in Physics.RaycastAll(bounds.center + Vector3.up * .1f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == targetBox || hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(targetBox.transform)) continue;
            bool conveyor = placementSurface != null && hit.collider == placementSurface;
            if (placementSurface == null)
                for (var t = hit.transform; t != null; t = t.parent)
                    if (t.name.IndexOf("Conveyor", StringComparison.OrdinalIgnoreCase) >= 0 || t.name == "PusherReceiverSurface") { conveyor = true; break; }
            if (conveyor && hit.point.y <= bounds.min.y + .01f) surfaceY = Mathf.Max(surfaceY, hit.point.y);
        }
        return !float.IsNegativeInfinity(surfaceY);
    }
    float PredictedBottom(Vector3 position, Quaternion rotation)
    {
        float bottom = float.PositiveInfinity;
        Quaternion delta = rotation * Quaternion.Inverse(tcp.rotation);
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = targetBox.center + Vector3.Scale(targetBox.size * .5f,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            bottom = Mathf.Min(bottom, (position + delta * (targetBox.transform.TransformPoint(c) - tcp.position)).y);
        }
        return bottom;
    }
    IEnumerator TurnParcelRoutine()
    {
        yield return null;
        model = new ArmKinematics(transform, tcp);
        var current = model.CurrentAngles();
        Quaternion original = tcp.rotation;
        var gripBase = GetComponentsInChildren<Transform>().First(t => t.name == "gripper_base_link");
        Vector3 axis = Vector3.ProjectOnPlane(JawClosingDirection(), Vector3.up).normalized;
        if (axis.sqrMagnitude < .5f) { status = "손가락 닫힘 축이 수평인 집기 자세에서 시작하세요."; parcelTurn = null; yield break; }
        if (!FindBelt(out float surfaceY)) { status = "박스 아래 벨트 접촉면을 찾지 못했습니다."; parcelTurn = null; yield break; }
        Vector3 raised = tcp.position;
        string failures = "";
        bool found = false;
        Quaternion chosen = original;
        foreach (var option in (from height in new[] { .10f, .15f, .06f }
            from offset in new[] { 0f, .08f, -.08f, .16f, -.16f }
            from sign in new[] { -1f, 1f } select new { height, offset, sign }))
        {
            float sign = option.sign;
            Vector3 inward = Vector3.ProjectOnPlane(transform.position - tcp.position, Vector3.up).normalized;
            raised = tcp.position + Vector3.up * option.height + inward * option.offset;
            Quaternion turn = faceSelectionBound ? selectedStageTurn : Quaternion.AngleAxis(sign * 90, axis);
            chosen = turn * original;
            route.Clear(); var seed = (float[])current.Clone();
            Vector3 predictedCentre = raised + chosen * Quaternion.Inverse(original) * (targetBox.bounds.center - tcp.position);
            Vector3 placed = raised + Vector3.up * (surfaceY + .002f - PredictedBottom(raised, chosen));
            placed += Vector3.ProjectOnPlane(placementAnchor - predictedCentre, Vector3.up);
            if (!AppendCartesian(ref seed, raised, original, false)) { failures += $"lift {option}: {pathFailure}\n"; continue; }
            if (!AppendCartesian(ref seed, raised, chosen, false)) { failures += $"turn {option}: {pathFailure}\n"; continue; }
            int keep = route.Count;
            if (!AppendCartesian(ref seed, placed, chosen, false)) { failures += $"down {option}: {pathFailure}\n"; continue; }
            if (!model.IsDistalAbove(seed, surfaceY - .002f)) { failures += $"belt collision {option}\n"; continue; }
            route.RemoveRange(keep, route.Count - keep);
            found = true; break;
        }
        if (!found) { route.Clear(); status = "양쪽 90도 방향 모두 벨트 놓기 경로가 없습니다. " + pathFailure; parcelTurn = null; yield break; }
        releaseDirection = -(chosen * Quaternion.Inverse(original) * (tcp.position - gripBase.position)).normalized;
        expectedTurnedBox = chosen * Quaternion.Inverse(original) * restingBoxRotation;
        hasTurnedBoxGoal = true;
        BeginRoute(raised, chosen);
        while (Moving && !cancellingTurn) yield return null;
        bool yawStage = faceSelectionBound && Vector3.Dot(selectedStageTurn * Vector3.up, Vector3.up) > .99f;
        for (int correction = 0; correction < (yawStage ? 5 : 3) && !cancellingTurn; correction++)
        {
            yield return new WaitForSeconds(.4f);
            Vector3 normal = Vector3.ProjectOnPlane(SelectedFaceNormal, Vector3.up).normalized;
            float yawError = Vector3.SignedAngle(normal, Vector3.forward, Vector3.up);
            float error = yawStage ? Mathf.Abs(yawError) : (faceSelectionBound ? Vector3.Angle(StageFaceNormal, Vector3.up) : Quaternion.Angle(targetBox.transform.rotation, expectedTurnedBox));
            if (error < 5) break;
            Quaternion delta = yawStage ? Quaternion.AngleAxis(yawError, Vector3.up) : (faceSelectionBound ? Quaternion.FromToRotation(StageFaceNormal, Vector3.up) : expectedTurnedBox * Quaternion.Inverse(targetBox.transform.rotation));
            Quaternion corrected = Quaternion.RotateTowards(tcp.rotation, delta * tcp.rotation, yawStage ? 20f : 15f);
            if (!MoveTo(tcp.position, corrected)) { parcelTurn = null; yield break; }
            while (Moving && !cancellingTurn) yield return null;
        }
        releaseDirection = (gripBase.position - tcp.position).normalized;
        if (!cancellingTurn && PositionError <= positionTolerance && (yawStage ? Vector3.Angle(Vector3.ProjectOnPlane(SelectedFaceNormal, Vector3.up), Vector3.forward) < 10 : (faceSelectionBound ? Vector3.Angle(StageFaceNormal, Vector3.up) < 10 : Quaternion.Angle(targetBox.transform.rotation, expectedTurnedBox) < 10)))
            status = "선택 회전 완료 · 박스 각도 확인됨. 5번을 누르세요.";
        else if (!cancellingTurn) status = "박스 회전 각도가 부족합니다. 집기 상태를 확인하세요.";
        parcelTurn = null;
    }
    public void PlaceTurnedParcel()
    {
        if (!Application.isPlaying || Moving || parcelTurn != null) return;
        if (targetBox == null || gripper == null || gripper.closeAmount <= .001f)
        { status = "먼저 박스를 잡고 90도 세우세요."; return; }
        cancellingTurn = false;
        precisePlacement = true;
        parcelTurn = StartCoroutine(PlaceParcelRoutine());
    }
    IEnumerator PlaceParcelRoutine()
    {
        yield return null;
        Quaternion gripRotation = tcp.rotation, boxRotation = targetBox.transform.rotation;
        if (!FindBelt(out float surfaceY))
        { status = "박스 아래 벨트 접촉면을 찾지 못했습니다. Placement Surface를 지정하세요."; parcelTurn = null; yield break; }
        var bounds = targetBox.bounds;
        CycleStep = "벨트 접촉 높이로 하강";
        var position = tcp.position + Vector3.up * (surfaceY + .002f - bounds.min.y);
        position += Vector3.ProjectOnPlane(placementAnchor - bounds.center, Vector3.up);
        var beforeLowering = new ArmKinematics(transform, tcp).CurrentAngles();
        if (!MoveTo(position, gripRotation)) { parcelTurn = null; yield break; }
        var loweringRoute = route.Select(q => (float[])q.Clone()).ToList();
        loweringRoute.Insert(0, beforeLowering);
        while (Moving && !cancellingTurn) yield return null;
        if (cancellingTurn || PositionError > positionTolerance || Quaternion.Angle(tcp.rotation, gripRotation) > angleTolerance || Quaternion.Angle(targetBox.transform.rotation, boxRotation) > 10)
        { status = "놓기 중단 · 위치 또는 박스 자세가 달라졌습니다. 손가락은 닫힌 상태입니다."; parcelTurn = null; yield break; }
        yield return new WaitForSeconds(.8f);
        float initialClose = gripper.closeAmount, start = Time.time;
        CycleStep = "벨트 위 손가락 열기";
        status = "박스 자세를 유지하며 벨트 위에서 손가락을 여는 중";
        while (Time.time - start < .8f && !cancellingTurn)
        { gripper.closeAmount = Mathf.Lerp(initialClose, 0, (Time.time - start) / .8f); yield return null; }
        if (cancellingTurn) { parcelTurn = null; yield break; }
        gripper.Open();
        yield return new WaitForSeconds(1f);
        if (cancellingTurn) { parcelTurn = null; yield break; }
        CycleStep = "손가락 분리 후 경로 역순 복귀";
        model = new ArmKinematics(transform, tcp);
        var seed = model.CurrentAngles(); route.Clear(); protectBox = false;
        for (int i = loweringRoute.Count - 1; i >= 0; i--)
        {
            AppendJointEdge(seed, loweringRoute[i], false);
            seed = loweringRoute[i];
        }
        model.Forward(beforeLowering, out var retreatPosition, out var retreatRotation);
        BeginRoute(retreatPosition, retreatRotation);
        while (Moving && !cancellingTurn) yield return null;
        if (!cancellingTurn)
            status = (faceSelectionBound
                ? (Vector3.Dot(selectedStageTurn * Vector3.up, Vector3.up) > .99f
                    ? Vector3.Angle(Vector3.ProjectOnPlane(SelectedFaceNormal, Vector3.up), Vector3.forward) <= 10
                    : Vector3.Angle(StageFaceNormal, Vector3.up) <= 10)
                : Quaternion.Angle(targetBox.transform.rotation, hasTurnedBoxGoal ? expectedTurnedBox : boxRotation) <= 10)
                ? "벨트 놓기 완료 · 세운 박스 자세 유지 확인" : "박스는 놓았으나 자세가 기울었습니다. 놓는 위치와 접촉면을 확인하세요.";
        parcelTurn = null;
    }
    public void OpenGripper() { if (gripper != null) gripper.Open(); }
    public float GripCloseForBox()
    {
        if (targetBox == null) return parcelGripCloseAmount;
        var gripBase = GetComponentsInChildren<Transform>().First(t => t.name == "gripper_base_link");
        Vector3 closing = JawClosingDirection();
        float width = Mathf.Abs(Vector3.Dot(targetBox.transform.TransformVector(Vector3.right * targetBox.size.x), closing))
            + Mathf.Abs(Vector3.Dot(targetBox.transform.TransformVector(Vector3.up * targetBox.size.y), closing))
            + Mathf.Abs(Vector3.Dot(targetBox.transform.TransformVector(Vector3.forward * targetBox.size.z), closing));
        return Mathf.Clamp(parcelGripCloseAmount + (.06f - width) * .5f, .003f, .04f);
    }
    public void CloseGripper() { if (gripper != null) gripper.closeAmount = parcelGripCloseAmount; }





    public void Stop()
    {
        cancellingTurn = true;
        if (faceCycle != null) { StopCoroutine(faceCycle); faceCycle = null; runningSelectedCycle = false; }
        if (parcelTurn != null) { StopCoroutine(parcelTurn); parcelTurn = null; }
        Hold(); status = "정지 · 현재 관절 각도를 유지합니다.";
    }

    void OnDrawGizmosSelected()
    {
        if (targetBox != null) { Gizmos.color = Color.yellow; Gizmos.DrawWireCube(targetBox.bounds.center, targetBox.bounds.size); }
        if (targetBox != null)
        {
            Gizmos.color = Color.magenta;
            Vector3 normal = SelectedFaceNormal.normalized, centre = targetBox.bounds.center;
            Gizmos.DrawLine(centre, centre + normal * .14f); Gizmos.DrawSphere(centre + normal * .14f, .006f);
        }
        if (Moving && tcp != null) { Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(GoalPosition, .015f); Gizmos.DrawLine(tcp.position, GoalPosition); }
    }
}
