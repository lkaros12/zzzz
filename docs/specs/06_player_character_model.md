# 작업: 메인 캐릭터 모델(character01) 적용

## 원칙 (AGENTS.md 와 같다)
1. Think Before Coding — 명세가 모호하거나 코드와 안 맞으면 추측하지 말고 멈추고 보고한다.
2. Simplicity First — 최소 코드. **좀비 모델·애니메이션 경로를 그대로 본뜬다** (새 추상화 금지). 새 런타임 클래스는 `PlayerAnimator` 1개만.
3. Surgical Changes — 관련 줄만. 좀비 쪽 동작 변경 금지.
4. Goal-Driven Execution — 완료 기준별로 무엇이 확인하는지 남긴다.
- UTF-8, 한글 주석("왜"), **perl 편집 금지**. Unity 에디터 조작·커밋 금지.
- **토큰 절약**: `V01SceneBuilder.cs` 전체를 읽지 마라. 참고 위치:
  `CreatePlayer` 343줄~ (캡슐, cc.height 2 / radius 0.32, `CapsuleFace.Build` 366줄),
  `EnsureZombieController` 4670줄, `LoadClip` 4741줄, `ApplyZombieModel` 4761줄, `MeasureModelHeight` 4787줄,
  `CheckZombieModelAndAnimation` 3456줄. 런타임 참고: `Assets/_Game/Scripts/Combat/ZombieAnimator.cs`.
  도시 빌더(`HadanCityBuilder.cs`)는 `CreatePlayer` 를 재사용하므로 따로 고칠 필요가 없어야 한다 (필요하면 보고).

## 입력
- `Assets/_Game/Art/Characters/Player/character01.fbx` (메인이 복사해 둠, LFS).
  실측: 리그 Generic, 클립 0개, SkinnedMeshRenderer 2개(`BodyT_rig`, `Hair_work`), 삼각형 246,528, 원래 키 약 1.12m,
  본 84개(Mixamo 이름 65 + 머리카락 19). 텍스처는 FBX 안에 들어 있다(embedded), 재질 2개 `material_0.001/.002`.
- 애니메이션(사용자가 Mixamo 에서 받아 넣는다, "Without Skin"):
  `Assets/_Game/Art/Characters/Player/character01@Idle.fbx`, `@Walk`, `@Run`, `@Attack`, `@Death`.
  **아직 없을 수 있다** — 없는 클립은 그 상태를 비워 두고 경고 로그만. 있으면 자동으로 연결된다 (빌더 재실행만으로).

## 만들 것
1. **임포트 설정** (빌더가 보장, 좀비와 같은 방식):
   - 모델: `animationType = Human`, 아바타 새로 생성(Mixamo 본 이름 자동 매핑). 매핑 실패하면 멈추고 보고.
   - 텍스처: FBX 내장 텍스처를 `Assets/_Game/Art/Characters/Player/Textures/` 로 추출하고 재질이 그걸 쓰게 한다.
     재질은 URP Lit (또는 좀비가 쓰는 셰이더와 같게). 추출은 한 번만 (재빌드 때 중복 생성 금지).
   - 클립 FBX: 좀비와 같은 4가지 설정 (Human / CopyFromOther / sourceAvatar = character01 아바타 / preserveHierarchy = true).
     Idle·Walk·Run 은 loopTime = true. 루트 모션 끔.
2. **플레이어 몸**: 캡슐 렌더러·`CapsuleFace` 를 없애고 character01 을 자식으로 붙인다.
   - **키 1.6m** (사용자 결정, 좀비와 같음): `modelScale = 1.6 / MeasureModelHeight(model)`. 발이 바닥(콜라이더 아래끝)에 닿게.
   - `CharacterController.height = 1.6`, center 도 맞춘다. radius 0.32 유지.
   - **2m 캡슐을 전제한 수치를 grep 해서 목록으로 보고**하고, 깨지는 것만 고친다 (예: 눈높이·어깨 높이·조준/히트스캔 원점,
     시야 원점, 좀비 공격 판정 높이, 문 인방 자기검사 `머리 + stepOffset < 문 높이`). 안 깨지면 건드리지 않는다.
   - 피격 번쩍임(`HitFlash`)·얼굴 색(`FaceTint`) 처럼 캡슐 렌더러를 잡던 컴포넌트가 있으면 새 몸의 렌더러를 잡게 한다. 널 참조 금지.
3. **애니메이터 컨트롤러** `Assets/_Game/Data/Animation/Player.controller` (빌더가 만듦, `EnsureZombieController` 방식):
   - 파라미터 Speed(float), Attack(trigger), Dead(bool).
   - Idle ↔ Walk(Speed > 0.1) ↔ Run(Speed > 1.4), Attack 은 어디서든 트리거로 들어갔다 끝나면 이동 상태로, Dead 는 어디서든.
   - 클립이 없으면 그 상태는 모션 없이 둔다.
4. **새 런타임 스크립트** `Assets/_Game/Scripts/Player/PlayerAnimator.cs` — `ZombieAnimator` 와 같은 방식:
   - Speed = 실제 수평 이동 속도 / 걷기 기준 속도 (PlayerMotor 의 걷기 속도, SerializeField), 부드럽게.
   - Attack: `PlayerCombat` 이 휘두를 때 (기존 공개 멤버 — 마지막 공격 시각 같은 것 — 을 읽는다. 없으면 공개 읽기 전용 프로퍼티 하나만 추가).
   - Dead: `PlayerStats.IsDead`. 부활하면 Dead=false, Idle 로.
   - 한 방향으로만 읽는다 (애니메이션이 게임 로직을 바꾸지 않는다).
5. **머리카락 흔들림**: 이번 범위 아님.

## 자기검사 (별장 `Tools/V0.1/Self Check` 에 추가, 기존 유지)
1. character01 임포트: Human, 아바타 유효(isHuman), 추출 텍스처 폴더에 텍스처가 있고 재질이 그걸 참조.
2. 플레이어에 Animator + Player.controller, 몸 키가 1.6m ± 0.02 (MeasureModelHeight × scale), cc.height 1.6.
3. 캡슐 MeshRenderer·CapsuleFace 가 플레이어에 없다.
4. 컨트롤러에 Idle/Walk/Run/Attack/Death 상태와 파라미터 3개. 있는 클립 파일은 4가지 임포트 설정이 맞고 상태에 연결돼 있다.
   없는 클립은 **경고만** (실패 아님), 로그에 "없는 클립: ..." 목록.
5. 기존 문 통과 검사(머리 + stepOffset < 문 높이) 등 기존 검사가 모두 통과.

## 하지 말 것
머리카락 물리, 폴리곤 줄이기(블렌더 작업), 얼굴 표정, 무기 손에 쥐기(다음 작업), 좀비 모델 변경.

## 출력
**변경/추가 파일 목록과 요약만**. 만들 것 1~4 와 자기검사 1~5 를 담당하는 코드를 한 줄씩,
"2m 전제 수치" grep 결과와 고친 것/안 고친 것, 명세와 다르게 한 부분과 이유, 막힌 것.
