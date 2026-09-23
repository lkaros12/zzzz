---
name: unity-animation-only-fbx
description: 블렌더에서 애니메이션만 든 FBX 를 유니티 휴머노이드 클립으로 들여오는 절차와 함정
metadata: 
  node_type: memory
  type: project
  originSessionId: 86ec9df7-57ce-43e0-a0d8-b8b95818bebd
  modified: 2026-09-17T07:08:11.705Z
---

zzzz 프로젝트에서 캐릭터 애니메이션을 넣는 경로는 "블렌더 액션 → 뼈만 든 FBX → 유니티 Humanoid 리타기팅" 이다. 믹사모 클립도 똑같은 경로를 탄다.

**블렌더 내보내기** (`object_types={'ARMATURE'}`, 메쉬 제외 = 믹사모의 "Without Skin"):
`bake_anim=True, bake_anim_use_all_actions=False`, `axis_forward='-Z', axis_up='Y'` — 모델 FBX(`rig_work/repair.py` 107행) 와 같은 축을 써야 한다.

**함정 1 — `preserveHierarchy` 를 반드시 켠다.** 애니메이션 전용 FBX 는 루트 오브젝트가 하나라서 유니티가 그 노드(`Armature`) 를 지워버린다. 모델 FBX 는 루트가 둘(Armature + remesh) 이라 남아 있다. 그래서 본 경로가 어긋나고, `avatarSetup = CopyFromOther` 가 조용히 실패해 `importedTakeInfos` 가 **0개**가 된다. FBX 바이너리에는 `AnimationCurve` 가 멀쩡히 들어 있으므로 내보내기를 의심하면 시간을 버린다.

**함정 2 — 콜라이더 높이.** 모델 실측 높이는 약 1.0m(치비) 인데 플레이어 히트스캔은 어깨(y 1.40) 에서 반지름 0.5 로 훑는다. 콜라이더를 1.0m 로 만들면 스윕 아래끝(0.90) 이 머리 위를 지나 총이 안 맞는다. `ZombieDefinition.bodyHeight` 로 크기를 데이터화하고 `modelScale = bodyHeight / 실측높이` 를 빌더가 박아둔다. 현재 값 1.6m.

**How to apply:** 새 클립을 추가할 때 `Assets/_Game/Art/Characters/zombi1@<이름>.fbx` 로 내보내고, 빌더의 `CheckZombieModelAndAnimation` 이 검사하는 임포트 설정 4개(Human / CopyFromOther / sourceAvatar / preserveHierarchy) 를 맞춘다. 관련: [[unity-playmode-frame-ticks]]
