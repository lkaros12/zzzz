---
name: combat-needs-line-of-sight
description: 거리·각도만 보는 전투 판정은 전부 벽을 통과한다 — zzzz 에서 세 군데 동시에 터졌음
metadata:
  type: feedback
---

zzzz 에서 "대상을 거리 + 각도로 고르는" 판정은 **전부** 시야 검사를 빠뜨리고 있었다. 사용자가 암살 하나를 신고했는데 같은 구멍이 세 군데였다:

1. `PlayerAssassinate.FindTarget` — 벽 너머 좀비를 암살 (게다가 `cc.enabled=false` 로 순간이동해 플레이어가 벽 밖으로 나갔다)
2. `PlayerCombat.HitTargetsInArc` — 근접 무기가 벽을 통과해 명중 (히트스캔 쪽은 `HasLineOfSight` 로 이미 막고 있었다 — 한쪽만 고쳐져 있었던 셈)
3. `Zombie.TryAttack` / `TryChargeHit` — 좀비가 벽 뚫고 물기

실측 (벽 사이 1.14m vs 벽 없이 1.14m): 고치기 전엔 둘 다 명중, 고친 뒤엔 벽 사이 0 / 벽 없음 1.

**Why:** 실외 샌드박스에서는 벽이 거의 없어서 몇 달 동안 안 드러났다. 실내 맵이 생기자 세 개가 동시에 보였다. 하나를 신고받으면 **같은 판정 방식을 쓰는 형제들을 전부 grep** 해야 한다.

**How to apply:** 대상 선택에 `distance`/`Vector3.Angle` 을 쓰는 코드를 보면 곧바로 `Vision.CanSee` 가 있는지 확인한다. 없으면 구멍이다. 또 `CharacterController` 를 끄고 `transform.position` 을 대입하는 이동은 벽을 통과하므로 `cc.Move(delta)` 로 쓸어서 옮긴다. 관련: [[unity-character-vs-interior-scale]]
