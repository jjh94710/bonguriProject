# bonguriProject

> [참조:SRS](/doc/봉우리_SRS_261029.pdf) <br />
> [참조:WBS](/doc/봉우리_WBS_261028.pdf) <br />     <!-- TODO: 최신화 -->
> [참조:클래스설계](/doc/봉우리_ClassDesign_261029_2.pdf) <br />
> [참조:ML설계안](/doc/봉우리_MLTraining설계안_261029.pdf) <br />
![참조:클래스다이어그램](/doc/봉우리_물류분류자동화_스크립트구성도_v0.4.png) <br />
![참조:클래스다이어그램](/doc/봉우리_물류분류자동화_처리흐름도_v0.4.png) <br />

<br />
상자 크기 변경 : 6*6*6 --> 5*6*7
BoxAttacher() : 흡착 그리퍼 동작으로 이용 예정, AgvContainer --> ToteBase (각 분기 컨베이어 끝 지점에 대기 중인 컨테이너로 진입하는 박스 옮길 로봇 투입) 지점에 사용 - 분류 이전 단계 사용 x (부착점 5cm 이내 범위도 허용 안 함)