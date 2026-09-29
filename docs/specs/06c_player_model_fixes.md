# 수정: 06/06b/07 Unity 실측 + 검수 지적

원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.
`V01SceneBuilder.cs` 전체를 읽지 말고 해당 함수 주변만.

## Unity 실측 (메인이 빌더 실행)
콘솔:
- `An infinite import loop has been detected ... Assets/_Game/Art/Characters/Player/Textures/Image_1_001 (changed), Image_0_001 (changed)`
- `Rig Error: Copied Avatar Rig Configuration mis-match. Transform 'Hair_Side_L_01' for human bone 'LeftEye' not found` (클립 6개 모두)
- `material_0.001/0.002 재질이 추출된 character01 텍스처를 참조하지 않습니다`, `플레이어 재질이 없습니다`
- `Self check FAILED: character01 내장 텍스처가 Textures 폴더에 추출됐다`
`Textures/` 실제 파일: `Image_0_001`, `Image_0_003`, `Image_1_001`, `Image_1_003` — **확장자 없음**, 네 개 모두 PNG 시그니처(89 50 4E 47), 서로 다른 이미지(복사본 아님, 4장 모두 필요).

## 고칠 것
1. **텍스처 확장자**: 추출 뒤 확장자 없는 파일은 PNG/JPEG 시그니처를 보고 `.png`/`.jpg` 를 붙여 `AssetDatabase.MoveAsset` 으로 이름을 바꾼다 (지금 있는 4개도 이 경로로 처리). 추출은 폴더에 이미지가 하나도 없을 때만.
   그다음 재질이 올바른 텍스처를 쓰게 한다 — FBX 재질이 원래 가리키던 텍스처(추출 뒤 임포터 재매핑 또는 FBX 재질의 원래 텍스처 이름)로 연결. 각 재질에 기본색(_BaseMap) 외에 노멀맵 등이 있으면 같이 연결 (노멀맵은 TextureImporter 를 NormalMap 으로).
   어느 텍스처가 어느 재질·슬롯인지 알 수 없으면 추측하지 말고 멈추고 보고 (가능하면 FBX 의 재질-텍스처 참조를 로그로 남겨라).
   무한 재임포트가 다시 안 나야 한다 — 임포트 설정은 바뀔 때만 적용.
2. **아바타 눈 매핑**: Human 자동 매핑이 머리카락 본을 LeftEye(아마 RightEye/Jaw 도)로 잡았다. character01 아바타의 humanDescription 에서 **LeftEye/RightEye/Jaw 매핑을 비운다** (선택 본이다). 클립 FBX 는 CopyFromOther 그대로. Rig Error 가 사라져야 한다.
   자기검사 1 에 "아바타의 LeftEye/RightEye/Jaw 가 Hair_ 본이 아니다" 추가.
3. **HairSpring 자기검사 3 거짓 통과**: `V01SceneBuilder.cs:3579-3589` 가 한 번에 17m 옮겨 `IsTeleport` 로 리셋만 된다. 순간이동 한계보다 작은 이동(프레임당 0.2m 등)을 여러 번 주며 Simulate 를 돌려 **실제로 본이 기준에서 움직였고(> 0.5°)** 동시에 한계 이하인지 둘 다 검사.
4. **가닥 누적 각도**: 지금은 본마다 부모 기준 12° 라 3마디 끝은 월드로 36° 까지 휜다. 원작자 실측(10~15° 안에서 틈 없음)에 맞춰 **가닥 뿌리 기준 누적 편차도 `maxAngle` 이하**가 되게 제한 (본마다 12° + 누적 12°).
5. **큰 dt 발산**: Simulate 에서 dt 를 1/30 초로 자른다. damping 은 프레임률 무관하게 (`Mathf.Pow(1-damping, dt*60)` 같은 식) 적용.
6. **공격 트리거 남음**: `PlayerAnimator.cs:69` 한 트리거를 켤 때 다른 공격 트리거는 `ResetTrigger`, 사망 시 두 공격 트리거 모두 `ResetTrigger`.
7. (선택 아님) `HairSpring` 본 null 이면 그 가닥 건너뛰기 한 줄.

## 하지 말 것
기준 자세를 매 프레임 애니메이션에서 다시 읽기(지금 클립이 머리카락을 안 움직임 — 보류), 중력 튜닝, 보조손 모션 선택.

## 출력
변경 파일 목록과 요약만. 1~7 한 줄씩, 텍스처-재질 매핑 결과(어느 파일이 어느 재질의 어느 슬롯), 막힌 것.
