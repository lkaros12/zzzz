using UnityEngine;

/// <summary>GDD 4항 기본 상태 + GDD 5항 생존 수치.</summary>
public class PlayerStats : MonoBehaviour
{
    [Header("HP / 스태미나")]
    [SerializeField] float maxHp = 100f;
    [SerializeField] float baseMaxStamina = 100f;
    [SerializeField] float staminaRegenPerSecond = 14f;
    [SerializeField] float regenDelayAfterUse = 0.6f;

    [Header("생존 수치 증가 (게임 내 1시간당, GDD 5항)")]
    [SerializeField] float hungerPerGameHour = 4f;
    [SerializeField] float thirstPerGameHour = 6f;
    [SerializeField] float fatiguePerGameHour = 5f;
    [Tooltip("달리기 등 활동 중 추가 배율 (GDD 5항 \"활동 중 추가 증가\")")]
    [SerializeField] float activityMultiplier = 2f;

    [Header("생존 수치 패널티")]
    [Tooltip("가장 나쁜 수치가 100 일 때 최대 스태미나가 줄어드는 비율")]
    [SerializeField, Range(0f, 0.9f)] float maxStaminaPenalty = 0.5f;
    [Tooltip("가장 나쁜 수치가 100 일 때 스태미나 회복 효율이 줄어드는 비율")]
    [SerializeField, Range(0f, 1f)] float regenPenalty = 0.7f;
    [Tooltip("허기/탈수가 100 일 때 게임 1시간당 잃는 HP (각각 적용)")]
    [SerializeField] float starvationDamagePerGameHour = 6f;

    [SerializeField] GameClock clock;

    public float MaxHp => maxHp;
    public float Hp { get; private set; }
    public float Stamina { get; private set; }
    public float Hunger { get; private set; }
    public float Thirst { get; private set; }
    public float Fatigue { get; private set; }
    public bool IsDead => Hp <= 0f;

    /// <summary>GDD 4항: 허기/탈수/피곤함이 높을수록 최대 스태미나가 내려간다.</summary>
    public float MaxStamina => baseMaxStamina * (1f - maxStaminaPenalty * WorstConditionRatio);

    /// <summary>GDD 4항: 회복 효율도 같이 내려간다.</summary>
    public float RegenMultiplier => 1f - regenPenalty * WorstConditionRatio;

    /// <summary>
    /// 허기/탈수/피곤함 중 가장 나쁜 값의 0~1 비율.
    /// GDD 는 계산식을 주지 않는다 — "가장 나쁜 상태가 지배한다" 로 단순화했다. 밸런싱 대상.
    /// </summary>
    public float WorstConditionRatio => Mathf.Max(Hunger, Mathf.Max(Thirst, Fatigue)) / 100f;

    /// <summary>달리기처럼 생존 수치를 더 빨리 깎는 행동 중인지. PlayerMotor 가 매 프레임 설정한다.</summary>
    public bool IsExerting { get; set; }

    float regenBlockedUntil;

    void Awake()
    {
        if (!clock) clock = FindFirstObjectByType<GameClock>();
        ResetStats();
    }

    public void ResetStats()
    {
        Hp = maxHp;
        Stamina = baseMaxStamina;
        Hunger = 0f;
        Thirst = 0f;
        Fatigue = 0f;
        regenBlockedUntil = 0f;
    }

    void Update()
    {
        if (clock) TickSurvival(clock.DeltaGameHours);
        TickStamina();
    }

    /// <summary>GDD 5항 증가율. 테스트에서 직접 호출할 수 있게 분리해 둔다.</summary>
    public void TickSurvival(float gameHours)
    {
        if (gameHours <= 0f) return;

        float multiplier = IsExerting ? activityMultiplier : 1f;
        Hunger = Mathf.Clamp(Hunger + hungerPerGameHour * gameHours * multiplier, 0f, 100f);
        Thirst = Mathf.Clamp(Thirst + thirstPerGameHour * gameHours * multiplier, 0f, 100f);
        Fatigue = Mathf.Clamp(Fatigue + fatiguePerGameHour * gameHours * multiplier, 0f, 100f);

        // GDD 4항: 허기/탈수를 100 으로 방치하면 HP 가 깎인다. 둘 다 100 이면 두 배.
        // ponytail: 티킹 후 상태로 구간 전체의 피해를 계산한다. 프레임 단위 delta 에서는 무의미한 오차지만,
        // 나중에 시간을 한 번에 크게 밀 일이 생기면(예: 수면/로드) 잘게 쪼개서 호출해야 한다.
        int starving = (Hunger >= 100f ? 1 : 0) + (Thirst >= 100f ? 1 : 0);
        if (starving > 0) TakeDamage(starvationDamagePerGameHour * gameHours * starving);
    }

    void TickStamina()
    {
        float max = MaxStamina;

        // 수치가 나빠지면 최대치가 내려가므로 현재값도 따라 내려온다.
        if (Stamina > max) Stamina = max;

        if (Time.time < regenBlockedUntil || Stamina >= max) return;
        Stamina = Mathf.Min(max, Stamina + staminaRegenPerSecond * RegenMultiplier * Time.deltaTime);
    }

    /// <summary>회피처럼 "충분할 때만" 소비하는 행동용. 부족하면 아무것도 소비하지 않고 false.</summary>
    public bool TrySpendStamina(float amount)
    {
        if (Stamina < amount) return false;

        SpendStamina(amount);
        return true;
    }

    /// <summary>달리기처럼 매 프레임 갈아쓰는 행동용. 0 에서 멈춘다.</summary>
    public void SpendStamina(float amount)
    {
        Stamina = Mathf.Max(0f, Stamina - amount);
        regenBlockedUntil = Time.time + regenDelayAfterUse;
    }

    public void TakeDamage(float amount) => Hp = Mathf.Max(0f, Hp - amount);
    public void Heal(float amount) => Hp = Mathf.Min(maxHp, Hp + amount);

    /// <summary>저장 복원용. HP/스태미나/생존 수치를 저장된 값으로 되돌린다.</summary>
    public void LoadState(float savedHp, float savedStamina, float savedHunger, float savedThirst, float savedFatigue)
    {
        Hp = Mathf.Clamp(savedHp, 0f, maxHp);
        Hunger = Mathf.Clamp(savedHunger, 0f, 100f);
        Thirst = Mathf.Clamp(savedThirst, 0f, 100f);
        Fatigue = Mathf.Clamp(savedFatigue, 0f, 100f);

        // 최대 스태미나는 생존 수치에 따라 달라지므로 수치를 먼저 넣고 나서 클램프한다.
        Stamina = Mathf.Clamp(savedStamina, 0f, MaxStamina);
    }

    /// <summary>테스트용. 생존 수치를 직접 세팅한다.</summary>
    public void SetSurvival(float hunger, float thirst, float fatigue)
    {
        Hunger = Mathf.Clamp(hunger, 0f, 100f);
        Thirst = Mathf.Clamp(thirst, 0f, 100f);
        Fatigue = Mathf.Clamp(fatigue, 0f, 100f);
    }

    // GDD 9항 소비 아이템 효과.
    public void RestoreHunger(float amount) => Hunger = Mathf.Max(0f, Hunger - amount);
    public void RestoreThirst(float amount) => Thirst = Mathf.Max(0f, Thirst - amount);
    public void RestoreFatigue(float amount) => Fatigue = Mathf.Max(0f, Fatigue - amount);
}
