using UnityEngine;

//  ============================================================
//  CellSettings
//  공정 파라미터를 한 에셋(.asset)에 모은 설정 파일.
//      - 코드를 고치지 않고 이 에셋 값만 바꿔 공정을 바꾼다.
//
//  [만들기] Project 창 우클릭 → Create → Factory → Cell Settings
//  [사용]   CellController가 시작할 때 복사본(Instantiate)을 만들어 다른 스크립트에 나눠 준다.
//           그래서 Play 중 운전 제어 패널에서 바꾼 값은 에셋 파일에 남지 않는다.
//  [송장 규칙] 지역 코드 표의 기본값은 SRS 규칙(11·22·33 = 서울·대전·부산)이다.
//           공정용 Variant는 LabelLayout이 이 표대로 찍는다 (원본 Prefab의 ShippingLabelNumber 10.20.30은 쓰지 않는다).
//           규칙을 바꾸려면 아래 세 배열만 바꾸면 된다 (NFR-10).
//  ============================================================
[CreateAssetMenu(fileName = "CellSettings", menuName = "Factory/Cell Settings")]
public class CellSettings : ScriptableObject
{
    //  ---------------- 투입 ----------------    (FR-01) 
    [Header("투입")]
    [Tooltip("상자 투입 간격(초). 운전 제어 패널에서도 1 ~ 30초 범위로 바꿀 수 있다")]
    [Range(1f, 30f)] public float feedInterval = 10f;

    [Tooltip("송장이 위를 향하는 비율(default: 70%)")]
    [Range(0f, 1f)] public float faceTop = 0.7f;

    [Tooltip("송장이 측면 한 방향을 향하는 비율. 앞·뒤·왼쪽·오른쪽 각각 이 값(default: 5%씩)")]
    [Range(0f, 0.25f)] public float faceSide = 0.05f;

    [Tooltip("송장이 바닥을 향하는 비율 (default: 10%)")]
    [Range(0f, 1f)] public float faceBottom = 0.1f;

    //  ---------------- 이상 상자 재현 (기본 0) ----------------
    //  테스트 케이스를 재현할 때만 올린다. 평소 운전은 0으로 둔다.
    [Header("이상 상자 재현 (평소 0)")]
    [Tooltip("송장 미부착 상자 비율")]
    [Range(0f, 1f)] public float noLabelRate = 0f;

    [Tooltip("번호 규칙 위반 상자 비율")]
    [Range(0f, 1f)] public float wrongLabelRate = 0f;

    //  ---------------- 교정 · 판독 ----------------       (FR-06, UI-04)
    [Header("교정 · 판독")]
    [Tooltip("교정 스테이션에서 상자 1개당 회전 최대 횟수 (default: 2회)")]
    [Range(1, 5)] public int maxRotations = 2;      // 180° 회전 포함, 어느 면이든 1회면 충분하다 (2회: "한 번 틀려도 한 번 더 기회")

    [Tooltip("판독 재시도 한도. 모름 칸을 다시 읽는 횟수")]
    [Range(0, 5)] public int maxRetry = 2;

    //  ---------------- 지역 코드 표 ----------------      (FR-07, FR-08, NFR-10)
    //  세 배열은 "같은 순서"로 한 줄씩 대응한다.
    //      예) 0번: 코드 11 → 주소 "서울" → 터미널 A
    [Header("지역 코드 표 - 세 배열의 길이와 순서 맞추기")]
    [Tooltip("유효 지역 코드 (송장 번호 앞 2자리)")]
    public int[] regionCodes = { 11, 22, 33 };

    [Tooltip("지역 코드별 주소 지역 이름. LabelReaderAgent 지역명 칸의 행동 번호와 같은 순서")]
    public string[] regionNames = { "서울", "대전", "부산" };

    [Tooltip("지역 코드별 터미널")]
    public Destination[] terminals = { Destination.A, Destination.B, Destination.C };

    //  ---------------- 연속 운전 ----------------         (FR-13)
    [Header("연속 운전")]
    [Tooltip("벨트 위 대기 상자 허용 수. 넘으면 투입을 잠시 멈춘다")]
    [Min(1)] public int maxQueue = 5;

    [Tooltip("대기 상자가 이 수 이하로 줄면 투입을 다시 시작한다 (maxQueue보다 작게)")]
    [Min(0)] public int resumeQueue = 2;

    //  ---------------- 적재 ----------------            (FR-08, FR-09)
    [Header("적재")]
    [Tooltip("터미널 컨테이너가 이 수에 닿으면 자동으로 비운다")]
    [Min(1)] public int containerFull = 10;

    [Tooltip("NG 토트 적재 수가 이 수를 넘으면 \"NG 토트 정리 필요\" 경고")]
    [Min(1)] public int ngWarnCount = 5;

    //  ---------------- AGV 출발 ----------------        (FR-15, Could)
    //  시간이 남으면 구현: AgvShuttle을 만들지 않으면 쓰이지 않는다.
    [Header("AGV 출발")]
    [Tooltip("AGV 출발 기준 적재 수")]
    [Min(1)] public int agvDepartCount = 5;

    [Tooltip("1개 이상 실은 뒤 마지막 적재에서 이 시간(초)이 지나면 출발")]
    [Min(0f)] public float agvDepartIdle = 60f;

    /// 에셋 값을 바꿀 때마다 Unity가 부른다 (에디터 전용).
    /// 값이 맞지 않으면 Console에 경고만 띄우고, 값은 바꾸지 않는다.
    void OnValidate()
    {
        //  1) 송장면 비율의 합 = 위 + 측면 × 4 + 바닥 = 1 이어야 한다
        float sum = faceTop + faceSide * 4f + faceBottom;
        if (Mathf.Abs(sum - 1f) > 0.001f)
            Debug.LogWarning($"[CellSettings] 송장면 비율의 합이 {sum:0.###}입니다. " +
                             "위 + 측면 × 4 + 바닥 = 1이 되도록 맞추세요.", this);

        //  2) 지역 코드 표의 세 배열은 길이가 같아야 한다
        int n = regionCodes != null ? regionCodes.Length : 0;
        int names = regionNames != null ? regionNames.Length : 0;
        int destinations = terminals != null ? terminals.Length : 0;
        if (names != n || destinations != n)
            Debug.LogWarning($"[CellSettings] 지역 코드 표의 길이가 다릅니다 (코드 {n}, 이름 {names}, 터미널 {destinations}). " +
                             "같은 순서로 한 줄씩 맞추세요.", this);

        //  3) 투입 재개 기준은 허용 수보다 작아야 한다 (같거나 크면 투입이 멈추지 않는다)
        if (resumeQueue >= maxQueue)
            Debug.LogWarning("[CellSettings] resumeQueue는 maxQueue보다 작아야 합니다.", this);
    }
}
