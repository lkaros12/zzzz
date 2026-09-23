---
name: unity-scene-rebuild-and-indoor-navmesh
description: 씬 재빌드 시 모달 대화상자로 에디터가 멈추는 문제와, 실내 맵에서 NavMesh 가 문간을 버리는 조건
metadata:
  type: project
---

zzzz 빌더로 씬을 다시 구울 때와 실내 맵을 만들 때 두 번 크게 막혔다.

**1. 모달 대화상자로 에디터가 통째로 멈춘다.**
열려 있는 씬의 에셋을 먼저 지우면 그 씬이 "수정됨" 이 되고, `BuildSandboxScene` 첫 줄의 `EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()` 가 **"Scene(s) Have Been Modified"** 대화상자를 띄운다. 그러면 메인 스레드가 막혀 MCP 명령이 전부 타임아웃된다.
- 진단: `Get-Process Unity` 의 CPU 가 12초에 0.02초밖에 안 늘고 `Responding=True` → 계산 중이 아니라 입력 대기.
- Unity 대화상자는 IMGUI 라 자식 버튼 핸들이 없다. 좌표 클릭 말고는 자동으로 못 누른다 → 사용자에게 부탁해야 한다.
- 해결: `Tools/V0.1/Rebuild Sandbox Scene (no prompt)` 를 쓴다. 빈 씬으로 먼저 갈아타 dirty 를 없앤 뒤 지운다. **자동화에서는 항상 이쪽을 부를 것.**

**2. NavMesh 가 문간을 통째로 버린다.**
에이전트 높이가 2.0m 인데 문 인방(문 위 벽) 아랫면을 2.05m 로 두면, 여유 5cm 가 복셀화에서 깎여 문간이 보행 불가가 되고 **실내 전체가 고립된 섬**이 된다. 경로는 건물 둘레만 돌고 안으로 안 들어온다. 문 높이는 2.2m 이상으로.
- 문 폭도 같이 본다: 실제 문 1.0m 를 쓰려면 에이전트 반지름을 0.5 → 0.3 으로 내려야 한다 (`ProjectSettings/NavMeshAreas.asset` 의 `m_Settings[0].agentRadius`, SerializedObject 로 수정 가능). 플레이어 CC 반지름 0.32, 좀비 0.30 으로 맞췄다.
- 닫힌 문은 NavMesh 를 구울 때 빼고(콜라이더 잠깐 끄기), 대신 문짝에 `NavMeshObstacle(carving)` 을 달아 실시간으로 막는다. 닫힌 채로 구우면 열어도 영영 못 들어온다.
- **검사할 때 주의**: 문이 닫혀 있으면 카빙 때문에 경로가 없는 게 정상이다. 배치가 통행 가능한지 보려면 문짝을 잠깐 끄고 다시 구운 뒤 물어봐야 한다.

**Why:** 둘 다 증상만 보면 원인을 못 찾는다. 1번은 "MCP 가 죽었나" 로, 2번은 "문 폭이 좁나" 로 헤매게 된다.

**How to apply:** MCP 명령이 연달아 타임아웃되면 먼저 Unity 프로세스 CPU 를 재고 창 목록을 훑어 대화상자를 찾는다. 실내 맵을 만들면 반드시 "현관 → 각 방" NavMesh 경로 검사를 넣는다 (빌더 `CheckLayoutIsWalkable`). 관련: [[unity-playmode-frame-ticks]]
