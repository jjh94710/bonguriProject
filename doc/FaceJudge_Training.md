# 송장면 판정 학습

카메라/센서/행동 순서는 Top(0), Front(1), Back(2), Left(3), Right(4), Bottom(5)이다.
Bottom은 5면 미검출이며 바닥 부착과 미부착을 합친 결과다. 다음 공정에서 뒤집고 재검사한다.
기존 카메라 참조는 FaceCameraRig의 순서 버전으로 한 번만 교환한다.
기존 ONNX는 입력 순서가 다를 수 있으므로 새 순서로 다시 학습해야 한다.

## Unity 설정

1. 패키지 복원이 끝나고 컴파일된 뒤 기존 씬을 별도 학습 씬으로 Save As 한다.
2. 학습 씬에서는 투입기/컨베이어/로봇 및 기존 상자를 비활성화한다. 카메라가 학습용 상자만 보도록 ScanTarget 레이어를 사용한다.
3. FaceCameraRig가 있는 오브젝트를 선택하고 Tools > Factory ML > 선택한 리그에 면 판정 Agent 설정을 실행한다.
4. 이 버튼은 카메라 5개, RGB 센서 5개, FaceJudge Behavior, 이산 행동 1분기(크기 6)를 설정한다.
   같은 오브젝트의 LabelDatasetGenerator에서 Boxes와 Label Child Name을 복사한다.
   여러 프리팹은 학습에서 균등하게 뽑고, Weight가 0인 항목은 제외한다.
5. FaceJudgeAgent의 Training Mode를 켠다. Boxes의 Label Local Normal과 Label Child Name이 실제 프리팹과 일치하는지 확인한다.
6. DecisionRequester는 붙이지 않는다. Agent가 샘플마다 한 번씩 판단을 요청한다.

## Python 및 학습

Unity 패키지는 ML-Agents 4.0.0(release 23)을 사용한다. 같은 release 23의 Python 환경을 사용한다.
Python 3.10.12 가상환경에서 실행한다.

```powershell
py -3.10 -m venv .venv-face
.\.venv-face\Scripts\Activate.ps1
python -m pip install mlagents==1.1.0
mlagents-learn config/face_judge.yaml --run-id=face_judge_01
```

연결 대기 메시지가 나오면 Unity Play를 누른다. 시각 관측이므로 그래픽 렌더링이 필요하다.
정답 +1, 오답 -0.8로 한 번의 판정을 한 에피소드로 학습한다.
TensorBoard의 FaceJudge/Accuracy로 학습 정확도를 보고, 별도 시드/상자 표본으로 실제 정확도를 검증한다.
학습 완료 시 results/face_judge_01/FaceJudge.onnx를 Assets로 가져온다.

## 공정 연결

공정 리그에도 같은 설정 버튼을 실행하고 Training Mode는 끈다.
Behavior Parameters의 Model에 ONNX를 넣고 Behavior Type을 Inference Only로 설정한다.
상자가 촬영 위치에서 멈춘 뒤 agent.RequestFace(box)를 호출한다.
true는 요청 접수, false는 모델 없음/처리 중/잘못된 설정이다. 결과는 비동기다.
FaceJudged 이벤트를 구독하거나 IsPending이 false가 될 때 box.judgedFace를 읽는다.
Bottom이면 이후 뒤집기 로직으로 연결한다. 이 변경은 뒤집기나 CellController.HasPolicy를 구현하지 않는다.
HasPolicy는 별도의 회전 정책(CorrectionAgent)용이다.
정답면과 송장 유무는 보상 계산에만 사용하며 영상 관측에는 넣지 않는다.

현재 코드 작성 시 Unity에서 실제 컴파일, 학습, ONNX 추론은 실행하지 않았다.

## 송장의 위치·방향 변화

FaceJudgeAgent와 LabelDatasetGenerator의 **Label Placement**를 켜면,
상자 회전과 별개로 송장(종이·글자·바코드 묶음) 자체를 면 안에서 이동·회전한다.
설정 버튼을 다시 실행하면 생성기의 Label Placement 설정도 Agent로 복사된다.

- Enabled: 기본 켜짐. 끄면 프리팹의 원래 송장 배치를 사용한다.
- Rotation Range: 기본 180도. 원래 방향 기준 -180~180도에서 뽑는다.
- Position Range: 기본 1. 면 안의 배치 가능한 영역 전체를 사용한다. 0이면 원래 위치를 기준으로 경계만 보정한다.
- Edge Margin: 기본 0.02. 각 변 길이의 2%를 가장자리 여백으로 둔다.

렌더러 경계로 종이와 글자가 면 밖으로 나가지 않는 위치를 계산한다.
면보다 커지는 회전은 다시 뽑고, 유효한 각도가 없으면 원래 회전으로 시도한다.
원래 배치도 여백 안에 들어가지 않거나 경계를 찾지 못하면 경고 후 원래 배치를 유지한다.
송장 크기는 바꾸지 않으므로 긴 송장일수록 가능한 회전 각도가 제한된다.
직육면체 상자의 BoxEntry.Label Local Normal은 실제 송장면의 로컬 축 방향으로 설정해야 한다.
상자 본체와 송장이 분리된 Renderer 계층이어야 하며, Label Child Name은 글자 하나가 아닌 송장 전체 묶음을 가리켜야 한다.

출력은 계속 6개 면 분류다. 송장 위치 좌표나 회전 각도를 출력하는 모델은 아니다.
기존 PNG에는 변화가 소급 적용되지 않으므로 데이터셋을 다시 생성하고 모델을 다시 학습한다.

## 짧은 시험 후 본 학습

현재 설치된 conda 환경 이름은 `mlagents`다. 기존 환경을 그대로 사용한다.
위치/충돌 수정 이전의 face_judge_01(4,098스텝)은 정확도 요약이 없어 성능 통과 근거로 사용하지 않는다.

```powershell
conda activate mlagents
cd /d C:\Users\Codelab\Desktop\unity\bonguriProject
mlagents-learn config/face_judge_quick.yaml --run-id=face_judge_quick_01
```

터미널이 연결을 기다릴 때 Unity Play를 누른다. 시험은 20,000스텝이며 1,000스텝마다 요약,
5,000스텝마다 체크포인트를 저장한다. CPU/렌더링 속도에 따라 소요 시간은 다르다.
결과 경로: results/face_judge_quick_01/FaceJudge.onnx.

짧은 실행에서는 생성 높이 고정, 5개 영상의 상자/송장 노출, 면별 판정, Bottom(미검출) 처리,
회전/위치가 달라진 송장에 대한 판정까지 확인한다. 전체 정확도만 보지 말고 각 면별로 확인한다.
Training Mode에서 수동 Heuristic을 돌린 정확도는 정답을 그대로 반환하므로 모델 평가로 사용하지 않는다.
별도 평가 코드 없이 ONNX를 공정에서 확인하려면 Training Mode를 끄고 Inference Only와 Model을 설정한 뒤,
정지한 상자마다 RequestFace를 호출하여 실제 면과 judgedFace를 비교한다.

정상 동작이 확인되면 기존 500,000스텝 설정으로 새 학습을 실행한다.

```powershell
mlagents-learn config/face_judge.yaml --run-id=face_judge_full_01
```

시험용 데이터와 다른 표본으로 정확도를 확인한 뒤 최종 모델을 사용한다.

## 5,000개 목표 진행 표시와 결과 점수

FaceJudgeAgent의 Show Training Progress는 기본 켜짐이며 Game 화면 하단에 표시한다.
Target Boxes 기본값은 5,000, Report Every Boxes는 250이다.
생성 수와 판정 완료 수를 따로 세고, 완료 수 기준 진행률/정답·오답/누적 정확도/최근 500개 정확도/
점수(정답 +1, 오답 -0.8)/평균 보상/면별 정답 수/경과 시간/예상 남은 시간을 표시한다.
한 상자당 영상은 5개지만 판정은 한 번이므로 5개 이미지가 아닌 상자 1개로 집계한다.
트레이너 미연결 또는 수동 실행 경고가 뜨면 그 점수를 학습 성능으로 해석하지 않는다.
학습 도중 정확도는 훈련 표본 점수이며 최종 모델의 별도 평가 정확도와 다르다.

250개마다 Unity Console에 요약을 출력하고 results/face_judge_monitor/<시각>_<세션ID>.csv에 기록한다.
5,000개 도달 시 및 정상 비활성화/Play 종료 시에도 마지막 기록을 저장한다.
CSV의 accuracy/recent500_accuracy는 0~1, progress_percent는 0~100이다.
집계는 현재 Play 세션의 실제 완료 수이며 trainer를 --resume한 누적 step과 합산하지 않는다.
표시 목표 도달 자체가 트레이너를 종료하지는 않는다.

5,000스텝 짧은 학습을 실행하려면:

```powershell
conda activate mlagents
mlagents-learn config/face_judge_5000.yaml --run-id=face_judge_5000_01
```

프로젝트 폴더에서 실행하고 연결 대기 후 Unity Play를 누른다.
trainer의 max_steps는 상자 개수 제한이 아닌 학습 step 예산이며 업데이트 단위로 초과할 수 있다.
정확한 생성/판정 개수는 화면과 CSV를 기준으로 확인한다.
TensorBoard에는 FaceJudge/Accuracy, FaceJudge/Recent500Accuracy, FaceJudge/Accuracy_<면>도 기록한다.

## 한 번에 상자 한 개만 학습

Boxes 목록의 유효한 프리팹 중 한 개를 무작위 선택해 생성하고, 한 번 판정한 뒤 다음 상자로 교체한다.
이전 샘플은 새 샘플 생성 전에 비활성화하고 Hierarchy에서도 숨긴 뒤 삭제한다.
Hide Scene Boxes During Training(기본 켜짐)은 학습 씬에 남아 있는 ParcelBox 이름의 상자와
BoxInfo가 있는 기존 상자를 학습 시작 시 숨기고 종료 시 복원한다. 다른 씬과 프리팹 에셋은 수정하지 않는다.
공정용 Training Mode가 꺼져 있을 때는 적용하지 않는다.
Game 화면 하단의 현재 상자 이름으로 선택된 종류를 확인할 수 있다.

## 빠른 교체로 겹쳐 보일 때

Game 화면 하단의 **천천히 보기**를 켜면 상자 한 개를 실제 시간 0.5초 동안 유지한 뒤 판정한다.
Inspector의 Slow Preview / Preview Hold Seconds에서도 켜거나 0.2~5초로 조절할 수 있다.
확인 후 끄면 원래 학습 속도로 돌아간다. 학습 상자의 Rigidbody 보간은 비활성화한다.
같은 상자가 유지되는 동안에도 윤곽이 여러 개 보이면 실제 촬영/렌더링 문제를 추가 확인해야 한다.
이 기능은 겹침을 자동 검출하거나 정상 여부를 보장하는 기능이 아니다.

## 반대쪽 글자가 비쳐 보이는 문제

TextMesh 송장 글자에는 Factory/DepthTestedLabelText 셰이더를 사용한다.
깊이 비교는 LEqual로 상자 뒤의 글자를 가리고, 정점 색과 동적 글꼴 아틀라스는 유지한다.
원본 글꼴 재질을 수정하지 않고 생성한 상자의 글자마다 임시 재질을 만들어 파괴 시 정리한다.
ML-Agents 학습 샘플, PNG 데이터셋 생성, ShippingLabelNumber가 있는 공정 상자에 적용된다.
이미 생성한 PNG/기존 ONNX에는 소급 적용되지 않는다. 수정 전 표본으로 학습한 모델은 새 표본으로 재검증한다.

Play를 정지한 뒤 Tools > Factory ML > 송장 글자 가림 검증으로 실제 URP 렌더링을 확인할 수 있다.
상자만 찍은 기준 영상에 대해 뒤쪽 글자는 차이가 거의 없어야 하고 앞쪽 글자는 보여야 PASS다.

## 측면 보조 조명

학습 씬의 ScanStation/FaceFillLights 아래에 앞·뒤·좌·우 스폿 조명 4개를 배치했다.
FaceCameraRig의 Use Side Fill Lights로 켜고 끄며 Side Fill Intensity(기본 0.2),
Side Fill Distance(기본 0.35m), Side Fill Color로 조절한다.
그림자는 끄고 카메라의 Culling Mask(현재 ScanTarget)에만 조명을 적용한다.
값을 바꾼 뒤 Inspector의 측면 보조 조명 4개 추가 / 갱신 버튼으로 갱신한다.
카메라 리그 갱신/Play 시작 때도 조명이 생성·갱신되며 기존 조명을 재사용한다.
새 조명 조건의 모델은 실제 공정에도 같은 보조 조명을 둔 상태에서 검증한다.

## 정답·관측·보상 연결 검증

FaceJudgeAgent의 Audit Decisions(기본 켜짐)는 처음 Audit Sample Limit(기본 12개)를 기록한다.
AuditedFaceCameraSensor가 실제 센서 Update 결과 PNG를 복사하므로, 다음 상자의 미리보기와 섞이지 않는다.
Game 화면의 검증 패널은 마지막 기록 영상을 고정 표시한다.
저장 위치는 results/face_judge_audit/<세션>/<상자 번호>/이며 영상 5장과 result.json을 저장한다.
plannedFace는 배정한 정답, geometryFace는 실제 LabelPaper 메쉬의 얇은 축에서 계산한 방향이다.
geometryMatches가 false면 영상의 정답에 문제가 있을 수 있다. rewardMatches는 해당 에피소드의
실제 누적 보상이 정답 +1/오답 -0.8과 같은지 비교한다.
policySource가 Heuristic이면 모델 성능 평가에서 제외한다.
검증 센서를 연결하지 않은 기존 씬은 Play를 정지한 뒤 리그를 선택하고 면 판정 Agent 설정 메뉴를 다시 실행한다.

2026-11-02 로컬 검증에서 Process 프리팹의 수동 법선 설정과 실제 종이 방향 불일치가 발견됐다.
학습과 이미지 생성은 이제 프리팹의 실제 종이 법선을 자동 계산하고, 메쉬를 찾지 못한 경우에만 수동 설정을 사용한다.
학습 샘플은 상자 교체 후 렌더 프레임을 기다리고 Rigidbody 자세도 동기화한다.
즉시 갱신이 필요한 샘플 렌더러에는 개별 MaterialPropertyBlock을 적용해 GPU Resident Drawer의 캐시 경로에서 제외한다.
수정 후 12개 실제 센서 표본에서 방향/정답 및 정답 보상 연결 일치를 확인했다. Heuristic 실행이므로 모델 정확도 검증은 아니다.
수정 이전 학습의 낮은 정확도를 학습량 부족만으로 판단하면 안 된다. 새 정답/촬영 조건으로 새 run-id에서 다시 학습한다.
외부에서 스크립트를 바꾼 뒤 자동 반영이 안 되면 Play 정지 후 Assets > Refresh(Ctrl+R)로 새로고침한다.

## 오답 보상 -0.8 실험

정답 +1, 오답 -0.8로 변경했다. Agent 보상, 누적 점수, 평균 보상, JSON 검증에 같은 보상 정의를 사용한다.
평균 보상 = 1.8 × 정확도 - 0.8이며, 정확도 약 44.4%에서 평균 보상이 0이 된다.
기존 +1/-1 실행과 보상 숫자만 비교하면 안 된다. 같은 검증 표본의 면별 정확도로 비교한다.

## 고정 ONNX 평가

Play 정지 후 Tools > Factory ML > 모델 평가에서 체크포인트를 선택하고 Play한다.
모델을 Assets/FactoryMLModels로 가져오고 Behavior Type을 Inference Only로 설정한다.
Python 트레이너 없이 결정적 추론으로 360개를 평가한 뒤 샘플 생성을 멈춘다.
각 면 60개, 각 프리팹 120개이며 Bottom 60개 중 30개는 미부착이다.
송장 위치와 회전은 Evaluation Seed로 생성한다. 결과 JSON은 results/face_judge_evaluation에 저장한다.
confusionMatrix는 행=정답, 열=예측이며 Top/Front/Back/Left/Right/Bottom 순서의 6×6 배열이다.
느리게 관찰하려면 ScanStation의 FaceJudgeAgent에서 Slow Preview를 켠다.
학습 재개 전에는 같은 메뉴의 학습 모드로 복귀를 실행한다.
이 메뉴는 모델 연결을 해제하고 Behavior Type을 Default로 되돌린다.

학습 중 표본 정확도가 높았다는 이유만으로 마지막 ONNX를 사용하지 않는다.
face_judge_quick_reward08_01 실행의 14,000스텝 구간 정확도는 높았지만 이후 하락했다.
14,000스텝 ONNX는 저장되어 있지 않으며 14,999스텝 모델을 같은 성능이라고 볼 수 없다.

2026-11-02 Unity 고정 ONNX 평가 결과(시드 20261103, 각 360개):

| 모델 | 정답 | 정확도 | 면별 정답(각 60개) |
| --- | --- | --- | --- |
| FaceJudge_14999 | 119/360 | 33.1% | 59, 0, 60, 0, 0, 0 |
| FaceJudge_20001 | 120/360 | 33.3% | 60, 0, 60, 0, 0, 0 |

두 모델 모두 Top 일부를 제외하면 Back으로 답하는 편향을 보였다. 실사용에 충분한 모델이 아니다.
최종 모델의 첫 12개 실제 센서 기록은 정답 방향과 보상 연결이 모두 일치했다.
보상 계산 오류는 확인되지 않았으나 학습 붕괴 원인까지 확정한 것은 아니다.
