using UnityEngine;

/// <summary>
/// GDD 5항 하단 중앙 퀵슬롯. 무기 슬롯과 소모품 슬롯을 분리한다.
///
/// 1번 = 주무기, 2번 = 보조무기 — 이 두 칸은 등록이 곧 장착이다.
/// PlayerEquipment 를 그대로 비추므로 Tab 화면의 장비 목록과 이중 관리되지 않는다.
/// 3~5번 = 소모품. 아이템 종류(id) 를 기억하고 숫자키로 사용한다.
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public class PlayerQuickSlots : MonoBehaviour
{
    public const int WeaponSlotCount = 2;
    public const int ConsumableSlotCount = 3;
    public const int SlotCount = WeaponSlotCount + ConsumableSlotCount;

    readonly ItemDefinition[] consumables = new ItemDefinition[ConsumableSlotCount];

    PlayerInventory inventory;
    PlayerEquipment equipment;
    PlayerConsumer consumer;

    /// <summary>등록 내용이 바뀔 때마다 증가. UI 갱신 기준.</summary>
    public int Version { get; private set; }

    void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        equipment = GetComponent<PlayerEquipment>();
        consumer = GetComponent<PlayerConsumer>();
    }

    public static bool IsWeaponSlot(int index) => index >= 0 && index < WeaponSlotCount;

    public static EquipmentSlot WeaponSlotFor(int index) =>
        index == 0 ? EquipmentSlot.MainHand : EquipmentSlot.OffHand;

    /// <summary>
    /// GDD 4항 "1~5 퀵슬롯 선택". Q 투척(GDD 8항) 이 무엇을 던질지 이 값으로 정한다.
    /// -1 = 선택 없음.
    /// </summary>
    public int SelectedIndex { get; private set; } = -1;

    public void Select(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        if (SelectedIndex == index) return;

        SelectedIndex = index;
        Version++;
    }

    /// <summary>
    /// 숫자키 입력. 처음 누르면 선택, 이미 선택된 칸을 다시 누르면 사용한다.
    /// 선택과 사용을 한 키에 겹쳐야 GDD 4항(선택) 과 기존 소모품 사용 조작을 둘 다 살릴 수 있다.
    /// </summary>
    public void PressSlot(int index)
    {
        if (index < 0 || index >= SlotCount) return;

        if (SelectedIndex != index) { Select(index); return; }
        TryUseSlot(index);
    }

    /// <summary>GDD 8항 Q 투척 대상. 선택된 칸의 스택. 무기 칸이면 장착된 무기를 던진다.</summary>
    public ItemStack SelectedStack => SelectedIndex < 0 ? null : GetDisplayedStack(SelectedIndex);

    /// <summary>드래그 중 이 칸에 넣을 수 있는지. UI 안내 문구에 쓴다.</summary>
    public bool CanAssign(int index, ItemDefinition definition)
    {
        if (!definition) return false;
        if (IsWeaponSlot(index)) return PlayerEquipment.CanEquipTo(WeaponSlotFor(index), definition);

        return definition.equipSlot == EquipmentSlot.None && definition.usable;
    }

    /// <summary>무기 슬롯은 장비를, 소모품 슬롯은 등록된 종류를 보여준다.</summary>
    public ItemDefinition GetDisplayed(int index)
    {
        if (IsWeaponSlot(index))
            return equipment ? equipment.Get(WeaponSlotFor(index)) : null;

        int consumableIndex = index - WeaponSlotCount;
        return consumableIndex >= 0 && consumableIndex < consumables.Length ? consumables[consumableIndex] : null;
    }

    /// <summary>무기 슬롯은 내구도를 보여줘야 하므로 스택이 필요하다.</summary>
    public ItemStack GetDisplayedStack(int index)
    {
        if (IsWeaponSlot(index))
            return equipment ? equipment.GetStack(WeaponSlotFor(index)) : null;

        return ResolveConsumable(index);
    }

    /// <summary>
    /// 인벤토리의 스택을 슬롯에 넣는다. 무기 슬롯이면 장착, 소모품 슬롯이면 등록.
    /// 슬롯 종류와 아이템이 맞지 않으면 실패한다.
    /// </summary>
    public bool TryAssign(int index, ItemStack stack)
    {
        if (stack == null || !stack.definition) return false;

        var definition = stack.definition;

        if (IsWeaponSlot(index))
        {
            // 어느 손에 들어갈 수 있는지는 PlayerEquipment 가 판단한다
            // (총/양손 = 주무기, 한손 근접 = 양쪽, 방패 = 보조무기).
            var slot = WeaponSlotFor(index);
            if (!equipment || !equipment.TryEquipTo(slot, stack)) return false;

            Version++;
            return true;
        }

        // 소모품 슬롯에는 장비가 아닌, 쓸 수 있는 아이템만 등록한다.
        if (definition.equipSlot != EquipmentSlot.None || !definition.usable) return false;

        int consumableIndex = index - WeaponSlotCount;

        // 같은 아이템이 다른 슬롯에 있으면 비워 중복 등록을 막는다.
        for (int i = 0; i < consumables.Length; i++)
            if (i != consumableIndex && consumables[i] == definition) consumables[i] = null;

        consumables[consumableIndex] = definition;
        Version++;
        return true;
    }

    public void Clear(int index)
    {
        if (IsWeaponSlot(index))
        {
            if (equipment && equipment.Unequip(WeaponSlotFor(index))) Version++;
            return;
        }

        int consumableIndex = index - WeaponSlotCount;
        if (consumableIndex < 0 || consumableIndex >= consumables.Length) return;
        if (!consumables[consumableIndex]) return;

        consumables[consumableIndex] = null;
        Version++;
    }

    /// <summary>등록된 종류의 스택을 인벤토리에서 찾는다. 다 썼으면 null.</summary>
    public ItemStack ResolveConsumable(int index)
    {
        var definition = GetDisplayed(index);
        if (!definition || IsWeaponSlot(index)) return null;

        var stacks = inventory.Stacks;
        for (int i = 0; i < stacks.Count; i++)
            if (stacks[i].definition == definition) return stacks[i];

        return null;
    }

    /// <summary>숫자키로 소모품 슬롯을 사용한다. 무기 슬롯은 좌/우클릭이 담당하므로 무시.</summary>
    public bool TryUseSlot(int index)
    {
        if (IsWeaponSlot(index) || !consumer) return false;

        return consumer.TryUse(ResolveConsumable(index));
    }

    /// <summary>등록된 종류의 총 수량. 바에 숫자를 그릴 때 쓴다.</summary>
    public int CountOf(int index)
    {
        var definition = GetDisplayed(index);
        if (!definition) return 0;
        if (IsWeaponSlot(index)) return 1;

        int total = 0;
        var stacks = inventory.Stacks;
        for (int i = 0; i < stacks.Count; i++)
            if (stacks[i].definition == definition) total += stacks[i].quantity;

        return total;
    }

    /// <summary>저장용. 소모품 칸에 등록된 id (없으면 빈 문자열). 무기 칸은 장비 저장이 담당한다.</summary>
    public string GetConsumableId(int consumableIndex) =>
        consumableIndex >= 0 && consumableIndex < consumables.Length && consumables[consumableIndex]
            ? consumables[consumableIndex].id
            : string.Empty;

    public void LoadConsumable(int consumableIndex, ItemDefinition definition)
    {
        if (consumableIndex < 0 || consumableIndex >= consumables.Length) return;

        consumables[consumableIndex] = definition;
        Version++;
    }

    public static string SlotName(int index)
    {
        if (index == 0) return "주무기";
        if (index == 1) return "보조무기";

        return (index + 1).ToString();
    }
}
