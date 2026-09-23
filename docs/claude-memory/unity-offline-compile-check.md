---
name: unity-offline-compile-check
description: Unity 에디터가 꺼져 있어도 zzzz 프로젝트 C# 스크립트를 컴파일 검증하는 방법
metadata:
  type: project
---

zzzz Unity 프로젝트에서 Unity 에디터가 실행 중이 아니면 unityMCP 브릿지가 안 붙는다("No Unity Editor instances found"). 이때도 스크립트 컴파일은 검증할 수 있다:

1. `Assembly-CSharp-Editor.csproj` 에서 `Reference/HintPath` 를 전부 추출 (337개, UnityEngine·UnityEditor·URP·UGUI·InputSystem 포함)
2. 각 HintPath 를 `-r:` 로, 검사할 .cs 를 소스로 넣어 response 파일 작성. `-target:library -langversion:9.0 -nostdlib+ -define:UNITY_EDITOR`
3. `"C:\Program Files\dotnet\dotnet.exe" "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\DotNetSdkRoslyn\csc.dll" "@<rsp>"`

**Why:** MCP 없이도 CLAUDE.md 4항(검증 의무)의 컴파일 단계를 만족시킬 수 있다. 런타임 동작은 여전히 검증 불가이므로 그건 사용자 플레이테스트로 넘겨야 한다.

**How to apply:** 스크립트를 쓴 뒤 "컴파일은 될 것"이라고 추측하지 말고 이 절차로 확인한다. Roslyn 은 `-noconfig` 무시 경고(CS2023)를 항상 내는데 이건 무해하다. 관련: [[hud-portrait-fade-deferred]]
