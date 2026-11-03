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

현재 A/B/C는 분기 근처의 짧은 정지 구간이다. 멀리 있는 터미널 로봇까지 이어지는 전체 벨트 경로, 경사/코너 이송, 흡착 적재를 연결하지 않았다. NG도 수거함 적재 대신 정지 구간에 보존한다.

BoxAttacher/ToteBase는 독립 적재 시험용 API를 사용하고, 현재 CellController가 기대하는 터미널 도착 → 집기 → ToteBase 수용 → Finish 연결은 구현되어 있지 않다. 따라서 이번 실행의 Processed=0은 유지되며, 처리 완료 수로 올리지 않는다. `사이클 정지`는 추가 투입을 멈추는 명령이므로 정지 구간의 상자가 남아 있는 한 CellController의 운전 상태가 종료되지 않는다. 시운전 종료는 Play 정지로 한다.

NG 예외 투입, Bottom 두 번 회전, 다른 측면/상자 종류, 연속 50개, 적재 후 완료 집계는 이번에 검증하지 않았다.
