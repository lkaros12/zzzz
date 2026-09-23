using UnityEngine;

/// <summary>
/// GDD 12·13항 무기 합성 레시피. 기본 무기 + 재료 → 합성 무기.
///
/// GDD 12항 규칙:
///  - 합성 무기의 최대 내구도는 항상 원본 무기의 정확히 2배 (빌더가 강제한다)
///  - 합성 직후 현재 내구도는 최대치
///  - 같은 재료를 다시 투입하면 내구도가 회복되며, 필요한 만큼만 재료를 소비한다
///  - V0.2 는 1단계 합성까지만 — 합성 무기를 다시 기본 무기로 쓰지 않는다
/// </summary>
[CreateAssetMenu(menuName = "V0.1/Craft Recipe", fileName = "recipe")]
public class CraftRecipe : ScriptableObject
{
    public ItemDefinition baseWeapon;
    public ItemDefinition material;
    public ItemDefinition result;

    [Tooltip("합성에 필요한 재료 수")]
    [Min(1)] public int materialCount = 1;

    [Tooltip("GDD 12항: 재료 1개당 회복되는 내구도. 예시값은 못 1개당 +10")]
    [Min(1)] public int repairPerMaterial = 10;

    public bool Matches(ItemDefinition weapon, ItemDefinition candidate) =>
        baseWeapon && material && result && weapon == baseWeapon && candidate == material;

    /// <summary>수리 대상인지. 이미 합성된 무기에 같은 재료를 넣는 경우.</summary>
    public bool MatchesRepair(ItemDefinition weapon, ItemDefinition candidate) =>
        result && material && weapon == result && candidate == material;
}
