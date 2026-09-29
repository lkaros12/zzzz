# 작업: 캐릭터 키 1.2m + 달리기 발목 꺾임 + 몸은 이동 방향·고개/시야는 마우스

## 원칙 (AGENTS.md 와 같다)
1. Think Before Coding — 모호하거나 코드와 안 맞으면 멈추고 보고.
2. Simplicity First — 최소 코드. 새 런타임 클래스 금지 (기존 PlayerAnimator / PlayerAim / PlayerMotor / VisionField 안에서).
3. Surgical Changes — 관련 줄만.
4. Goal-Driven Execution — 완료 기준별 확인 방법을 남긴다.
- UTF-8, 한글 주석("왜"), perl 금지, Unity 조작·커밋 금지. `V01SceneBuilder.cs` 전체를 읽지 말 것 (`CreatePlayer` 343줄~, `BodyY` 약 5165줄, 플레이어 모델·클립 임포트 약 4780~4900줄, 관련 자기검사만).

## A. 키 1.2m (사용자 결정)
- 모델 키 1.2m (`modelScale = 1.2 / MeasureModelHeight`), `CharacterController.height = 1.2`, center·`BodyY`·스폰 높이 맞춤. radius 는 0.32 → 0.26 정도로 체형에 맞게 (문·통로 통과 자기검사 유지).
- **눈높이는 발 기준 1.15m 로 유지**한다 (지금 값). 캡슐 중심 + `Vision.EyeOffset` 방식이면 1.2m 키에서 0.9m 가 되어 창턱(0.9)·조리대(0.95)에 시야가 막힌다. 발 기준 절대 높이로 바꾸거나 오프셋을 조정 — 가장 짧은 쪽.
- 조준 높이(`PlayerCombat.aimHeight`, 히트스캔 원점)는 좀비(1.6m) 몸통에 맞는지 확인하고, 맞으면 그대로. 안 맞으면 보고 후 최소 조정.
- 2m/1.6m 전제 수치 중 1.2m 에서 깨지는 것을 grep 해서 목록으로 보고 (지난 검수 목록: Vision.cs:21 EyeOffset, PlayerCombat.cs:31/369 aimHeight, PlayerCrafting.cs:67, PlayerDeath.cs:114 대체 위치 1.05, WindowFrame VaultTarget).
- 자기검사: 몸 키 1.2 ± 0.02, cc.height 1.2, 눈높이(발 기준) 1.15 ± 0.05, 기존 문 인방 검사 통과.

## B. 달리기 때 발목 꺾임 (사용자 관찰, 원인 미확인)
- **먼저 원인을 잰다**, 추측으로 고치지 않는다. 후보:
  1. 모델 아바타의 LeftFoot/RightFoot/LeftToes/RightToes 매핑이 틀림 (지난번 눈 매핑처럼 머리카락/다른 본이 잡혔는지).
  2. 애니메이터 상태의 Foot IK 가 꺼져 있음 (`AnimatorState.iKOnFeet`).
  3. 클립 임포트 설정 (Humanoid 클립의 `keepOriginalPositionY/XZ`, `heightFromFeet`, 루프 포즈) 문제.
  4. 모델 T-포즈/아바타 근육 설정(발 회전 한계).
- 확인 방법 예: 에디트 모드에서 Run 클립을 `AnimationMode.SampleAnimationClip` 으로 여러 시점 샘플링해 발 본의 로컬 회전(정강이 대비 각도)을 재고, 원본 Mixamo 클립(Generic 으로 임시 로드하거나 클립 곡선) 값과 비교.
- 원인을 찾으면 가장 작은 수정으로 고치고, 원인·측정값을 출력에 적는다. 못 찾으면 측정값만 보고하고 멈춘다.
- 자기검사: Run·Walk 샘플 시점마다 발목(정강이-발) 굽힘 각도가 정상 범위(예: 45° 이하 편차) 안인지 — 기준값은 측정 후 정하고 근거를 주석으로.

## C. 몸은 이동 방향, 고개·시야는 마우스 (사용자 결정, 좀보이드 방식)
- **시야(VisionField.Facing)는 마우스 조준 방향**(`PlayerAim.AimDirection`)을 쓴다. 지금은 `transform.forward`(몸) 를 쓴다.
- **몸 회전**:
  - 이동 중: 몸은 이동 방향을 본다. 단, 몸 정면과 조준 방향 차이가 **80°** 를 넘으면 몸을 조준 쪽으로 80° 안까지 돌린다 (고개가 못 돌아가는 만큼).
  - 멈춰 있을 때 / 공격할 때 / 조준 관련 행동(암살·밀치기 등 기존에 조준 방향을 쓰는 행동): 몸도 조준 방향.
    특히 **공격 판정 방향이 몸 정면(transform.forward)을 쓰는 곳이 있으면 공격 순간 몸을 조준 방향으로 돌린 뒤 판정**해서 지금 동작과 같게 유지. 판정이 이미 AimDirection 이면 그대로.
  - 기존 `PlayerAim.FaceAim` / `PlayerMotor` 의 회전 코드(128~129줄 `!aim` 조건)를 고쳐서 한 곳에서만 몸을 돌리게 (두 곳이 싸우지 않게).
- **고개**: Humanoid IK 로 머리가 조준점을 본다 — `PlayerAnimator` 에서 `OnAnimatorIK` → `SetLookAtWeight(1, body≈0.2, head 1, eyes 0, clamp≈0.5)` + `SetLookAtPosition(조준점, 눈높이)`.
  컨트롤러 레이어 IK Pass 를 빌더가 켠다. 사망·붙잡힘·암살 중에는 weight 0.
- 자기검사: 컨트롤러 IK Pass 켜짐. 에디트 모드로 가능한 범위에서 몸 회전 규칙 함수(이동 방향, 조준 방향 → 몸 방향)가 80° 규칙을 지키는지 (입력 몇 쌍으로 직접 호출. 이를 위해 순수 함수 하나만 노출).

## 하지 말 것
뒷걸음·옆걸음 애니메이션 추가(없는 클립), 머리카락 설정 변경, 좀비 변경, 카메라 변경.

## 출력
변경 파일 목록과 요약만. A 의 grep 목록과 고친 것/안 고친 것, B 의 측정값과 원인·수정, C 담당 코드 한 줄씩, 명세와 다르게 한 부분과 이유, 막힌 것.
