# 작업: 메인 캐릭터 머리카락 흔들림 (직접 만든 스프링 본)

## 원칙 (AGENTS.md 와 같다)
1. Think Before Coding — 모호하거나 코드와 안 맞으면 멈추고 보고.
2. Simplicity First — 새 런타임 클래스 `HairSpring` 1개만. 외부 패키지 금지.
3. Surgical Changes — 관련 줄만.
4. Goal-Driven Execution — 완료 기준별 확인 방법을 남긴다.
- UTF-8, 한글 주석("왜"), perl 금지, Unity 조작·커밋 금지.
- 06b 를 먼저 끝낸 뒤 이어서 한다 (같은 `CreatePlayer` 를 건드린다).

## 입력
- character01 에 머리카락 본 **7가닥, 19본** (Mixamo 65본과 별개, 총 84본). 본 이름은 FBX 를 보고 찾는다
  (Mixamo `mixamorig:` 접두사가 없고 Head 아래에 달린 체인). 못 찾거나 7가닥/19본이 아니면 멈추고 보고.
- 원작자 실측: **흔들림 각도 10~15° 안쪽에서 틈·찢어짐 없음**. 그 범위로 제한.

## 만들 것
1. `Assets/_Game/Scripts/Player/HairSpring.cs` — 한 컴포넌트가 모든 가닥을 처리 (가닥마다 컴포넌트 붙이지 않는다).
   - 가닥 = 뿌리 본에서 끝까지의 체인. 각 본의 "끝점"을 질점으로 두고 LateUpdate 에서 (애니메이션이 자세를 잡은 뒤):
     관성(이전 위치 차이 × (1 - damping)) + 원래 방향으로 돌아가는 힘(stiffness) + 약한 중력.
   - 계산한 방향으로 본을 회전하되, **애니메이션 기준 자세에서 벗어난 각도를 `maxAngle` 로 자른다** (기본 12°).
   - [SerializeField]: stiffness, damping, gravity, maxAngle(12, 범위 0~15 로 Range 제한).
   - 순간이동(부활·씬 도착) 때 한 프레임 이동이 크면 질점을 현재 자세로 리셋 (머리카락이 날아가지 않게). 임계값은 06b 의 PlayerAnimator 순간이동 판정과 같은 방식.
   - 매 프레임 할당 없음. `Time.deltaTime` 이 0 이면(일시정지) 건너뛴다.
   - 머리·몸 충돌 검사 없음 (각도 제한으로 대신).
2. **빌더**: `CreatePlayer` 에서 character01 에 `HairSpring` 을 붙이고 가닥 뿌리 본 목록을 직렬화해 넣는다 (런타임에 이름으로 찾지 않는다).

## 자기검사 (별장 `Tools/V0.1/Self Check` 에 추가)
1. 플레이어 모델에 HairSpring 1개, 가닥 7개, 본 합계 19.
2. maxAngle ≤ 15, 기본 12.
3. 에디트 모드 동작 검사: 모델 루트를 한 번 크게 옮긴 것처럼 시뮬레이션 함수를 몇 번 직접 호출해도 모든 머리카락 본의 기준 자세 대비 각도가 maxAngle + 0.5° 이하.
   (함수를 직접 부를 수 있게 internal/public 메서드 하나만 노출. 실시간 모습은 메인이 재생 모드에서 확인.)

## 하지 말 것
옷 물리, 머리카락 충돌체, 바람, 좀비 머리카락, 외부 패키지(UniVRM·Dynamic Bone·Magica Cloth).

## 출력
변경 파일 목록과 요약만. 찾은 머리카락 본 이름(가닥별), 만들 것·자기검사 담당 코드 한 줄씩, 명세와 다르게 한 부분과 이유, 막힌 것.
