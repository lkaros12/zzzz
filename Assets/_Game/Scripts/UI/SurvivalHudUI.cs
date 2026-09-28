using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GDD 16항 좌측 하단 허기/탈수/피곤함 아이콘 + 우측 상단 DAY/시간.
/// 아이콘은 상태가 나빠질수록 어두워진다 — 아트가 없는 동안 이모지 글리프로 대체.
/// GDD 23항대로 읽기만 한다.
/// </summary>
public class SurvivalHudUI : MonoBehaviour
{
    [SerializeField] PlayerStats stats;
    [SerializeField] GameClock clock;

    [Header("상태 아이콘")]
    [SerializeField] Text hungerIcon;
    [SerializeField] Text thirstIcon;
    [SerializeField] Text fatigueIcon;

    [SerializeField] Text clockText;

    [Header("상태가 좋을 때 색 — 나빠지면 badColor 로 어두워진다")]
    [SerializeField] Color hungerColor = new Color(0.95f, 0.65f, 0.25f);
    [SerializeField] Color thirstColor = new Color(0.35f, 0.72f, 0.96f);
    [SerializeField] Color fatigueColor = new Color(0.72f, 0.56f, 0.92f);
    [Tooltip("상태가 최악일 때 색")]
    [SerializeField] Color badColor = new Color(0.17f, 0.15f, 0.15f, 1f);

    [Tooltip("아이콘 반투명도. 뒤의 월드가 비쳐 보이는 정도")]
    [SerializeField, Range(0.1f, 1f)] float iconAlpha = 0.72f;

    int lastMinuteShown = -1;

    public void Bind(PlayerStats playerStats, GameClock gameClock,
                     Text hunger, Text thirst, Text fatigue, Text clockLabel)
    {
        stats = playerStats;
        clock = gameClock;
        hungerIcon = hunger;
        thirstIcon = thirst;
        fatigueIcon = fatigue;
        clockText = clockLabel;
    }

    /// <summary>
    /// KoreanFontApplier 가 Awake 에서 캔버스 밑의 Text 를 전부 한글 폰트로 바꾼다.
    /// 이모지 폰트는 그 뒤에 덮어써야 하므로 Start 에서 지정한다.
    /// </summary>
    void Start()
    {
        var font = EmojiFont.Get();
        if (!font) return;

        ApplyFont(hungerIcon, font);
        ApplyFont(thirstIcon, font);
        ApplyFont(fatigueIcon, font);
    }

    static void ApplyFont(Text text, Font font)
    {
        if (text) text.font = font;
    }

    void Update()
    {
        if (stats)
        {
            // 허기/탈수/피곤함은 값이 클수록 나쁘다.
            Tint(hungerIcon, hungerColor, stats.Hunger / 100f);
            Tint(thirstIcon, thirstColor, stats.Thirst / 100f);
            Tint(fatigueIcon, fatigueColor, stats.Fatigue / 100f);
        }

        UpdateClock();
    }

    void Tint(Text icon, Color goodColor, float badRatio)
    {
        if (!icon) return;

        var color = Color.Lerp(goodColor, badColor, Mathf.Clamp01(badRatio));
        color.a = iconAlpha;
        icon.color = color;
    }

    void UpdateClock()
    {
        if (!clockText || !clock) return;

        // 게임 시간 1분마다만 문자열을 다시 만든다.
        int minute = clock.Hour * 60 + clock.Minute;
        if (minute == lastMinuteShown) return;
        lastMinuteShown = minute;

        clockText.text = string.Format("DAY {0}   {1:00}:{2:00}   {3}",
            clock.Day, clock.Hour, clock.Minute, clock.IsNight ? "밤" : "낮");
    }
}
