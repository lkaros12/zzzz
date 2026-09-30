# 수정: 12 검수 지적

원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.

1. **방패 장착 실패 (확실)**: `PlayerEquipment.cs:61-65 TryEquip`. 방패 에셋(trashcan_lid, riot_shield, metal_sign)이 handedness 1, ranged 0 이라 `IsOneHandedMelee(방패)` 가 true → MainHand 로 보내려다 실패. 인벤토리 우클릭·더블클릭·E 장착(`InventoryUI.cs:414/418`) 에서 "장착할 수 없습니다".
   `isShield` 는 먼저 OffHand 로. `IsOneHandedMelee` 가 방패를 제외하게(방패는 무기 판정에서 빠짐) — 다른 호출부 영향 확인. 자기검사: 방패를 TryEquip 하면 OffHand 에 들어간다.
2. **손전등 끄면 불 켜진 방이 검게 보임 (확실)**: `VisionFog.cs:126` 마스크 영역이 `(field.Range + 2) * 2` 라 range 0.9 가 되면 5.8m 로 줄어 영역 밖이 검게 된다. 마스크 영역 크기는 **손전등 켠 range(원래 값) 기준으로 고정**하고, 끈 상태는 부채꼴만 줄인다.
3. **방패 조합 제한 (메인 결정)**: 방패는 **한손 근접 무기 또는 빈손**과만. 양손 무기·원거리 무기와 방패는 함께 장착 불가:
   - 방패가 있는데 양손/원거리를 MainHand 에 장착 → 방패를 인벤토리로 내리고 장착 (자리 없으면 장착 거부 + 메시지). 가장 짧은 방법, 기존 양손 무기 장착 시 OffHand 처리 코드가 있으면 그것을 재사용.
   - 양손/원거리를 든 상태에서 방패 장착 → 거부 + 메시지 "양손 무기와 방패는 함께 들 수 없습니다" 류.
   자기검사: 두 방향 모두.
4. **방패 파손 시 자세**: `PlayerAnimator.AimPoseFor` 방패 IsBroken 이면 방패 자세(6) 대신 무기 자세.
5. **F 토글 조건**: `VisionField.cs:70-75` 사망 중, 인벤토리(Tab) 열림 중에는 무시.
6. **자기검사 보강**: 실제 VisionField 인스턴스에서 SetFlashlight(false) → range 0.9, SetFlashlight(true) → 원래 range 복원. `CanAim` 순수 함수 조건 5개(UI 위, 사망, 붙잡힘, 암살, 재장전 → false).

## 출력
변경 파일 목록과 요약만. 1~6 한 줄씩, 3번 구현 방식, 막힌 것.
