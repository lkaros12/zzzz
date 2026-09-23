# 작업: Tab 화면 캐릭터 패널 + HUD 부상 제거 + 피격 테두리 + 출혈 + 치료 시간/서서히 회복

## 원칙 (AGENTS.md 와 같다)
1. Think Before Coding — 명세가 모호하거나 코드와 안 맞으면 추측하지 말고 멈추고 보고한다.
2. Simplicity First — 최소 코드. 새 런타임 클래스는 꼭 필요한 것만 (이 작업에서 허용: 캐릭터 부상 그림 UI 1개, 화면 테두리 UI 1개).
   그 외 로직은 기존 클래스(PlayerInjuries, PlayerConsumer, EquipmentUI, SurvivalHudUI, SaveSystem) 안에 넣는다.
3. Surgical Changes — 관련 줄만. 재포맷·무관한 변경 금지.
4. Goal-Driven Execution — 완료 기준별로 무엇이 확인하는지 남긴다.
- 파일은 UTF-8, 한글 주석. **perl 편집 금지**. 주석은 기존 스타일(한국어, "왜").
- Unity 에디터 조작(재빌드·재생·자기검사) 금지, 커밋 금지. 컴파일·Unity 테스트는 메인이 한다.
- **토큰 절약**: 아래 "관련 코드 위치" 에 적힌 함수·줄 주변만 읽는다. `V01SceneBuilder.cs`(5,400줄) 전체를 훑지 마라.

## 관련 코드 위치 (줄 번호는 대략)
- `Assets/_Game/Editor/V01SceneBuilder.cs`
  - `CreateHud` 385줄~. 409줄 `CreateStatusIcon(canvasRect, "InjuryIcon", "⚕", "부상", -75f)`, 416줄 `SurvivalHudUI.Bind(...)`.
  - Tab 패널 791줄~ `"InventoryPanel"`. 상수 `equipWidth = 230f`, `nearbyWidth = 240f`, `gridWidth = inventory.Width * cellSize`.
    `equipRect`(834~838) 가 가운데, `nearbyRect`(849~853) 가 오른쪽. 870·874줄에서 NearbyItemsUI / EquipmentUI 를 Bind.
  - 아이템 정의: 1158줄 붕대 `Consumable(..., 0,0,0, 12f, InjurySeverity.Serious)`, 1225줄 구급상자 `Consumable(..., 35f, InjurySeverity.Critical)`,
    `static ItemDefinition Consumable(...)` 1468줄. 붕대 관련 기존 자기검사 2079줄 근처.
  - 자기검사 메뉴: `Tools/V0.1/Self Check` 가 부르는 검사 함수들 (`Check(bool, string)`).
- `Assets/_Game/Scripts/Player/PlayerInjuries.cs` — `enum BodyPart {Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg}`,
  `enum InjurySeverity {None, Minor, Serious, Critical}`, `GetSeverity`, `Apply`, `ResolveHit`(117줄), `Treat`(127줄), `WorstSeverity`, `DrainPerGameHour`.
- `Assets/_Game/Scripts/Items/PlayerConsumer.cs` — `TryUse(ItemStack)` 35줄~. 60~67줄 치료 도구 판정, 70~76줄에서 즉시 `stats.Heal(definition.hpRestore)`.
- `Assets/_Game/Scripts/Player/PlayerStats.cs` — `Heal`(121줄), `MaxHp`(100).
- `Assets/_Game/Scripts/Combat/Zombie.cs` — `Bite(float damage)` 1079줄, 여기서 `playerInjuries.ResolveHit(RollHitPart(), damage)`.
- `Assets/_Game/Scripts/Combat/PlayerGrapple.cs` — `IsGrappled`.
- `Assets/_Game/Scripts/Player/PlayerMotor.cs` — `ReadMoveInput()` 243줄 (WASD).
- `Assets/_Game/Scripts/Core/SaveData.cs` 28줄 `injuries` 배열, `SaveSystem.cs` 120줄 근처 부상 저장/복원.
- `Assets/_Game/Scripts/UI/SurvivalHudUI.cs` — 좌하단 허기/탈수/부상/피곤함 아이콘.
- `Assets/_Game/Scripts/UI/EquipmentUI.cs` — 지금은 장비 슬롯 세로 목록. `Bind`(34줄), `Rebuild`(61줄).
- `enum EquipmentSlot { None, Head, Body, Legs, Feet, MainHand, OffHand, Backpack, Misc }` (PlayerEquipment.cs).

## A. Tab 화면 레이아웃 (사용자 승인한 목업 그대로)
- 왼쪽: 인벤토리 격자 — 그대로.
- 가운데: **캐릭터 패널** — 폭을 인벤토리 격자 폭(`gridWidth`)과 같게 키운다. 높이 `gridHeight`.
  - 가운데에 사람 그림: UI Image 6개 (머리=원에 가까운 둥근 사각형, 몸통, 왼팔, 오른팔, 왼다리, 오른다리), 사람 모양으로 배치.
  - 부위 색 = 그 부위 부상 단계: 없음 `#D3D1C7`, 경상 `#EF9F27`, 중상 `#D85A30`, 치명 `#A32D2D`. 표시 전용(클릭 없음).
  - **출혈 중인 부위**는 눈에 띄는 표시(예: 진한 빨강 테두리나 작은 핏방울 표시)를 덧붙인다.
  - 그림 아래 한 줄 텍스트: 부상 부위 요약 (예: "왼팔 중상 · 오른다리 경상 · 출혈 1").
  - **장비 칸을 그림 양옆에** 세로로 4개씩: 왼쪽 Head/Body/Legs/Feet, 오른쪽 Backpack/MainHand/OffHand/Misc.
    기존 EquipmentUI 의 동작(칸 클릭 = 해제, 장착 아이템 표시)은 그대로 유지하고 배치만 바꾼다.
- 오른쪽: **주변/상자 패널** — 폭을 `gridWidth * 0.65` 로 줄인다. 동작은 그대로.
- 패널 전체 폭 계산(`panelRect.sizeDelta`)도 새 폭에 맞춘다.

## B. 메인 HUD
- 좌하단 **부상 아이콘 제거** → 허기/탈수/피곤함 3개만. 나머지 아이콘 간격은 빈자리 없이 다시 붙인다.
- Z 키 상태 패널(StatusPanelUI) 은 건드리지 않는다.

## C. 출혈 (새 규칙, PlayerInjuries 에 넣는다)
- 좀비에게 **물릴 때(Zombie.Bite)** 물린 부위에 **40%** 확률로 출혈 시작. 부위마다 따로 bool.
- 출혈 부위 1곳당 **5초마다 체력 −2** (실시간 초). 여러 부위면 그만큼 더 깎인다.
- 저절로 멎지 않는다.
- 조정 가능하도록 [SerializeField] 로: 확률 0.4, 틱 간격 5초, 틱당 2.
- **저장/불러오기**에 출혈 상태 포함 (SaveData 에 필드 추가, 기존 세이브 호환: 없으면 출혈 없음).
- 외부에서 쓸 공개 멤버: 부위별 출혈 조회, 출혈 부위 수, "출혈 틱이 방금 일어났다" 를 알 방법(이벤트 또는 카운터 — 단순한 쪽).

## D. 치료 시간 + 서서히 회복 (PlayerConsumer 에 넣는다)
- 치료 도구(treatUpTo != None) 를 쓰면 **즉시 효과 대신 "감는 중" 상태**가 된다.
  - 감는 시간: **붕대 5초, 구급상자 20초** (아이템 정의 값으로 둔다).
  - **취소 조건**: 이동 입력(WASD)이 들어옴 / 좀비에게 물림 / 붙잡힘(IsGrappled). 취소되면 **아이템 소모 없음**.
  - 감는 중에 다른 치료 도구를 쓰면 앞의 것을 취소하고 새로 시작.
  - 화면에 진행 표시 (예: "붕대 감는 중 3.2 / 5.0초") — 기존 HUD 텍스트/프롬프트 재사용이면 충분.
- **다 감은 순간**: 아이템 1개 소모 → 출혈 부위 1곳 멈춤(있으면) → 기존 부상 치료(`Treat`) → 서서히 회복 시작.
- **서서히 회복**: 붕대 총 +12 를 **6초**에 걸쳐, 구급상자 총 **+60**(35→60 으로 변경) 를 **10초**에 걸쳐.
  - 회복 중 다시 치료를 완료하면 **더하지 않고 새 것으로 교체, 시간 처음부터** (사용자 요구, 작은 붕대로 바뀌어도 교체).
  - 맞아도 회복은 계속된다. 최대 체력 초과 금지.
- 사용 거부 조건: 치료할 부상도, 출혈도 없고, 체력도 가득이면 거부 (기존 메시지 유지).
- 음식 등 치료 도구가 아닌 소비 아이템은 지금처럼 즉시.

## E. 화면 테두리 번쩍임 (새 UI 1개, HUD 캔버스)
- 화면 가장자리 빨간 비네트. 텍스처는 코드에서 생성(가장자리 진하고 가운데 투명). 평소 알파 0.
- **좀비에게 물릴 때마다 강하게** (알파 약 0.75), **출혈 틱마다 약하게** (알파 약 0.45). 약 0.6초에 걸쳐 사라진다.
- 레이캐스트를 막지 않는다(`raycastTarget = false`).

## 새 자기검사 (기존 검사는 모두 유지)
1. HUD 에 부상 아이콘이 없고 허기/탈수/피곤함 3개가 있다.
2. Tab 패널: 캐릭터 패널 폭 = 격자 폭(±2px), 주변 패널 폭 = 격자 폭 × 0.65(±2px), 부위 이미지 6개, 장비 칸 8개가 그림 양옆에 4개씩.
3. 붕대 감는 시간 5초, 구급상자 20초, 회복량 12 / 60, 회복 시간 6 / 10초, 붕대 치료 한도 Serious 유지 / 구급상자 Critical 유지.
4. PlayerInjuries 출혈 설정값 0.4 / 5초 / 2 가 기본값이다.
5. 테두리 UI 가 HUD 에 있고 raycastTarget 이 꺼져 있다.
6. 에디트 모드에서 가능한 범위의 동작 검사: 출혈 부위를 켜고 → 치료 완료 함수를 직접 호출 → 출혈이 꺼지고 회복 목표가 설정되는지.
   (실시간 틱·취소는 메인이 재생 모드에서 확인한다.)

## 하지 말 것
진짜 사람 일러스트, 감염/좀비화, 새 아이템 추가, 인벤토리 격자 크기 변경, 좀비 쪽 로직 변경(Bite 에 출혈 판정 한 줄 추가는 허용).

## 출력
**변경 파일 목록과 요약만**. A~E 와 자기검사 1~6 을 담당하는 코드를 한 줄씩, 명세와 다르게 한 부분과 이유, 막힌 것.
