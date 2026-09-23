using UnityEngine;

/// <summary>
/// GDD 18항 시간 시스템. 권장 속도는 현실 1초 = 게임 1분 (현실 1분 = 게임 1시간).
/// GDD 5항 생존 수치가 전부 "게임 내 1시간당" 으로 정의돼 있어 이 클록이 기준이 된다.
/// </summary>
public class GameClock : MonoBehaviour
{
    const float MinutesPerDay = 24f * 60f;

    [Tooltip("현실 1초당 흐르는 게임 분. GDD 18항 권장값 1 (현실 1초 = 게임 1분)")]
    [SerializeField] float gameMinutesPerRealSecond = 1f;
    [SerializeField] int startDay = 1;
    [SerializeField, Range(0f, 23.99f)] float startHour = 8f;

    public int Day { get; private set; }

    /// <summary>하루 안에서 경과한 분. 0 ~ 1440.</summary>
    public float MinuteOfDay { get; private set; }

    public int Hour => (int)(MinuteOfDay / 60f);
    public int Minute => (int)(MinuteOfDay % 60f);

    /// <summary>GDD 18항 낮/밤. 06:00 ~ 20:00 을 낮으로 본다.</summary>
    public bool IsNight => Hour < 6 || Hour >= 20;

    /// <summary>이번 프레임에 흐른 게임 시간(시간 단위). 생존 수치와 부상 피해가 이 값으로 진행한다.</summary>
    public float DeltaGameHours { get; private set; }

    void Awake() => Reset();

    public void Reset()
    {
        Day = startDay;
        MinuteOfDay = startHour * 60f;
        DeltaGameHours = 0f;
    }

    void Update() => Advance(Time.deltaTime * gameMinutesPerRealSecond);

    /// <summary>저장 복원용.</summary>
    public void LoadState(int savedDay, float savedMinuteOfDay)
    {
        Day = Mathf.Max(1, savedDay);
        MinuteOfDay = Mathf.Repeat(savedMinuteOfDay, MinutesPerDay);
        DeltaGameHours = 0f;
    }

    /// <summary>테스트에서 시간을 직접 밀 수 있게 분리해 둔다.</summary>
    public void Advance(float gameMinutes)
    {
        if (gameMinutes <= 0f)
        {
            DeltaGameHours = 0f;
            return;
        }

        DeltaGameHours = gameMinutes / 60f;
        MinuteOfDay += gameMinutes;

        while (MinuteOfDay >= MinutesPerDay)
        {
            MinuteOfDay -= MinutesPerDay;
            Day++;
        }
    }
}
