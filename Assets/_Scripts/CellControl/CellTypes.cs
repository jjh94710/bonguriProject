//  ============================================================
//  CellTypes
//  공정 스크립트가 함께 쓰는 구분값(enum) 7개를 한 파일에 모은다.
//      - MonoBehaviour가 아니므로 오브젝트에 붙이지 않는다. 파일만 있으면 된다.
//
//  [주의] Inspector에는 enum이 "숫자"로 저장된다.
//         새 값은 항상 맨 뒤에 추가하고, 이미 있는 값의 순서는 바꾸지 않는다.
//         (순서를 바꾸면 Inspector에 저장해 둔 값이 다른 값으로 바뀐다)
//  [표시]  CSV 로그에는 enum 이름(예: NoLabel)을 그대로 쓰고,
//         화면에는 한글 이름(예: 송장 미검출)으로 바꿔 보여 준다.
//  ============================================================

/// 송장면이 향한 방향 (위 1, 측면 4, 바닥 1).
/// 기준은 월드 축이다. 면별 방향은 BoxInfo.FaceDirection에 있다.
/// [용도] 투입 방향 배정(BoxFeeder), 카메라 판정(FaceCameraRig), 교정 회전 선택
public enum BoxFace
{
    Top,        //  위 (+Y). 판독 스테이션은 이 면의 송장만 읽는다
    Front,      //  앞 (−Z)
    Back,       //  뒤 (+Z)
    Left,       //  왼쪽 (−X)
    Right,      //  오른쪽 (+X)
    Bottom      //  바닥 (−Y). 180° 뒤집어야 위로 온다
}

/// 상자 1개의 현재 상태.
/// 공정 순서: Moving → Judging ⇄ Correcting → Transferring → Reading → Sorting → Loaded 또는 Exception
/// [주의] Reading, Sorting은 v0.3에서 추가되어 맨 뒤에 있다. 숫자 순서와 공정 순서가 다르다.
public enum BoxState
{
    Moving,         //  교정 스테이션까지 컨베이어로 이동 중
    Judging,        //  교정 스테이션: 카메라 5대로 송장 위치를 재는 중
    Correcting,     //  교정 스테이션: 로봇암이 상자를 돌리는 중
    Transferring,   //  교정 스테이션 → 판독 스테이션으로 이송 중 (FR-16)
    Loaded,         //  [끝] 터미널 컨테이너에 적재됨 (분류 성공)
    Exception,      //  [끝] NG 토트로 배출됨 (예외)
    Reading,        //  판독 스테이션: 송장을 읽는 중
    Sorting         //  분류 구간: 푸셔 쪽으로 이동 중
}

/// 예외 사유. None이 아니면 그 상자의 목적지는 NG가 된다 (BoxInfo.Mark).
public enum ExceptionReason
{
    None,           //  예외 없음
    NoLabel,        //  송장 미검출: 판독 스테이션 윗면에서 송장을 찾지 못함 (FR-06)
    ReadFail,       //  판독 실패: 재시도 후에도 모름 칸이 남음, 또는 숫자가 5자리가 아님 (FR-04)
    RuleViolation,  //  번호 규칙 위반: 지역 코드가 없거나 주소 지역과 다름 (FR-07)
    CorrectionFail, //  교정 실패: IK 계산 실패, 동작 시간 초과 (FR-05)
    TransferFail,   //  이송 실패: 상자가 벨트를 벗어나거나 스토퍼를 넘음 (FR-02, FR-16)
    JudgeFail       //  측정 실패: 카메라 영상을 받지 못함 (FR-03)
}

/// 번호 규칙 검증 결과 (FR-07). LabelValidator.Validate가 돌려준다.
public enum LabelCheck
{
    OK,              //  통과: 숫자 5자리 + 유효 지역 코드 + 주소 지역 일치
    DigitError,      //  숫자 5자리가 아님 (예: 2213). 예외 사유는 ReadFail로 기록
    RegionError,     //  앞 2자리가 유효 지역 코드가 아님 (예: 23137)
    AddressMismatch  //  지역 코드와 주소 지역이 다름 (예: 11137인데 주소가 부산)
}

/// 목적지. 판독 스테이션이 상자에 기록하고, 분류 구간의 푸셔가 읽는다 (FR-08, FR-09).
public enum Destination
{
    None,   //  아직 정해지지 않음
    A,      //  터미널 A (분기점 왼쪽 벨트)
    B,      //  터미널 B (직진)
    C,      //  터미널 C (분기점 오른쪽 벨트)
    NG      //  예외 레인 (NG 토트)
}

/// 교정 스테이션의 제어 방식 (FR-12). 판독은 제어 방식과 관계없이 항상 LabelReaderAgent가 한다.
public enum ControlMode
{
    Basic,  //  기본 제어: 카메라 비율이 가장 큰 면을 보고 규칙으로 회전을 정한다
    Policy  //  학습 정책: CorrectionAgent(ML ①)가 회전을 고른다
}

/// 설비 상태 (FR-10). 대시보드에 색과 글자로 함께 표시한다.
public enum CellState
{
    Running,    //  가동 (초록)
    Waiting,    //  대기: 상자를 기다리거나 투입을 잠시 멈춤 (노랑)
    Stopped     //  정지: 운전 정지 또는 알람 (빨강)
}
