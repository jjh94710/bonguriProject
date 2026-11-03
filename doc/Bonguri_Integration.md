# bongyuri 공정 통합 시운전

2026-11-03 Unity 6000.6.0f1 Editor에서 실제 Play 실행했다.

## 연결한 범위

`Assets/Scenes/bongyuri.unity`의 `BonguriProcess`에 CellController, BoxFeeder, 교정/판독/분류 ConveyorLine, 터미널 A/B/C 및 NG 정지 구간과 시운전 패널을 추가했다.

- 기존 ScanStation 카메라 5대와 LabelPresence ONNX를 CellController에 연결했다.
- LabelReaderStation 프리팹의 실제 번호/지역 OCR ONNX를 연결했다. placeholderRead는 꺼져 있다.
- 기존 fr5_with_gripper_first의 CorrectionSequence를 연결하고 벨트 Collider를 Placement Surface에 지정했다. 로봇 없는 RotateInPlace 경로를 사용하지 않는다.
- 기존 SeparatorNG/A/C의 분기점, 목적지, 받을 구간을 연결했다. B는 직진이다.
- 손으로 놓은 시험 상자, 학습 데이터 생성기와 시험용 받침은 비활성화했다. 원래 오브젝트는 씬에 보존했다.
- 교정 제한 시간은 실제 로봇 시험을 위해 90초로 설정했다. 기존 기본값 10초와 다르므로 인수 요구시간 충족을 뜻하지 않는다.
- 기존 두 LinearConveyor는 비활성화하고 ConveyorLine이 상자 이송을 담당한다. 벨트 무늬 스크롤 Renderer 연결은 아직 없다.

변경 전 씬 복사본은 `Temp/BonguriIntegrationBackup`에 있다.

## 실행 결과

| 실행 | 실제 결과 |
| --- | --- |
| 윗면 3개 | 3개 모두 Top 판정, 번호/지역 판독, A/B/C 각 1개 이송 |
| BOX-0001 | 22001 / 대전 → B |
| BOX-0002 | 33002 / 부산 → C, SeparatorC 작동 |
| BOX-0003 | 11003 / 서울 → A, SeparatorA 작동 |
| 측면 1개 | Right 판정 → 실제 그리퍼 로봇 교정 1회 → Top 재판정 → 22001 / 대전 → B |

두 실행의 상자 모두 reason=None이다. 측면 상자는 교정 스테이션 도착 약 4.37초, Top 재판정 약 49.40초, 판독 도착 약 53.27초였다. 교정에 약 45초가 걸린 단일 표본이므로 여러 면/크기의 회전 성능이나 공정 목표 사이클타임 통과로 해석하지 않는다.

원본 기록:

- `results/bonguri_integration/20261103_100104_top/report.json` 및 `events.log`
- `results/bonguri_integration/20261103_100158_side/report.json` 및 `events.log`

report의 complete는 시운전 기록 종료를 뜻하며 전체 공정 합격이나 적재 완료를 뜻하지 않는다. terminalCounts는 해당 구간에 들어간 상자 수이고, 구간 끝에 정지한 수와도 다르다. 기록 종료 뒤에도 상자는 각 구간 정지점까지 이동했다.

## 재현

1. `Assets/Scenes/bongyuri.unity`를 열고 Play한다.
2. Game 화면의 `Bonguri 통합 시운전`에서 `윗면 상자 3개 시험` 또는 `측면 상자 1개 · 실제 로봇 교정`을 누른다.
3. 상자의 판정/판독/목적지는 BoxInfo와 새 `results/bonguri_integration` 기록에서 확인한다.
4. 다른 시험을 하려면 Play를 정지하고 다시 시작한다. 상자를 정지 구간에 보존하므로 같은 Play 세션에서 중복 투입하지 않는다.

시험 동안만 실행 중 CellSettings 복사본의 면 비율과 투입 간격을 바꾸고 종료 시 복원한다. 원본 설정 에셋은 바꾸지 않는다. 학습/평가 씬을 열 때 자동 실행되는 시험은 없다.

## 남은 통합 작업

### 2026-11-03 터미널 적재 인계 연결 추가

CellController의 Terminal Stations 배열에 A/B/C의 ConveyorLine과 BoxAttacher를 연결했다. Run()이 각 터미널 감시 코루틴을 시작한다. 터미널 정지 상자는 BoxAttacher.TryLoadBox()에 명시적으로 전달하며, 상자 물리를 켜고 실제 흡착·적재를 요청한다. 수거함·슬롯 연결과 상자 크기를 사전 검사한다.

적재 확인 후에만 해당 구간에서 TakeAway()하고 Finish(box, 실제 터미널)로 집계한다. BoxInfo.actualDestination에 실제 도착을 기록한다. 적재는 됐지만 팔 복귀가 실패한 경우에는 적재를 한 번 집계하고 별도 경고를 남긴다. 흡착 중이거나 이미 이동한 상자를 자동으로 순간이동시키거나 재시도하지 않는다. 실패 상자는 보존하며 RetryTerminalLoading(목적지)로 명시적 재시도를 요청할 수 있다. 만적은 빈자리가 생길 때까지 기다리며 자동 비우기는 아직 구현하지 않았다.

로봇이 정지점의 상자를 들어도 뒤따르는 상자가 정지점 점유를 덮어쓰지 않도록 ConveyorLine의 대기 간격을 보강했다. 알람 및 운전 종료 때 진행 중인 적재 로봇도 StopLoading()으로 중단한다. 벨트·푸셔 전체 정지와 NG 최종 수거는 이번 변경 범위 밖이다.

표면 경로 연결 전 정지점에서 적재 로봇 베이스까지 거리는 A 약 16.99m, B 약 14.23m, C 약 17.16m다. 이 임시 분기 정지점 도착을 적재 도착으로 판단하지 않도록 수정했다. CellController는 NextLine을 따라 최종 구간을 찾고, 중간 정지점은 다음 구간이 받을 수 있을 때 해제한다. 로봇 앞 최종 정지점에 도착한 뒤에만 적재를 요청한다. 최종 구간이 아직 분기 근처에서 끝나면 상자를 Transferring 상태로 유지하고 연결 대기 메시지만 한 번 남긴다. 적재를 시도하거나 적재 실패 알람을 발생시키지 않는다.

maxPickupDistance 기본값 1m는 IK 성공을 보장하는 작업 반경이 아니라 잘못 지정된 최종 정지점을 구분하는 검사다. 거리를 키워 우회하기보다 실제 벨트 끝으로 이송 경로를 이어야 한다.

검증 메뉴: Tools > Bonguri > Integration > Verify Terminal Loading Handoff. 실제 투입기·판정·OCR·분기를 실행해 상자 3개가 분기 뒤 정지점에 도착해도 이송 대기를 유지하고, 조기 적재 시도·적재 실패 알람·삭제·완료 집계가 없는지 검사한다. 추가로 적재 중 다음 상자가 정지점을 덮어쓰지 않는지 검사한다. 원본 결과는 results/terminal_loading_handoff/latest.json 및 events.log에 저장한다. 이 검사는 물리 적재 성공을 뜻하지 않는다.

Verify Classified Box Conveyor Routes 메뉴는 상자를 조작하지 않고 0.5초 간격 실제 좌표, 각 구간 Entry/Exit/Stop/NextLine, 로봇 위치를 results/conveyor_route_check에 기록한다. 2026-11-03 실행에서 세 상자 모두 컨베이어로 이동해 지정된 분기 뒤 정지점에 도착했지만, A/B/C의 NextLine은 모두 None이었다. 이송 코드는 해당 구간까지 동작하며 이후 시각적 벨트와의 연결은 남아 있다.

표면 경로 연결 전 A/B/C는 분기 근처의 짧은 정지 구간이었다. 멀리 있는 터미널 로봇까지 이어지는 전체 벨트 경로와 경사/코너 이송은 아직 이어지지 않았고, 이 경로를 이용한 실제 흡착 적재 성공도 확인하지 않았다. NG도 수거함 적재 대신 정지 구간에 보존한다.

처음 시운전 당시에는 터미널 도착 → 집기 → ToteBase 수용 → Finish 연결이 없었다. 위 추가 변경으로 코드 연결은 구현했지만 실제 터미널 이송 경로와 적재 성공 검증은 남아 있다. 처음 실행의 Processed=0은 그대로 보존한다. `사이클 정지`는 추가 투입을 멈추는 명령이므로 미완료 상자가 남아 있는 한 CellController의 운전 상태가 종료되지 않는다. 시운전 종료는 Play 정지로 한다.

NG 예외 투입, Bottom 두 번 회전, 다른 측면/상자 종류, 연속 50개, 적재 후 완료 집계는 이번에 검증하지 않았다.

### 2026-11-03 벨트 표면 경로 연결

TerminalA/B/C의 ConveyorLine에 Use Surface Path를 켜고 실제 벨트 Collider와 SurfaceRoute 경유점을 연결했다. 경로 길이는 A 19.521m, B 16.470m, C 19.482m이며 최종 StopPoint는 각 적재 로봇 앞 벨트 끝이다. 앞 절의 짧은 분기 정지점과 미연결 기록은 연결 전 검증 결과다.

상자는 경로 누적 거리를 기준으로 이동하며 곡선을 돌고, Collider 표면 법선으로 경사 자세를 보정한다. BoxCollider 크기·스케일·중심 오프셋을 반영해 바닥을 맞추며 경사 연결부에서는 모서리 높이와 ComputePenetration으로 겹침을 보정한다. 기본 여유 높이는 2mm다. 롤러 사이 틈에는 최대 25mm 주변 표면을 확인한다. 더 큰 표면 누락이나 허용 경사(35도)를 넘는 수직 단차는 검증 실패로 처리한다. 실제 경사 연결 없이 높이만 다른 벨트 사이를 임의로 통과시키지 않는다.

기존 수평 직선 구간은 이전 모드로 유지한다. 푸셔 PushDistance는 변경하지 않고 A/C 입구를 현재 밀기 끝점에 맞췄다. 정지 상자 및 대기열 간격은 경로 거리를 기준으로 유지한다.

수정 위치: BonguriProcess 아래 TerminalA/B/C의 SurfaceRoute 자식. 벨트를 재배치하면 해당 경유점과 Belt Surfaces 연결도 맞춘다. 전체 연결 메뉴는 최초 설정용이며 이미 SurfaceRoute가 있으면 중복 생성을 거부한다.

검증 메뉴: Tools > Bonguri > Integration > Verify Belt Surface Motion. 실제 상자 3개를 0/90/180도 방향으로 투입해 전체 경로를 이동했다. results/conveyor_surface_check/latest.json에서 세 정지점 도착, 3,567회 측정, 최대 관통 0m, 상자 중심 높이 0.887~1.630m를 확인했다. 속도 2m/s는 Play 동안만 적용하고 종료 시 복원한다. 이 검사는 벨트 이송 검증이며 흡착 적재 성공을 뜻하지 않는다.

Verify Classified Box Conveyor Routes는 실제 CellController의 판정·판독·분기를 거친 도착을 확인한다. 도착 검증 중에는 Play 세션의 적재 감시 배열만 비우고 표면 경로 속도를 1m/s로 바꿔 상자를 끝에 보존한다. 저장된 씬과 정상 운전의 적재 연결은 유지한다.

통합 경로 재검증에서도 실제 투입 상자 3개가 판정·판독·분기를 거쳐 A/B/C 최종 정지점에 도착했다(passed=true). 최종 상자 중심에서 로봇 베이스까지 거리는 각각 약 0.44/0.43/0.46m다. 롤러 모서리의 수평 접촉 법선은 최대 30mm 범위에서 위로 보정하고, 해소되지 않으면 이송을 정지한다. results/conveyor_route_check/latest.json 및 events.log에 기록했다. 흡착·수거함 적재는 이 도착 검사에서 실행하지 않았다.
### B 진입 직후 역행 수정

B는 첫 직선 벨트의 중간으로 진입하는데 최초 경로 생성기가 벨트 상류 끝을 먼저 경유하도록 만들었다. 따라서 TerminalB 인계 직후 상자가 잠깐 상류로 이동한 뒤 방향을 바꿨다. 같은 시각 두 번째 상자의 C 푸셔가 작동해 푸셔 간섭처럼 보였다. 기존 좌표 기록에서 BOX-0001이 19.661~20.165초 사이 X=-2.5313에서 -2.1884로 역행한 것을 확인했다.

B의 첫 직선은 현재 Entry에서 하류 끝으로 직접 이어지도록 생성기를 수정했고, 씬의 TerminalB SurfaceRoute도 다시 구성했다. A/C 경로와 푸셔 동작은 변경하지 않았다. Verify Classified Box Conveyor Routes에는 B 진입 이후 매 갱신의 실제 Rigidbody 위치를 진행 방향으로 투영해 1mm를 넘는 역행을 실패로 처리하는 검사를 추가했다. 결과 JSON에 bDirectionSamples와 maximumBReverseStep을 기록한다.

수정 후 같은 고정 시드의 실제 상자 3개로 재검증했다. A/B/C 모두 최종 정지점에 도착했고, C/A 푸셔 작동을 포함한 B 위치 2,540회 비교에서 최대 역행량은 0m였다(passed=true).

### 실제 흡착 집기 확인

Verify Actual Suction Pickup 에디터 검사로 실제 CellController 투입·분류·이송·집기 요청을 실행했다. 테스트 중만 표면 이송 속도를 1m/s로 설정했다. 세 로봇 모두 해당 상자를 전달받았으나 흡착 접촉 자세를 찾지 못해 Attached/Lifted/Placed가 모두 false, Processed=0이었다. A는 X 방향 경로에서 IK 위치/각도 오차 202.1mm/12.7도, B는 Z 방향 124.3mm/64.6도, C는 Z 방향 117.3mm/63.4도를 보고했다. B/C는 두 번의 시도 모두 실패했고, A는 첫 시도 실패를 확인한 시점에 검사를 종료했다. 이 수치는 접촉 자세 풀이의 오차이며 로봇 베이스와 상자 간 거리가 아니다. 위치만 가깝다고 실제 흡착 자세가 가능한 것은 아니므로 집기 정지점·흡착면·IK 접근 자세를 함께 조정해야 한다. 원본은 results/pickup_check/latest.json 및 events.log에 보존했다. 공정/로봇 실행 코드는 이번 확인에서 변경하지 않았다.

### 현재 C 기준 A/B 배치 맞춤

사용자가 집기·적재에 성공한 현재 C 배치를 기준으로, 각 최종 벨트의 진행 방향 좌표계에서 A/B 로봇과 수거함의 상대 위치·회전을 맞췄다. 로봇 받침대는 PartRobot 그룹과 함께 이동했다. 로봇 베이스의 정지점 상대 좌표는 세 터미널 모두 (0.0474,-0.0650,0.5753)m다. A 로봇 위치는 (-18.6845,0.7990,-2.0735), B는 (-18.3945,0.7990,1.8365)로 조정했다. A/B 수거함은 Y=0.3898에서 C와 같은 Y=0.7971로 높였다. C 배치와 벨트 경유점은 그대로 유지했다. 수정 전 씬은 Temp/BonguriIntegrationBackup/before_match_loading_C_20261103_181751.unity에 보존했다.

첫 검증에서는 A의 X 방향 상자가 실제 흡착·상승·수거함 배치에 성공했다. B/C의 Z 방향 상자는 흡착 접촉 자세 계산에 실패했다. 이는 배치 조정과 별도로 남은 방향별 경로 문제다. 원본은 results/pickup_check/after_layout_random.json 및 after_layout_random_events.log에 보존했다.

이어 실제 투입·판정·판독·분류 경로는 유지하고, 시험 중에만 새 상자의 윗면은 유지한 채 yaw=90도로 통일해 X 방향 흡착을 비교했다. A/B/C 모두 실제 흡착·약 0.227m 상승·수거함 첫 슬롯 배치와 완료 집계에 성공했다(Processed=3). results/pickup_check/after_layout_uniform_x.json 및 after_layout_uniform_x_events.log에 보존했다. 정상 투입의 무작위 방향이나 IK 실행 코드는 바꾸지 않았다. 따라서 동일 방향에서는 배치 일치 효과를 확인했고, Z 방향 실패는 별도 남은 문제다.

### 방향별 상자 1개 직접 집기 패널

Play의 Bonguri 통합 시운전 패널에서 A/B/C 로봇을 고르고 X/Y/Z 방향 1개 버튼을 누르면 해당 터미널 최종 벨트 위에 시험 상자 1개를 생성해 BoxAttacher.TryLoadBox로 직접 인계한다. 축은 5×6cm 흡착면 법선의 월드 방향이다. X는 yaw=90도, Y는 pitch=90도, Z는 기본 회전이다. Y 방향을 교정 공정에서 다시 바꾸지 않도록 투입·교정·판독·분류는 생략하며, 통합 처리 수에는 포함하지 않는다. 성공 상자는 수거함에 남고 실패 상자는 실패한 시험 상자 정리 버튼으로 제거한 뒤 다시 시험한다. 흡착/이동 중 삭제 및 중복 생성은 막는다. 통합 운전과 직접 집기 시험의 동시 시작도 막는다. 컴파일과 Game 패널 표시, 실제 PICKUP-A-X-1 단일 생성 및 집기 접근을 확인했다.

### Z 방향 집기·회전 경로 수정

Z 방향의 기존 IK 탐색은 손목이 벨트 아래로 가는 해에 수렴해 안전 자세 조건에서 탈락했다. BackArmIK에서 기존 접촉 풀이 실패 시 손목/베이스 각도를 바꾼 초기값을 추가로 시도한다. 초기값은 관절 제한으로 자르고, 접촉 위치 1.5mm·각도 1.5도와 팔꿈치·손목 높이 조건은 그대로 검사한다. 조건 없는 풀이는 후보 초기값 계산에만 쓰고 실제 경로에는 안전 조건을 만족한 해만 사용한다.

BoxAttacher의 Z 방향도 Z Direction 설정으로 접근·상승·이동·분할 회전을 집기 전에 검증한다. 기존의 흡착 후 단일 회전 경로는 제거했다. 현재 배치에서 검증된 손목 180도와 로봇 쪽/옆쪽 경유점을 먼저 시도하지만, 모든 후보는 동일한 전체 경로 검증을 거친다. Z 방향의 든 상자 이동 중간 샘플은 실제 관절 구동 도착 기준과 같은 2도 이내로 풀고, 접촉과 최종 배치 끝점의 1.5도 기준은 유지한다.

실제 Play에서 Z 상자를 직접 생성한 A/B/C 모두 흡착·약 0.228m 상승·수거함 첫 슬롯 배치에 성공했다. 이후 실제 투입·ONNX·OCR·분류·컨베이어 이송을 포함한 상자 3개도 적재와 Processed=3까지 확인했다(B/C=Z, A=X). C는 첫 접근 계획 실패 후 기존 자동 재시도의 두 번째 시도에서 성공했다. 모든 상자 방향·연속 만적 시험을 통과했다는 뜻은 아니다. 원본은 results/pickup_diagnosis/z_all_placed.json, z_integration_passed.json, z_integration_events.log, summary.json이다. 임시 접촉/관절 탐색 진단 코드는 제거했고, 재현용 에디터 Z 직접 집기 시험만 남겼다.
