# 수정: 09 Unity 실측 + 검수 지적

원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.

## Unity 실측 (메인, 재빌드 직후 에디트 모드)
- 플레이어 Model localScale **1.170**, `BakeMesh(mesh, true)` + `smr.transform.localToWorldMatrix` 로 잰 높이 **1.200** (맞음).
  (배율 2.19 때 같은 방법으로 2.25 → 배율에 선형 비례하므로 이 측정 방법은 스케일을 두 번 곱하지 않는다. 원래 키 ≈ 1.026m.)
- **발이 묻힘**: 구운 정점 minY = **-0.52**, 플레이어 루트 y 0.65, 땅은 약 0.05. 발이 약 0.57m 아래. 발 맞춤이 옛(틀린) 측정 기반인 듯.
- 자기검사 `character01 몸 높이가 1.2m ± 0.02m다` **FAILED** (위 방법으론 1.200 인데 실패 → 검사 쪽 측정이 다름, 검수 3번: `V01SceneBuilder.cs:5402` `BakeMesh(mesh,false)` + `TransformPoint` 스케일 이중 적용 가능성).

## 고칠 것
1. **측정 한 가지로 통일**: 키 측정·발 맞춤·자기검사가 모두 같은 방법 — 위 메인 방법(`BakeMesh(mesh, true)` + renderer `localToWorldMatrix` 월드 정점, 또는 동등하게 검증된 것) 을 쓴다.
2. **발 맞춤**: 모델 localPosition.y 를 구운 정점 **minY 가 CharacterController 바닥(발)과 같게** 맞춘다.
3. **자기검사**: (a) 보이는 키 1.2 ± 0.03, (b) 구운 정점 minY 가 컨트롤러 바닥 ± 0.03, (c) 가능하면 Idle 클립을 샘플한 자세의 Head 뼈 높이가 0.8~1.15 사이 (T-포즈 순환 검증 보완). 셋 다 같은 측정 함수를 쓰되 (b)(c) 는 독립 기준.
4. **근접 판정 방향**: `PlayerCombat.cs:414` 부채꼴이 `transform.forward` → `aim.AimDirection` (옆걸음·뒷걸음 중 휘두르면 45° 어긋남).
5. **뒷걸음 중 몸 좌우 튐 (메인 결정)**: `PlayerMotor.cs:147 ResolveBodyDirection`. yaw = 조준→이동 부호 각도.
   - |yaw| ≤ 90°: 몸 = 조준을 이동 쪽으로 `clamp(yaw, ±45°)` 만큼 튼다 (지금과 같음).
   - |yaw| > 90°: 틂 = `sign(yaw) × 45° × (180° − |yaw|) / 90°` → 90° 에서 45°, 180°(정확히 뒤) 에서 0°. 연속이고 180° 부근에서 튀지 않는다.
   자기검사: yaw 0/45/90/135/180/-135/-179/179 입력에 대한 기대 틂값.
6. **좀비 불러오기 여유 부호**: `SaveSystem.cs:428` 결과가 저장 위치보다 5cm 아래 → 위로 +여유가 되게.
7. **테스트 저장·도착 y 1.05**: `V01SceneBuilder.cs:2403, 2512` → 플레이어 중심 높이(`BodyY`) 사용.
8. **LookAt 초기값**: `PlayerAnimator.ApplyLookAtIK` 조준점이 한 번도 해석되지 않았으면(AimPoint 가 초기값) weight 0.

## 출력
변경 파일 목록과 요약만. 1~8 한 줄씩, 막힌 것.
