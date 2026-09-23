using UnityEngine;

/// <summary>
/// GDD 12·13항 합성/개조. 인벤토리에서 재료를 무기 위로 끌어다 놓으면 실행된다.
///
/// 같은 조작이 두 가지 일을 한다:
///  - 기본 무기 + 재료  → 합성 (GDD 13항 표)
///  - 합성 무기 + 재료  → 내구도 회복 (GDD 12항)
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public class PlayerCrafting : MonoBehaviour
{
    [SerializeField] CraftRecipe[] recipes;

    PlayerInventory inventory;

    /// <summary>UI 에 띄우는 마지막 결과 메시지.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    void Awake() => inventory = GetComponent<PlayerInventory>();

    public void Bind(CraftRecipe[] list) => recipes = list;

    /// <summary>
    /// 재료 스택을 무기 스택에 투입한다. 합성이나 수리가 일어났으면 true.
    /// 어느 쪽도 아니면 false 를 돌려주고, 호출한 UI 는 평소의 이동/합치기로 넘어간다.
    /// </summary>
    public bool TryApply(ItemStack weaponStack, ItemStack materialStack)
    {
        if (recipes == null || weaponStack == null || materialStack == null) return false;
        if (!weaponStack.definition || !materialStack.definition) return false;
        if (weaponStack == materialStack) return false;

        for (int i = 0; i < recipes.Length; i++)
        {
            var recipe = recipes[i];
            if (!recipe) continue;

            if (recipe.Matches(weaponStack.definition, materialStack.definition))
                return Craft(recipe, weaponStack, materialStack);

            if (recipe.MatchesRepair(weaponStack.definition, materialStack.definition))
                return Repair(recipe, weaponStack, materialStack);
        }

        return false;
    }

    bool Craft(CraftRecipe recipe, ItemStack weaponStack, ItemStack materialStack)
    {
        if (materialStack.quantity < recipe.materialCount)
        {
            LastMessage = recipe.material.displayName + " " + recipe.materialCount + "개가 필요합니다";
            return false;
        }

        // 기본 무기 1개와 재료를 소비하고 결과물을 같은 자리에 놓는다.
        var origin = weaponStack.origin;
        if (inventory.TakeFrom(weaponStack, 1) <= 0) return false;
        if (inventory.TakeFrom(materialStack, recipe.materialCount) <= 0) return false;

        // GDD 12항: 합성 직후 현재 내구도는 최대치.
        var crafted = ItemStack.Create(recipe.result, 1, origin);
        if (!inventory.PlaceExact(crafted) && !inventory.TryPlaceStack(crafted))
        {
            // 자리가 없으면 발밑에 떨어뜨린다 — 결과물을 소멸시키는 쪽이 최악이다.
            WorldItem.Spawn(recipe.result, 1, transform.position + Vector3.up * 0.8f, Vector3.zero)
                     .SetDurability(crafted.durability);
        }

        LastMessage = recipe.result.displayName + " 합성 완료 (내구도 " + crafted.durability + ")";
        return true;
    }

    bool Repair(CraftRecipe recipe, ItemStack weaponStack, ItemStack materialStack)
    {
        int max = weaponStack.definition.maxDurability;
        int missing = max - weaponStack.durability;
        if (missing <= 0)
        {
            LastMessage = weaponStack.definition.displayName + " 내구도가 이미 최대입니다";
            return false;
        }

        // GDD 12항: 실제 회복에 필요한 만큼만 재료를 소비한다.
        int needed = Mathf.CeilToInt(missing / (float)recipe.repairPerMaterial);
        int spend = Mathf.Min(needed, materialStack.quantity);
        if (spend <= 0) return false;

        int consumed = inventory.TakeFrom(materialStack, spend);
        if (consumed <= 0) return false;

        // 최대치를 초과하지 않는다.
        weaponStack.durability = Mathf.Min(max, weaponStack.durability + consumed * recipe.repairPerMaterial);

        LastMessage = weaponStack.definition.displayName + " 내구도 " + weaponStack.durability + "/" + max
                      + " (" + recipe.material.displayName + " " + consumed + "개 소비)";
        return true;
    }
}
