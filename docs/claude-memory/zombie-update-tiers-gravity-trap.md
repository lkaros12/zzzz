---
name: zombie-update-tiers-gravity-trap
description: 좀비 갱신을 거리별로 건너뛰면 중력 낙하가 tickDelta 제곱으로 커져서 좀비가 지면에 붙어버린다. 그리고 성능 A/B 측정법
metadata: 
  node_type: memory
  type: project
  originSessionId: 86ec9df7-57ce-43e0-a0d8-b8b95818bebd
  modified: 2026-09-22T06:06:06.403Z
---

좀보이드 "Tiered Zombie Updates" 를 따라 거리별로 좀비 Update 를 건너뛰게 만들었다
(`Zombie.ShouldTickNow`, 26m 매프레임 / 50m 4프레임 / 그밖 12프레임, instance id 로 위상 분산).
건너뛴 시간은 `tickDelta` 에 쌓아 `Time.deltaTime` 자리에 쓴다.

## 함정 — 중력이 tickDelta 제곱으로 커진다

`verticalVelocity += gravity * dt` 로 속도가 dt 만큼 커지고, `cc.Move(... * dt)` 에서 또 곱해진다.
즉 **낙하 거리는 dt²** 이다. 등급이 12배면 낙하가 144배가 되어 수직 성분이 수평을 눌러버리고
좀비가 제자리에 붙는다 — **실측: 55m 밖 좀비가 10.29초에 0.22m (0.02m/s, 설정 2.5m/s)**.
수평 0.30m vs 수직 0.41m 가 되는 순간 깨진다.

**해법:** 땅에 붙어 있을 때는 낙하 거리를 고정값으로 자른다.
```csharp
float fall = verticalVelocity * tickDelta;
if (cc.isGrounded) fall = Mathf.Max(fall, -0.05f);
cc.Move(horizontal * tickDelta + Vector3.up * fall);
```
`tickDelta` 자체에도 상한(0.25s) 을 둔다 — 에디터가 멈췄다 돌아오면 벽을 뚫는다.
고친 뒤 실측 2.39m/s.

## 성능 A/B 측정할 때

- **`UnityEditor.UnityStats` 의 drawCalls/triangles 는 못 믿는다** — 에디터 Scene 뷰까지
  같이 센다. 프레임시간(`Time.smoothDeltaTime`) 과 직접 센 값만 쓴다.
- **좀비가 움직여서 A/B 가 오염된다.** A 를 재는 동안 201마리가 플레이어로 모여들어
  B 시점에는 전부 근거리 등급이 됐다. 매번 같은 씨앗으로 새로 소환하고 같은 경과시간에 재라.
- 시험 배치도 조심 — 처음에 6~30m 에 몰아놔서 전원이 근거리 등급이라 등급제가 아예 발동하지 않았다.
  면적 균등(`sqrt(rand) * R`) 으로 맵 전체에 퍼뜨려야 실제 분포가 된다.

## 결과와 남은 병목

200마리 맵 전체 분포: 프레임 13.20 → 11.32ms, 그려지는 좀비 200 → 74, 경로계산/프레임 12(포화) → 4.
`Zombie.PathBudgetPerFrame = 12` 로 `NavMesh.CalculatePath` 를 프레임당 제한한다
(좀보이드는 워커 스레드 + 예산, Unity 의 CalculatePath 는 메인 스레드 동기라 예산만 가져왔다).

**삼각형은 병목이 아니었다.** `zombi1_humanoid` 를 130,408 → 5,999 로 데시메이트해서
렌더 삼각형을 14.87M → 1.01M (−93%) 로 줄였는데 **프레임은 13.20 → 12.99ms 뿐**이었다.
이 기계에서는 GPU 삼각형 바운드가 아니고 CPU 바운드다 (드로우콜 제출, 스키닝 준비,
CharacterController 물리, 200개 MonoBehaviour Update).
데시메이트로 얻은 건 여유분이다 — 에셋 9.38MB → 479KB, 약한 GPU·더 많은 좀비·캐스케이드 추가 여유.
자기검사가 모델 20,000 삼각형 넘으면 경고한다.

**교훈: "무거워 보이는 것" 을 병목이라고 단정하지 말고 A/B 로 재라.** 나는 13만 삼각형을
보고 최대 병목이라고 단정했는데 측정은 아니라고 했다.

관련: [[mesh-budget-and-batching-trap]], [[unity-playmode-frame-ticks]]
