---
name: hadan-city-generated-not-in-git
description: 하단 도시 씬·메쉬는 git 에 없다 — 받은 뒤 Build Hadan Pilot 을 한 번 눌러야 생긴다
metadata:
  node_type: memory
  type: project
  originSessionId: 86ec9df7-57ce-43e0-a0d8-b8b95818bebd
  modified: 2026-09-30T08:56:39.721Z
---

2026-09-30 부터 `Assets/_Game/Data/Hadan/HadanCityMeshes.asset`(실내 추가 후 112MB, GitHub 100MB 한도 초과)과 `Assets/_Game/Scenes/V01_City_Hadan.unity`(28MB)는 `.gitignore` 에 있다 (사용자 결정: LFS 대신 다시 만들기).

**Why:** 한 파일이 100MB 를 넘어 push 가 `pre-receive hook declined` 로 거부됐다. LFS 는 재빌드마다 140MB 씩 쌓여 무료 1GB 가 금방 찬다.

**How to apply:** 새로 받은 컴퓨터(집)에서는 Unity 에서 `Tools/V0.1/Build Hadan Pilot` 을 한 번 실행해야 도시 씬이 생긴다 (약 1분, NavMesh 약 18초). 별장 대문 씬 이동이 도시 씬을 못 찾으면 이것부터 확인. 커밋 전 큰 파일 확인: `git diff --cached --stat` 에서 수 MB 이상인 생성물이 없는지. 관련: [[mesh-budget-and-batching-trap]]
