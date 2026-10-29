## 양방향 푸셔

SeparatorA는 +Z 오른쪽 벨트, SeparatorB는 -Z 왼쪽 벨트로 민다. Play 후 해당 판을 선택하고 밀기 시작을 누른다. 동작 중 반대쪽 판은 비활성화하며, 복귀 시 박스와 겹치지 않을 때만 원래 활성 상태로 복구한다. 반대쪽 판이 동작 중이면 시작을 거부한다. 양쪽 도착 벨트는 기본 정지 유지한다. A 오른쪽 이송 Play 검증 통과: 원위치 오차 0m, 오른쪽 벨트 정지 확인. 보고서 Temp/RightPusherPlayTest.json. 설정과 테스트는 수동 메뉴로만 실행한다.

# 박스 푸셔

Play 후 Hierarchy의 SeparatorB를 선택하고 Inspector의 Parcel Pusher에서 밀기 시작을 누른다.
판은 실제 충돌로 박스를 왼쪽(-Z) 벨트 안쪽까지 밀고, 잠시 정지한 뒤 원위치로 복귀한다. 박스가 도착 벨트 위에 남아 있는지 확인한 후 연결된 LinearConveyor를 시작한다. 박스를 부모로 붙이거나 순간이동하지 않는다.

현재 동작은 중앙 컨베이어가 정지한 조건이다. 판의 Push Speed는 0.08m/s, Return Speed는 0.15m/s, 도착 벨트 속도는 0.12m/s이다. 도착하지 못하면 벨트를 시작하지 않고 정지한다. 정지 및 판 원위치로 복귀 버튼을 제공한다.

도착 벨트 자식 PusherReceiverSurface는 박스를 지지하는 물리 접촉면이다. 기존 LinearConveyor가 해당 Rigidbody의 표면 속도로 박스를 운반한다. 밀기 정지 시에는 벨트 경계보다 12cm 안쪽에 들어왔는지 확인하고, 복귀 완료 시에는 박스 전체가 벨트 범위 안에 있는지 다시 확인한다.

Tools > Bonguri > Run Parcel Pusher Play Test에서 수동 재시험할 수 있다. 컴파일 또는 프로젝트 열기 시 자동 실행하지 않는다. 검증 결과: 판 원위치 오차 0m, 복귀 후 벨트 운반 약 0.1199m/1초, 실제 박스 지지 및 이송 확인. 결과 파일 Temp/ParcelPusherPlayTest-v5.json.