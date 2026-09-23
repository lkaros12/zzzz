# 작업: 하단 시험 칸 (도시 1차) — 실제 지도로 건물·도로 세우기

## 원칙 (AGENTS.md 와 같다)
1. Think Before Coding — 명세가 모호하거나 코드와 안 맞으면 추측하지 말고 멈추고 보고한다.
2. Simplicity First — 최소 코드. 새 추상화 금지. 기존 빌더 헬퍼 재사용.
3. Surgical Changes — 별장 씬(`V01_Sandbox`)과 그 빌드 경로는 건드리지 않는다.
4. Goal-Driven Execution — 완료 기준별로 무엇이 확인하는지 남긴다.
- 파일은 UTF-8, 한글 주석. **perl 편집 금지**. 주석은 기존 스타일(한국어, "왜").
- Unity 에디터 조작(빌드·재생·자기검사) 금지, 커밋 금지. 컴파일·Unity 테스트는 메인이 한다.
- **토큰 절약**: `V01SceneBuilder.cs`(5,400줄) 전체를 읽지 마라. 필요한 헬퍼는 아래에 적었다.

## 목적
실제 지도 데이터로 하단역 주변을 세워서 **크기감(배율)·성능·지도 품질**을 잰다. 실내·게임플레이는 이번 범위가 아니다.

## 입력 데이터
- `docs/map/hadan_500m.json` — 오픈스트리트맵 Overpass 결과 (`out tags geom`).
  - `elements[]` 각 항목은 way. `tags` 와 `geometry`(`[{lat, lon}, ...]`, 닫힌 폴리곤은 첫 점 = 끝 점).
  - `tags.building` 이 있으면 건물 (459채), `tags.highway` 가 있으면 도로 (115구간).
  - 범위: 위도 35.1040~35.1085, 경도 128.9640~128.9695. 중심 (35.10625, 128.96675).
- 실측 요약 (메인이 확인): 건물 종류 `yes` 95%, 층수 태그 52채(2~27층), 바닥 면적 중앙값 114㎡, 꼭짓점 최대 21개.
- 도로 종류: service 42, footway 34, primary 10, secondary 9, tertiary 8, primary_link 4, residential 2, pedestrian 2.

## 만들 것
1. **새 에디터 스크립트** `Assets/_Game/Editor/HadanCityBuilder.cs` (빌더 본체와 분리).
   메뉴 `Tools/V0.1/Build Hadan Pilot` → 새 씬 `Assets/_Game/Scenes/V01_City_Hadan.unity` 를 만들어 저장.
   - 플레이어·카메라·HUD·시야 음영은 별장 빌더의 생성 함수를 재사용할 수 있으면 재사용한다
     (`V01SceneBuilder` 의 `CreatePlayer`, `CreateCamera`, `CreateHud`, `ApplyRenderSettings`, `CreateLight`, `BuildNavMesh` 등 —
     private 이면 `internal` 로 바꾸는 정도의 최소 변경만 허용). 재사용이 막히면 멈추고 보고.
2. **좌표 변환**: 중심 기준 등거리 근사.
   `x = (lon - 128.96675) * 111320 * cos(35.10625°) * CityScale`, `z = (lat - 35.10625) * 110540 * CityScale`.
   `const float CityScale = 1.6f;` 한 곳에서 바꿀 수 있게. (메인이 1.0 과 1.6 을 비교한다.)
3. **건물**: 폴리곤 외곽을 위로 밀어 올린 덩어리 메쉬 (옆면 + 평평한 지붕, 바닥면 불필요).
   - 오목한 폴리곤이 있으므로 지붕은 귀 자르기(ear clipping) 삼각분할. 감김 방향이 섞여 있을 수 있으니 정규화한다.
   - `MeshCollider`(볼록 아님) 를 달아 시야와 통행을 막는다. 레이어는 기본(Default) — `Vision.BlockerMask` 에 걸려야 한다.
   - 높이 = 층수 × 3.6m. 층수 규칙:
     `building:levels` 태그가 있으면 그 값 / `building=apartments` 면 15 /
     그 외 면적(실제 ㎡, CityScale 적용 전)으로: <100 → 3, 100~250 → 5, 250~500 → 8, ≥500 → 12.
   - 재질 2~3개(회색 계열)로 돌려 써서 건물이 구분되게. 같은 재질을 공유한다.
   - 정적 배칭 대상으로 표시한다 (`StaticEditorFlags`), 메쉬는 씬에 저장되도록 에셋으로 저장하거나 씬에 직렬화되게 한다.
4. **도로**: 선을 따라 얇은 바닥 판(높이 0.02, 콜라이더 없음).
   폭(실제 m, CityScale 곱함): primary 12 / primary_link·secondary 9 / tertiary 7 / residential·service 5 / footway·pedestrian 2.5 / 그 외 4.
5. **지면**: 범위 전체를 덮는 평면 + 두꺼운 박스 콜라이더 (별장 `MakeGroundSolid` 방식).
6. **플레이어 시작 위치**: 가장 굵은 도로(primary) 위, 범위 중심에 가장 가까운 점. 건물 콜라이더와 겹치지 않아야 한다.
7. **NavMesh**: 씬 전체를 굽고, 걸린 시간(ms)을 로그로 남긴다 (`System.Diagnostics.Stopwatch`).
8. 로그 한 줄 요약: 건물 수, 도로 수, 삼각형 수, 가장 높은 건물, 굽기 시간.

## 자기검사 (메뉴 `Tools/V0.1/Check Hadan Pilot`)
1. 씬 건물 수가 JSON 건물 수(459)의 ±5% 안이다. 모든 건물에 콜라이더가 있다.
2. 건물 메쉬에 뒤집힌 면이 없다 (옆면 법선이 바깥, 지붕 법선이 위).
3. 플레이어 시작 위치가 어떤 건물 콜라이더 안에도 없다.
4. NavMesh 가 구워졌고 시작 위치에서 반경 50m 안의 임의 도로 점 몇 개로 경로가 이어진다.
5. 씬 전체 고유 메쉬 삼각형 수를 로그로 남긴다 (기준 없음, 측정용).
6. 별장 씬 자기검사(`Tools/V0.1/Self Check`)는 영향을 받지 않는다 — 별장 빌더 파일 변경은 접근자(internal) 정도만.

## 하지 말 것
실내, 문·창문, 지형 높낮이, 구역 나눠 불러오기, 간판·차량·나무, 좀비 행동 변경, 새 셰이더.

## 출력
**변경/추가 파일 목록과 요약만**. 만들 것 1~8 과 자기검사 1~6 을 담당하는 코드를 한 줄씩, 명세와 다르게 한 부분과 이유, 막힌 것.
