# 카메라별 송장 유무 모델

카메라 순서는 0 Top, 1 Front, 2 Back, 3 Left, 4 Right다.
같은 CNN에 각 사진을 한 장씩 넣어 송장 검출 점수 5개를 얻는다.
검증 데이터에서 정한 threshold 이상인 카메라 중 점수가 가장 높은 면을 선택한다.
모두 기준 미만이면 Bottom(미검출)이다. 아랫면 부착과 미부착을 구분한 결과는 아니다.
점수는 sigmoid 출력이며 실제 성공 확률로 보정된 값은 아니다.

## 데이터와 학습

저장소에 포함된 모델만 실행할 때는 Python 설치가 필요 없다.
Assets/Scenes/bongyuri mlagentBox.unity를 열고 Play하면 360상자 평가를 실행한다.
연결을 다시 설정하려면 송장 유무 메뉴의 2번을 선택한다.
로컬 results에 새 모델이 있으면 그 모델을 가져오고, 없으면 포함된 ONNX와
LabelPresence.deployment.json을 사용한다.
Python에서 재학습하려면 Python 3.10 환경에
`pip install -r scripts/requirements-label-presence.txt`로 의존성을 설치할 수 있다.

1. 학습 씬에서 Play를 정지한다.
2. Tools > Factory ML > 송장 유무 > 1. 데이터셋 1800상자 생성 준비를 선택한다.
3. Play하고 생성 완료를 기다린다. 상자 한 개씩 5장, 총 9000장을 저장한다.
4. Play를 정지하고 프로젝트 폴더에서 다음 명령을 실행한다.

```powershell
conda activate mlagents
python scripts/train_label_presence.py
```

ML-Agents 트레이너를 실행하는 방식이 아니다. 기존 환경의 PyTorch로 지도학습한다.
완료된 최신 LabelPresenceDataset 하위 세션을 기본으로 선택한다.
특정 세션은 `--dataset LabelPresenceDataset/<세션>`으로 선택한다.
출력 폴더는 기본 results/label_presence이며 재실행 시 해당 폴더의 모델/기록을 갱신한다.
별도 실험을 보존하려면 `--output results/<새 이름>`을 사용한다.

각 상자의 5장은 반드시 같은 split에 들어간다. train 1260상자, val 180상자,
test 360상자이며 각 묶음에 3종 프리팹과 6면이 균등하게 포함된다.
학습 데이터의 양성/음성 불균형은 BCEWithLogitsLoss의 pos_weight로 보정한다.
모델 체크포인트와 검출 기준은 val만 보고 고른다. test는 마지막에 한 번 평가한다.
기존 FaceJudgeAgent의 실제 종이 법선, 송장 위치/각도 변화, 프레임 대기,
샘플 고정, 장면 상자 숨김을 공통으로 사용한다.
송장이 있는 면은 해당 카메라 한 곳에 보이는 축 정렬 배치를 전제로 정답을 만든다.
다른 프리팹, 비스듬한 공정 자세, 조명 조건으로의 일반화는 별도 평가가 필요하다.

## Unity 실행

1. Tools > Factory ML > 송장 유무 > 2. ONNX 연결 및 360상자 평가 준비를 선택한다.
2. Play한다. 학습 때와 다른 시드로 새 상자 360개(면당 60개)를 검사한다.
3. 화면에서 카메라별 검출 점수, 선택 면, 면별 정확도를 확인한다.

results/label_presence/label_presence.onnx와 deployment.json을 읽어
Assets/FactoryMLModels/LabelPresence.onnx와 LabelPresenceDetector에 연결한다.
ML-Agents BehaviorParameters는 Heuristic Only로 두지만 이 경로에서는
RequestDecision을 호출하지 않는다. 판정은 LabelPresenceDetector의 ONNX가 담당한다.
표시되는 보상은 비교용 집계이며 지도학습의 업데이트에 사용하지 않는다.

공정 코드에서 상자가 정지하고 자세가 안정된 뒤 `LabelPresenceDetector.Judge(rig)`를 호출할 수 있다.
반환값과 FaceJudged 이벤트가 선택 면을 전달하고 Scores에 카메라별 점수가 남는다.
카메라 렌더링과 추론은 Unity 메인 스레드에서 동기 실행한다.
기존 FaceJudgeAgent를 공정 연결에 쓰려면 Training Mode를 끄고 Presence Detector를 연결한 뒤
RequestFace(box)를 호출한다. 결과는 기존 FaceJudged 이벤트와 box.judgedFace에 기록된다.

입력 규약은 96×96 RGB, 값 0~1, NCHW, 위에서 아래 순서다.
Unity GetPixels32의 세로 순서를 뒤집어 PNG/Pillow 학습과 맞춘다.
모델 출력은 `[1,1]` sigmoid 점수이며, 6개 면을 출력하는 이전 ML-Agents ONNX와 호환되지 않는다.

기존 PPO로 돌아가려면 Tools > Factory ML > 모델 평가 > 학습 모드로 복귀를 선택한다.

## 2026-11-02 검증 결과

데이터셋: LabelPresenceDataset/20261102_161035_9612803b, 총 9000장.
25 epoch 중 검증 면 정확도와 검증 손실로 고른 가중치를 ONNX로 내보냈다.

| 검사 | 결과 |
| --- | --- |
| 분리한 테스트 이미지 1800장 | 양성 300/300, 음성 1500/1500 |
| 분리한 테스트 상자 360개 | 360/360, 각 면 60/60 |
| Unity ONNX 새 시드 20261104 | 360/360, 각 면 60/60 |
| 같은 자세에서 송장만 제거한 비교 30쌍 | 30/30, 원래 정답 면 → Bottom |
| 저장된 Unity 영상 50장의 Unity/PyTorch 출력 비교 | 최대 절대 차이 약 3.64e-10 |
| 같은 50장의 ONNX/PyTorch 출력 비교 | 최대 절대 차이 약 5.96e-8 |

송장 제거 검사는 평가 모드의 처음 30개 가시 송장 표본에 자동 실행된다.
results/label_presence_pair_check/<세션>/report.json과 처음 5쌍의 영상을 저장한다.
`python scripts/verify_label_presence.py`로 최신 비교 기록의 출력 일치를 확인한다.
최초 Unity 평가는 results/face_judge_evaluation/LabelPresence_20261102_161607.json에 저장했다.

이 수치는 현재 세 프리팹과 촬영 조건의 합성 표본에 대한 결과다.
새 송장 디자인, 비슷한 흰 스티커, 실제 카메라 영상, 가림/반사, 다른 조명에 대한 성능은 검증하지 않았다.
현재 모델은 송장 유무 분류이며 글자 판독이나 송장 위치의 박스 좌표를 출력하는 검출기는 아니다.
