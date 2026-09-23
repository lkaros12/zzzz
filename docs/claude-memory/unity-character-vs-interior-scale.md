---
name: unity-character-vs-interior-scale
description: 실내 맵에서 문을 못 지나가거나 방이 좁게 느껴지는 네 가지 원인. 두 번째 '방이 작다' 는 거의 항상 카메라 줌이다
metadata:
  type: project
---

zzzz 시작 맵(아파트) 을 만들면서 "문으로 못 나간다 / 방이 화장실만 하다" 가 한꺼번에 터졌다. 원인이 세 개였다.

**1. stepOffset 이 문 인방을 때린다 (가장 안 보이는 원인).**
`CharacterController` 는 이동할 때 턱을 넘으려고 `stepOffset` 만큼 몸을 들어올려 본다. 그때 머리가 문 위 인방에 닿으면 **문이 열려 있어도 막힌다**. 캡슐 스윕(`CapsuleCastAll`)이나 `OverlapBox` 로는 아무것도 안 잡혀서 "보이지 않는 벽" 처럼 보인다.
- 실측: 머리 2.13m, 인방 2.25m (여유 12cm) 인데 stepOffset 0.75 → 전 문 통과 불가. stepOffset 을 0.30 이하로 내려야 통과.
- 진단법: 인방 콜라이더만 꺼 보면 즉시 통과한다. 그게 결정적 증거다.
- 해결: 천장 2.7→3.0, 문 2.25→2.6, agentClimb/stepOffset 0.75→0.35. 계단 한 칸이 0.25m 라 stepOffset 은 그보다는 커야 한다.
- 자기검사: `머리 + stepOffset < 문 높이` 를 기하로 못 박아 두면 재생 모드 없이 잡힌다.

**2. 보이는 몸이 콜라이더보다 굵었다.**
`CreatePrimitive(Capsule)` 은 지름 1m 다. `cc.radius` 만 0.32 로 줄이면 몸이 실제의 1.5배로 보여서 방이 훨씬 좁아 보인다. `transform.localScale = (radius*2, 1, radius*2)` 로 맞춰야 한다.

**3. 도면 실측을 그대로 쓰면 게임에서 너무 좁다.**
참고 도면(거실 3.5x4.1m) 을 그대로 지으면 2m 캐릭터에게 화장실이다. 좌표는 '도면 미터' 로 쓰고 `Flat()` 에서 `Scale`(1.6) 로 한 번에 키운다. 가구는 따로 `FurnitureScale`(1.15) 만 적용 — 같이 1.6배 하면 침대가 2.6x3.3m 가 된다.

**4. 두 번째로 "방이 작다" 는 말이 나오면 방이 아니라 카메라다 (가장 헷갈리는 원인).**
별장(방 하나 19~38m2, 실제로는 큰 방) 에서도 "화장실 같다" 는 지적이 나왔다. 재보니:
- 직교 카메라 ortho 9 + 부감 30도 → **지면 49.3 x 36.0m** 를 비춘다. 집 전체(18x14m) 에
  바닥 여백까지 한 화면에 들어와서 **방 하나가 화면 폭의 5~14%** 짜리 칸으로 보였다.
- 좀보이드는 25x18m 쯤을 비춘다 — 면적으로 우리가 4배를 보여주고 있었다.
- 줍기 반경 2m(지름 4m) 도 방 하나를 통째로 덮어서 Tab 에 방 안 물건이 다 떴다.

**환산해서 봐라.** `방 폭 / 화면 지면 폭` 이 15% 아래면 무조건 좁게 느껴진다.
아이소메트릭 지면 커버리지 = `orthographicSize * 2 / sin(부감각)` (세로), `* aspect` (가로).
해결은 셋을 같이: 방 1.6배 + ortho 9→5.5 + 줍기 2.0→1.2m. 합치면 화면 비율 10% → 22~58%.
자기검사에 `가장 작은 방 폭 / 화면 폭 > 0.15` 와 `줍기 지름 < 가장 작은 방 폭 * 0.6` 을 박아뒀다.

**부수 함정:** `ProjectSettings/NavMeshAreas.asset` 은 `AssetDatabase.SaveAssets()` 로 flush 되지 않는다. `EditorUtility.SetDirty` + `AssetDatabase.SaveAssetIfDirty` 를 쓰고 **디스크 파일을 직접 읽어 확인**할 것. 런타임 값만 보면 바뀐 줄 안다.

**How to apply:** 실내 맵에서 통행이 막히면 (1) NavMesh 경로, (2) 물리 통행 을 따로 본다. 물리가 막히는데 스윕에 안 잡히면 거의 항상 stepOffset x 인방이다. 관련: [[unity-scene-rebuild-and-indoor-navmesh]]
