using UnityEngine;

//  ============================================================
//  BackArmIK
//  흡착 로봇(fr5_suction)에 붙는 ArmIK. 코드는 모두 ArmIK에 있고, 이 클래스는 이름만 있다.
//      - bongyuri.unity의 흡착 로봇과 봉현님 BoxAttacher · SuctionLoadingSetup이 이 이름을 쓴다.
//      - 예전 ArmIK.cs의 GUID를 이어받았다. 예전 씬에 붙어 있던 ArmIK 컴포넌트는 BackArmIK로 보인다 (그대로 동작한다).
//
//  [!] 고칠 것이 있으면 ArmIK.cs를 고친다. 이 파일에는 코드를 넣지 않는다.
//  [!] 한 로봇에 FrontArmIK · BackArmIK · ArmIK 중 하나만 붙인다.
//  ============================================================
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class BackArmIK : ArmIK
{
}
