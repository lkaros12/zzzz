# 작업: 16 마무리 (사용량 한도로 도중에 끊긴 작업 이어서)

원명세 `docs/specs/16_hadan_hollow_floors_stairs.md` 를 먼저 읽는다. 원칙(AGENTS.md): 모호하면 멈추고 보고 / 최소 코드 / 관련 줄만 / 목표 확인. UTF-8·한글 주석, perl 금지, Unity 조작·커밋 금지.
현재 `HadanCityBuilder.cs` 와 새 `Assets/_Game/Scripts/World/BuildingFloors.cs` 에 절반 이상 구현돼 있다. **다시 짜지 말고 아래만** 한다.

## 읽기 전용 검수 결과 (현재 컴파일 실패 — Editor 오류 3개)
1. `CreateBuildings` 약 600줄: `StairPlan stair = default;` 먼저 선언 후 `hasStairs = floors > 1 && TryStairPlan(polygon, out stair);` (CS0165).
2. `CountStairBuildings(BuildingFloors)` 추가 (hasStairs 인 건물 수) — 246·247줄에서 호출 중인데 정의 없음 (CS0103).
3. `CheckBuildingPaths(BuildingFloors)` 추가: 계단 건물을 고정 시드로 5채 이상 뽑아 문 앞 → 꼭대기 층 착지점 `NavMesh.SamplePosition` + `CalculatePath` 가 PathComplete 인 채 수를 돌려준다 (문 차단 장애물 고려).
4. **층 숨김 결함**: `BuildingFloors.ApplyVisibility/ShowAll` 이 `SetActive(false)` 로 숨겨 콜라이더·시야 차단까지 꺼진다 (지금 층 천장이 사라져 위로 시야가 뚫리고 위층 물건이 떨어짐). → **Renderer.enabled 만 끄고** 콜라이더는 유지. 위층 외벽에 붙은 간판·실외기 결합 메쉬도 같은 층 단위로 숨겨지게(층별로 묶거나 층 정보로 필터 — 최소 방법, 안 되면 보고).
5. **계단 끝 0.2m 틈**: `HoleHalfPlane/HoleCorners` 가 계단 앞뒤로 0.2m 더 판다 → 진행 방향 여유 제거(옆 방향만 유지), 계단 끝과 위층 바닥이 이어지게.
6. `StairFits`: 1층 문 앞 1.5m 원과 계단(구멍·옆벽)이 겹치면 탈락 (다른 후보 위치 시도, 없으면 1층만).
7. `firstFloorOnly` 는 `floors > 1 && !hasStairs` 만 센다.
8. `CheckFloorLocator`: `TryLocate` 로 (건물 번호, 층) 정답 확인 + 평면상 건물 바깥 점 → 없음.
9. `CheckBuildingProps` 외관 수: 15번 실측 기준(간판 356, 실외기 1623, 옥탑방 91, 전선 1278, 같은 지도·시드) 과 ±5% 비교 — 지금은 방금 센 값끼리 비교라 항상 통과.
10. **빠진 예전 검사 되살리기 (메인 결정)**: 법선 검사(`badFaces` — 새 층 구조 메쉬에 맞게), "문·차양에 콜라이더 없음"(이제 문은 실제 문이라 차양만), "난간이 지붕 메쉬에 병합". 명세 16 자기검사 1~6 은 모두 유지.
11. `HadanFadeMaterial` 이 새로 만드는 `_fade.mat` 때문에 "Data 파일 수 같다" 검사가 첫 빌드에 한 번 실패하는 것은 그대로 둬도 됨(두 번째 빌드부터 통과) — 주석만.

## 끝나면
`dotnet build Assembly-CSharp-Editor.csproj --no-restore` 오류 0 확인.

## 출력
변경 파일 목록과 요약만. 1~11 한 줄씩, 막힌 것.
