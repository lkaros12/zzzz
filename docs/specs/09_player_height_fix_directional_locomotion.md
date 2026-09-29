# 작업: 캐릭터 키 측정 버그 + 옆걸음·뒷걸음 방향 이동 블렌드

원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.
`V01SceneBuilder.cs` 전체를 읽지 말 것 (플레이어 모델 배치·`MeasureModelHeight`·`EnsurePlayerController`·클립 임포트·관련 자기검사만).

## A. 키 측정 버그 (메인 실측, 확실)
재생 중 플레이어 모델을 `SkinnedMeshRenderer.BakeMesh` 로 구워 월드 정점 높이를 재니 **2.25m** (minY 0.04, maxY 2.29), 모델 localScale **2.19**.
즉 `MeasureModelHeight` 가 character01 원래 키를 약 0.55m 로 잘못 잰다 (실제 약 1.03m). 좀비(zombi1)는 0.999m 로 맞게 잰다.
- 원인을 찾아 고친다 (스킨 메쉬 bounds·bind pose·FBX 단위 차이 추정 — 확인 후 수정). 플레이어 키 측정은 **BakeMesh 월드 정점** 또는 발-머리꼭대기 뼈 기반처럼 실제 보이는 높이로.
  좀비 측정 결과(0.999)가 바뀌지 않게 (좀비 쪽 경로는 건드리지 않거나 같은 값이 나오는지 확인).
- 자기검사의 "몸 키 1.2 ± 0.02" 가 **실제 보이는 높이**를 재도록 (지금은 잘못된 측정값 × scale 이라 거짓 통과했다).

## B. 옆걸음·뒷걸음 (사용자 요구)
새 클립 (메인이 넣어 둠, Mixamo Without Skin, In Place):
`character01@WalkBack`(Walking Backwards), `@WalkLeft`(Left Strafe Walking), `@WalkRight`(Right Strafe Walking),
`@RunBack`(Running Backward), `@RunLeft`(Left Strafe), `@RunRight`(Right Strafe).
- 임포트: 기존 플레이어 클립과 같은 방식 (Human / CreateFromThisModel / loopTime / 루트 모션 잠금). `PlayerClipNames` 에 추가.
- 컨트롤러: Idle/Walk/Run 을 **하나의 2D Freeform Directional 블렌드 트리 상태 `Locomotion`** 으로 바꾼다.
  파라미터 `MoveX`, `MoveZ` (몸 기준 로컬 이동 속도 / 걷기 기준 속도, 걷기 = 1, 달리기 ≈ 1.86).
  점: (0,0) Idle, (0,1) Walk, (0,-1) WalkBack, (-1,0) WalkLeft, (1,0) WalkRight, (0,1.86) Run, (0,-1.86) RunBack, (-1.86,0) RunLeft, (1.86,0) RunRight. Foot IK 켬.
  Attack / AttackOneHand / Death 전이는 Locomotion 기준으로 그대로. 기존 `Speed` 파라미터는 다른 곳에서 안 쓰면 제거, 쓰면 유지.
- `PlayerAnimator`: 실제 수평 이동(순간이동 제외)을 몸 로컬 좌표로 바꿔 MoveX/MoveZ 에 부드럽게 넣는다.
- **몸 회전 규칙 (08 을 바꾼다)**: 이제 뒷걸음·옆걸음이 있으니 **몸은 항상 조준 방향을 향하되**, 이동 중에는 이동 방향 쪽으로 최대 **45°** 까지만 틀어 자연스럽게 (고개 IK 가 나머지를 조준 쪽으로). 멈춤·공격 중은 조준 방향 그대로.
  → 기존 80° 규칙 순수 함수와 자기검사를 이 규칙으로 바꾼다 (이동 방향이 조준과 0°/90°/180° 인 경우 몸 방향 검사).
- 자기검사: 블렌드 트리 점 9개와 클립 연결, 새 클립 6개 임포트 설정.

## 출력
변경 파일 목록과 요약만. A 원인과 수정 후 기대 scale, B 담당 코드 한 줄씩, 명세와 다르게 한 부분과 이유, 막힌 것.
