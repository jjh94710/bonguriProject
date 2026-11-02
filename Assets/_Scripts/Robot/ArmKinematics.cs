using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

//  ============================================================
//  ArmKinematics
//  FR5 로봇암 관절 6개의 계산 모델. 관절각으로 손끝(tcp) 위치를 구하고(순운동학), 목표 위치 · 회전에 맞는 관절각을 찾는다(역운동학).
//      - 컴포넌트가 아니다. ArmIK가 로봇 루트와 tcp로 만들어 쓴다: new ArmKinematics(transform, tcp)
//      - 만들 때 읽어 둔 링크 배치로 계산만 한다. 실제 관절은 움직이지 않는다 (ApplyEditorPose()만 예외).
//      - 같은 모델로 자세 조건(팔꿈치 · 손목 높이)과 팔 · 상자 겹침도 검사한다. ArmIK가 경로를 고를 때 쓴다.
//      - 만들 때의 관절각(reference)과 그때의 링크 회전을 같이 기억하고, 계산할 때는 (관절각 − reference)만큼 돌린다.
//
//  [사용] 시퀀스 스크립트는 ArmIK만 부른다. 에디터 도구(SetRobotReadyPose 등)는 ArmIK.Model 또는 new ArmKinematics()로 직접 쓴다.
//  [!] 각도는 도(°), 길이는 m. Solve() 안의 자코비안 계산만 라디안을 쓴다.
//  [!] 자세 조건 · 겹침 검사 함수는 안에서 Forward()를 먼저 부른다 (Forward()가 채운 링크 행렬을 쓴다).
//
//  [설계서와의 차이]
//      * Solve()에 선택 인자가 3개 있다: allowedAngle(허용 각도 오차, default: 3°), acceptable(관절각 조건), allowedPosition(허용 위치 오차, default: 0.005 m).
//      * 설계서에 없는 공개 함수: HasRaisedElbow() · WristAbovePlane() · IsDistalAbove() · IsElbowRaised()(자세 조건), ApplyEditorPose()(에디터 전용).
//      * 설계서는 "다른 스크립트는 ArmIK만 부른다"이다. 에디터 도구는 이 클래스를 직접 쓴다.
//  ============================================================
public sealed class ArmKinematics
{
    //  ============================================================
    //  모델 (만들 때 한 번 읽어 둔 값)
    //  ============================================================
    /// 회전 관절 6개 (루트 쪽부터: shoulder, upperarm, forearm, wrist1, wrist2, wrist3)
    public ArticulationBody[] Joints { get; }

    readonly Transform parent;                              //  로봇 루트의 부모. 계산은 이 좌표계에서 시작한다
    readonly Node[] nodes;                                  //  루트 → tcp 사이 링크 (0번이 루트, 마지막이 tcp)
    readonly float[] reference;                             //  만들 때의 관절각 6개 (도). nodes의 회전은 이 각도일 때의 값이다
    readonly float[] lower;                                 //  관절 하한 (도). 제한 없는 관절은 −360
    readonly float[] upper;                                 //  관절 상한 (도). 제한 없는 관절은 +360
    readonly Part[] parts;                                  //  팔 링크의 Collider (겹침 · 높이 검사용)
    readonly Matrix4x4[] world;                             //  링크별 월드 행렬 (nodes와 같은 순서). Forward()가 채운다
    static BoxCollider clearanceProbe;                      //  IsBoxClear()용 숨은 BoxCollider. 처음 쓸 때 한 번 만든다

    // 팔 링크 Collider 하나.
    struct Part
    {
        public int node;                                    //  붙어 있는 링크 (nodes 번호)
        public Matrix4x4 relative;                          //  링크 기준 Collider 위치 · 회전 · 크기
        public Vector3 center;                              //  Collider를 감싼 상자의 중심 (Collider 로컬)
        public Vector3 size;                                //  Collider를 감싼 상자의 크기 (Collider 로컬)
        public Collider collider;                           //  실제 Collider (정밀 검사용)
    }

    // 링크 하나 (만들 때의 로컬 배치).
    struct Node
    {
        public Vector3 position;                            //  localPosition
        public Vector3 scale;                               //  localScale
        public Vector3 anchor;                              //  관절 회전 중심 (anchorPosition × localScale). 관절이 아니면 0
        public Quaternion rotation;                         //  localRotation (reference 관절각일 때)
        public Quaternion anchorRotation;                   //  관절 축 방향 (관절은 이 회전의 X축으로 돈다). 관절이 아니면 identity
        public int joint;                                   //  Joints 번호. 관절이 아니면 −1
    }


    //  ============================================================
    //  만들기
    //  ============================================================
    // 로봇 루트에서 tcp까지 링크를 따라가며 관절 · 링크 배치 · Collider를 읽는다.
    /// [호출] ArmIK (Initialize(), 경로 계획마다 새로 만든다), 에디터 도구
    /// [!] tcp가 root 아래에 없거나 회전 관절이 6개가 아니면 예외를 던진다.
    public ArmKinematics(Transform root, Transform tcp)
    {
        //  1) tcp가 로봇 아래에 있는지 확인
        if (tcp == null || !tcp.IsChildOf(root))
        { throw new Exception("로봇 아래의 tcp를 지정하세요."); }

        //  2) 루트 → tcp 링크 목록 (tcp에서 부모로 올라간 뒤 뒤집는다)
        var chain = new List<Transform>();
        for (var link = tcp; link != root; link = link.parent)
        { chain.Add(link); }
        chain.Add(root);
        chain.Reverse();
        parent = root.parent;

        //  3) 회전 관절 6개 (루트 관절 제외)
        Joints = chain.Select(link => link.GetComponent<ArticulationBody>())
            .Where(body => body != null && !body.isRoot && body.jointType == ArticulationJointType.RevoluteJoint).ToArray();
        if (Joints.Length != 6)
        { throw new Exception("6개의 회전 관절이 필요합니다. 발견: " + Joints.Length); }

        //  4) 기준 관절각, 관절 제한 (자유 회전 관절은 ±360)
        reference = CurrentAngles();
        lower = new float[6];
        upper = new float[6];
        nodes = new Node[chain.Count];
        for (int i = 0; i < 6; i++)
        {
            var drive = Joints[i].xDrive;
            lower[i] = Joints[i].twistLock == ArticulationDofLock.FreeMotion ? -360 : drive.lowerLimit;
            upper[i] = Joints[i].twistLock == ArticulationDofLock.FreeMotion ? 360 : drive.upperLimit;
        }

        //  5) 링크별 로컬 배치와 관절 앵커
        for (int i = 0; i < chain.Count; i++)
        {
            var link = chain[i];
            int j = Array.FindIndex(Joints, body => body.transform == link);
            nodes[i] = new Node
            {
                position = link.localPosition,
                rotation = link.localRotation,
                scale = link.localScale,
                joint = j,
                anchor = j < 0 ? Vector3.zero : Vector3.Scale(Joints[j].anchorPosition, link.localScale),
                anchorRotation = j < 0 ? Quaternion.identity : Joints[j].anchorRotation
            };
        }
        world = new Matrix4x4[nodes.Length];

        //  6) 팔 링크의 Collider (켜져 있고 트리거가 아닌 Box · Mesh Collider만)
        var geometry = new List<Part>();
        foreach (var collider in root.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger) { continue; }

            //  Collider를 감싼 상자 (Collider 로컬). Mesh는 메시 범위 상자. 다른 모양은 건너뛴다
            Bounds bounds;
            if (collider is BoxCollider box)
            { bounds = new Bounds(box.center, box.size); }
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null)
            { bounds = mesh.sharedMesh.bounds; }
            else
            { continue; }

            //  Collider가 붙은 가장 가까운 링크 (tcp 쪽부터 찾는다)
            int index = -1;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (collider.transform == chain[i] || collider.transform.IsChildOf(chain[i]))
                {
                    index = i;
                    break;
                }
            }
            if (index >= 0)
            {
                geometry.Add(new Part
                {
                    node = index,
                    relative = chain[index].worldToLocalMatrix * collider.transform.localToWorldMatrix,
                    center = bounds.center,
                    size = bounds.size,
                    collider = collider
                });
            }
        }
        parts = geometry.ToArray();
    }


    //  ============================================================
    //  관절각
    //  ============================================================
    // 지금 관절각 6개 (도).
    /// Play 중에는 물리 관절의 실제 각도(jointPosition), Play 전에는 링크 회전에서 계산한 각도(−180 ~ 180).
    public float[] CurrentAngles()
    {
        var result = new float[Joints.Length];
        for (int i = 0; i < result.Length; i++)
        {
            var body = Joints[i];
            if (Application.isPlaying && body.dofCount > 0)
                { result[i] = body.jointPosition[0] * Mathf.Rad2Deg; }
            else
            {
                //  부모 앵커 기준 링크 회전 → 관절 X축 둘레 각도
                var relative = Quaternion.Inverse(body.parentAnchorRotation) * body.transform.localRotation * body.anchorRotation;
                result[i] = Mathf.DeltaAngle(0, 2 * Mathf.Atan2(relative.x, relative.w) * Mathf.Rad2Deg);
            }
        }
        return result;
    }

    // (에디터 전용) 관절각 6개대로 링크를 돌려 놓고, 관절 구동 목표도 그 각도로 맞춘다.
    /// [호출] 에디터 도구 (SetRobotReadyPose, ArrangeParcelIKStation, SuctionLoadingSetup)
    /// [!] Play 중에 부르면 예외를 던진다. Play 중에는 ArmIK가 구동 목표로 움직인다.
    public void ApplyEditorPose(float[] angles)
    {
        if (Application.isPlaying)
            { throw new InvalidOperationException("Editor pose only"); }

        for (int j = 0; j < Joints.Length; j++)
        {
            var body = Joints[j];
            var node = nodes.First(candidate => candidate.joint == j);

            //  앵커 둘레로 (관절각 − reference)만큼 돌리고, 앵커 점이 제자리에 남도록 위치도 옮긴다
            var rotation = node.rotation * node.anchorRotation * Quaternion.AngleAxis(angles[j] - reference[j], Vector3.right) * Quaternion.Inverse(node.anchorRotation);
            body.transform.localRotation = rotation;
            body.transform.localPosition = node.position + node.rotation * node.anchor - rotation * node.anchor;

            var drive = body.xDrive;
            drive.target = angles[j];
            drive.targetVelocity = 0;
            body.xDrive = drive;
        }
    }


    //  ============================================================
    //  순운동학 (관절각 → 손끝)
    //  ============================================================
    // 관절각 6개일 때 tcp의 월드 위치 · 회전을 구한다. 링크별 월드 행렬은 world에 남긴다.
    /// 루트의 부모에서 시작해 링크마다 로컬 행렬을 곱한다. 관절 링크는 앵커 둘레로 (관절각 − reference)만큼 돌린다.
    public void Forward(float[] angles, out Vector3 position, out Quaternion rotation)
    {
        var matrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
        rotation = parent != null ? parent.rotation : Quaternion.identity;
        for (int i = 0; i < nodes.Length; i++)
        {
            var node = nodes[i];
            var localRotation = node.rotation;
            var localPosition = node.position;
            if (node.joint >= 0)
            {
                localRotation = node.rotation * node.anchorRotation * Quaternion.AngleAxis(angles[node.joint] - reference[node.joint], Vector3.right) * Quaternion.Inverse(node.anchorRotation);
                localPosition += node.rotation * node.anchor - localRotation * node.anchor;
            }
            matrix *= Matrix4x4.TRS(localPosition, localRotation, node.scale);
            rotation *= localRotation;
            world[i] = matrix;
        }
        position = matrix.MultiplyPoint3x4(Vector3.zero);
    }


    //  ============================================================
    //  자세 조건 (Solve()의 acceptable로 넘겨 자세를 거른다)
    //  ============================================================
    // 팔꿈치(forearm 관절)가 minimumHeight 이상에 있는지 (월드 y, m).
    /// [호출] ArmIK 경로 계획 (raisedElbowClearance > 0일 때), 에디터 도구
    public bool HasRaisedElbow(float[] angles, float minimumHeight)
    {
        Forward(angles, out _, out _);
        int elbow = Array.FindIndex(nodes, node => node.joint == 2);
        return elbow >= 0 && world[elbow].MultiplyPoint3x4(Vector3.zero).y >= minimumHeight;
    }

    // 손목(wrist1)부터 끝까지 링크 Collider의 아랫면이 모두 height 이상인지 (월드 y, m).
    /// [호출] ArmIK 경로 계획 (minimumWristPlane), 에디터 도구
    /// 아랫면 = 감싼 상자 중심 높이 − 월드로 돌린 상자의 반높이.
    public bool WristAbovePlane(float[] angles, float height)
    {
        Forward(angles, out _, out _);
        int firstWrist = Array.FindIndex(nodes, node => node.joint == 3);
        foreach (var part in parts)
        {
            if (part.node < firstWrist) { continue; }

            var matrix = world[part.node] * part.relative;
            float extentY = (Mathf.Abs(matrix.m10) * part.size.x + Mathf.Abs(matrix.m11) * part.size.y + Mathf.Abs(matrix.m12) * part.size.z) * 0.5f;
            if (matrix.MultiplyPoint3x4(part.center).y - extentY < height) { return false; }
        }
        return true;
    }

    // 손목(wrist1)부터 끝까지 링크가 모두 height 위에 있는지. WristAbovePlane()과 결과가 같다 (계산 방식만 다르다).
    /// [호출] 우리 스크립트는 부르지 않는다 (봉현님 브랜치의 CorrectionSequence가 벨트 충돌 검사에 쓴다).
    public bool IsDistalAbove(float[] angles, float height)
    {
        Forward(angles, out _, out _);
        int wrist = Array.FindIndex(nodes, node => node.joint == 3);
        foreach (var part in parts)
        {
            if (part.node < wrist) { continue; }

            var matrix = world[part.node] * part.relative;
            float radius = Mathf.Abs(matrix.MultiplyVector(Vector3.right * part.size.x * 0.5f).y)
                + Mathf.Abs(matrix.MultiplyVector(Vector3.up * part.size.y * 0.5f).y)
                + Mathf.Abs(matrix.MultiplyVector(Vector3.forward * part.size.z * 0.5f).y);
            if (matrix.MultiplyPoint3x4(part.center).y - radius < height) { return false; }
        }
        return true;
    }

    // 윗팔(어깨 관절 → 팔꿈치 관절)이 서 있는지: 0.20 m 넘게 올라가고, 수직에서 약 49° 안쪽.
    /// [호출] 에디터 도구 SetRobotReadyPose (준비 자세 찾기)
    public bool IsElbowRaised(float[] angles)
    {
        Forward(angles, out _, out _);
        int shoulder = Array.FindIndex(nodes, node => node.joint == 1);
        int elbow = Array.FindIndex(nodes, node => node.joint == 2);
        Vector3 segment = world[elbow].MultiplyPoint3x4(Vector3.zero) - world[shoulder].MultiplyPoint3x4(Vector3.zero);
        return segment.y > 0.20f && Vector3.Dot(segment.normalized, Vector3.up) > 0.65f;
    }


    //  ============================================================
    //  팔 · 상자 겹침
    //  ============================================================
    // 이 관절각에서 팔 링크가 상자(사방으로 margin만큼 키운 크기)와 겹치지 않으면 true. 상자가 없으면 false.
    /// [호출] ArmIK 경로 계획 · FixedUpdate() (상자 보호), 에디터 도구
    /// 2단계로 본다: ① 감싼 상자끼리 분리축 검사(Overlap())로 거르고, ② 겹칠 수 있는 링크만 Physics.ComputePenetration()으로 실제 모양을 본다.
    /// ②는 상자 자리에 둔 숨은 BoxCollider(clearanceProbe)로 한다. 상자 자체 Collider는 바꾸지 않는다.
    public bool IsBoxClear(float[] angles, BoxCollider box, float margin)
    {
        if (box == null) { return false; }

        //  1) 검사용 숨은 BoxCollider (처음 한 번만 만든다. Ignore Raycast 레이어 트리거, 멀리 둔다)
        if (clearanceProbe == null)
        {
            var probeObject = new GameObject("IK_ClearanceQuery_Only") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            probeObject.transform.position = Vector3.one * -10000;
            clearanceProbe = probeObject.AddComponent<BoxCollider>();
            clearanceProbe.isTrigger = true;
        }

        //  2) 검사용 BoxCollider = 상자 크기 + 사방 margin
        var scale = box.transform.lossyScale;
        clearanceProbe.transform.localScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        clearanceProbe.center = box.center;
        clearanceProbe.size = box.size + new Vector3(2 * margin / Mathf.Abs(scale.x), 2 * margin / Mathf.Abs(scale.y), 2 * margin / Mathf.Abs(scale.z));

        //  3) 링크 위치 계산, 상자의 월드 중심 · 단위 축 · 반크기(+ margin)
        Forward(angles, out _, out _);
        var boxMatrix = box.transform.localToWorldMatrix;
        Vector3 boxCenter = boxMatrix.MultiplyPoint3x4(box.center);
        Vector3[] boxAxes = { boxMatrix.MultiplyVector(Vector3.right), boxMatrix.MultiplyVector(Vector3.up), boxMatrix.MultiplyVector(Vector3.forward) };
        Vector3 boxExtents = box.size * 0.5f;
        for (int i = 0; i < 3; i++)
        {
            boxExtents[i] = boxExtents[i] * boxAxes[i].magnitude + margin;
            boxAxes[i].Normalize();
        }

        //  4) 링크마다 ① → ②. 하나라도 겹치면 false
        foreach (var part in parts)
        {
            var matrix = world[part.node] * part.relative;
            Vector3 center = matrix.MultiplyPoint3x4(part.center);
            Vector3[] axes = { matrix.MultiplyVector(Vector3.right), matrix.MultiplyVector(Vector3.up), matrix.MultiplyVector(Vector3.forward) };
            Vector3 extents = part.size * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                extents[i] *= axes[i].magnitude;
                axes[i].Normalize();
            }
            if (Overlap(center, axes, extents, boxCenter, boxAxes, boxExtents) &&
                Physics.ComputePenetration(clearanceProbe, box.transform.position, box.transform.rotation,
                    part.collider, matrix.MultiplyPoint3x4(Vector3.zero), matrix.rotation, out _, out _))
                { return false; }
        }
        return true;
    }

    // 분리축 검사: 두 상자(중심, 단위 축 3개, 반크기)가 겹칠 수 있으면 true.
    /// 축 15개(A의 축 3개, B의 축 3개, 서로의 축 외적 9개) 중 하나라도 두 상자를 가르면 겹치지 않는다.
    /// Collider를 감싼 상자로 보므로 실제보다 넉넉하게 겹친다고 본다. tcp만이 아니라 팔 링크 전체를 본다.
    static bool Overlap(Vector3 centerA, Vector3[] axesA, Vector3 extentsA, Vector3 centerB, Vector3[] axesB, Vector3 extentsB)
    {
        var difference = centerB - centerA;
        for (int i = 0; i < 15; i++)
        {
            //  평행한 두 축의 외적(길이 0)은 건너뛴다
            Vector3 axis = i < 3 ? axesA[i] : i < 6 ? axesB[i - 3] : Vector3.Cross(axesA[(i - 6) / 3], axesB[(i - 6) % 3]);
            if (axis.sqrMagnitude < 1e-10f) { continue; }

            //  이 축 위의 두 상자 그림자 반길이 합보다 중심 거리가 멀면 갈라진다
            float radius = 0;
            for (int j = 0; j < 3; j++)
                { radius += extentsA[j] * Mathf.Abs(Vector3.Dot(axesA[j], axis)) + extentsB[j] * Mathf.Abs(Vector3.Dot(axesB[j], axis)); }
            if (Mathf.Abs(Vector3.Dot(difference, axis)) > radius + 0.000001f) { return false; }
        }
        return true;
    }


    //  ============================================================
    //  역운동학 (목표 → 관절각)
    //  ============================================================
    // 목표 위치 · 회전에 맞는 관절각(best)을 찾는다. 허용 오차 안이고 acceptable 조건도 맞으면 true.
    /// [호출] ArmIK 경로 계획 (MoveTo(), MoveThrough(), MoveToJointPath() 등), 에디터 도구
    /// seed(보통 지금 관절각)에서 시작해 반복으로 오차를 줄인다 (감쇠 최소제곱. 자코비안은 관절을 0.25°씩 움직여 잰다).
    /// 시작점은 9개: seed, 그다음 seed를 흔든 자세 (1 ~ 3번째 ±35°, 4 ~ 8번째 ±95°). 시작점마다 최대 260번 반복한다.
    /// best = 모든 시작점 중 오차가 가장 작고 acceptable을 만족한 관절각. 실패해도 best와 오차는 채운다.
    /// allowedPosition은 0.0005 ~ 0.02 m로 자른다.
    public bool Solve(Vector3 target, Quaternion targetRotation, 
                      float[] seed, out float[] best, out float positionError, out float angleError,
                      float allowedAngle = 3f, Func<float[], bool> acceptable = null, float allowedPosition = 0.005f)
    {
        //  허용 오차와 수렴 기준
        float requiredPosition = Mathf.Clamp(allowedPosition, 0.0005f, 0.02f);
        float convergencePosition = Mathf.Min(0.003f, requiredPosition * 0.6f);
        best = (float[])seed.Clone();
        float bestCost = float.PositiveInfinity;
        const float weight = 0.25f;                         //  회전 오차 무게. 1 rad를 0.25 m로 친다 (1° ≈ 4.4 mm)
        const float epsilon = 0.25f;                        //  자코비안을 잴 때 관절을 움직이는 양 (도)

        for (int attempt = 0; attempt < 9; attempt++)
        {
            //  1) 시작점: 첫 시도는 seed, 그다음은 seed를 정해진 양만큼 흔든다 (관절 제한 안)
            var angles = (float[])seed.Clone();
            if (attempt > 0)
                for (int j = 0; j < 6; j++)
                    { angles[j] = Mathf.Clamp(angles[j] + Mathf.Sin(attempt * 5.37f + j * 2.41f) * (attempt < 4 ? 35 : 95), lower[j], upper[j]); }

            for (int iteration = 0; iteration < 260; iteration++)
            {
                //  2) 지금 오차. 지금까지 중 가장 작고 조건도 맞으면 best로 남긴다
                Forward(angles, out var tipPosition, out var tipRotation);
                Vector3 positionDelta = target - tipPosition;
                Vector3 rotationDelta = RotationError(targetRotation, tipRotation);
                float cost = positionDelta.sqrMagnitude + weight * weight * rotationDelta.sqrMagnitude;
                if (cost < bestCost && (acceptable == null || acceptable(angles)))
                {
                    bestCost = cost;
                    Array.Copy(angles, best, 6);
                }

                //  3) 충분히 가까우면 이 시작점은 끝
                if (positionDelta.magnitude < convergencePosition && rotationDelta.magnitude < Mathf.Min(2f, allowedAngle * 0.65f) * Mathf.Deg2Rad) 
                    { break; }

                //  4) 자코비안: 관절 하나씩 epsilon만큼 움직였을 때 손끝 위치 · 회전이 변하는 양 (라디안당)
                var jacobian = new float[6, 6];
                for (int j = 0; j < 6; j++)
                {
                    angles[j] += epsilon;
                    Forward(angles, out var nudgedPosition, out var nudgedRotation);
                    angles[j] -= epsilon;
                    var positionColumn = (nudgedPosition - tipPosition) / (epsilon * Mathf.Deg2Rad);
                    var rotationColumn = RotationError(nudgedRotation, tipRotation) * weight / (epsilon * Mathf.Deg2Rad);
                    jacobian[0, j] = positionColumn.x;
                    jacobian[1, j] = positionColumn.y;
                    jacobian[2, j] = positionColumn.z;
                    jacobian[3, j] = rotationColumn.x;
                    jacobian[4, j] = rotationColumn.y;
                    jacobian[5, j] = rotationColumn.z;
                }

                //  5) 정규방정식 (JᵀJ + 감쇠) Δ = Jᵀe. 7번째 열이 오른쪽 항이다
                float[] error = { positionDelta.x, positionDelta.y, positionDelta.z, rotationDelta.x * weight, rotationDelta.y * weight, rotationDelta.z * weight };
                var normal = new float[6, 7];
                for (int row = 0; row < 6; row++)
                {
                    for (int col = 0; col < 6; col++)
                        for (int k = 0; k < 6; k++) { normal[row, col] += jacobian[k, row] * jacobian[k, col]; }
                    normal[row, row] += 0.000025f;
                    for (int k = 0; k < 6; k++)
                        { normal[row, 6] += jacobian[k, row] * error[k]; }
                }

                //  6) 풀어서 관절 변화량 Δ(라디안)를 얻는다. 풀 수 없으면 이 시작점은 끝
                if (!Eliminate(normal)) { break; }

                //  7) 한 번에 관절 하나가 7°를 넘게 움직이지 않도록 줄인다
                float largest = 0;
                for (int j = 0; j < 6; j++)
                    { largest = Mathf.Max(largest, Mathf.Abs(normal[j, 6] * Mathf.Rad2Deg)); }
                float stepScale = largest > 7 ? 7 / largest : 1;

                //  8) 관절각 갱신. 360° 차이 나는 같은 각도 중 seed에 가까운 쪽을 쓴다 (관절 제한 안일 때만)
                for (int j = 0; j < 6; j++)
                {
                    float value = angles[j] + normal[j, 6] * Mathf.Rad2Deg * stepScale;
                    float wrapped = value + 360f * Mathf.Round((seed[j] - value) / 360f);
                    angles[j] = Mathf.Clamp(wrapped >= lower[j] && wrapped <= upper[j] ? wrapped : value, lower[j], upper[j]);
                }
            }

            //  9) best가 허용 오차 안이면 다른 시작점은 해 보지 않는다
            Forward(best, out var bestPosition, out var bestRotation);
            if (Vector3.Distance(bestPosition, target) < requiredPosition && Quaternion.Angle(bestRotation, targetRotation) < allowedAngle) 
                { break; }
        }

        //  결과: best의 오차. 허용 오차 안이고 조건도 맞으면 성공
        Forward(best, out var finalPosition, out var finalRotation);
        positionError = Vector3.Distance(finalPosition, target);
        angleError = Quaternion.Angle(finalRotation, targetRotation);
        return positionError < requiredPosition && angleError < allowedAngle && (acceptable == null || acceptable(best));
    }

    // 회전 차이(actual → target)를 회전 벡터로 바꾼다: 방향 = 회전축, 길이 = 각도(라디안). 짧은 쪽으로 돈다.
    /// 축을 구할 수 없으면(차이가 거의 0) Vector3.zero.
    static Vector3 RotationError(Quaternion target, Quaternion actual)
    {
        var delta = target * Quaternion.Inverse(actual);
        if (delta.w < 0)
            { delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w); }
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        return axis.sqrMagnitude < 0.1f || float.IsNaN(axis.x) ? Vector3.zero : axis * (Mathf.DeltaAngle(0, angle) * Mathf.Deg2Rad);
    }

    // 6 × 7 확대 행렬을 가우스-조르단 소거(부분 피벗)로 푼다. 답은 7번째 열에 남는다. 풀 수 없으면 false.
    /// [호출] Solve() 5) → 6)
    static bool Eliminate(float[,] matrix)
    {
        for (int k = 0; k < 6; k++)
        {
            //  k열에서 절댓값이 가장 큰 행을 피벗으로 고른다
            int pivot = k;
            for (int i = k + 1; i < 6; i++)
                if (Mathf.Abs(matrix[i, k]) > Mathf.Abs(matrix[pivot, k])) { pivot = i; }
            if (Mathf.Abs(matrix[pivot, k]) < 1e-10f) { return false; }

            //  피벗 행과 k행을 바꾸고, k행을 피벗 값으로 나눈다
            for (int j = k; j < 7; j++)
            {
                float swap = matrix[k, j];
                matrix[k, j] = matrix[pivot, j];
                matrix[pivot, j] = swap;
            }
            float divisor = matrix[k, k];
            for (int j = k; j < 7; j++)
                { matrix[k, j] /= divisor; }

            //  다른 행의 k열을 0으로 만든다
            for (int i = 0; i < 6; i++)
            {
                if (i != k)
                {
                    float factor = matrix[i, k];
                    for (int j = k; j < 7; j++)
                        { matrix[i, j] -= factor * matrix[k, j]; }
                }
            }
        }
        return true;
    }
}