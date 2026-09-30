using UnityEngine;

/// <summary>GDD 10항 장비 슬롯.</summary>
public enum EquipmentSlot
{
    None = 0, Head = 1, Body = 2, Legs = 3, Feet = 4,
    MainHand = 5, OffHand = 6, Backpack = 7, Misc = 8
}

/// <summary>
/// GDD 10항 장비. 인벤토리 무게로는 이동속도를 깎지 않고 장비 고유 속성으로만 보정한다.
/// 방어력은 슬롯이 덮는 부위에만 적용된다 (GDD 6항 부위별 방어력).
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public class PlayerEquipment : MonoBehaviour
{
    public const int SlotCount = 8;

    static readonly BodyPart[] NoParts = new BodyPart[0];
    static readonly BodyPart[] HeadParts = { BodyPart.Head };
    static readonly BodyPart[] TorsoParts = { BodyPart.Torso, BodyPart.LeftArm, BodyPart.RightArm };
    static readonly BodyPart[] LegParts = { BodyPart.LeftLeg, BodyPart.RightLeg };

    // 인덱스 = (int)EquipmentSlot. 0(None) 은 쓰지 않는다.
    // GDD 11항 내구도가 개체 상태라 ItemDefinition 이 아니라 ItemStack 을 들고 있어야 한다.
    readonly ItemStack[] equipped = new ItemStack[SlotCount + 1];

    PlayerInventory inventory;

    /// <summary>장비 구성이 바뀔 때마다 증가. UI 갱신 기준.</summary>
    public int Version { get; private set; }

    public float MoveSpeedMultiplier { get; private set; } = 1f;
    public float StaminaCostMultiplier { get; private set; } = 1f;
    public float InjuryChanceMultiplier { get; private set; } = 1f;
    public string LastMessage { get; private set; } = string.Empty;

    void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        Recalculate();
    }

    /// <summary>장착된 아이템의 정의. 내구도까지 필요하면 GetStack 을 쓴다.</summary>
    public ItemDefinition Get(EquipmentSlot slot)
    {
        var stack = GetStack(slot);
        return stack != null ? stack.definition : null;
    }

    /// <summary>장착된 스택. 내구도가 여기 들어 있다.</summary>
    public ItemStack GetStack(EquipmentSlot slot)
    {
        int index = (int)slot;
        return index > 0 && index < equipped.Length ? equipped[index] : null;
    }

    /// <summary>
    /// 인벤토리의 스택에서 1개를 꺼내 장착한다. 이미 낀 장비는 인벤토리로 돌아간다.
    /// 돌려줄 칸이 없으면 장착을 포기한다 — 장비를 잃어버리는 쪽이 최악이다.
    /// </summary>
    public bool TryEquip(ItemStack stack) => TryEquipTo(
        stack != null && stack.definition && stack.definition.isShield
            ? EquipmentSlot.OffHand
            : stack != null && IsOneHandedMelee(stack.definition)
            ? EquipmentSlot.MainHand
            : stack != null && stack.definition ? stack.definition.equipSlot : EquipmentSlot.None,
        stack);

    /// <summary>
    /// 슬롯을 지정해 장착한다. 한손 무기는 주무기 칸에도 들어갈 수 있어야 해서 필요하다
    /// (GDD 21항 "한손 무기 + 방패" 조합). 슬롯이 맞지 않으면 실패한다.
    /// </summary>
    public bool TryEquipTo(EquipmentSlot slot, ItemStack stack)
    {
        LastMessage = string.Empty;
        if (stack == null || !stack.definition) return false;
        if (slot == EquipmentSlot.None) return false;

        var definition = stack.definition;
        if (!CanEquipTo(slot, definition)) return false;

        var mainHand = Get(EquipmentSlot.MainHand);
        if (definition.isShield && !CanShareShield(mainHand))
        {
            LastMessage = "양손·원거리 무기와 방패는 함께 들 수 없습니다";
            return false;
        }

        var previous = equipped[(int)slot];
        if (previous != null && previous.definition == definition) return false;
        var shield = slot == EquipmentSlot.MainHand && !CanShareShield(definition)
            ? GetStack(EquipmentSlot.OffHand)
            : null;

        // 내구도와 잔탄을 먼저 챙긴다 — TakeFrom 이 스택을 리스트에서 지울 수 있다.
        // 잔탄을 빼먹으면 총을 장착하는 순간 탄이 사라진다 (실제로 겪었다).
        int carriedDurability = stack.durability;
        int carriedAmmo = stack.loadedAmmo;
        if (inventory.TakeFrom(stack, 1) <= 0) return false;

        equipped[(int)slot] = MakeEquipped(definition, carriedDurability, carriedAmmo);

        if (previous != null && !inventory.TryPlaceStack(previous))
        {
            // 되돌리기: 방금 비운 칸이 있으므로 원래 장비를 다시 끼고 새 장비를 인벤토리로 돌려준다.
            equipped[(int)slot] = previous;
            inventory.TryPlaceStack(MakeEquipped(definition, carriedDurability, carriedAmmo));
            Recalculate();
            return false;
        }

        if (shield != null && !inventory.TryPlaceStack(shield))
        {
            if (previous != null)
            {
                inventory.TakeFrom(previous, 1);
                previous.quantity = 1;
            }
            equipped[(int)slot] = previous;
            inventory.TryPlaceStack(MakeEquipped(definition, carriedDurability, carriedAmmo));
            LastMessage = "인벤토리에 방패를 넣을 자리가 없습니다";
            Recalculate();
            return false;
        }

        if (shield != null) equipped[(int)EquipmentSlot.OffHand] = null;

        Recalculate();
        return true;
    }

    /// <summary>
    /// 어떤 물건이 어떤 슬롯에 들어갈 수 있는지. 손 슬롯만 예외 규칙이 있다.
    ///
    /// 사용자 결정: 주무기 칸은 무기, 보조손 칸은 방패만 들어간다.
    /// </summary>
    public static bool CanEquipTo(EquipmentSlot slot, ItemDefinition definition)
    {
        if (!definition || slot == EquipmentSlot.None) return false;

        if (slot == EquipmentSlot.OffHand) return definition.isShield;
        if (slot == EquipmentSlot.MainHand)
            return !definition.isShield
                   && (definition.equipSlot == EquipmentSlot.MainHand || IsOneHandedMelee(definition));
        return definition.equipSlot == slot;
    }

    public static bool IsOneHandedMelee(ItemDefinition definition) =>
        definition && !definition.isShield
        && definition.handedness == WeaponHandedness.OneHanded && !definition.ranged;

    public static bool IsTwoHandedMelee(ItemDefinition definition) =>
        definition && definition.handedness == WeaponHandedness.TwoHanded && !definition.ranged;

    public static bool CanShareShield(ItemDefinition definition) =>
        !definition || IsOneHandedMelee(definition);

    public bool Unequip(EquipmentSlot slot)
    {
        var stack = GetStack(slot);
        if (stack == null) return false;

        // 인벤토리에 자리가 없으면 벗지 않는다. 스택 객체를 그대로 돌려줘 내구도를 보존한다.
        if (!inventory.TryPlaceStack(stack)) return false;

        equipped[(int)slot] = null;
        Recalculate();
        return true;
    }

    static ItemStack MakeEquipped(ItemDefinition definition, int durability, int loadedAmmo) => new ItemStack
    {
        definition = definition,
        quantity = 1,
        durability = durability,
        loadedAmmo = loadedAmmo,
    };

    /// <summary>저장 복원용. 인벤토리를 거치지 않고 슬롯에 직접 넣는다.</summary>
    public void LoadEquip(EquipmentSlot slot, ItemDefinition definition, int durability)
    {
        int index = (int)slot;
        if (index <= 0 || index >= equipped.Length) return;

        // 저장 복원: 잔탄은 SaveSystem 이 바로 뒤에서 덮어쓴다. 여기서는 꽉 채워 둔다.
        equipped[index] = MakeEquipped(definition, durability, definition ? definition.magazineSize : 0);
        Recalculate();
    }

    /// <summary>GDD 8항 사망 시 장비도 전부 잃는다. 비우기 전의 스택 목록을 반환한다.</summary>
    public ItemStack[] StripAll()
    {
        var removed = new System.Collections.Generic.List<ItemStack>();
        for (int i = 1; i < equipped.Length; i++)
        {
            if (equipped[i] == null) continue;

            removed.Add(equipped[i]);
            equipped[i] = null;
        }

        Recalculate();
        return removed.ToArray();
    }

    /// <summary>GDD 6항: 해당 부위를 덮는 장비들의 방어력 합.</summary>
    public float GetDefense(BodyPart part)
    {
        float total = 0f;
        for (int i = 1; i < equipped.Length; i++)
        {
            var definition = equipped[i] != null ? equipped[i].definition : null;
            if (!definition) continue;

            var parts = PartsCovered((EquipmentSlot)i);
            for (int p = 0; p < parts.Length; p++)
            {
                if (parts[p] != part) continue;

                total += definition.defense;
                break;
            }
        }
        return total;
    }

    void Recalculate()
    {
        MoveSpeedMultiplier = 1f;
        StaminaCostMultiplier = 1f;
        InjuryChanceMultiplier = 1f;

        for (int i = 1; i < equipped.Length; i++)
        {
            var definition = equipped[i] != null ? equipped[i].definition : null;
            if (!definition) continue;

            MoveSpeedMultiplier *= definition.moveSpeedMultiplier;
            StaminaCostMultiplier *= definition.staminaCostMultiplier;
            InjuryChanceMultiplier *= definition.injuryChanceMultiplier;
        }

        Version++;
    }

    /// <summary>
    /// 슬롯이 덮는 부위. GDD 는 매핑을 주지 않아 아래로 단순화했다 — 게임 규칙이라 아이템 데이터가 아니라 코드에 둔다.
    /// 손/가방/기타 슬롯은 방어 부위가 없다 (무기·용량 보정용).
    /// </summary>
    public static BodyPart[] PartsCovered(EquipmentSlot slot)
    {
        switch (slot)
        {
            case EquipmentSlot.Head: return HeadParts;
            case EquipmentSlot.Body: return TorsoParts;
            case EquipmentSlot.Legs: return LegParts;
            case EquipmentSlot.Feet: return LegParts;
            default: return NoParts;
        }
    }

    public static string SlotName(EquipmentSlot slot)
    {
        switch (slot)
        {
            case EquipmentSlot.Head: return "머리";
            case EquipmentSlot.Body: return "몸";
            case EquipmentSlot.Legs: return "다리";
            case EquipmentSlot.Feet: return "신발";
            case EquipmentSlot.MainHand: return "주무기";
            case EquipmentSlot.OffHand: return "방패";
            case EquipmentSlot.Backpack: return "가방";
            case EquipmentSlot.Misc: return "기타";
            default: return "-";
        }
    }
}
