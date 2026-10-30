using TMPro;            //  TextMeshPro (Unity 6에서는 com.unity.ugui 패키지에 포함, 따로 설치 필요 없음)
using UnityEngine;

//  ============================================================
//  BoxInfo.cs
//  상자 1개의 정보와 처리 기록.
//      - 상자 Prefab의 루트(BoxCollider가 있는 오브젝트)에 붙인다.
//
//  공정 스크립트가 "같은 상자"를 보며 판단하고, 결과를 받는 즉시 이 컴포넌트에 기록한다.
//  다른 스크립트는 box의 값만 읽는다. → 벨트 위에 상자가 여러 개 있어도 결과가 섞이지 않는다.
//
//  [기록 순서] BoxFeeder(투입) → FaceCameraRig(측정) → 기본 제어 또는 CorrectionAgent(회전)
//             → LabelReaderAgent(판독) → LabelValidator(검증) → 푸셔(분류) → 끝 로봇(적재) → CellController.Finish(최종 상태)
//  [확인 방법] Play 중 Hierarchy에서 상자를 선택하면 Inspector에서 값이 바뀌는 것을 볼 수 있다.
//  [송장 글자] numberText · regionText는 설계서대로 TextMeshPro(TMP)이다.
//             [!] 원본 상자 Prefab(ParcelBox, 봉현님)의 글자는 TextMesh라서 그대로 두고,
//             [!] 공정용 Variant에서 TextMesh 글자를 끄고 TMP 글자를 새로 둔다
                    /////   : TODO :
//             (4단계(LabelLayout) 확인이 끝나면 아래 줄 지우기)
//             그 전까지 두 칸이 비어 있어도 1 ~ 3단계는 동작한다.
//  ============================================================
[DisallowMultipleComponent]
public class BoxInfo : MonoBehaviour
{
    //  ---------------- 투입 정보 (BoxFeeder가 기록) ----------------
    [Header("투입 정보")]
    [Tooltip("상자 ID(BoxFeeder가 기록). \"BOX-0001\" 형식, 세션 안에서 유일")]
    public string boxId;

    [Tooltip("생성 시각 (Time.time, 초)")]
    public float createdTime;

    [Tooltip("투입할 때 배정한 송장면 방향. 배정 전에는 None")]
    public BoxFace initialFace = BoxFace.None;

    [Tooltip("송장을 붙였는지. false면 송장 미부착 상자")]
    public bool hasLabel = true;

    [Tooltip("[정답] 송장 번호 5자리. Heuristic과 학습 보상에만 쓰고, 판독에는 쓰지 않는다")]
    public string labelNumber;

    [Tooltip("[정답] 주소 지역 이름. 쓰임은 labelNumber와 같다")]
    public string labelRegion;

    //  ---------------- 송장 오브젝트 (Prefab에서 연결) ----------------
    //  컴포넌트를 처음 붙이면 Reset()이 찾아 자동 연결한다. TMP 글자는 4단계에서 추가한 뒤 Reset을 다시 누른다.
    [Header("송장 오브젝트 (자동 연결)")]
    [Tooltip("송장 루트 (ShippingLabel). 이 오브젝트의 위쪽(+Y)이 송장이 향한 방향이다")]
    public Transform label;

    [Tooltip("송장 번호 글자 (TextMeshPro). LabelLayout.Print가 숫자를 칸 위치대로 찍는다")]
    public TextMeshPro numberText;

    [Tooltip("주소 지역 글자 (TextMeshPro). LabelLayout.Print가 지역명 칸에 찍는다")]
    public TextMeshPro regionText;

    //  ---------------- 진행 상태 ----------------
    [Header("진행 상태")]
    [Tooltip("현재 상태. 최종 상태(Loaded, Exception)는 CellController.Finish만 바꾼다")]
    public BoxState state = BoxState.Moving;

    [Tooltip("이 상자를 처리한 교정 제어 방식")]
    public ControlMode mode = ControlMode.Basic;

    //  ---------------- 교정 스테이션 ----------------           (FR-03, FR-05, FR-06) 
    [Header("교정 스테이션")]
    [Tooltip("카메라 5대의 송장 색 비율 (0 위, 1 앞, 2 뒤, 3 왼쪽, 4 오른쪽). FaceCameraRig.Measure가 기록")]
    public float[] faceRatios = new float[5];

    [Tooltip("규칙 판정: 비율이 가장 큰 면. 모두 기준 미만이면 Bottom, 판정 전에는 None (기본 제어용)")]
    public BoxFace judgedFace = BoxFace.None;

    [Tooltip("교정 스테이션에서 회전한 횟수")]
    public int rotationCount;

    [Tooltip("마지막으로 고른 회전 (오일러 각, 도). 기본 제어 또는 CorrectionAgent가 기록")]
    public Vector3 chosenRotation;

    [Tooltip("180° 뒤집기를 이미 했는지")]
    public bool flipped;

    //  ---------------- 판독 스테이션 ----------------           (FR-04, FR-07) 
    [Header("판독 스테이션")]
    [Tooltip("윗면에서 송장을 찾았는지")]
    public bool labelFound;

    [Tooltip("판독한 번호 5자리. 모름 칸은 ?로 남는다")]
    public string readText;

    [Tooltip("판독한 주소 지역 이름")]
    public string readRegion;

    [Tooltip("모름으로 답한 칸 수 (0이면 6칸 모두 읽음)")]
    public int unknownCount;

    [Tooltip("판독 성공 여부")]
    public bool readOk;

    [Tooltip("판독 재시도 횟수")]
    public int retryCount;

    [Tooltip("번호 규칙 검증 결과. 검증 전에는 의미가 없으므로 regionCode로 확정 여부를 본다")]
    public LabelCheck check = LabelCheck.OK;

    [Tooltip("확정 지역 코드. 확정 전에는 -1")]
    public int regionCode = -1;

    //  ---------------- 결과 ----------------            (FR-08, FR-09) 
    [Header("결과")]
    [Tooltip("목적지 A·B·C·NG. 판독 스테이션이 기록하고 푸셔가 읽는다")]
    public Destination destination = Destination.None;

    [Tooltip("예외 사유. None이 아니면 목적지는 NG")]
    public ExceptionReason reason = ExceptionReason.None;

    //  ---------------- 시각 (대기 · 사이클 시간 계산용) ----------------
    [Header("시각 (Time.time, 초)")]
    [Tooltip("교정 스테이션 정지 위치에 도착한 시각")]
    public float correctArriveTime;

    [Tooltip("판독 스테이션 정지 위치에 도착한 시각")]
    public float readArriveTime;

    [Tooltip("터미널 컨테이너나 NG 토트에 들어간 시각")]
    public float finishTime;


    //  ============================================================
    //  계산 속성
    //  ============================================================
    /// 송장이 "지금" 향한 면. label의 위쪽(+Y)을 6개 면 방향과 비교해 가장 가까운 면을 고른다.
    /// [용도] CorrectionAgent 학습 보상, 테스트 확인. 공정의 판단은 카메라 측정값(faceRatios)으로 한다.
    /// [주의] 송장 오브젝트가 연결되지 않았거나 송장 미부착 상자(label == null || !hasLabel)면 None을 돌려준다.
    public BoxFace LabelFaceNow
    {
        get
        {
            if (label == null || !hasLabel) { return BoxFace.None; }

            Vector3 normal = label.up;          //  송장 종이가 바라보는 방향 (월드 좌표)
            BoxFace best = BoxFace.Top;
            float bestDot = -2f;                //  내적은 -1 ~ 1이므로 -2에서 시작

            for (int i = 0; i < 6; i++)
            {
                BoxFace face = (BoxFace)i;
                float dot = Vector3.Dot(normal, FaceDirection(face));   //  1에 가까울수록 같은 방향
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = face;
                }
            }
            return best;
        }
    }

    /// 최종 상태(적재 또는 예외)가 정해졌는지
    public bool IsFinished => state == BoxState.Loaded || state == BoxState.Exception;

    /// 예외 표시가 되어 있는지 (목적지가 NG로 바뀐 상자)
    public bool IsMarked => reason != ExceptionReason.None;


    //  ============================================================
    //  함수
    //  ============================================================
    // 예외 표시. 처음 들어온 사유만 남기고, 목적지를 NG로 바꾼다.
    /// [호출] 판독 실패, 규칙 위반, 교정 실패 등을 발견한 스크립트
    /// [주의] 최종 상태(Exception)는 여기서 바꾸지 않는다. NG 토트에 들어갈 때 CellController.Finish()가 확정.
    public void Mark(ExceptionReason why)
    {
        if (why == ExceptionReason.None) { return; }

        if (reason == ExceptionReason.None) { reason = why; }   //  두 번째 사유부터는 무시한다
        destination = Destination.NG;
    }

    // 면 → 월드 방향.
    /// 투입 방향(RotationFor)과 송장 방향 계산(LabelFaceNow)이 이 표를 함께 쓴다.
    /// 교정 카메라 5대도 이 표에 맞춰 배치한다 (예: 앞 카메라는 −Z 쪽에서 상자를 본다).
    /// [설계서에 없는 도우미 함수] 방향 표를 한 곳에 두려고 추가.
    public static Vector3 FaceDirection(BoxFace face)
    {
        switch (face)
        {
            case BoxFace.Top: { return Vector3.up; }        //  +Y
            case BoxFace.Front: { return Vector3.back; }      //  −Z
            case BoxFace.Back: { return Vector3.forward; }   //  +Z
            case BoxFace.Left: { return Vector3.left; }      //  −X
            case BoxFace.Right: { return Vector3.right; }     //  +X
            case BoxFace.Bottom: { return Vector3.down; }       // -Y
            default: { return Vector3.zero; }      //  None: 방향 없음
        }
    }

    // 송장(상자 로컬 +Y 면)이 face 쪽을 향하게 하는 회전 (투입용, BoxFeeder가 사용).
    /// yaw: 위쪽 축 둘레로 먼저 돌릴 각도(도). 같은 면이라도 송장이 놓인 방향을 섞을 때 쓴다.
    /// [전제] 송장이 상자 Prefab의 위쪽(+Y) 면에 붙어 있다 (ParcelBox Prefab의 ShippingLabel).
    public static Quaternion RotationFor(BoxFace face, float yaw = 0f)
    {
        //  방향이 없으면 기본 자세 (송장 위)
        if (face == BoxFace.None) { return Quaternion.Euler(0f, yaw, 0f); }

        //  ① 위쪽 축 둘레로 yaw만큼 돌리고  ② 위쪽(+Y)을 face 방향으로 기울인다
        return Quaternion.FromToRotation(Vector3.up, FaceDirection(face)) * Quaternion.Euler(0f, yaw, 0f);
    }


    //  ============================================================
    //  에디터 편의 (공정에는 영향 없음)
    //  ============================================================
    /// 컴포넌트를 처음 붙이거나 ⋮ → Reset을 누를 때 Unity가 부른다 (에디터 전용).
    /// 송장 루트는 이름(ShippingLabel)으로, TMP 글자는 송장 아래의 TextMeshPro 중 이름에
    /// "Number" · "Region"이 들어간 것으로 찾는다. 못 찾으면 비워 두고, 직접 끌어다 넣어도 된다.
    void Reset()
    {
        label = FindChild(transform, "ShippingLabel");
        if (label == null) { return; }

        ///  꺼진 오브젝트까지 포함해 송장 아래의 TMP 글자를 모두 살펴본다
        foreach (TextMeshPro tmp in label.GetComponentsInChildren<TextMeshPro>(true))
        {
            if (numberText == null && tmp.name.Contains("Number")) { numberText = tmp; }
            if (regionText == null && tmp.name.Contains("Region")) { regionText = tmp; }
        }
    }

    /// root 아래(자기 자신 포함, 꺼진 오브젝트 포함)에서 이름이 같은 첫 오브젝트를 찾는다.
    static Transform FindChild(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }

    // [테스트] BoxInfo ⋮ 메뉴 → "테스트/지금 송장 방향 출력"
    [ContextMenu("테스트/지금 송장 방향 출력")]
    void TestLogFace()
    {
        Debug.Log($"[BoxInfo] {name}: 송장이 향한 면 = {LabelFaceNow}", this);
    }

    // [테스트] BoxInfo ⋮ 메뉴 → "테스트/다음 면으로 돌려 보기"
    /// 상자를 다음 면 방향으로 돌린 뒤, LabelFaceNow가 같은 면을 돌려주는지 확인한다.
    /// Play 중에 하면 씬 파일은 바뀌지 않는다.
    [ContextMenu("테스트/다음 면으로 돌려 보기")]
    void TestNextFace()
    {
        if (LabelFaceNow == BoxFace.None)
        {
            Debug.LogWarning($"[BoxInfo] {name}: 송장이 없어 돌려 볼 수 없습니다.", this);
            return;
        }

        BoxFace next = (BoxFace)(((int)LabelFaceNow + 1) % 6);   //  Top → Front → … → Bottom → Top
        transform.rotation = RotationFor(next);

        BoxFace now = LabelFaceNow;
        string result = now == next ? "OK" : "불일치";
        Debug.Log($"[BoxInfo] {name}: {next}로 돌림 → LabelFaceNow = {now} ({result})", this);
    }
}
