using UnityEngine;

/// <summary>
/// 상태 아이콘용 기호 글리프 폰트. 아트 에셋이 없는 동안의 플레이스홀더다.
///
/// 중요: 레거시 uGUI Text 는 서로게이트 페어(U+FFFF 초과) 를 렌더링하지 못한다.
/// 🍗(U+1F357) 같은 이모지는 폰트가 로드되어도 preferredWidth 가 0 이 되어 아무것도 그려지지 않는다.
/// 따라서 여기서 쓰는 글리프는 반드시 BMP 범위(U+FFFF 이하)여야 한다.
/// 진짜 이모지를 쓰려면 TextMeshPro + 이모지 폰트 에셋이 필요하다.
/// </summary>
public static class EmojiFont
{
    static Font cached;

    public static Font Get()
    {
        if (cached) return cached;

        cached = Font.CreateDynamicFontFromOSFont(
            new[] { "Segoe UI Symbol", "Segoe UI Emoji", "Malgun Gothic" }, 78);
        return cached;
    }
}
