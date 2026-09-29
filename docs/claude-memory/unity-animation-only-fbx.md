---
name: unity-animation-only-fbx
description: 블렌더에서 애니메이션만 든 FBX 를 유니티 휴머노이드 클립으로 들여오는 절차와 함정
metadata: 
  node_type: memory
  type: project
  originSessionId: 86ec9df7-57ce-43e0-a0d8-b8b95818bebd
  modified: 2026-09-29T01:34:38.427Z
---

zzzz 프로젝트에서 캐릭터 애니메이션을 넣는 경로는 "블렌더 액션 → 뼈만 든 FBX → 유니티 Humanoid 리타기팅" 이다. 믹사모 클립도 똑같은 경로를 탄다.

**블렌더 내보내기** (`object_types={'ARMATURE'}`, 메쉬 제외 = 믹사모의 "Without Skin"):
`bake_anim=True, bake_anim_use_all_actions=False`, `axis_forward='-Z', axis_up='Y'` — 모델 FBX(`rig_work/repair.py` 107행) 와 같은 축을 써야 한다.

**함정 1 — `preserveHierarchy` 를 반드시 켠다.** 애니메이션 전용 FBX 는 루트 오브젝트가 하나라서 유니티가 그 노드(`Armature`) 를 지워버린다. 모델 FBX 는 루트가 둘(Armature + remesh) 이라 남아 있다. 그래서 본 경로가 어긋나고, `avatarSetup = CopyFromOther` 가 조용히 실패해 `importedTakeInfos` 가 **0개**가 된다. FBX 바이너리에는 `AnimationCurve` 가 멀쩡히 들어 있으므로 내보내기를 의심하면 시간을 버린다.

**함정 2 — 콜라이더 높이.** 모델 실측 높이는 약 1.0m(치비) 인데 플레이어 히트스캔은 어깨(y 1.40) 에서 반지름 0.5 로 훑는다. 콜라이더를 1.0m 로 만들면 스윕 아래끝(0.90) 이 머리 위를 지나 총이 안 맞는다. `ZombieDefinition.bodyHeight` 로 크기를 데이터화하고 `modelScale = bodyHeight / 실측높이` 를 빌더가 박아둔다. 현재 값 1.6m.

**함정 3 — Mixamo 에서 받은 클립은 CopyFromOther 를 쓰면 안 된다 (2026-09-29, 메인 캐릭터 character01).**
블렌더 모델은 `ArmatureT/mixamorig:Hips/...` 인데 Mixamo "Without Skin" 클립은 루트 바로 아래 `mixamorig:Hips/...` 라 계층이 달라서, preserveHierarchy 를 켜도 takes 가 조용히 0개다. 클립마다 `CreateFromThisModel`(sourceAvatar 없음) 로 자기 아바타를 만들면 Humanoid 리타기팅이 알아서 옮긴다 (Idle 8.33s humanMotion=True 확인).
또 Human 자동 매핑이 머리카락 본(`Hair_Side_L_01`)을 **LeftEye** 로 잡아 "Copied Avatar Rig Configuration mis-match" 가 난다 → 모델 아바타에서 LeftEye/RightEye/Jaw 매핑을 비운다.

**함정 4 — FBX 내장 텍스처 추출.** `ModelImporter.ExtractTextures` 가 **확장자 없는** 파일(`Image_0_001`, 실제로는 PNG)을 만들어 무한 재임포트 경고가 나고, `.png` 를 붙여도 **TextureCube 로** 들어와 `LoadAssetAtPath<Texture2D>` 가 null 이다. 확장자 복구 + `textureShape = Texture2D` 둘 다 필요.

**How to apply:** 새 클립을 추가할 때 `Assets/_Game/Art/Characters/zombi1@<이름>.fbx` 로 내보내고, 빌더의 `CheckZombieModelAndAnimation` 이 검사하는 임포트 설정 4개(Human / CopyFromOther / sourceAvatar / preserveHierarchy) 를 맞춘다. 관련: [[unity-playmode-frame-ticks]]
