# Inference Engine 연동 검증

2026-11-02 Unity Editor 실행 결과: **PASS**.

검증 범위는 WBS의 'Inference Engine 연동·5면 판정'에 해당하는 판정 모듈이다.
전체 컨베이어 운전, 로봇 회전, OCR, 분류·적재까지 완료한 결과는 아니다.

## 실행 경로

`FaceJudgeAgent`가 새 시드 20261105로 한 번에 상자 하나를 배치한다.
`CellController.TryJudgeFace(box)` → `LabelPresenceDetector.TryJudgeBox(box, rig)` →
카메라 5장 촬영 → `Unity.InferenceEngine.Worker` CPU 추론 →
`BoxInfo.judgedFace`와 `BoxInfo.faceRatios` 기록을 확인했다.
이 경로는 모델에 정답 면이나 카메라 번호를 별도 관측으로 주지 않는다.
정답은 판정 후 정확도 집계에만 사용한다.

모델: Assets/FactoryMLModels/LabelPresence.onnx.
Unity: 6000.6.0f1, Inference Engine: 2.6.1, 백엔드: CPU.
입력은 image float32 [1,3,96,96], RGB 0~1, 위에서 아래 순서다.
출력은 label_score float32 [1,1]이다.
0 Top / 1 Front / 2 Back / 3 Left / 4 Right 순서로 추론하며,
점수가 모두 검출 기준 0.5 미만이면 Bottom(아랫면 또는 미부착에 따른 미검출)이다.
이 점수는 보정된 확률이나 기존 색상 면적 비율이 아니다.

## 결과

| 항목 | 결과 |
| --- | --- |
| 상자 360개 | 360/360 정답 |
| Top / Front / Back / Left / Right / Bottom | 각각 60/60 |
| 모델 미연결 | 거부 |
| 잘못된 영상 크기(2×2) | 거부 |
| 카메라 누락 | 거부 |
| 모듈 비활성 | 거부 |
| 실패 후 원래 자식별 레이어 복원 | 통과 |
| 성공 후 원래 자식별 레이어 복원 | 통과 |
| 비활성화·재활성화 후 Worker 재생성 | 통과 |
| null 상자 요청 | 거부 |

성능 측정은 카메라 5장 촬영·추론을 포함한다. 프레임 대기, 상자 생성,
공정 이동, 로그 출력은 포함하지 않는다. 첫 실행을 뺀 359회에서 백분위수를 계산했다.

| 지표 | 시간 |
| --- | --- |
| 첫 실행(모델 로드 포함) | 45.18 ms |
| 이후 중앙값 | 8.51 ms |
| 이후 P95 | 10.97 ms |
| 이후 최대 | 15.63 ms |

원본 결과: results/inference_integration/20261102_174315.json.
면별 판정 결과: results/face_judge_evaluation/LabelPresence_20261102_174315.json.
테스트는 기존 세 프리팹·현재 조명 조건의 합성 표본이다. 다른 촬영 조건으로 일반화된 성능은 아니다.

## 재실행

1. 학습 씬(Assets/Scenes/bongyuri mlagentBox.unity)에서 Play를 정지한다.
2. Tools > Factory ML > 송장 유무 > 3. Inference Engine 공정 연동 검증 준비를 선택한다.
3. 별도 Assets/Scenes/InferenceEngineValidation.unity 씬이 생성·설정된다.
4. Play하면 360개 평가와 오류 처리를 실행하고 Console에 PASS/FAIL과 JSON 위치를 기록한다.

이미 만들어진 InferenceEngineValidation 씬을 열고 Play해도 된다. Python 연결은 필요 없다.

## 실제 공정 씬 연결

CellController의 Face Rig에 촬영 리그, Face Detector에 LabelPresenceDetector를 연결한다.
Detector에는 최종 ONNX와 threshold 0.5를 연결한다. 카메라는 ScanTarget만 촬영해야 한다.
상자가 정지하고 카메라 위치가 맞는 상태에서 기존 교정 루프의 Measure가 ONNX 경로를 사용한다.
Face Detector를 비워두면 기존 색상 비율 판정을 유지한다.
모델을 연결했는데 추론이 실패하면 색상 판정으로 넘어가지 않고 JudgeFail과 알람 경로로 보낸다.
ControlMode.Policy/HasPolicy는 로봇 회전 정책의 설정이므로 송장 영상 판정과 별개다.

현재 새 ONNX 경로는 별도 검증 씬에 연결했다. 원래 공정 씬의 배선과 전체 연속 운전은 별도 검증 대상이다.

## 추가 검증: 회전 뒤 재판정과 글자 가림

2026-11-02 추가 실행 결과도 PASS.
results/inference_integration/20261102_190557.json에서 전체 360/360 정답과
측면 송장 표본 30/30의 회전 뒤 Top 판정을 확인했다.
검증기는 모델이 선택한 측면을 위로 향하게 상자 Transform/Rigidbody 자세를 바꾸고,
같은 CellController 경로로 재촬영·재판정한 뒤 원래 자세로 복구한다.
이는 판정 모듈이 자세 변화 뒤에도 동작하는지 보는 검사이며 로봇의 파지/회전 운동 검사가 아니다.
이번 실행의 촬영·추론 중앙값은 8.57ms, P95는 11.50ms였다.
회전 검사를 수행한 표본은 복구 후 마지막 촬영·추론 시간을 수집한다. 별도 회전 동작 시간은 포함하지 않는다.

Tools > Factory ML > 송장 글자 가림 검증에서 실제 URP 렌더링을 검사했다.
상자만 찍은 기준 영상과 비교해 뒤쪽 글자의 변화는 0픽셀,
앞쪽 글자는 2880픽셀 변화로 정상 표시됐다.
Prepare 100회 반복에서 같은 재질과 컴포넌트를 재사용했다.
편집 모드에서 컴포넌트를 제거할 때 정리 콜백이 실행되지 않아 임시 재질이 남는 문제를 발견했고,
LabelTextOcclusion에 ExecuteAlways를 적용해 수정했다. 수정 후 재질 해제 검사도 통과했다.
원본 결과는 results/text_occlusion/latest.json에 저장했다.

LabelLayout은 TMP 글자 배치, LabelTextOcclusion은 TextMesh 깊이 처리 역할로 분리되어 있다.
이번 검증에서 두 파일을 합치거나 LabelLayout.Print에 새 Prepare 호출을 추가하지 않았다.
