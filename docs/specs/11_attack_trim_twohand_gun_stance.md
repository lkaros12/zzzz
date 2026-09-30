# 작업: 공격 클립 휘두르는 구간만 사용 + 양손 무기 공격 + 총 조준 자세·사격 모션

원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.
`V01SceneBuilder.cs` 전체를 읽지 말 것 (플레이어 클립 임포트·`EnsurePlayerController`·관련 자기검사만).

## 입력 (메인이 넣어 둠, Mixamo Without Skin 30fps)
기존: `character01@Attack`(Stabbing), `@AttackOneHand`(Torch Attack 02).
새로: `@AttackTwoHand`(Standing Melee Attack Horizontal, 양손 도끼 가로 베기), `@PistolAim`(Pistol Idle, 겨눈 채 서기, 루프),
`@PistolFire`(Shooting Gun), `@RifleAim`(Rifle Aiming Idle, 루프), `@RifleFire`(Firing Rifle).

## 메인 실측 (에디트 모드 AnimationMode 샘플, 오른손 속도 m/s)
- Attack 2.63s: 첫 찌르기 최고 0.70s, 구간 0.40~0.97 (뒤에 반복 찌르기 더 있음)
- AttackOneHand 2.33s: 최고 0.63s, 구간 0.57~0.93 (0.3s 부터 팔 들기)
- AttackTwoHand 2.40s: 최고 0.93s, 구간 0.67~1.03 (0.1~0.3 에 예비 동작)
- RifleFire 1.17s: 반동 0.1~0.3s. PistolFire 5.30s: 여러 발, 손 움직임 작음(한 발 구간 불명확).
- PistolAim 1.33s, RifleAim 3.10s.

## 만들 것
1. **클립 자르기** (임포트 clipAnimations firstFrame/lastFrame, 30fps — 사용자 결정 "휘두르는 부분만"):
   | 클립 | 시작 | 끝 |
   |---|---|---|
   | Attack | 0.25s | 1.10s |
   | AttackOneHand | 0.30s | 1.05s |
   | AttackTwoHand | 0.55s | 1.20s |
   | PistolFire | 0.00s | 0.80s (잠정, 메인이 눈으로 확인) |
   | RifleFire | 0.00s | 0.60s |
   숫자는 빌더 상수 한 곳(표)에 모아 나중에 쉽게 고칠 수 있게. 공격 속도 배율(10번 작업의 `max(1, 클립 길이/간격)`)은 **잘린 길이**를 쓴다.
2. **양손 근접 공격**: 컨트롤러에 `AttackTwoHand` 상태·트리거 (Attack 과 같은 전이, AttackSpeed 연결).
   선택: 주무기 근접 + TwoHanded → AttackTwoHand, 근접 OneHanded → AttackOneHand, 맨손/그 외 → Attack. 기존 `IsOneHandedMelee` 옆에 같은 방식의 판정(재사용, 복제 금지).
3. **총 조준 자세 (상체 레이어)**:
   - 상체 AvatarMask 에셋(허리 위: Body/Head/Arms/Fingers/IK 손, 다리·루트 제외)을 빌더가 만든다 (있으면 재사용, GUID 유지).
   - 컨트롤러 두 번째 레이어 `UpperBody`(마스크 적용, Override, IK Pass): 상태 `Empty`(모션 없음), `PistolAim`(루프), `RifleAim`(루프), `PistolFire`, `RifleFire`.
   - 파라미터 `GunPose` int (0 없음, 1 권총, 2 소총) → Empty/PistolAim/RifleAim 전이. `Fire` 트리거 → 현재 자세의 Fire 상태 → 끝나면 Aim 으로.
     Fire 상태도 AttackSpeed 와 같은 방식 속도 배율(`FireSpeed = max(1, 잘린 길이/사격 간격)`).
   - `PlayerAnimator`: 주무기가 원거리 OneHanded(권총) → 1, 원거리 TwoHanded 이면서 활이 아님(소총·산탄총) → 2, 그 외 0. 원거리 공격 순간 `Fire` (근접 트리거 대신).
     활 판정: 기존 데이터로 구분 가능한 가장 짧은 방법(예: 아이템 id/탄약 id). 없으면 멈추고 보고.
   - 레이어 가중치: GunPose 가 0 이면 상체 레이어 weight 0 (Empty 도 괜찮지만 확실히 0).
4. **자기검사**: 잘린 구간이 표와 같다(±1프레임), 공격 상태 3개 + AttackSpeed, UpperBody 레이어·마스크·상태 5개·GunPose/Fire/FireSpeed, 새 클립 5개 임포트 설정(Human/CreateFromThisModel, Aim 두 개 loopTime).

## 하지 말 것
총·무기를 손에 쥐는 모델(무기 메쉬 부착), 활 모션, 장전 모션, 조준(우클릭) 입력 추가, 근접 공격을 상체 레이어로 옮기기.

## 출력
변경 파일 목록과 요약만. 1~4 한 줄씩, 활 판정 방법, 명세와 다르게 한 부분과 이유, 막힌 것.
