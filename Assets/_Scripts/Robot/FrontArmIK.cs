using UnityEngine;

//  ============================================================
//  FrontArmIK
//  교정 로봇(fr5_with_gripper)에 붙는 ArmIK. 코드는 모두 ArmIK에 있고, 이 클래스는 이름만 있다.
//      - bongyuri.unity · FactoryExample.unity의 교정 로봇이 이 스크립트로 연결되어 있어서 남겨 둔다.
//      - CorrectionSequence는 GetComponent<ArmIK>()로 찾으므로 FrontArmIK도 그대로 쓴다.
//
//  [!] 고칠 것이 있으면 ArmIK.cs를 고친다. 이 파일에는 코드를 넣지 않는다.
//  [!] 한 로봇에 FrontArmIK · BackArmIK · ArmIK 중 하나만 붙인다.
//  ============================================================
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class FrontArmIK : ArmIK
{
}
