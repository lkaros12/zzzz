---
name: stop-play-before-work
description: Unity 가 재생 모드면 묻지 말고 멈춘 뒤 빌드·검사를 시작한다
metadata:
  node_type: memory
  type: feedback
  originSessionId: 86ec9df7-57ce-43e0-a0d8-b8b95818bebd
  modified: 2026-09-29T02:04:59.627Z
---

작업(재빌드·자기검사·임포트)을 시작할 때 에디터가 재생 모드면 **사용자에게 묻지 말고 `manage_editor stop` 으로 멈추고 진행**한다.

**Why:** 2026-09-29 재생 중에 재빌드를 눌러 실패했고, 멈춰 달라고 사용자에게 부탁했더니 "작업할 때는 알아서 플레이 멈추고 들어가, 난 플레이된 줄도 몰랐다" 고 했다. 재생은 대개 내가 테스트하다 남긴 것이다.

**How to apply:** 빌드 메뉴를 부르기 전에 `EditorApplication.isPlaying` 을 확인하고 true 면 stop → "컴파일중=False" 확인 → 빌드. 관련: [[unity-playmode-frame-ticks]]
