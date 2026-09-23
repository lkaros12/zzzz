---
name: srgb-gamma-fog-alpha
description: 화면을 어둡게 덮을 때 선형 알파 0.82는 화면에서 회색으로 보인다 — sRGB 감마 때문에 0.99 이상이 필요
metadata: 
  node_type: memory
  type: project
  originSessionId: 86ec9df7-57ce-43e0-a0d8-b8b95818bebd
  modified: 2026-09-22T02:33:31.519Z
---

알파 블렌딩으로 화면을 어둡게 덮는 값을 고를 때, 선형 공간의 잔광 비율을 그대로 믿으면 안 된다.

- 알파 0.82 → 선형 잔광 18%. "충분히 어둡다"고 생각했지만 화면에서는 sRGB 감마가 `0.18^(1/2.2) ≈ 0.48` 로 끌어올려서 **거의 원본만큼 읽힌다**.
- 알파 0.975 → 선형 2.5% → 화면 16%. 여전히 가구와 방 배치가 다 보인다.
- 알파 0.995 + 피그색 거의 검정 → 화면 9.6%. 여기서 처음 "검다"고 보인다.

**적용:** 어둡기 기준은 반드시 `pow(선형_잔광, 1/2.2)` 로 환산해서 판정한다.
`Assets/_Game/Editor/V01SceneBuilder.cs` 의 밀실 음영 자기검사가 이 식을 쓴다.

**곁들인 함정:** URP + 직교 카메라에서 깊이를 읽는 셰이더(VisionFog)는
`cam.Render()` 로 RenderTexture 에 수동 렌더하면 깊이 텍스처가 없어 통째로 사라진다.
화면 확인은 `ScreenCapture.CaptureScreenshot` 으로 해야 한다.
그리고 진단할 때 `renderer.material` 을 읽으면 사본이 생겨 원본 머티리얼 갱신이 끊긴다 —
`sharedMaterial` 로 읽어야 한다.

관련: [[vision-mask-angular-cliff]], [[unity-playmode-frame-ticks]]
