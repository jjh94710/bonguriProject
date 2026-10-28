# fr5_gripper_description

FAIRINO FR5 협동로봇 + 2지 평행 그리퍼 URDF (Unity URDF-Importer용)

## 구성
- 팔: 공식 `FAIR-INNOVATION/frcobot_ros2` 의 `fairino5_v6.urdf` 그대로 (관절 이름 `j1`~`j6`, ROS2 드라이버와 동일)
- 그리퍼: `Intouchables8/FR5_Reinforcement-learning` 의 DH PGI-140-80 메시를 정리해서 사용 (Mulan PSL v2)
  - 케이블 형상 제거, 장착면 중심을 원점으로 재배치
  - 두 손가락을 좌우 대칭으로 맞추고, 닫힐 때 서로 겹치지 않게 다듬음
  - 손가락당 스트로크 40 mm (0 = 열림, 0.04 = 닫힘), 닫힌 상태에서 패드 간격 약 11 mm

## 링크 구조
```
base_link → shoulder_link → upperarm_link → forearm_link → wrist1_link → wrist2_link → wrist3_link
  → tool0 (플랜지 면) → gripper_base_link
      ├─ finger_right_link  (finger_right_joint, prismatic)
      ├─ finger_left_link   (finger_left_joint, prismatic, mimic)
      └─ tcp                (패드 사이 파지점, 플랜지에서 140 mm)
```

## Unity에서 가져오기
1. 이 폴더(`fr5_gripper_description`) 전체를 `Assets/` 아래에 복사
2. `urdf/fr5_with_gripper.urdf` 우클릭 → Import Robot from Selected URDF file
3. 생성된 로봇 루트의 `base_link` ArticulationBody에서 Immovable 체크 (바닥 고정)
4. `Scripts/GripperController.cs` 를 로봇 루트에 붙이고 `closeAmount` 를 0 ~ 0.04 로 조절
   - Unity는 URDF의 mimic을 무시하므로 스크립트가 두 손가락을 같이 움직임
   - 그리퍼가 반대로 움직이면 `SetTarget` 에 넘기는 값의 부호를 바꿔 주세요

## ROS2 연동 시
- 팔 관절 이름은 공식 드라이버와 같은 `j1`~`j6`
- 그리퍼는 `finger_right_joint` 하나만 제어하면 됨 (`finger_left_joint` 는 mimic)
