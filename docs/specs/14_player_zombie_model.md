# 작업: 플레이어가 죽어 생긴 좀비를 캡슐 대신 character01 모델로

원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.
`V01SceneBuilder.cs` 전체를 읽지 말 것 (플레이어 모델·`ApplyZombieModel`·`EnsureZombieController`·관련 자기검사만).

## 현재 (사용자 지적: "죽고 좀비될 때 왜 도형으로 나와")
`Zombie.cs:1251 Spawn(..., playerCorpse)` 가 플레이어 좀비면 모델 없이 캡슐 + 전용 색(`corpseMaterial`)을 쓴다 (주석: "내 시체라는 걸 색으로 알리려고"). `PlayerDeath.cs:65` 에서 호출.

## 만들 것
1. 플레이어 좀비는 **플레이어와 같은 character01 모델**(같은 키 1.2m, 같은 배율)로 생성. 콜라이더 높이·반지름도 플레이어와 같게.
2. Animator: 컨트롤러는 **Zombie.controller** (좀비 AI·`ZombieAnimator` 가 그대로 구동), 아바타는 character01 아바타 (Humanoid 리타기팅으로 좀비 걷기·공격·죽음 클립이 그대로 입혀진다). 좀비 클립이 character01 에서 재생되는지 자기검사로 확인할 수 있는 범위에서 확인.
3. **좀비 색**: character01 재질 2개의 좀비용 사본(빌더가 에셋으로 만듦, 이름 `character01_zombie_*`, 기존 텍스처 유지 + 회녹색 톤(_BaseColor 곱) + 약간 어둡게). "내 시체" 구분은 이 색으로 한다.
4. 모델·배율·재질·아바타는 **빌더가 PlayerDeath 에 직렬화 필드로 넣는다** (런타임 에셋 검색 금지). `Zombie.Spawn` 에 모델·배율을 받는 경로를 최소로 추가 (기존 종류(ZombieDefinition) 모델 경로 재사용).
   HairSpring·PlayerLookAtRelay·PlayerAnimator 같은 플레이어 전용 컴포넌트는 좀비에 붙이지 않는다.
5. 캡슐 경로는 모델이 없을 때의 대체로만 남긴다. 도시 씬(`HadanCityBuilder` 가 CreatePlayer 재사용)에서도 같게 동작해야 한다.
6. 자기검사: PlayerDeath 에 좀비 모델·재질이 연결돼 있다, 좀비 재질 사본이 텍스처를 참조한다, (에디트 모드에서 Spawn 을 한 번 호출해) 생성된 플레이어 좀비에 캡슐 MeshRenderer 가 없고 SkinnedMeshRenderer 와 Zombie.controller Animator 가 있다 → 검사 후 삭제.

## 하지 말 것
좀비 AI·일반 좀비 모델 변경, 좀비 머리카락 물리, 소지품 규칙 변경.

## 출력
변경 파일 목록과 요약만. 1~6 한 줄씩, 막힌 것.
