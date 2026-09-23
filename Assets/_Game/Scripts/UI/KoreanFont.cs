using UnityEngine;

/// <summary>
/// 내장 레거시 폰트에는 한글 글리프가 없고 TMP 폰트 에셋도 미임포트 상태라 OS 폰트를 잡아 쓴다.
/// ponytail: 윈도우 전용. 다른 플랫폼을 지원하려면 프로젝트에 한글 폰트 에셋을 넣어야 한다.
/// </summary>
public static class KoreanFont
{
    static Font cached;

    public static Font Get()
    {
        if (cached) return cached;

        cached = Font.CreateDynamicFontFromOSFont(
            new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Arial Unicode MS" }, 22);
        return cached;
    }
}
