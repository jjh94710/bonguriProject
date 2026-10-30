## 최신 파지 테스트 결과

받침 높이를 12cm 낮추고 팔꿈치를 올린 준비 자세에서 접근·하강·그리퍼 닫기·상승을 실행했다. 실제 물리 테스트에서 박스가 0.0804m 상승했다. 아래의 이전 실패 기록은 수정 전 결과이다. 다른 박스/배치에 대한 성공은 별도 검증이 필요하다. 테스트 메뉴: Tools > Bonguri > Run Parcel Pick And Lift Test. 자동 실행하지 않는다.

# FR5 박스 접근 IK — 작업 중

현재 구현은 박스를 지정하여 팔을 접근시키는 실험용 기능이다. **현재 씬에서 접근 중 관절이 멈추는 문제가 남아 있고, 박스 파지·들어 올리기 성공은 검증되지 않았다.**

## 사용

`fr5_with_gripper_first`의 Inspector에서 `Parcel Arm IK`를 사용한다. `Target Box`는 집을 박스 최상위의 Box Collider이다.

Play 후 그리퍼를 열고 **박스 위로 이동 → 잡는 위치로 내려가기 → Close Amount를 천천히 증가 → 위로 들어 올리기** 순서로 테스트한다. 상태 메시지와 실제 접촉을 확인하고 이상 동작에는 정지한다.

## 구현

- `Scripts/CorrectionSequence.cs`: 6개 회전 관절 수치 IK, 관절 제한, 단계별 경로, 모터 목표값 보간, 타깃 박스 Collider와의 충돌 검사, 초기 관절 각도 및 로봇 자체 중량 보상.
- `Assets/Editor/CorrectionSequenceEditor.cs`: Inspector 조작 버튼과 계산 검증 메뉴.
- `Assets/Editor/CorrectionSequencePlayTest.cs`: 수동 Play 테스트, 도착 오차·박스 이동·관절 추종·접촉 진단. 결과는 `Temp/CorrectionSequencePlayTest-v3.json`에 기록한다.
- `Assets/Editor/ArrangeParcelIKStation.cs`: 사용자가 메뉴에서 실행할 때 씬을 백업하고 테스트 로봇 배치/초기 자세를 다시 설정한다.

설정 도구는 `Tools > Bonguri`에 있다. 프로젝트를 열거나 컴파일할 때 씬 재배치와 Play 테스트를 자동 실행하지 않는다.

박스와의 충돌 검사가 공장 전체 장애물 회피를 보장하지는 않는다. 로봇 자체 중량 보상은 링크에 적용하며 박스 중력은 유지한다. 그리퍼 여닫기는 기존 GripperController가 담당한다.

## 검증 상태

초기 버전에서 박스 위 접근 오차 약 6mm를 기록했지만 이동 중 박스를 치는 문제가 확인되었다. 이후 경로 검사 및 재배치를 추가한 테스트에서는 박스 이동과 겹침은 기록되지 않았으나, 목표에서 약 22mm 남긴 상태로 관절 추종이 멈추어 타임아웃되었다. 이후 추가한 초기 관절 각도 설정/중량 보상은 최종 재검증 전이다. 이 기능을 완료된 자동 픽앤플레이스로 취급하지 않는다.

복구한 박스 재질, 한글 폰트와 OFL 라이선스, 송장 스크립트 및 .meta 파일을 함께 버전 관리해야 한다. URDF Importer 설치 정보는 Packages/manifest.json과 packages-lock.json에 포함되어 있다.
