# 송장 OCR 판독과 숫자 규칙 검증

기준은 `봉우리_SRS_v1.6_초안.docx` FR-04·FR-07이다. 원본 문서는 수정하지 않았다.
현재 구현은 지도학습 CNN을 ONNX로 내보내 Unity Inference Engine CPU에서 실행한다.

## 실행 경로

`CellController.ReadStationLoop` → `LabelReader.Read` → 송장 유무 ONNX로 윗면 확인 → 번호·지역명 촬영 → OCR ONNX → `LabelValidator.Apply` → `BoxInfo.destination` 기록.

- `LabelNumber.onnx`: 회색조 `[1,1,32,186]`, 위에서 아래 순서, 픽셀 /255. 출력 `[1,46,11]` CTC 로짓. 클래스 0~9와 blank=10.
- `LabelRegion.onnx`: 회색조 `[1,1,32,96]`. 출력 `[1,3]` 로짓. 클래스 순서는 서울·대전·부산.
- CTC의 연속 중복 출력을 합치고 blank를 제거한다. 한 글자 구간의 최고 확률이 0.8 미만이면 `?`로 기록한다. 이는 확률 보정이나 모든 훼손 영상에 대한 보장을 뜻하지 않는다.
- 번호 길이가 5보다 짧으면 `?`로 채우고, 길면 `?????`로 기록한다. 모름이 남으면 최대 2회 재촬영하고 2초 제한을 넘으면 ReadFail/NG로 보낸다.
- 정상적으로 읽은 번호는 ASCII 숫자 5자리인지 검사하고, 앞 두 자리와 주소 지역을 대조한다. `11 서울 A`, `22 대전 B`, `33 부산 C`. 뒤 세 자리는 목적지 결정에 사용하지 않는다. 체크디지트는 없다.
- 코드 위반/주소 불일치는 재판독 없이 RuleViolation/NG, 송장 미검출은 NoLabel/NG다. 이전 공정의 예외 사유도 유지한다.
- OCR는 `labelNumber`, `labelRegion`, TMP의 text를 읽지 않는다. 송장 좌표는 `LabelLayout`과 `BoxInfo.label`로 조준한다.

## SRS에 적힌 방식과의 차이

SRS는 ML-Agents가 숫자 32×32 칸 5개와 지역명 1칸을 순서대로 읽도록 제안한다. 이 구현은 앞서 선택한 지도학습·ONNX 방식으로 번호 전체 186×32와 지역명 96×32를 읽는다. 카메라 위치를 흔들어 재시도할 때도 두 영역을 함께 다시 읽는다. 따라서 동작·규칙·정확도 기준과 별개로 ML-Agents 및 모름 칸만 재촬영한다는 구현 방식까지 일치한다고 볼 수 없다.

송장 ROI와 글자의 방향은 알려진 배치 좌표를 이용한다. 임의의 실제 송장에서 위치와 회전을 찾는 검출기는 포함하지 않는다. 현재 글꼴·배치용 모델이므로 글꼴/배치/카메라 규격 변경 시 데이터를 재생성하고 재학습해야 한다.

## 재현

학습 환경은 기존 mlagents conda 환경의 Python 3.10, PyTorch 2.1.1 CPU다. 의존성은 `scripts/requirements-label-presence.txt`와 같다.

```powershell
conda activate mlagents
python scripts/train_label_reader.py
```

기존 `LabelReaderDataset` 3,000개 상자의 번호·지역 이미지로 학습했다. 종류별 2,700개 학습/300개 검증, 별도 시드의 `LabelReaderDatasetTest` 300개로 평가했다. 검증 정확도가 가장 높은 체크포인트를 선택했으며 테스트 표본으로 선택하지 않았다. 번호 문자열 중복을 배제한 분할은 아니므로 미지 글꼴/미지 번호 체계 일반화 성능으로 해석하지 않는다.

Python 테스트: 번호 전체 296/300, 지역명 300/300. 모델은 `results/label_reader`, 배포본은 `Assets/FactoryMLModels/LabelNumber.onnx`, `LabelRegion.onnx`다.

Unity를 Play 정지 상태에서 `Tools > Factory ML > OCR 판독 및 규칙 검증`을 실행한다. 모델을 가져오고 새 시드 20261107의 50개 송장을 실제 렌더링해 읽는다. 정답 필드를 다른 문자열로 덮어써 정답을 참조하는 경로도 차단한다. 정상/위반/재판독/미부착 경로와 순수 규칙 검사 결과는 아래에 저장한다.

- `results/label_reader_unity/latest.json`: 요약
- `predictions.csv`: 50개 OCR 결과
- `read_pipeline.csv`: 윗면 확인부터 목적지·예외 결정까지
- `number_0.png`~`number_4.png`: 촬영 확인용 번호 이미지

2026-11-02 최종 실행 결과: 번호 전체·앞 두 자리·지역명 각각 50/50, 순수 규칙 검사 9/9, 판독 경로 검사 15/15 통과했다. 판독 경로에는 코드 23·12·주소 불일치를 각각 3회, 4자리 번호를 3회, 정상·빈 글자·미부착을 각각 1회 포함한다. 빈 글자와 4자리 번호는 재판독 2회 뒤 ReadFail, 미부착은 재판독 없이 NoLabel이었다. 기록된 최대 판독 시간은 47.61ms였다(정지 상자 Editor 실행, 프레임 대기 시간은 운전 시 다시 측정해야 한다).

이 검증은 정지 상자를 이용한 Editor 렌더·실제 ONNX 실행 검사다. 컨베이어에서 실제로 멈추고 푸셔가 NG 레인까지 배출하는 TC-07 전체 동작이나 연속 투입 TC-13 완료를 의미하지 않는다.

## 공정 연결

검증 메뉴가 `Assets/prefabs/Process/LabelReaderStation.prefab`도 생성한다. 번호·지역 카메라, OCR 모델, 송장 유무 모델과 전용 판독 리그가 연결되어 있다. 이 프리팹을 공정 씬에 배치하고 `CellController`의 Reader에 루트의 LabelReader를 연결한다. 교정용 FaceCameraRig와 판독 리그는 공유하지 않는다. 판독 리그는 정지 상자의 BoxCollider 바운드에 맞춰 카메라를 배치한다.

`LabelLayout.sizeTweak`은 생성 때의 글자 크기 보정값을 에셋에 저장한다. 학습 영상과 실제 BoxFeeder 인쇄 크기가 다시 달라지는 것을 방지한다. 생성/보정 후 기존 상자는 다시 인쇄하거나 새로 생성해야 한다.
