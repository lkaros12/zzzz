# 작업: 우클릭 조준(조준해야만 공격) + 보조손 없애고 방패 통합 + F 손전등

원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.
`V01SceneBuilder.cs` 전체를 읽지 말 것 (플레이어 클립 임포트·`EnsurePlayerController`·관련 자기검사만).

## 사용자 결정
- **우클릭 누르고 있기 = 조준 자세. 조준 중일 때만 좌클릭 공격이 나간다** (좀보이드). 근접·총 모두.
- **보조손 공격 없음.** 보조손 칸은 **방패 전용**. 한손 무기 + 방패 = 검방: 조준(우클릭) = 방패 들기(막기), 그 상태에서 좌클릭 = 무기 공격.
- **손전등**: 항상 들고 있음(아이템 없음), 배터리 없음. 지금 보이는 시야 = 켠 상태. **F** 로 끄면 어둠.

## 입력 (메인이 넣어 둠, Mixamo Without Skin 30fps, 루프 자세)
`character01@UnarmedAim`(Fight Idle), `@MeleeAimOneHand`(Standing Torch Idle 01), `@MeleeAimTwoHand`(Standing Idle With Axe), `@ShieldBlock`(Sword And Shield Block Idle).
기존 `@PistolAim`, `@RifleAim` 도 조준 자세로 쓴다.

## 만들 것
1. **입력** (`PlayerCombat.cs:105~117`):
   - `IsAiming` 공개 프로퍼티 = 우클릭 누르는 중 (UI 위 제외, 사망·붙잡힘·암살·재장전 중 false).
   - 좌클릭 공격은 `IsAiming` 일 때만. 우클릭 `wasPressedThisFrame` → 보조손 공격 경로 삭제 (`TryAttack(OffHand)` 및 212줄 OffHand 분기 정리).
2. **보조손 = 방패 전용**:
   - `PlayerEquipment.CanEquipTo` (약 111~117줄): OffHand 에는 `isShield` 만. 한손 근접 무기는 MainHand 만.
   - UI 이름(`PlayerEquipment.cs:241` "보조무기") → "방패".
   - `PlayerQuickSlots.cs:36` 인덱스 1 이 OffHand 를 가리키는 부분: 방패가 아닌 아이템이면 OffHand 로 보내지 않게 (가장 짧은 방법, 동작 보고).
   - `DebugStartState.cs:102~104` 보조손에 무기를 넣는 경로가 있으면 방패만.
   - 기존 세이브에 OffHand 무기가 있으면 불러올 때 인벤토리로 되돌린다 (넣을 칸 없으면 바닥에). 가장 짧은 경로.
   - `PlayerShield.cs:77` 방어 조건: `mouse.rightButton.isPressed` 대신 `PlayerCombat.IsAiming` (조준 = 방패 들기). 좌클릭으로 무기를 휘두르는 순간(공격 판정 동안)은 방어 해제, 끝나면 다시 방어.
3. **조준 자세 애니메이션** (11번의 `UpperBody` 레이어 확장):
   - 파라미터 `GunPose` → `AimPose` int 로 바꾸거나 새로: 0 없음, 1 권총, 2 소총, 3 맨손, 4 한손 근접, 5 양손 근접, 6 방패(한손 무기 + 방패, 또는 방패만).
   - 상체 레이어 가중치: **조준 중일 때만 1** (조준 안 하면 0 → 평소 걷기 그대로). 총도 이제 조준 중일 때만 총 자세 (11번의 "총 들면 항상" 을 바꾼다).
   - 근접 공격(Attack/AttackOneHand/AttackTwoHand, Base Layer 전신)은 그대로. 공격 중에는 상체 레이어 가중치 0 (공격 모션이 가리지 않게), 끝나면 조준 자세 복귀.
   - 사격(Fire)은 11번대로 상체 레이어.
   - 새 클립 4개 임포트: Human / CreateFromThisModel / loopTime / 루트 잠금.
   - 활(ammo_arrow) 은 조준 자세 0.
4. **손전등 F** (`VisionField`):
   - F 로 켜기/끄기 토글 (시작은 켬). 끄면 시야 부채꼴 `range` 를 `alwaysVisibleRadius` 수준으로 줄인다 → 발밑 원과 **불 켜진 방(revealLitRoom) 만** 보인다 (메인 가정: 불 켜진 방은 계속 보임).
   - 상태는 저장/불러오기·씬 이동에 포함하지 않아도 됨 (다음에). HUD 에 작은 표시 한 줄(예: 좌하단 아이콘 옆 "손전등 꺼짐") — 기존 HUD 텍스트 재사용.
   - F 키가 다른 곳에서 안 쓰이는지 확인 (메인 확인: 안 씀).
5. **11번 검수 남은 지적**:
   - 무기 교체·근접 공격 시 `ResetTrigger(Fire)` (소비 안 된 Fire 가 나중에 튀는 문제).
   - 사격 중 자세가 바뀌면(무기 교체) Fire 상태에서도 AimPose 전이 허용.
   - 클립 자르기의 30fps 고정 → `importedTakeInfos[0].sampleRate` 사용 (자기검사도).
   - 활 자기검사: bow.asset 이 없으면 FAILED (지금은 null 이면 거짓 통과).
6. **자기검사**: 우클릭 없이 좌클릭 공격이 안 나가는 입력 조건 함수(순수 함수로 노출), OffHand 에 무기 장착 거부·방패 허용, 상체 레이어 조준 상태 7개(Empty 포함)와 AimPose 매핑, 손전등 끄면 range 가 줄고 켜면 복원.

## 하지 말 것
조준 중 이동 속도 감소(요청 없음 — 필요하면 다음), 무기 모델 손에 쥐기, 손전등 배터리·아이템, 새 입력 시스템 액션 에셋(기존처럼 Mouse/Keyboard.current).

## 출력
변경 파일 목록과 요약만. 1~6 한 줄씩, 퀵슬롯 2번이 이제 어떻게 동작하는지, 명세와 다르게 한 부분과 이유, 막힌 것.
