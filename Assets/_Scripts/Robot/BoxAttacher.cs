using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Suction attachment and the requested six-slot loading operation. ArmIK owns all robot motion.
[DefaultExecutionOrder(-1000), RequireComponent(typeof(ArmIK)), DisallowMultipleComponent]
public sealed class BoxAttacher : MonoBehaviour
{
    public ArmIK armIK;
    public Transform tcp, suctionCup, container;
    public BoxCollider targetBox;
    public Transform[] slots = new Transform[6];
    public float[] readyAngles;
    [Min(.03f)] public float clearance = .16f;
    [Min(.001f)] public float contactTolerance = .008f;
    public string Status = "Play 후 다음 박스 적재를 누르세요.";
    public bool Busy => routine != null;
    public int LoadedCount => placed.Count(b => b != null);
    public Vector3 PickupPosition => pickupCentre;
    public bool Attached => joint != null;
    public IReadOnlyList<BoxCollider> PlacedBoxes => placed;
    readonly List<BoxCollider> placed = new List<BoxCollider>();
    readonly List<(Collider robot, Collider parcel)> ignored = new List<(Collider, Collider)>();
    Coroutine routine;
    FixedJoint joint;
    BoxCollider held;
    Vector3 pickupCentre;
    float[] pickupAngles, contactAngles;
    Vector3 contactPoint;
    bool cancelled;
    readonly List<(Collider a, Collider b)> selfPairs = new List<(Collider, Collider)>();
    void Awake()
    {
        if (armIK == null) armIK = GetComponent<ArmIK>();
        var colliders=GetComponentsInChildren<Collider>();
        for(int i=0;i<colliders.Length;i++)for(int j=i+1;j<colliders.Length;j++)
            if(!Physics.GetIgnoreCollision(colliders[i],colliders[j])) { Physics.IgnoreCollision(colliders[i],colliders[j],true);selfPairs.Add((colliders[i],colliders[j])); }
        foreach(var body in GetComponentsInChildren<ArticulationBody>()){body.solverIterations=60;body.solverVelocityIterations=20;body.maxJointVelocity=4f;}
        if (tcp != null) { Configure(); armIK.Initialize(readyAngles); }
        var root=GetComponentsInChildren<ArticulationBody>().First(b=>b.isRoot);
        if(readyAngles!=null&&readyAngles.Length==6){root.SetJointPositions(readyAngles.Select(v=>v*Mathf.Deg2Rad).ToList());root.SetJointVelocities(Enumerable.Repeat(0f,6).ToList());}
    }
    IEnumerator Start()
    {
        if (armIK == null) armIK = GetComponent<ArmIK>();
        if (tcp == null || suctionCup == null || slots.Length != 6 || slots.Any(s => s == null))
        { Status = "TCP / 흡착컵 / 6개 슬롯을 연결하세요."; yield break; }
        pickupCentre = targetBox == null ? tcp.position : targetBox.bounds.center;
        Configure();
        yield return new WaitForFixedUpdate();
        armIK.Model = new ArmKinematics(transform, tcp); armIK.Hold();
        Status = "흡착 적재 준비 완료 · 다음 박스 적재";
    }
    void Configure() { armIK.Configure(tcp, null, 20, .07f, .003f, 3, .006f, true); armIK.PrecisePlacement = true; }
    public bool Attach(BoxCollider box)
    {
        if (!Application.isPlaying || Attached || box == null || box.attachedRigidbody == null || box.attachedRigidbody.isKinematic) return false;
        Vector3 top = box.bounds.center; top.y = box.bounds.max.y;
        Vector3 axis = (tcp.position - suctionCup.position).normalized;
        if (Vector3.Distance(tcp.position, top) > contactTolerance || Vector3.Dot(axis, Vector3.down) < .98f)
        { Status = "흡착 실패 · 컵이 박스 윗면에 닿고 아래를 향해야 합니다."; return false; }
        held = box;
        foreach (var a in GetComponentsInChildren<Collider>()) foreach (var b in box.attachedRigidbody.GetComponentsInChildren<Collider>())
            if (!Physics.GetIgnoreCollision(a,b)) { Physics.IgnoreCollision(a,b,true); ignored.Add((a,b)); }
        var body = box.attachedRigidbody; body.solverIterations = 20; body.solverVelocityIterations = 10;
        joint = body.gameObject.AddComponent<FixedJoint>();
        joint.connectedArticulationBody = suctionCup.GetComponentInParent<ArticulationBody>();
        joint.breakForce = Mathf.Infinity; joint.breakTorque = Mathf.Infinity;
        joint.enableCollision = false;
        Status = "흡착 고정됨"; return true;
    }
    public void Detach()
    { if (joint != null) { Destroy(joint); joint = null; } held = null; }
    void RestoreContacts()
    { foreach (var pair in ignored) if(pair.robot != null && pair.parcel != null) Physics.IgnoreCollision(pair.robot,pair.parcel,false); ignored.Clear(); }
    public void LoadNextBox()
    {
        if (!Application.isPlaying || Busy || armIK == null || armIK.Moving) return;
        if (LoadedCount >= 6) { Status = "수거함 가득 참 · 6/6"; return; }
        if (targetBox == null || placed.Contains(targetBox))
            targetBox = FindObjectsByType<BoxCollider>().Where(b => b.attachedRigidbody != null && !b.attachedRigidbody.isKinematic && !placed.Contains(b) && b.transform.Find("CardboardBody") != null && Vector3.Distance(b.bounds.center,pickupCentre)<.3f).OrderBy(b => Vector3.Distance(b.bounds.center,pickupCentre)).FirstOrDefault();
        if (targetBox == null) { Status = "벨트 집기 위치에 다음 박스가 없습니다."; return; }
        cancelled = false; routine = StartCoroutine(LoadRoutine());
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
        placed.Add(box);Status="적재 완료 · "+LoadedCount+"/6 (슬롯 "+(index+1)+")";routine=null;
    }
    void OnDisable(){StopAllCoroutines();routine=null;Detach();RestoreContacts();foreach(var pair in selfPairs)if(pair.a!=null&&pair.b!=null)Physics.IgnoreCollision(pair.a,pair.b,false);selfPairs.Clear();}
}
