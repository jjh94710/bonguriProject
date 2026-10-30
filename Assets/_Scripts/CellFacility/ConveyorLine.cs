using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//  ============================================================
//  ConveyorLine
//  상자를 입구에서 출구까지 일정한 속도로 옮기는 컨베이어 구간 1개.
//      - 구간마다 하나씩 붙인다 (교정 구간, 판독 구간, 분류 구간, 터미널 A · B · C).
//      - 정지 위치가 있으면 그 자리에 상자를 세우고, Release()를 받으면 다시 보낸다.
//      - 출구에 닿은 상자는 다음 구간(nextLine)으로 넘긴다.
//
//  [공정 단계]
//  투입(CorrectLine entry) --> 교정 스테이션(stopPoint) --> CorrectLine exit = ReadLine entry
//  --> 판독 스테이션(stopPoint) --> ReadLine exit = 분류 구간 entry
//
//  [이송 방식] 가이드처럼 물리를 끈(kinematic) 상자를 스크립트가 물리 스텝마다 옮긴다.
//             흐름 방향은 입구 → 출구로 계산하므로 벨트가 어느 축을 향해도 된다.
//  [대기열]   벨트 위 상자는 들어온 순서대로 줄을 서고, 앞 상자와 gap만큼 띄워 멈춘다.
//  [!] 상자를 세우는 것은 스크립트이다. 스토퍼는 보이는 동작일 뿐 충돌로 막지 않는다.
//  [!] 벨트는 수평이라고 본다 (상자 높이는 입구에서 정한 그대로 간다).
//
                    ///// : TODO : 
//  푸셔(pushers)와 끝 컨테이너(endTote)는 아직 없는 단계
//
//  [설계서와의 차이]
//      * 설계서의 - 칸은 [SerializeField]로 두었다.
//      * cell 칸을 추가했다. 이송 실패를 CellController.RaiseAlarm()으로 알린다.
//      * gap, beltWidth, 벨트 무늬(uvPerMeter, scrollAlongY), 테스트 칸(testPrefab, autoRelease, holdTime, guiPosition)을 추가했다.
//      * 흐름 방향은 입구 → 출구로 계산한다 (가이드는 월드 X축 고정).
//      * 스토퍼는 시작 위치에서 stopperDrop만큼 내려간다 (가이드는 월드 높이 두 값).
//  ============================================================
public class ConveyorLine : MonoBehaviour
{
    //  ---------------- 구간 ----------------    (FR-02)
    [Header("구간")]
    [Tooltip("로그와 테스트 패널에 표시할 이름 (예: 교정 구간, 판독 구간, 터미널 A)")]
    [SerializeField] string lineName = "구간";

    [Tooltip("입구. 벨트 윗면 높이, 벨트 폭 가운데에 둔다")]
    [SerializeField] Transform entry;                               // (spawnPoint)

    [Tooltip("출구. 입구 → 출구 방향이 흐름 방향이다")]
    [SerializeField] Transform exit;                                // exit --> (다음 경유점) nextLine.entry

    [Tooltip("이송 속도 m/s (default: 0.3)")]
    [SerializeField] float speed = 0.3f;

    [Tooltip("줄 선 상자 사이의 간격 m (default: 0.03)")]
    [SerializeField] float gap = 0.03f;

    [Tooltip("벨트 폭 m. 상자 중심이 이 폭 밖으로 나가면 이송 실패로 본다 (default: 0.2)")]
    [SerializeField] float beltWidth = 0.2f;

    [Tooltip("(선택) 출구에서 상자를 넘길 다음 구간. 비우면 출구에 닿은 상자를 지운다")]
    [SerializeField] ConveyorLine nextLine;

    [Tooltip("(선택) 이송 실패를 알릴 CellController. 비우면 Console 경고만 남긴다")]
    [SerializeField] CellController cell;

                        ///// : TODO : 
    //  [BoxPusher · ToteBase 구현 후] pushers (BoxPusher[], 진행 순서대로), endTote (ToteBase) 칸을 연다.

    //  ---------------- 정지 위치 ----------------    (FR-02, FR-16)
    [Header("정지 위치 (선택, 비우면 멈추지 않음)")]
    [Tooltip("상자 중심이 멈출 위치 (교정 · 판독 스테이션)")]
    [SerializeField] Transform stopPoint;

    [Tooltip("(선택) 정지 위치 바로 앞의 스토퍼. 보낼 때 내려갔다가, 상자가 지나가면 올라온다")]
    [SerializeField] Transform stopper;

    [Tooltip("스토퍼를 내리는 거리 m. 스토퍼 윗면이 벨트 아래로 내려갈 만큼 (default: 0.05)")]
    [SerializeField] float stopperDrop = 0.05f;

    [Tooltip("스토퍼를 올리고 내리는 시간(초) (default: 0.2)")]
    [SerializeField] float stopperMoveTime = 0.2f;

    [Tooltip("정지 오차 허용값 m. 넘으면 경고 로그 (default: 0.05)")]
    [SerializeField] float stopTolerance = 0.05f;

    //  ---------------- 무작위 오차 ----------------
    //  학습 범위 안에서만 올린다. 평소 운전은 0으로 둔다.
    [Header("무작위 오차 (평소 0)")]
    [Tooltip("투입할 때 벨트 폭 방향으로 ± 이 거리 안에서 무작위 m")]
    [SerializeField] float lateralRange = 0f;

    [Tooltip("정지 위치 오차: 흐름 방향 ± m")]
    [SerializeField] float stopErrorX = 0f;

    [Tooltip("정지 위치 오차: 폭 방향 ± m. 투입할 때 lateralRange에 더한다")]
    [SerializeField] float stopErrorZ = 0f;

    //  ---------------- 벨트 무늬 ----------------
    [Header("벨트 무늬 (상자가 움직일 때만 흐름)")]
    [Tooltip("이 구간의 벨트 표면 렌더러")]
    [SerializeField] Renderer[] beltRenderers;

    [Tooltip("벨트 1 m당 텍스처 좌표 변화량. 무늬가 반대로 흐르면 음수로 (default: 2.74)")]
    [SerializeField] float uvPerMeter = 2.74f;

    [Tooltip("켜면 텍스처를 Y 방향으로 흘린다. 무늬가 옆으로 흐르면 바꾼다")]
    [SerializeField] bool scrollAlongY = false;

    //  ---------------- 테스트 ----------------
    [Header("테스트")]
    [Tooltip("테스트로 투입할 상자 Prefab (ParcelBox_Process)")]
    [SerializeField] BoxInfo testPrefab;

    [Tooltip("켜면 정지 위치에 도착한 상자를 Hold Time 뒤에 자동으로 보낸다 (스테이션 흉내)")]
    [SerializeField] bool autoRelease = false;

    [Tooltip("자동으로 보내기 전에 기다리는 시간(초) (default: 2)")]
    [SerializeField] float holdTime = 2f;

    [Tooltip("켜면 화면에 테스트 패널을 그린다")]
    [SerializeField] bool showTestButtons = false;

    [Tooltip("테스트 패널 위치 (x, y). 구간마다 겹치지 않게 바꾼다")]
    [SerializeField] Vector2 guiPosition = new Vector2(10, 10);

    //  ============================================================
    //  상태 (다른 스크립트가 읽는 값)
    //  ============================================================
    /// 정지 위치에 멈춰 있는 상자 (없으면 null)
    public BoxInfo CurrentBox { get; private set; }

    /// 정지 위치에 상자가 있는지
    public bool HasBoxAtStop => CurrentBox != null;

    /// 구간 위 상자 수    (FR-13)
    public int QueueCount => boxes.Count;

    /// 마지막 정지 오차 m (수평 거리)
    public float LastStopError { get; private set; }

    /// 입구가 비어 새 상자를 받을 수 있는지.
    /// 마지막 상자가 입구에서 "상자 1개 길이 + 간격"만큼 나갔으면 받는다 (상자 크기는 모두 같다고 본다).
    public bool CanAccept
    {
        get
        {
            if (entry == null || exit == null) { return false; }
            if (boxes.Count == 0) { return true; }

            BoxInfo last = boxes[boxes.Count - 1];
            if (last == null) { return true; }

            return AlongBelt(PositionOf(last)) - HalfSize(last, Flow) * 2f >= gap;
        }
    }

    readonly List<BoxInfo> boxes = new List<BoxInfo>();     //  구간 위 상자 (들어온 순서, 0번이 가장 앞)
    BoxInfo releasedBox;                                    //  Release()로 정지 위치를 떠나는 중인 상자
    float arriveTime;                                       //  CurrentBox가 도착한 시각 (자동 보내기용)

    bool stopperUp = true;
    bool stopperMoving;
    Vector3 stopperUpPosition;                              //  시작할 때 스토퍼 위치 = 올린 위치

    readonly List<Material> beltMaterials = new List<Material>();
    float beltOffset;                                       //  지금 무늬 위치 (0 ~ 1)
    float lastScrollTime = -1f;                             //  같은 물리 스텝에 상자 여러 개가 움직여도 무늬는 한 번만 흐르게

    int testCount;                                          //  테스트 상자 번호

    /// 흐름 방향 (입구 → 출구, 수평 단위 벡터)
    Vector3 Flow
    {
        get
        {
            Vector3 flow = exit.position - entry.position;
            flow.y = 0f;
            return flow.normalized;
        }
    }

    /// 입구에서 출구까지 흐름 방향 거리 m
    float BeltLength => AlongBelt(exit.position);


    //  ============================================================
    //  시작
    //  ============================================================
    // 스토퍼의 올린 위치를 기억하고, 무늬를 흘릴 벨트 재질을 모은다.
    void Start()
    {
        if (entry == null || exit == null)
            { Debug.LogWarning($"[Conveyor] {lineName}: Entry / Exit가 비어 있습니다.", this); }
        
        if (stopper != null) { stopperUpPosition = stopper.position; }

        if (beltRenderers != null)
            foreach (Renderer beltRenderer in beltRenderers)
                if (beltRenderer != null) { beltMaterials.Add(beltRenderer.material); }    //  .material = 이 렌더러 전용 복사본
    }

    // 자동 보내기 (테스트용). 정지한 지 holdTime이 지났고 다음 구간이 받을 수 있으면 보낸다.
    /// 공정에서는 CellController가 스테이션 작업을 끝낸 뒤 Release()를 부른다.
    void Update()
    {
        if (!autoRelease || CurrentBox == null) { return; }
        if (Time.time - arriveTime < holdTime) { return; }
        if (nextLine != null && !nextLine.CanAccept) { return; }

        Release();
    }


    //  ============================================================
    //  투입 · 보내기 · 빼기 (다른 스크립트가 부르는 함수)
    //  ============================================================
    // 입구에 상자를 올리고 이송을 시작한다. 입구가 차 있으면 false.
    /// [호출] BoxFeeder(투입), 앞 구간(출구에서 넘길 때), 테스트 메뉴
    /// 상자의 방향은 그대로 두고, 위치만 입구로 옮긴다 (상자 바닥이 벨트 윗면에 닿게).
    public bool Feed(BoxInfo box)
    {
        if (box == null || !CanAccept) { return false; }

        //  1) 입구에 놓기: 벨트 가운데 + 폭 방향 무작위, 높이는 벨트 윗면 + 상자 높이의 절반
        Vector3 across = Vector3.Cross(Vector3.up, Flow);       //  흐름 방향의 오른쪽 (벨트 폭 방향)
        float lateral = Random.Range(-lateralRange, lateralRange) + Random.Range(-stopErrorZ, stopErrorZ);
        Vector3 position = entry.position + across * lateral + Vector3.up * HalfSize(box, Vector3.up);

        //  2) 물리를 끄고 스크립트로 옮긴다
        Rigidbody body = box.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;    //  움직이는 동안 충돌을 놓치지 않게
            body.position = position;
        }
        box.transform.position = position;

        //  3) 줄 맨 뒤에 세우고 이송 시작
        boxes.Add(box);
        StartCoroutine(CarryRoutine(box));
        return true;
    }

    // 정지 위치의 상자를 출구 쪽으로 보낸다.
    /// [호출] CellController (스테이션 작업이 끝났을 때), 자동 보내기, 테스트 메뉴
    /// 스토퍼를 내리고, 상자가 스토퍼를 지나가면 CarryRoutine()이 다시 올린다.
    public void Release()
    {
        if (CurrentBox == null)
        {
            Debug.LogWarning($"[Conveyor] {lineName}: 보낼 상자가 없습니다.", this);
            return;
        }

        releasedBox = CurrentBox;
        CurrentBox = null;
        if (stopper != null) { StartCoroutine(MoveStopper(false)); }
    }

    // 구간 목록에서 상자를 뺀다. 이 구간은 그 상자를 더 옮기지 않는다.
    /// [호출] 푸셔(밀어낼 때), 로봇이 상자를 다른 곳으로 옮길 때
    /// [!] 교정 로봇이 상자를 들었다가 같은 자리에 내려놓을 때는 부르지 않는다 (정지한 상자는 구간이 움직이지 않는다).
    public void Take(BoxInfo box)
    {
        if (box == null) { return; }

        boxes.Remove(box);
        if (CurrentBox == box) { CurrentBox = null; }
        if (releasedBox == box)
        {
            releasedBox = null;
            if (stopper != null) { StartCoroutine(MoveStopper(true)); }
        }
    }


    //  ============================================================
    //  이송
    //  ============================================================
    // 상자 1개를 출구까지 옮긴다 (상자마다 하나씩 돈다).
    /// 출구 · 정지 위치 · 앞 상자 뒤 중 가장 가까운 곳까지 일정한 속도로 가고, 막히면 그 자리에서 기다린다.
    /// distance = 입구에서 흐름 방향으로 온 거리 m
    IEnumerator CarryRoutine(BoxInfo box)
    {
        float stopAt = stopPoint != null ? AlongBelt(stopPoint.position) + Random.Range(-stopErrorX, stopErrorX) : 0f;
        bool passedStop = stopPoint == null;        //  정지 위치가 없으면 처음부터 "지난 것"으로 본다

        while (true)
        {
            yield return new WaitForFixedUpdate();      //  물리 스텝마다 한 칸씩

            //  1) 지워졌거나 Take()로 빠졌으면 끝낸다
            if (box == null)
            {
                boxes.RemoveAll(item => item == null);
                yield break;
            }
            if (!boxes.Contains(box)) { yield break; }

            //  2) 정지 위치에 서 있는 상자는 움직이지 않는다 (스테이션 작업 중)
            if (box == CurrentBox) { continue; }
            if (box == releasedBox) { passedStop = true; }

            //  3) 벨트 밖으로 벗어났으면 이송 실패
            if (CheckOffBelt(box)) { yield break; }

            //  4) 이번 스텝에 갈 수 있는 곳 = 출구, 정지 위치, 앞 상자 뒤 중 가장 가까운 곳
            float distance = AlongBelt(PositionOf(box));
            float limit = BeltLength;
            if (!passedStop) { limit = Mathf.Min(limit, stopAt); }

            int index = boxes.IndexOf(box);
            if (index > 0 && boxes[index - 1] != null)
            {
                BoxInfo ahead = boxes[index - 1];
                limit = Mathf.Min(limit, AlongBelt(PositionOf(ahead)) - HalfSize(ahead, Flow) - HalfSize(box, Flow) - gap);
            }

                                ///// : TODO : 
            //  [BoxPusher 구현 후] 분기 지점에 온 상자는 pusher.TryPush(this, box). 밀면 Take()로 빠지므로 여기서 끝난다.

            //  5) 일정한 속도로 한 칸 옮긴다 (뒤로는 가지 않는다)
            float next = Mathf.Min(distance + speed * Time.fixedDeltaTime, limit);
            if (next > distance)
            {
                MoveBox(box, Flow * (next - distance));
                ScrollBelt(next - distance);
                distance = next;
            }

            //  6) 정지 위치에 도착
            if (!passedStop && distance >= stopAt - 0.0005f)
            {
                Arrive(box);
                continue;
            }

            //  7) 보낸 상자의 뒷면이 스토퍼를 1 cm 넘게 지나면 스토퍼를 다시 올린다
            if (box == releasedBox)
            {
                float stopperAt = stopper != null ? AlongBelt(stopper.position) : stopAt;
                if (distance - HalfSize(box, Flow) > stopperAt + 0.01f)
                {
                    releasedBox = null;
                    if (stopper != null) { StartCoroutine(MoveStopper(true)); }
                }
            }

            //  8) 출구 도착: 다음 구간으로 넘긴다. 다음 구간이 없으면 지우고, 입구가 차 있으면 출구에서 기다린다
                                ///// : TODO : 
            //  [ToteBase 구현 후] endTote가 있으면 endTote.Add(box) (터미널 벨트 끝의 컨테이너)
            if (distance >= BeltLength - 0.0005f)
            {
                if (nextLine == null)
                {
                    boxes.Remove(box);
                    Debug.Log($"[Conveyor] {lineName}: {box.boxId} 출구 도착 (다음 구간 없음, 지움)", this);
                    Destroy(box.gameObject);
                    yield break;
                }
                if (nextLine.CanAccept)
                {
                    boxes.Remove(box);
                    nextLine.Feed(box);
                    yield break;
                }
            }
        }
    }

    // 정지 위치에 도착한 상자를 등록하고 정지 오차를 잰다.
    void Arrive(BoxInfo box)
    {
        CurrentBox = box;
        arriveTime = Time.time;

        Vector3 error = PositionOf(box) - stopPoint.position;
        error.y = 0f;                                   //  높이 차이는 빼고 수평 거리만 잰다
        LastStopError = error.magnitude;

        if (LastStopError > stopTolerance)
            { Debug.LogWarning($"[Conveyor] {lineName}: {box.boxId} 정지 오차 {LastStopError * 1000f:F0}mm (허용 {stopTolerance * 1000f:F0}mm 초과)", this); }
        else
            { Debug.Log($"[Conveyor] {lineName}: {box.boxId} 정지 위치 도착 (오차 {LastStopError * 1000f:F1} mm)", this); }
    }

    // 상자가 벨트 밖으로 벗어났는지 확인하고, 벗어났으면 이송 실패로 처리한다.
    /// 상자 중심이 벨트 폭의 절반을 넘었거나, 벨트 윗면보다 아래로 내려갔으면 벗어난 것으로 본다.
    /// 벗어난 상자는 목록에서 빼고, 물리를 켜서 떨어지게 둔다.
    bool CheckOffBelt(BoxInfo box)
    {
        Vector3 offset = PositionOf(box) - entry.position;
        float sideways = Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, Flow)));
        bool fell = offset.y < 0f;
        if (sideways <= beltWidth * 0.5f && !fell) { return false; }

        boxes.Remove(box);
        box.Mark(ExceptionReason.TransferFail);
        Rigidbody body = box.GetComponent<Rigidbody>();
        if (body != null) { body.isKinematic = false; }

        Debug.LogWarning($"[Conveyor] {lineName}: {box.boxId} 이송 실패 (벨트 밖)", this);
        if (cell != null) { cell.RaiseAlarm("이송 실패", box.boxId); }
        return true;
    }


    //  ============================================================
    //  계산 · 이동 도우미
    //  ============================================================
    // 위치 → 입구에서 흐름 방향으로 잰 거리 m
    float AlongBelt(Vector3 position) => Vector3.Dot(position - entry.position, Flow);

    // 상자의 지금 위치. Rigidbody가 있으면 물리 엔진의 위치를 쓴다 (MovePosition과 같은 값).
    static Vector3 PositionOf(BoxInfo box)
    {
        Rigidbody body = box.GetComponent<Rigidbody>();
        return body != null ? body.position : box.transform.position;
    }

    // 상자를 move만큼 옮긴다.
    /// kinematic 상자는 MovePosition으로 옮겨야 물리 엔진이 "이동"으로 안다 (위에 놓인 물체도 같이 반응).
    static void MoveBox(BoxInfo box, Vector3 move)
    {
        Rigidbody body = box.GetComponent<Rigidbody>();
        if (body != null) { body.MovePosition(body.position + move); }
        else { box.transform.position += move; }
    }

    // 상자를 axis 방향으로 쟀을 때 길이의 절반. 상자가 돌아가 있어도 맞게 계산한다.
    /// 각 변의 절반 길이 × 그 변이 axis를 향한 정도(|내적|)를 더한다.
    /// 예) 5 × 6 × 7 cm 상자가 똑바로 서 있으면 위쪽(axis = up)으로 3 cm, 옆으로 눕히면 2.5 cm 또는 3.5 cm.
    static float HalfSize(BoxInfo box, Vector3 axis)
    {
        BoxCollider boxCollider = box.GetComponent<BoxCollider>();
        if (boxCollider == null) { return 0f; }

        Transform boxTransform = box.transform;
        Vector3 half = Vector3.Scale(boxCollider.size, boxTransform.lossyScale) * 0.5f;
        return Mathf.Abs(Vector3.Dot(boxTransform.right, axis)) * half.x
             + Mathf.Abs(Vector3.Dot(boxTransform.up, axis)) * half.y
             + Mathf.Abs(Vector3.Dot(boxTransform.forward, axis)) * half.z;
    }

    // 스토퍼를 올리거나 내린다 (stopperMoveTime 동안 부드럽게).
    IEnumerator MoveStopper(bool up)
    {
        while (stopperMoving) { yield return null; }    //  앞 동작이 끝날 때까지 기다린다
        if (stopperUp == up) { yield break; }

        stopperMoving = true;
        Vector3 from = stopper.position;
        Vector3 to = up ? stopperUpPosition : stopperUpPosition + Vector3.down * stopperDrop;
        for (float time = 0f; time < stopperMoveTime; time += Time.deltaTime)
        {
            stopper.position = Vector3.Lerp(from, to, time / stopperMoveTime);
            yield return null;
        }
        stopper.position = to;
        stopperUp = up;
        stopperMoving = false;
    }

    // 벨트 무늬를 이동 거리만큼 흘린다.
    /// 이동 거리 m × 1 m당 텍스처 변화량만큼 텍스처를 민다. Repeat으로 0 ~ 1을 반복한다.
    void ScrollBelt(float distance)
    {
        if (beltMaterials.Count == 0 || Time.fixedTime == lastScrollTime) { return; }
        lastScrollTime = Time.fixedTime;

        beltOffset = Mathf.Repeat(beltOffset + distance * uvPerMeter, 1f);
        Vector2 offset = scrollAlongY ? new Vector2(0f, beltOffset) : new Vector2(beltOffset, 0f);
        foreach (Material material in beltMaterials) { material.mainTextureOffset = offset; }
    }


    //  ============================================================
    //  테스트 (Play 중에만)
    //  ============================================================
    // [테스트] ConveyorLine ⋮ 메뉴 → "테스트/상자 1개 투입"
    /// testPrefab으로 상자를 만들고, 송장면과 방향을 무작위로 돌린 뒤 Feed()로 올린다.
    [ContextMenu("테스트/상자 1개 투입")]
    void TestFeedOne()
    {
        if (!Application.isPlaying || testPrefab == null || entry == null)
        {
            Debug.LogWarning($"[Conveyor] {lineName}: Play 중에, Test Prefab과 Entry를 채운 뒤 실행하세요.", this);
            return;
        }
        if (!CanAccept)
        {
            Debug.Log($"[Conveyor] {lineName}: 입구가 차 있습니다.", this);
            return;
        }

        Quaternion rotation = BoxInfo.RotationFor((BoxFace)Random.Range(0, 6), 90f * Random.Range(0, 4));
        BoxInfo box = Instantiate(testPrefab, entry.position, rotation);
        testCount++;
        box.boxId = $"TEST-{testCount:00}";
        box.name = box.boxId;
        Feed(box);
    }

    // [테스트] ConveyorLine ⋮ 메뉴 → "테스트/상자 5개 연속 투입"
    /// 입구가 빌 때마다 하나씩 넣는다. 정지 위치 뒤로 상자가 줄을 서는지 본다.
    [ContextMenu("테스트/상자 5개 연속 투입")]
    void TestFeedFive()
    {
        if (Application.isPlaying) { StartCoroutine(TestFeedSeveral(5)); }
    }

    IEnumerator TestFeedSeveral(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new WaitUntil(() => CanAccept);
            TestFeedOne();
            yield return new WaitForSeconds(0.1f);
        }
    }

    // [테스트] ConveyorLine ⋮ 메뉴 → "테스트/정지 상자 보내기"
    [ContextMenu("테스트/정지 상자 보내기")]
    void TestRelease()
    {
        if (Application.isPlaying) { Release(); }
    }

    // 테스트 패널 (showTestButtons가 켜져 있을 때만)
    void OnGUI()
    {
        if (!showTestButtons) { return; }

        GUILayout.BeginArea(new Rect(guiPosition.x, guiPosition.y, 240, 100), GUI.skin.box);
        GUILayout.Label($"[{lineName}] 상자 {QueueCount}개 | 정지 {(HasBoxAtStop ? CurrentBox.boxId : "-")}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1개 투입")) { TestFeedOne(); }
        if (GUILayout.Button("5개 연속")) { StartCoroutine(TestFeedSeveral(5)); }
        GUILayout.EndHorizontal();
        GUI.enabled = HasBoxAtStop;
        if (GUILayout.Button("정지 상자 보내기")) { Release(); }
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    // Scene 뷰에서 구간을 선택하면 입구 → 출구(노란 선)와 정지 위치(빨간 공)를 그린다.
    void OnDrawGizmosSelected()
    {
        if (entry == null || exit == null) { return; }

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(entry.position, exit.position);
        Gizmos.DrawWireSphere(entry.position, 0.01f);
        if (stopPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(stopPoint.position, 0.01f);
        }
    }
}
