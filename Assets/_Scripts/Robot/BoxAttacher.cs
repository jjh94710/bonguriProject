using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Suction attachment and the configured-slot loading operation. BackArmIK owns all robot motion.
[DefaultExecutionOrder(-1000), RequireComponent(typeof(BackArmIK)), DisallowMultipleComponent]
public sealed class BoxAttacher : MonoBehaviour
{
    public BackArmIK armIK;
    public TotoBase toto;
    public Transform tcp, suctionCup, container;
    public BoxCollider targetBox;
    public Transform[] slots = new Transform[6];
    public float[] readyAngles;
    [Serializable]
    public sealed class DirectionSettings
    {
        [Min(.03f)] public float standOff = .16f;
        [Min(0)] public float approachAbove = .12f;
        [Min(.05f)] public float liftHeight = .16f;
        [Range(0, .08f)] public float rotationRise = .025f;
        [Range(5, 30)] public float rotationStepDegrees = 15f;
        public float[] wristRolls = { 0f, 90f, -90f, 180f };
    }
    [Header("5×6면의 월드 방향별 설정 (자동 판별)")]
    public DirectionSettings xDirection = new DirectionSettings { standOff = .16f, approachAbove = .12f, liftHeight = .16f, rotationRise = .025f, wristRolls = new[] { 90f, -90f, 0f, 180f } };
    public DirectionSettings yDirection = new DirectionSettings { standOff = .12f, approachAbove = .08f, liftHeight = .12f, rotationRise = .02f };
    [SerializeField] string activeFaceDirection = "자동 판별 · Z는 기존 경로";

    [Min(.03f)] public float clearance = .16f;
    [Min(.001f)] public float contactTolerance = .008f;
    public string Status = "Play 후 다음 박스 적재를 누르세요.";
    public bool Busy => routine != null;
    public bool LastLoadSucceeded { get; private set; }
    bool placementReached;
    public int LoadedCount => placed.Count(b => b != null);
    public Vector3 PickupPosition => pickupCentre;
    public bool Attached => heldBody != null;
    public IReadOnlyList<BoxCollider> PlacedBoxes => placed;
    readonly List<BoxCollider> placed = new List<BoxCollider>();
    readonly List<(Collider robot, Collider parcel)> ignored = new List<(Collider, Collider)>();
    Coroutine routine;
    BoxCollider held;
    Rigidbody heldBody;
    bool heldWasKinematic, heldUsedGravity;
    Vector3 heldLocalPosition;
    Quaternion heldLocalRotation;
    Vector3 pickupCentre;
    float[] pickupAngles, contactAngles;
    float[] cycleReadyAngles;
    Quaternion cycleToolRotation;
    Vector3 contactPoint;
    bool cancelled;
    float suctionFloor = -10000f;
    readonly List<(Collider a, Collider b)> selfPairs = new List<(Collider, Collider)>();
    readonly List<(ArticulationBody body, bool gravity)> gravityStates = new List<(ArticulationBody, bool)>();
    void Awake()
    {
        if (armIK == null) armIK = GetComponent<BackArmIK>();
        var colliders=GetComponentsInChildren<Collider>();
        for(int i=0;i<colliders.Length;i++)for(int j=i+1;j<colliders.Length;j++)
            if(!Physics.GetIgnoreCollision(colliders[i],colliders[j])) { Physics.IgnoreCollision(colliders[i],colliders[j],true);selfPairs.Add((colliders[i],colliders[j])); }
        foreach(var body in GetComponentsInChildren<ArticulationBody>()){gravityStates.Add((body, body.useGravity));body.useGravity=false;body.solverIterations=60;body.solverVelocityIterations=20;body.maxJointVelocity=4f;}
        if (tcp != null) { Configure(); armIK.Initialize(readyAngles); }
        // Drive targets are configured by BackArmIK.Initialize. Directly replacing the
        // root reduced-space arrays here can crash the Unity 6 PhysX solver.
    }
    IEnumerator Start()
    {
        if (armIK == null) armIK = GetComponent<BackArmIK>();
        if (tcp == null || suctionCup == null || slots == null || slots.Length == 0 || slots.Any(s => s == null))
        { Status = "TCP / 흡착컵 / 적재 슬롯을 연결하세요."; yield break; }
        pickupCentre = targetBox == null ? tcp.position : targetBox.bounds.center;
        Configure();
        yield return new WaitForFixedUpdate();
        armIK.Model = new ArmKinematics(transform, tcp); armIK.Hold();
        cycleReadyAngles = armIK.Model.CurrentAngles();
        cycleToolRotation = Quaternion.Inverse(transform.rotation) * tcp.rotation;
        Status = "흡착 적재 준비 완료 · 다음 박스 적재";
    }
    void Configure() { armIK.minimumWristPlane=toto != null ? suctionFloor : -10000f; armIK.raisedElbowClearance=toto != null ? .10f : 0; armIK.driveDamping=1500f; armIK.driveForceLimit=10000f; armIK.Configure(tcp, null, 20, .07f, .003f, 3, .006f, true); armIK.PrecisePlacement = true; }
    public bool Attach(BoxCollider box)
    {
        if (!Application.isPlaying || Attached || box == null || box.attachedRigidbody == null || box.attachedRigidbody.isKinematic) return false;
        Vector3 top = box.bounds.center; top.y = box.bounds.max.y;
        Vector3 axis = (tcp.position - suctionCup.position).normalized;
        if (requiredSuctionFace ? !AttachRequiredFace(box) : (Vector3.Distance(tcp.position, top) > contactTolerance || Vector3.Dot(axis, Vector3.down) < .98f))
        { Status = "흡착 실패 · 컵이 박스 윗면에 닿고 아래를 향해야 합니다."; return false; }
        held = box;
        foreach (var a in GetComponentsInChildren<Collider>()) foreach (var b in box.attachedRigidbody.GetComponentsInChildren<Collider>())
            if (!Physics.GetIgnoreCollision(a,b)) { Physics.IgnoreCollision(a,b,true); ignored.Add((a,b)); }
        var body = box.attachedRigidbody;
        body.solverIterations = 20; body.solverVelocityIterations = 10;
        heldBody = body;
        heldWasKinematic = body.isKinematic;
        heldUsedGravity = body.useGravity;
        heldLocalPosition = tcp.InverseTransformPoint(body.position);
        heldLocalRotation = Quaternion.Inverse(tcp.rotation) * body.rotation;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
        Status = "흡착 고정됨"; return true;
    }
    public void Detach()
    {
        if (heldBody != null)
        {
            var body = heldBody;
            heldBody = null;
            body.isKinematic = heldWasKinematic;
            body.useGravity = heldUsedGravity;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
        held = null;
    }
    void FixedUpdate()
    {
        if (heldBody == null || tcp == null) return;
        heldBody.MovePosition(tcp.TransformPoint(heldLocalPosition));
        heldBody.MoveRotation(tcp.rotation * heldLocalRotation);
    }
    void RestoreContacts()
    { foreach (var pair in ignored) if(pair.robot != null && pair.parcel != null) Physics.IgnoreCollision(pair.robot,pair.parcel,false); ignored.Clear(); }
    public void LoadNextBox()
    {
        if (!Application.isPlaying || Busy || armIK == null || armIK.Moving) return;
        if (slots == null || slots.Length == 0 || slots.Any(s => s == null)) { Status = "적재 슬롯을 연결하세요."; return; }
        if (LoadedCount >= slots.Length) { Status = "수거함 가득 참 · " + LoadedCount + "/" + slots.Length; return; }
        if (targetBox == null || placed.Contains(targetBox))
            targetBox = FindObjectsByType<BoxCollider>().Where(b => b.attachedRigidbody != null && !b.attachedRigidbody.isKinematic && !placed.Contains(b) && b.transform.Find("CardboardBody") != null && Vector3.Distance(b.bounds.center,pickupCentre)<.3f).OrderBy(b => Vector3.Distance(b.bounds.center,pickupCentre)).FirstOrDefault();
        if (targetBox == null) { Status = "벨트 집기 위치에 다음 박스가 없습니다."; return; }
        LastLoadSucceeded = false; cancelled = false; routine = StartCoroutine(LoadRoutine());
    }
    public void StopLoading()
    { cancelled=true;if(routine!=null)StopCoroutine(routine);routine=null;if(armIK!=null)armIK.StopMotion();Status=Attached?"중단 · 흡착 유지 중":"중단"; }
    bool Arrived => !cancelled && armIK.Status != null && armIK.Status.StartsWith("목표 도착");
    IEnumerator Move(Vector3 point, Quaternion rotation)
    {
        Configure();
        if (!armIK.MoveTo(point,rotation)) { Status="이동 실패 ["+Status+"] · "+armIK.Status+" goal="+point+" target="+targetBox.bounds.center; yield break; }
        while(armIK.Moving && !cancelled) yield return null;
        if(!Arrived) Status="이동 중단 · "+armIK.Status;
    }
    bool SlotOccupied(int index,BoxCollider own)
    {
        Vector3 centre=slots[index].position;
        return Physics.OverlapBox(centre, own.bounds.extents*.8f,Quaternion.identity).Any(c=>c!=own && !c.isTrigger && c.attachedRigidbody!=null && !c.transform.IsChildOf(transform));
    }
    IEnumerator LoadRoutine()
    {
        yield return null;
        if (toto != null) { yield return LoadTotoRoutine(); yield break; }
        var box=targetBox;
        foreach(var hit in Physics.RaycastAll(box.bounds.center+Vector3.up*.1f,Vector3.down,.5f))
        {var conveyor=hit.collider.GetComponentInParent<RoboLacyTech.Conveyors.LinearConveyor>();if(conveyor!=null)conveyor.Stop();}
        int index=Array.FindIndex(slots,s=>s!=null && !placed.Any(b=>b!=null && Vector3.Distance(b.bounds.center,s.position)<.035f));
        if(index<0||SlotOccupied(index,box)){Status="빈 슬롯이 없거나 놓는 자리에 장애물이 있습니다.";routine=null;yield break;}
        Vector3 top=box.bounds.center;top.y=box.bounds.max.y+.001f;
        Vector3 localAxis=tcp.InverseTransformDirection((tcp.position-suctionCup.position).normalized);
        Quaternion down=Quaternion.FromToRotation(tcp.rotation*localAxis,Vector3.down)*tcp.rotation;
        Status="박스 위 접근";Configure();
        bool approachFound=pickupAngles!=null && armIK.MoveToJointAngles(pickupAngles,box);
        foreach(float yaw in approachFound ? Array.Empty<float>() : new[]{0f,90f,-90f,180f})
        { var candidate=Quaternion.AngleAxis(yaw,Vector3.up)*down;
          if(armIK.MoveToJointPath(top+Vector3.up*clearance,candidate,box)){down=candidate;approachFound=true;break;} }
        if(!approachFound){Status="접근 실패 · "+armIK.Status;routine=null;yield break;}
        while(armIK.Moving&&!cancelled)yield return null;
        if(!Arrived){routine=null;yield break;}
        pickupAngles = new ArmKinematics(transform,tcp).CurrentAngles();
        down=tcp.rotation;
        Status="흡착면 접촉";
        if(contactAngles!=null && Vector3.Distance(contactPoint,top)<.03f)
        {
            if(!armIK.MoveToJointAngles(contactAngles)){Status="접촉 복귀 실패 · "+armIK.Status;routine=null;yield break;}
            while(armIK.Moving&&!cancelled)yield return null;
        }
        else { yield return Move(top,down); if(Arrived){contactAngles=new ArmKinematics(transform,tcp).CurrentAngles();contactPoint=top;} }
        if(!Arrived||!Attach(box)){routine=null;yield break;}
        yield return new WaitForSeconds(.3f);
        float bottom=box.bounds.min.y;Status="박스 들어 올리기";
        yield return Move(tcp.position+Vector3.up*clearance,down);
        if(!Arrived||!Attached||box.bounds.min.y-bottom<.05f){Status="들어 올리기 실패 · 흡착 상태를 확인하세요.";routine=null;yield break;}
        Vector3 offset=tcp.position-box.bounds.center;
        Vector3 place=slots[index].position+offset;
        float rim=container.GetComponentsInChildren<Collider>().Max(c=>c.bounds.max.y);
        float carryY=Mathf.Max(tcp.position.y,place.y+clearance,rim+clearance+offset.y);
        Status="수거함 위로 운반";yield return Move(new Vector3(tcp.position.x,carryY,tcp.position.z),down);
        if(!Arrived){routine=null;yield break;}
        yield return Move(new Vector3(place.x,carryY,place.z),down);
        if(!Arrived){routine=null;yield break;}
        Status="슬롯 "+(index+1)+" 내려놓기";yield return Move(place+Vector3.up*.002f,down);
        if(!Arrived||!Attached){routine=null;yield break;}
        Detach();yield return new WaitForSeconds(.4f);
        Status="흡착 해제 후 상승";yield return Move(new Vector3(place.x,carryY,place.z),down);
        RestoreContacts();yield return new WaitForSeconds(.5f);
        if(!Arrived||Vector3.Distance(box.bounds.center,slots[index].position)>.018f||box.attachedRigidbody.linearVelocity.magnitude>.05f)
        {Status="적재 확인 실패 · 슬롯 위치와 박스 자세를 확인하세요.";routine=null;yield break;}
        placed.Add(box);Status="적재 완료 · "+LoadedCount+"/"+slots.Length+" (슬롯 "+(index+1)+")";routine=null;
    }
    // The short local Z axis normal identifies the 5 x 6 cm suction faces.
    Vector3 requiredSuctionPoint, requiredSuctionNormal;
    bool requiredSuctionFace;
    bool AttachRequiredFace(BoxCollider box)
    {
        Vector3 axis = (tcp.position - suctionCup.position).normalized;
        if (!toto.IsCorrectSize(box) || Vector3.Distance(tcp.position, requiredSuctionPoint) > contactTolerance || Vector3.Dot(axis, -requiredSuctionNormal) < .98f)
        { Status = "흡착 중단 · 컵이 5×6cm 면 중심에 수직으로 접촉하지 않았습니다."; return false; }
        return true;
    }
    IEnumerator LoadTotoRoutine()
    {
        yield return null;
        var box = targetBox; suctionFloor = box.bounds.min.y + .001f;
        if (cycleReadyAngles == null)
        {
            cycleReadyAngles = new ArmKinematics(transform, tcp).CurrentAngles();
            cycleToolRotation = Quaternion.Inverse(transform.rotation) * tcp.rotation;
        }
        if (LoadedCount > 0)
        {
            Status = "다음 박스 전 동일 준비 자세로 복귀";
            Configure();
            if (!armIK.MoveToJointAngles(cycleReadyAngles, box))
            { Status = "준비 자세 복귀 실패 · " + armIK.Status; routine = null; yield break; }
            while (armIK.Moving && !cancelled) yield return null;
            if (!Arrived) { Status = "준비 자세 복귀 중단 · " + armIK.Status; routine = null; yield break; }
        }
        Quaternion referenceToolRotation = transform.rotation * cycleToolRotation;
        if (!toto.TryNext(box, out int index, out Vector3 slotCentre, out string reason))
        { Status = reason; routine = null; yield break; }
        foreach (var hit in Physics.RaycastAll(box.bounds.center + Vector3.up * .1f, Vector3.down, .5f))
        { var belt = hit.collider.GetComponentInParent<RoboLacyTech.Conveyors.LinearConveyor>(); if (belt != null) belt.Stop(); }
        Vector3 cupAxis = tcp.InverseTransformDirection((tcp.position - suctionCup.position).normalized);
        Vector3 faceAxis = box.transform.forward.normalized;
        Vector3 absolute = new Vector3(Mathf.Abs(faceAxis.x), Mathf.Abs(faceAxis.y), Mathf.Abs(faceAxis.z));
        string direction = absolute.y >= absolute.x && absolute.y >= absolute.z ? "Y" : absolute.x > absolute.z ? "X" : "Z";
        bool originalZ = direction == "Z";
        DirectionSettings settings = originalZ ? null : direction == "X" ? xDirection : yDirection;
        if (!originalZ && settings == null) { Status = direction + " 방향 설정이 없습니다."; routine = null; yield break; }
        activeFaceDirection = direction + " 방향 · " + (originalZ ? "기존 경로" : "접근·상승·회전 사전 확인");
        float approachDistance = originalZ ? clearance : Mathf.Max(.03f, settings.standOff);
        float approachAbove = originalZ ? .12f : Mathf.Max(0f, settings.approachAbove);
        float liftHeight = originalZ ? clearance : Mathf.Max(.05f, settings.liftHeight);
        float[] rollCandidates = originalZ ? new[] { 0f, 90f, -90f, 180f } : settings.wristRolls;
        if (rollCandidates == null || rollCandidates.Length == 0) rollCandidates = new[] { 0f, 90f, -90f, 180f };
        if (direction == "X")
            rollCandidates = new[] { 0f, 90f, -90f, 180f, 45f, -45f, 135f, -135f }.Concat(rollCandidates).Distinct().ToArray();
        string rejectedPath = "", rejectedApproach = "", rejectedRotation = "";
        int validApproaches = 0, rotationAttempts = 0;
        Quaternion selectedBoxRotation = Quaternion.identity;

        float score(float sign)
        {
            Vector3 normal = box.transform.forward * sign;
            return Vector3.Dot(normal, Vector3.up) * 2 + Vector3.Dot(normal, (transform.position - box.bounds.center).normalized);
        }
        float selectedSign = 1;
        Quaternion contactRotation = tcp.rotation;
        bool found = false;
        foreach (float sign in new[] { 1f, -1f }.OrderByDescending(score))
        {
            var normal = box.transform.forward.normalized * sign;
            // Do not approach the face which is resting on the belt.
            if (Vector3.Dot(normal, Vector3.up) < -.5f) continue;
            var point = toto.SuctionPoint(box, sign);
            var aligned = Quaternion.FromToRotation(referenceToolRotation * cupAxis, -normal) * referenceToolRotation;
            foreach (float roll in rollCandidates)
            {
                var rotation = Quaternion.AngleAxis(roll, normal) * aligned;
                Status = direction + " 방향 · 접근/회전 경로 계산"; Configure();
                Vector3 contact = point + normal * .001f;
                if (!armIK.PlanSuctionApproach(point + normal * approachDistance + Vector3.up * approachAbove, point + normal * approachDistance, contact, rotation, box, preview: !originalZ))
                { rejectedPath = rejectedApproach = armIK.Status; continue; }
                validApproaches++;
                if (!originalZ)
                {
                    bool rotationFound = false;
                    Quaternion canonical = toto.PlacementRotation(sign);
                    // A 180-degree yaw keeps the same 5x6 footprint while avoiding an unnecessary wrist turn.
                    var desiredRotations = direction == "X" ? new[] { canonical, Quaternion.AngleAxis(180f, toto.transform.up) * canonical } : new[] { canonical };
                    var liftOptions = direction == "X" ? new[] { liftHeight, .12f, .08f }.Distinct().ToArray() : new[] { liftHeight };
                    // Candidate offsets are relative to the pickup point, in world metres.
                    // Move toward the robot and/or sideways to obtain a different wrist configuration.
                    Vector3 inward = Vector3.ProjectOnPlane(transform.position - box.bounds.center, Vector3.up).normalized;
                    Vector3 lateral = Vector3.Cross(Vector3.up, inward);
                    var waypoints = new[] {
                        inward * .08f + Vector3.up * .04f,
                        inward * .08f + lateral * .10f + Vector3.up * .04f,
                        inward * .08f - lateral * .10f + Vector3.up * .04f,
                        lateral * .12f + Vector3.up * .08f,
                        -lateral * .12f + Vector3.up * .08f,
                        Vector3.up * .10f
                    };
                    var riseOptions = direction == "X" ? new[] { settings.rotationRise, 0f }.Distinct().ToArray() : new[] { settings.rotationRise };
                    foreach (var desired in desiredRotations)
                    {
                        foreach (float lift in liftOptions)
                        {
                            foreach (float rise in riseOptions)
                            {
                                foreach (var waypoint in waypoints)
                                {
                                    rotationAttempts++;
                                    Status = direction + " 방향 · 회전 대기 위치 후보 " + rotationAttempts + " 계산";
                                    if (armIK.PreviewSuctionRotation(box, desired, lift, rise, Mathf.Min(settings.rotationStepDegrees, 10f), waypoint))
                                    { selectedBoxRotation = desired; rotationFound = true; break; }
                                    rejectedPath = rejectedRotation = armIK.Status;
                                    yield return null;
                                    if (cancelled) { routine = null; yield break; }
                                }
                                if (rotationFound) break;
                            }
                            if (rotationFound) break;
                        }
                        if (rotationFound) break;
                    }
                    if (!rotationFound) { yield return null; continue; }
                    if (!armIK.StartPreparedSuctionApproach(contact, rotation))
                    { rejectedPath = "검증한 접근 경로 시작 실패"; continue; }
                }
                selectedSign = sign; contactRotation = rotation;
                requiredSuctionPoint = point; requiredSuctionNormal = normal; found = true; break;
            }
            if (found) break;
        }
        if (!found)
        {
            Status = direction + " 방향 경로 선택 실패 · 접근 성공 " + validApproaches + "개 / 회전 확인 " + rotationAttempts + "회 · "
                + (validApproaches > 0 ? rejectedRotation : rejectedApproach);
            routine = null; yield break;
        }
        Status = "5×6cm 면 옆/위 대기 위치로 이동";
        while (armIK.Moving && !cancelled) yield return null;
        if (!Arrived) { Status = "흡착면 접근 중단 · " + armIK.Status; routine = null; yield break; }
        requiredSuctionFace = true;
        bool attached = Attach(box); requiredSuctionFace = false;
        if (!attached) { routine = null; yield break; }
        yield return new WaitForSeconds(.3f);
        float bottom = box.bounds.min.y;
        Status = "5×6cm 면 흡착 후 들어 올리기";
        if (originalZ) yield return Move(tcp.position + Vector3.up * liftHeight, tcp.rotation);
        else
        {
            if (!armIK.StartPreparedSuctionLift())
            { Status = direction + " 방향 사전 상승 경로 시작 실패 · " + armIK.Status; routine = null; yield break; }
            while (armIK.Moving && !cancelled) yield return null;
        }
        if (!Arrived || !Attached || box.bounds.min.y - bottom < .04f)
        { Status = "흡착 상승 실패 · " + armIK.Status; routine = null; yield break; }
        Quaternion desiredBox = originalZ ? toto.PlacementRotation(selectedSign) : selectedBoxRotation;
        Quaternion delta = desiredBox * Quaternion.Inverse(box.transform.rotation);
        Quaternion toolRotation = delta * tcp.rotation;
        if (originalZ)
        {
            // Preserve the successful Z-direction movement.
            Vector3 turnCentre = box.bounds.center + Vector3.up * .035f;
            Vector3 toolPoint = turnCentre + delta * (tcp.position - box.bounds.center);
            Status = "Z 방향 · 5×6cm 면을 위로 회전";
            yield return Move(toolPoint, toolRotation);
        }
        else
        {
            Status = direction + " 방향 · 흡착 유지 후 회전 대기 위치로 이동";
            if (!armIK.StartPreparedSuctionTransfer())
            { Status = direction + " 방향 중간 위치 이동 시작 실패 · " + armIK.Status; routine = null; yield break; }
            while (armIK.Moving && !cancelled) yield return null;
            if (!Arrived || !Attached || cancelled)
            { Status = direction + " 방향 중간 위치 이동 중단 · " + armIK.Status; routine = null; yield break; }
            Status = direction + " 방향 · 중간 위치에서 사전 확인한 분할 회전";
            if (!armIK.StartPreparedSuctionTurn())
            { Status = direction + " 방향 사전 회전 경로 시작 실패 · " + armIK.Status; routine = null; yield break; }
            while (armIK.Moving && !cancelled) yield return null;
        }
        if (!Arrived || !Attached || Quaternion.Angle(box.transform.rotation, desiredBox) > 6)
        { Status = direction + " 방향 적재 자세 회전 실패 · " + armIK.Status; routine = null; yield break; }
        // All boxes use the tote's 5 x 6 cm footprint; local Z is now the 7 cm height.
        if (!toto.TryNext(box, out int checkedIndex, out slotCentre, out reason) || checkedIndex != index)
        { Status = "적재 지지 재확인 실패 · " + reason; routine = null; yield break; }
        Vector3 offset = tcp.position - box.bounds.center;
        Vector3 place = slotCentre + offset;
        float rim = toto.RimHeight;
        Vector3 releaseCentre = toto.DropCentre(box, slotCentre);
        Vector3 releasePoint = releaseCentre + offset;
        float carryY = Mathf.Max(tcp.position.y, rim + clearance + offset.y, place.y + clearance);
        toolRotation = tcp.rotation;
        Status = "수거함 위로 운반";
        yield return Move(new Vector3(tcp.position.x, carryY, tcp.position.z), toolRotation);
        if (!Arrived) { routine = null; yield break; }
        yield return Move(new Vector3(place.x, carryY, place.z), toolRotation);
        if (!Arrived) { routine = null; yield break; }
        Status = (index < 4 ? "아래층 " : "위층 ") + (index % 4 + 1) + "번 입구 위에서 놓기";
        yield return MoveToDropHeight(box, index, slotCentre, releasePoint, toolRotation);
        if (!placementReached || !Attached || cancelled) { routine = null; yield break; }
        Detach();
        // Release above the rim; gravity places the parcel into the chosen slot.
        yield return new WaitForSeconds(.25f);
        Status = "흡착 해제 후 상승";
        yield return Move(new Vector3(tcp.position.x, carryY, tcp.position.z), toolRotation);
        bool retracted = Arrived;
        RestoreContacts();
        float deadline = Time.time + 5f;
        while (Time.time < deadline && !cancelled && !toto.IsSettledAt(index, box)) yield return new WaitForFixedUpdate();
        if (cancelled || !toto.Register(index, box))
        { Status = "적재 확인 실패 · " + toto.Status; routine = null; yield break; }
        placed.Add(box);
        LastLoadSucceeded = retracted;
        Status = retracted ? toto.Status : "박스 적재됨 · 팔 복귀 실패로 반복 중단 · " + armIK.Status;
        routine = null;
    }

    IEnumerator MoveToDropHeight(BoxCollider box, int index, Vector3 centre, Vector3 point, Quaternion rotation)
    {
        placementReached = false;
        Configure();
        bool planned = armIK.MoveTo(point, rotation);
        float supportedSince = -1f;
        do
        {
            if (cancelled || !Attached) yield break;
            if (toto.ReadyToRelease(index, box, centre))
            {
                if (supportedSince < 0) supportedSince = Time.time;
                if (Time.time - supportedSince >= .15f)
                { armIK.StopMotion(); placementReached = true; Status = "입구 위 해제 높이 도착 · 흡착 해제"; yield break; }
            }
            else supportedSince = -1f;
            if (!planned || !armIK.Moving) break;
            yield return new WaitForFixedUpdate();
        } while (true);
        // Recheck the measured release height after the final physics step.
        float deadline = Time.time + .4f;
        while (!cancelled && Attached && Time.time < deadline)
        {
            if (toto.ReadyToRelease(index, box, centre))
            { armIK.StopMotion(); placementReached = true; yield break; }
            yield return new WaitForFixedUpdate();
        }
        Status = "놓기 중단 · 칸 정렬/입구 위 높이 확인 실패 · " + armIK.Status;
    }

    void OnDisable(){foreach(var entry in gravityStates)if(entry.body!=null)entry.body.useGravity=entry.gravity;gravityStates.Clear();StopAllCoroutines();routine=null;Detach();RestoreContacts();foreach(var pair in selfPairs)if(pair.a!=null&&pair.b!=null)Physics.IgnoreCollision(pair.a,pair.b,false);selfPairs.Clear();}
}
