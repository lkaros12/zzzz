# 작업: 근접 휘두르기 막대기 제거 + 공격 모션 속도를 공격 간격에 맞춤

원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.
`V01SceneBuilder.cs` 전체를 읽지 말 것 (자기검사 2753~2757줄 근처, `EnsurePlayerController` 공격 상태 부분만).

## A. 막대기 제거 (사용자 요구)
- 근접 공격 때 나오는 부채꼴 막대기 = `AttackSwingFx.Play` (`PlayerCombat.cs:257`). 호출과 근접 블레이드 코드(pivot/blade/material, Play)를 지운다.
- **총 궤적(`PlayTracer`)은 유지** (원거리 즉발 궤적은 다른 기능).
- 자기검사 2753줄 `swingFx.Play(` 문자열 검사는 "근접 막대기가 없다" 로 바꾸고, 트레이서 검사(2755·2757)는 유지.
- 클래스 주석의 "실제 애니메이션이 들어오면 지운다" 도 트레이서만 남은 현실에 맞게 한 줄 수정.

## B. 공격 모션 속도 (사용자 요구: 너무 느리다, 공속에 맞춰 빠르게)
- 공격 간격: `PlayerCombat.cs:218` `interval = useWeapon ? weapon.attackInterval : unarmedInterval`.
- 컨트롤러 Attack / AttackOneHand 상태에 speed 배율 파라미터(`AttackSpeed`, float, 기본 1) 연결 (`speedParameterActive`).
- `PlayerAnimator` 가 공격 트리거를 켤 때 `AttackSpeed = max(1, 해당 클립 길이 / interval)` — 한 번 휘두르는 모션이 공격 간격 안에 끝난다. 간격이 클립보다 길면 1 (느리게 늘이지 않음).
  interval 은 PlayerCombat 에 이미 있는 값을 공개 읽기 전용으로 읽는다 (없으면 프로퍼티 하나 추가). 클립 길이는 컨트롤러에서 한 번 캐시.
- Attack 상태 exit time 전이(`hasExitTime`)가 배율 때문에 어긋나지 않는지 확인 (정규화 시간이라 그대로면 OK).
- 자기검사: 두 공격 상태에 speedParameter `AttackSpeed` 가 연결돼 있다. 순수 계산(클립 1.5s, 간격 0.8 → 1.875 / 간격 2.0 → 1) 검사.

## 출력
변경 파일 목록과 요약만. A·B 한 줄씩, 실제 클립 길이(Attack, AttackOneHand)와 현재 무기 간격 예시, 막힌 것.
