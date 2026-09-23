---
name: mesh-budget-and-batching-trap
description: 블렌더 작업본은 베벨/서브디비전이 적용돼 들어와 15~40배 무겁다. 그리고 정적 배칭 때문에 삼각형을 그냥 세면 10배 뻥튀기된다
metadata: 
  node_type: memory
  type: project
  originSessionId: 86ec9df7-57ce-43e0-a0d8-b8b95818bebd
  modified: 2026-09-22T05:44:27.539Z
---

**GPT가 블렌더에서 만들어 보낸 에셋은 서브디비전·베벨이 적용된 상태로 들어온다.**
박스 하나면 12삼각형인 마룻장이 188, 이불 한 장이 22,530 이었다. 실측 분포:

| 것 | 개수 | 삼각형 |
|---|---|---|
| Oak floorboard | 214 | 40,232 |
| Plant stem | 72 | 35,136 |
| Leaf | 72 | 31,680 |
| Duvet (1장) | 1 | 22,530 |
| Curtain fold | 20 | 10,160 |

아이소메트릭 부감에서는 전혀 안 보이는 디테일이라 그냥 지웠다 (225,562 → 83,964, −63%).
`V01SceneBuilder.SimplifyRefinedProps` 가 이름 앞부분으로 지운다.
**이 프로젝트는 삼각형이 일반 URP보다 2배 비싸다** — `VisionFog` 가 깊이 텍스처를 요구해서
깊이 프리패스가 한 번 더 돌기 때문이다.

## 측정 함정 — 정적 배칭

재생 모드에서 `MeshFilter.sharedMesh.triangles.Length` 를 렌더러마다 더하면 안 된다.
정적 배칭이 결합 메쉬를 만들고 모든 렌더러가 **같은 결합 메쉬**를 가리키므로,
나무 216그루가 같은 메쉬를 216번 세서 **908만 삼각형**이 나왔다 (실제 8만).
→ `HashSet<int>` 으로 `GetInstanceID()` 를 중복 제거하거나, 에디트 모드에서 재라.
실제 렌더량은 `UnityEditor.UnityStats.triangles` 로 본다.

## 숲

나무 = 내장 Cube 3개를 `Mesh.CombineMeshes` 로 구운 메쉬 하나(36삼각형), 에셋으로 저장
(`Assets/_Game/Art/Meshes/LowPolyTree.asset` — 저장 안 하면 씬 다시 열 때 사라진다).
216그루 전부가 이 메쉬 하나와 재질 하나를 공유해서 정적 배칭이 한 묶음으로 삼킨다.
콜라이더는 안 준다 — 수백 개면 NavMesh 굽기와 물리 질의가 같이 비싸진다.
`CombineInstance` 는 `UnityEngine` 네임스페이스다 (`Mesh.CombineInstance` 아님).

관련: [[srgb-gamma-fog-alpha]], [[unity-scene-rebuild-and-indoor-navmesh]]
