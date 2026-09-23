---
name: perl-utf8-korean-pitfall
description: 한글이 든 UTF-8 소스를 perl 로 치환할 때 인코딩을 섞으면 파일 전체가 이중 인코딩된다
metadata:
  type: feedback
---

한글 주석/문자열이 든 UTF-8 소스 파일을 `perl -0777 -pi -e` 로 편집할 때, 삽입할 블록을 `:encoding(UTF-8)` 레이어로 읽으면 **파일 전체가 이중 인코딩**된다. perl 이 원본은 바이트로 읽고 삽입분은 디코드된 문자로 들고 있다가, 출력 시 문자열 전체를 UTF-8 로 업그레이드하면서 원래 정상이던 한글 바이트를 한 번 더 인코딩한다. 증상: `Wide character in print` 경고 + `file` 이 `NEL line terminators` 를 보고함 + 기존 한글이 `M-CM-,M-BM-^W` 식 모지바케.

**Why:** C# 은 U+0085(NEL) 를 줄바꿈으로 취급하므로 라인이 쪼개져 컴파일이 깨질 수 있고, 주석/문자열의 한글이 전부 망가진다.

**How to apply:**
- 삽입 블록은 반드시 **raw 바이트**로 읽는다: `open(F,"<",$file); binmode(F); local $/; $ins=<F>;` — `:encoding` 레이어를 쓰지 말 것.
- 셸 쿼팅 문제도 잦다. 치환문에 `||` 가 있으면 `s|...|...|` 의 구분자와 충돌하므로 `s#...#...#` 를 쓰고, 긴 블록은 heredoc 대신 Write 도구로 스크래치 파일에 쓴 뒤 `$ENV{...}` 로 읽는다.
- 이미 망가졌으면: 파일을 UTF-8 로 디코드해 0x80~0xFF 범위 문자의 연속 구간만 골라 바이트로 pack 한 뒤 strict UTF-8 로 디코드 시도, 성공한 구간만 교체한다. 실패한 구간(예: 정상 문자 U+00B7 `·`)은 그대로 둔다.
- 편집 후 `file <경로>` 로 `NEL` 이 없는지, `grep` 으로 한글이 온전한지 확인한다.

관련: [[unity-offline-compile-check]]
