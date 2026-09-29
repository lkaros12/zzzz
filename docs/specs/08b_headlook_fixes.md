# 수정: 08 검수 지적

원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.

## 고칠 것
1. **고개 IK 가 안 불림 (확실)**: `PlayerAnimator.cs:92` `OnAnimatorIK` 는 Animator 와 같은 GameObject 에만 온다. Animator 는 자식 `Model`, PlayerAnimator 는 루트.
   Model 에 붙는 아주 작은 중계 스크립트 1개(예: `PlayerLookAtRelay`, OnAnimatorIK → PlayerAnimator 메서드 호출)를 허용한다 (메인 결정). 빌더가 붙인다.
   자기검사: Model 에 중계 스크립트가 있고 PlayerAnimator 를 참조한다.
2. **VisionField aim 을 Awake 에서만 찾음**: `VisionField.cs:59,86`. `Sample()` 에서 `if (!aim) aim = GetComponent<PlayerAim>();`.
3. **던지기 방향**: `PlayerInteractor.cs:378,380` `transform.forward` → 조준 방향 (몸 정렬 후 던지거나 AimDirection 사용, 짧은 쪽).
4. **방패 판정**: `PlayerShield.cs:103` `transform.forward` → `AimDirection` (몸이 돌아가는 0.2초 동안 옛 방향으로 판정되지 않게).
5. **공격 모션 동안 몸 방향 유지**: `PlayerCombat.cs:234` FaceAim 이 한 프레임만 덮고 다음 프레임 Motor 가 이동 방향으로 되돌려 휘두르는 도중 몸·`SwingPivot` 궤적이 돈다.
   공격 후 공격 간격(또는 휘두르기 표시 시간) 동안 PlayerMotor 가 몸을 조준 방향으로 유지하게 (Motor 의 기존 "조준 방향 유지" 조건에 공격 중 추가 — 한 곳에서만 회전).
6. **발목 자기검사 거짓 통과**: `V01SceneBuilder.cs:3808` `MeasureAnkleDeviation` 는 `AnimationMode.SampleAnimationClip` 이 Foot IK 를 안 거쳐 수정과 무관하고 기준도 정강이 대비가 아니다. **이 검사를 지우고** Walk/Run 상태 `iKOnFeet == true` 검사만 남긴다 (실제 발목 각도는 메인이 재생 모드에서 잰다).
7. **EyeOffset 이 좀비에게도 적용됨**: `Vision.cs:19` 0.3→0.5 가 Zombie.cs:777/1019, PlayerAssassinate.cs:115 에도 퍼진다. **좀비 눈높이는 원래대로(0.3)** 두고, 플레이어만 발 기준 1.15m 가 되게 (플레이어 경로만 다른 오프셋 또는 발 기준 절대 높이). 목표점(플레이어 눈) 1.15m 는 유지.
8. **암살 중 시야 방향**: `VisionField.cs:86` 암살 중(PlayerAssassinate 가 몸을 좀비 쪽으로 돌림)에는 몸 방향(transform.forward)을 쓴다.
9. **좀비 불러오기 높이 (기존 버그)**: `SaveSystem.cs:424` `1.05f` 하드코딩 → 좀비 cc.height/2 (+ 작은 여유). 불러온 좀비가 0.25m 묻힌다.

## 출력
변경 파일 목록과 요약만. 1~9 한 줄씩, 막힌 것.
