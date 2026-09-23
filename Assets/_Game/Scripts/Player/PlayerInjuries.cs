using UnityEngine;

/// <summary>GDD 6항 부위.</summary>
public enum BodyPart { Head = 0, Torso = 1, LeftArm = 2, RightArm = 3, LeftLeg = 4, RightLeg = 5 }

/// <summary>GDD 6항: 경상 / 중상 / 심각한 부상 으로 단순화.</summary>
public enum InjurySeverity { None = 0, Minor = 1, Serious = 2, Critical = 3 }

/// <summary>
/// GDD 6항 부상. 부위별로 하나만 유지하고 더 심한 부상이 오면 덮어쓴다.
/// 치료하지 않으면 HP 가 지속적으로 감소한다.
/// </summary>
public class PlayerInjuries : MonoBehaviour
{
    public const int PartCount = 6;

    [Tooltip("부상 등급별 게임 1시간당 HP 감소 (None/경상/중상/심각)")]
    [SerializeField] float[] hpDrainPerGameHour = { 0f, 1.5f, 4f, 9f };

    [Tooltip("GDD 6항은 몸통 부상에 지속 피해를 명시했으므로 배율을 더 준다")]
    [SerializeField] float torsoDrainMultiplier = 1.5f;

    [Tooltip("다리 부상 등급별 이동속도 배율")]
    [SerializeField] float[] legSpeedMultiplier = { 1f, 0.85f, 0.65f, 0.45f };

    [Tooltip("팔 부상 등급별 공격 효율")]
    [SerializeField] float[] armAttackEfficiency = { 1f, 0.85f, 0.65f, 0.4f };

    [Tooltip("머리 부상 등급별 행동 효율")]
    [SerializeField] float[] headActionEfficiency = { 1f, 0.9f, 0.7f, 0.5f };

    [Tooltip("이 값만큼 방어력을 넘긴 피해에서 부상 확률이 100% 가 된다")]
    [SerializeField] float injuryChanceScale = 30f;

    // 부상 등급은 "맞은 크기" 가 아니라 "남은 HP 비율" 로 정한다.
    // 한 대 맞고 HP 가 거의 안 깎였는데 중상이 뜨는 게 이상했기 때문이다.
    [Tooltip("남은 HP 비율이 이 값 이하면 중상. 그 위는 경상")]
    [SerializeField, Range(0f, 1f)] float seriousHpRatio = 0.5f;
    [Tooltip("남은 HP 비율이 이 값 이하면 심각한 부상")]
    [SerializeField, Range(0f, 1f)] float criticalHpRatio = 0.2f;

    [SerializeField] GameClock clock;

    readonly InjurySeverity[] severities = new InjurySeverity[PartCount];

    PlayerStats stats;
    PlayerEquipment equipment;

    /// <summary>부상 상태가 바뀔 때마다 증가. UI 가 매 프레임 문자열을 다시 만들지 않게 하는 용도.</summary>
    public int Version { get; private set; }

    public bool HasAnyInjury { get; private set; }
    public InjurySeverity WorstSeverity { get; private set; }
    public int InjuryCount { get; private set; }

    public InjurySeverity GetSeverity(BodyPart part) => severities[(int)part];

    // GDD 6항 효과: 다리 = 이동, 팔 = 공격 효율, 머리 = 행동 효율.
    public float MoveSpeedMultiplier => Lookup(legSpeedMultiplier, WorstOf(BodyPart.LeftLeg, BodyPart.RightLeg));
    public float AttackEfficiency => Lookup(armAttackEfficiency, WorstOf(BodyPart.LeftArm, BodyPart.RightArm));
    public float ActionEfficiency => Lookup(headActionEfficiency, GetSeverity(BodyPart.Head));

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        equipment = GetComponent<PlayerEquipment>();
        if (!clock) clock = FindFirstObjectByType<GameClock>();
        ClearAll();
    }

    void Update()
    {
        if (!HasAnyInjury || !clock || !stats) return;

        float drain = DrainPerGameHour() * clock.DeltaGameHours;
        if (drain > 0f) stats.TakeDamage(drain);
    }

    public void ClearAll()
    {
        for (int i = 0; i < severities.Length; i++) severities[i] = InjurySeverity.None;
        Recalculate();
    }

    /// <summary>같은 부위에는 더 심한 등급만 덮어쓴다.</summary>
    public void Apply(BodyPart part, InjurySeverity severity)
    {
        if (severity <= InjurySeverity.None) return;
        if (severity <= severities[(int)part]) return;

        severities[(int)part] = severity;
        Recalculate();
    }

    /// <summary>
    /// GDD 6항 판정: 피격 부위는 호출자(전투)가 정하고, 여기서는 부위 방어력으로 부상 확률과 등급을 굴린다.
    /// </summary>
    public InjurySeverity RollInjury(BodyPart part, float damage, float partDefense, float chanceMultiplier = 1f)
    {
        float chance = InjuryChance(damage, partDefense, injuryChanceScale) * chanceMultiplier;
        if (Random.value > chance) return InjurySeverity.None;

        // 부상 여부는 방어력을 뚫었는지로, 등급은 남은 HP 비율로 정한다.
        InjurySeverity severity = SeverityFromHp();

        Apply(part, severity);
        return severity;
    }

    /// <summary>
    /// GDD 6항 피격 처리 진입점. 부위 방어력과 부상확률 보정을 장비에서 읽어 한 번에 굴린다.
    /// 7단계 전투가 이걸 호출한다.
    ///
    /// 방어력이 HP 피해까지 줄이는 것은 GDD 에 명시가 없는 가정이다 —
    /// GDD 6항은 방어력을 부상 확률 계산에만 쓰라고 한다. 방어구가 피해를 안 줄이면 이상해서 넣었다.
    /// </summary>
    public InjurySeverity ResolveHit(BodyPart part, float damage)
    {
        float defense = equipment ? equipment.GetDefense(part) : 0f;
        float chanceMultiplier = equipment ? equipment.InjuryChanceMultiplier : 1f;

        if (stats) stats.TakeDamage(Mathf.Max(0f, damage - defense));
        return RollInjury(part, damage, defense, chanceMultiplier);
    }

    /// <summary>GDD 6항 치료. 치료 가능한 등급 중 가장 심한 부상 하나를 낫게 한다.</summary>
    public bool Treat(InjurySeverity maxTreatable)
    {
        int best = -1;
        for (int i = 0; i < severities.Length; i++)
        {
            if (severities[i] == InjurySeverity.None || severities[i] > maxTreatable) continue;
            if (best < 0 || severities[i] > severities[best]) best = i;
        }

        if (best < 0) return false;

        severities[best] = InjurySeverity.None;
        Recalculate();
        return true;
    }

    public float DrainPerGameHour()
    {
        float total = 0f;
        for (int i = 0; i < severities.Length; i++)
        {
            float drain = Lookup(hpDrainPerGameHour, severities[i]);
            if (i == (int)BodyPart.Torso) drain *= torsoDrainMultiplier;
            total += drain;
        }
        return total;
    }

    /// <summary>현재 HP 비율로 부상 등급을 정한다.</summary>
    public InjurySeverity SeverityFromHp()
    {
        float ratio = stats && stats.MaxHp > 0f ? stats.Hp / stats.MaxHp : 1f;
        return SeverityForHpRatio(ratio, seriousHpRatio, criticalHpRatio);
    }

    /// <summary>순수 함수 — 난수 없이 검증할 수 있게 분리.</summary>
    public static InjurySeverity SeverityForHpRatio(float hpRatio, float seriousAt, float criticalAt)
    {
        if (hpRatio <= criticalAt) return InjurySeverity.Critical;
        if (hpRatio <= seriousAt) return InjurySeverity.Serious;

        return InjurySeverity.Minor;
    }

    /// <summary>순수 함수 — 난수 없이 검증할 수 있게 분리.</summary>
    public static float InjuryChance(float damage, float partDefense, float scale) =>
        scale <= 0f ? 0f : Mathf.Clamp01((damage - partDefense) / scale);

    InjurySeverity WorstOf(BodyPart a, BodyPart b)
    {
        var first = GetSeverity(a);
        var second = GetSeverity(b);
        return first >= second ? first : second;
    }

    void Recalculate()
    {
        HasAnyInjury = false;
        WorstSeverity = InjurySeverity.None;
        InjuryCount = 0;

        for (int i = 0; i < severities.Length; i++)
        {
            if (severities[i] == InjurySeverity.None) continue;

            HasAnyInjury = true;
            InjuryCount++;
            if (severities[i] > WorstSeverity) WorstSeverity = severities[i];
        }

        Version++;
    }

    static float Lookup(float[] table, InjurySeverity severity)
    {
        int index = (int)severity;
        if (table == null || table.Length == 0) return 1f;
        return index < table.Length ? table[index] : table[table.Length - 1];
    }

    public static string SeverityName(InjurySeverity severity)
    {
        switch (severity)
        {
            case InjurySeverity.Minor: return "경상";
            case InjurySeverity.Serious: return "중상";
            case InjurySeverity.Critical: return "심각한 부상";
            default: return "없음";
        }
    }

    public static string PartName(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.Head: return "머리";
            case BodyPart.Torso: return "몸통";
            case BodyPart.LeftArm: return "왼팔";
            case BodyPart.RightArm: return "오른팔";
            case BodyPart.LeftLeg: return "왼다리";
            default: return "오른다리";
        }
    }
}
