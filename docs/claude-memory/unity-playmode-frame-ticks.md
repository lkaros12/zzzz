---
name: unity-playmode-frame-ticks
description: MCP execute_code 로 재생 모드를 검증할 때 프레임이 안 흐르는 문제와 해결
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 86ec9df7-57ce-43e0-a0d8-b8b95818bebd
  modified: 2026-09-17T04:01:15.030Z
---

zzzz 프로젝트에서 `execute_code` 로 재생 모드 동작을 검증할 때:

1. **에디터 창이 백그라운드면 프레임이 거의 안 흐른다.** 값을 바꾸고 바로 읽으면 이전 프레임 값이 나온다(`Time.frameCount` 가 그대로). 첫 줄에 `Application.runInBackground = true;` 를 넣어야 계속 돈다.
2. **`EditorApplication.QueuePlayerLoopUpdate()` 는 호출한 코드 안에서 프레임을 돌리지 않는다.** 큐에 넣기만 하므로 같은 호출 안에서 상태 변경 → 틱 → 읽기/스크린샷 을 하면 전부 같은 프레임이다. 상태 변경과 결과 확인은 **별도 execute_code 호출**로 나눠야 한다.
3. **스크린샷 비교 전에 `GameClock` 을 끈다.** 해가 움직여서 조명이 바뀌고, 전체 화면이 달라져 diff 가 무의미해진다.

**Why:** 이걸 모르고 "WallFader 가 0개를 흐리게 한다" 를 버그로 오진했고, 창문 개폐 스크린샷 두 장이 같은 상태로 찍혔다. 프레임 타이밍 착각이 없는 버그를 있는 것처럼 보이게 만든다.

**How to apply:** 재생 모드 검증은 (1) 상태 변경 호출 → (2) 결과 읽기 호출 로 최소 2번 나눈다. 픽셀 단위 확인이 필요하면 `WorldToScreenPoint` 로 좌표를 구하고 `ImageConversion.LoadImage` 로 PNG 를 다시 읽어 수치로 비교한다 — 눈으로 보고 판단하지 않는다. 관련: [[unity-offline-compile-check]]
