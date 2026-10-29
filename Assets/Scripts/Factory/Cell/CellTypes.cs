// 공정 전체가 함께 쓰는 구분값 (클래스 설계서 v0.3 "CellTypes").
// 다른 enum(Destination, LabelCheck, BoxState 등)도 이 파일에 모은다.

/// <summary>
/// 상자의 면. 판정 카메라 리그는 Top ~ Front 5 면만 찍는다 (FR-03).
/// Bottom 은 카메라가 없고, 5 면 모두 송장이 없을 때 180 도 뒤집어 Top 으로 확인한다 (FR-05, FR-06).
/// </summary>
public enum BoxFace
{
    Top = 0,
    Right = 1,   // 월드 +X
    Back = 2,    // 월드 +Z
    Left = 3,    // 월드 -X
    Front = 4,   // 월드 -Z
    Bottom = 5,
    None = 6,    // 5 면에서 송장을 찾지 못함
}
