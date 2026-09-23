---
name: zombie-siege-pitfalls
description: "여러 좀비가 문/창문을 두드리게 만들 때 반드시 밟는 세 가지 함정 (중심점 몰림, 실내 문 선택, 카빙에 걸린 경로)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 86ec9df7-57ce-43e0-a0d8-b8b95818bebd
  modified: 2026-09-22T03:11:47.989Z
---

좀비가 개구부를 두드려 부수고 넘어 들어오는 공성을 만들 때 실측으로 걸린 함정들.

1. **모두가 개구부 중심 한 점을 목표로 하면 한 마리만 붙는다.** CharacterController 끼리
   밀려서 2번째가 1.52m 로 빠지고, 중심점 거리로 사거리를 재면 사거리 밖이 된다.
   → 붙는 판정은 점 거리가 아니라 **구멍 폭 안 + 창면까지의 수직거리** 로 재고
   (`WindowFrame.IsAtOpening`), 설 자리는 폭 안에서 좀비마다 다른 칸을 준다
   (instance id 로 고정 — 매 프레임 흔들리면 안 된다).

2. **"플레이어에 가장 가까운 개구부" 를 고르면 실외 좀비가 실내 문을 고른다.**
   3마리 전부 `Door_Bathroom` 을 물고 현관 앞에 서서 두드리는수 0 이었다.
   → 후보마다 **내가 걸어갈 수 있는지**(`NavMesh.CalculatePath` 완주) 를 확인한다.

3. **목표를 문간 안쪽으로 잡으면 경로가 항상 끊긴다.** 닫힌 문의 `NavMeshObstacle`
   카빙이 바로 그 자리를 파먹기 때문이다. → 목표는 **내가 있는 쪽 바깥 0.9m** 로 잡는다
   (`WindowFrame.StandPoint`).

**곁들임:** 두드리기 사거리(`poundRange` 1.3) 는 `WindowFrame.vaultRange`(1.5) 보다
작아야 한다. 크면 부서진 뒤에도 `CanVault` 가 false 라서 추적↔공성을 무한 반복한다.

**창유리는 시야를 막지 않는다** (`TransparentFX` 레이어 — 이미 `Vision.BlockerMask` 에서
빠져 있다). 커튼만 막는다. 닫힌 유리가 시야까지 막으면 커튼이 무의미해진다.

관련: [[combat-needs-line-of-sight]], [[unity-scene-rebuild-and-indoor-navmesh]]
