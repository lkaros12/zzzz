# 작업: 06 마무리 — 캐릭터 모델 검수 지적 + 한 손 무기 공격 모션

직전에 `docs/specs/06_player_character_model.md` 를 구현하다 **사용량 한도로 끊겼다** (코드는 컴파일됨, 빌더 미실행).
읽기 전용 검수 결과로 남은 것만 한다. 원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인.
UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지. `V01SceneBuilder.cs` 전체를 읽지 말고 아래 줄 주변만.

## 입력 (메인이 넣어 둠)
`Assets/_Game/Art/Characters/Player/` 에 Mixamo "Without Skin" FBX for Unity 30fps:
`character01@Idle`, `@Walk`(In Place), `@Run`(In Place), `@Attack`(Stabbing), `@Death`(Dying), **`@AttackOneHand`(Standing Torch Melee Attack 02)**.

## 고칠 것
1. **한 손 근접 무기 공격 모션 (사용자 요구)**: 컨트롤러에 상태 `AttackOneHand` + 트리거 파라미터 `AttackOneHand` 추가 (Attack 과 같은 전이 규칙).
   `PlayerAnimator` 가 공격을 알릴 때, 지금 든 주무기가 `handedness == OneHanded && !ranged` 이면 `AttackOneHand`, 아니면 `Attack`.
   무기 판정은 기존 코드(`PlayerEquipment.cs:117` 의 oneHandedMelee 판정 등) 를 재사용 — 새 판정 로직 복제 금지.
   클립 설정·자기검사 4 목록에 `AttackOneHand` 포함.
2. **컨트롤러 GUID 유지**: `V01SceneBuilder.cs` 약 4923줄 `DeleteAsset(Player.controller)` 때문에 빌드마다 GUID 가 바뀌어
   별장·도시 중 한쪽을 다시 빌드하면 다른 씬 Animator 참조가 끊긴다. 있으면 **지우지 말고** 제자리에서 상태·파라미터를 다시 채운다.
3. **창 넘기 착지 높이**: `WindowFrame.cs:283` `VaultTarget(p)` 기본 1.05, `PlayerVault.cs:37` 이 이걸 쓴다. 좀비처럼 `cc.height * 0.5f` 를 넘긴다.
4. **자기검사 NRE**: `V01SceneBuilder.cs:3555` `material` 이 null 이면 메시지 문자열의 `material.name` 에서 예외. null 이면 FAILED 로.
5. **순간이동 시 Speed 튐**: `PlayerAnimator.cs:38-44` 부활·씬 도착 순간이동 거리를 dt 로 나눠 Run 이 된다. 한 프레임 이동이 (달리기 속도 × dt × 3) 보다 크면 그 프레임은 속도 측정을 건너뛴다.
6. **텍스처 재참조 실패 시 빌드 중단 위험**: 약 4877줄 예외. 추출 텍스처를 못 찾으면 빌드 전체를 멈추지 말고 경고 + 자기검사 1 에서 FAILED.
7. 주석만: 5213 "머리(2.08m)" → 1.73, 5217 "눈높이(1.35)" → 1.15.

## 하지 말 것
사망 흐름 변경(지금은 죽는 즉시 부활해서 Death 모션이 거의 안 보인다 — 알려진 한계로 보고만), 머리카락 물리, 폴리곤 줄이기.

## 출력
변경 파일 목록과 요약만. 1~7 을 어떻게 고쳤는지 한 줄씩, 막힌 것.
