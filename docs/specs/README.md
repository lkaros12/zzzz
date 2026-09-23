# Codex 대기 작업

승인된 명세. 번호 순서대로 Codex 에 보낸다 (CLAUDE.md 워크플로).

| 번호 | 파일 | 상태 |
|---|---|---|
| 01 | 01_roof_and_ceiling_height.md | 승인됨, Codex 한도로 대기 (2026-09-24 12:57 이후) |
| 02 | 02_tab_ui_bleeding_healing.md | 승인됨, 01 다음 |

보내는 법 (저장소 루트에서):

    codex exec -s workspace-write -C . -o result.md - < docs/specs/01_roof_and_ceiling_height.md
