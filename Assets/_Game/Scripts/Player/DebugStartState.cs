using UnityEngine;

/// <summary>
/// 테스트용. 플레이 시작 시 생존 수치와 부상을 중간값으로 맞춘다.
/// PlayerStats.Awake 의 ResetStats 이후에 돌아야 하므로 Start 에서 덮어쓴다.
///
/// ponytail: V0.1 검증용 임시 컴포넌트다. 실제 게임 시작 상태가 정해지면 이 파일과
/// 빌더의 AddComponent 한 줄만 지우면 된다.
/// </summary>
public class DebugStartState : MonoBehaviour
{
    const string TestChestName = "테스트 무기 상자";

    [SerializeField] bool enabledOnStart = true;

    [Header("생존 수치 시작값 (0~100)")]
    [SerializeField, Range(0f, 100f)] float hunger = 50f;
    [SerializeField, Range(0f, 100f)] float thirst = 50f;
    [SerializeField, Range(0f, 100f)] float fatigue = 50f;

    [Header("테스트용 시작 아이템 (id)")]
    [Tooltip("탄약을 이 수량만큼 준다. 1발이면 재장전 시험이 안 된다")]
    [SerializeField] int ammoStartQuantity = 24;

    [Tooltip("시작할 때 인벤토리에 넣을 아이템 id. Data/Items 의 파일명과 같다")]
    [SerializeField] string[] startingItems =
    {
        "kitchen_knife", "riot_shield", "pistol", "ammo_pistol",
        "bandage", "water_bottle", "canned_food",
    };
    [SerializeField] bool giveAllWeapons = true;
    [SerializeField] ItemCatalog catalog;
    [Tooltip("시작 무기를 자동으로 장착한다. 끄면 우클릭 착용을 직접 테스트할 수 있다")]
    [SerializeField] bool equipWeaponsOnStart = true;

    [Header("시작 부상")]
    [Tooltip("None 이면 부상 없이 시작한다. 등급별 일러스트는 Tools > V0.1 > Debug > 부상 등급 순환 으로 확인.")]
    [SerializeField] BodyPart injuredPart = BodyPart.LeftLeg;
    [SerializeField] InjurySeverity injurySeverity = InjurySeverity.None;

    void Start()
    {
        if (!enabledOnStart) return;

        var stats = GetComponent<PlayerStats>();
        if (stats) stats.SetSurvival(hunger, thirst, fatigue);

        var injuries = GetComponent<PlayerInjuries>();
        if (injuries && injurySeverity != InjurySeverity.None)
            injuries.Apply(injuredPart, injurySeverity);

        GiveStartingItems();

        Debug.Log(string.Format("[V0.1] 테스트 시작값 적용 — 허기 {0}/100, 탈수 {1}/100, 피곤함 {2}/100, {3} {4}",
            hunger, thirst, fatigue, PlayerInjuries.PartName(injuredPart),
            PlayerInjuries.SeverityName(injurySeverity)));
    }
    /// <summary>테스트용 시작 아이템. 카탈로그가 비어 있으면 씬의 SaveSystem 에서 빌려온다.</summary>
    void GiveStartingItems()
    {
        if ((startingItems == null || startingItems.Length == 0) && !giveAllWeapons) return;

        var inventory = GetComponent<PlayerInventory>();
        if (!inventory) return;

        var lookup = catalog ? catalog : FindCatalog();
        if (!lookup)
        {
            Debug.LogWarning("[V0.1] ItemCatalog 를 찾을 수 없어 시작 아이템을 넣지 못했습니다.");
            return;
        }

        int given = 0;
        for (int i = 0; startingItems != null && i < startingItems.Length; i++)
        {
            var definition = lookup.Find(startingItems[i]);
            if (!definition) continue;

            // 소모품은 3개, 탄약은 넉넉하게, 장비/무기는 1개.
            int amount = 1;
            if (definition.maxStack > 1)
                amount = definition.id.StartsWith("ammo_")
                    ? Mathf.Min(ammoStartQuantity, definition.maxStack)
                    : Mathf.Min(3, definition.maxStack);
            if (inventory.Add(definition, amount) > 0) given++;
        }

        if (equipWeaponsOnStart) EquipBestWeapons();

        Debug.Log("[V0.1] 테스트 시작 아이템 " + given + "종 지급"
                  + (equipWeaponsOnStart ? " (무기 자동 장착)" : string.Empty));

        if (giveAllWeapons) GiveAllWeaponsAndAmmo(lookup, inventory);
    }

    /// <summary>카탈로그의 모든 무기·방패와 탄약을 인벤토리 우선으로 지급한다.</summary>
    void GiveAllWeaponsAndAmmo(ItemCatalog lookup, PlayerInventory inventory)
    {
        var chest = FindOrCreateTestChest();
        int inventoryCount;
        int chestCount;
        DistributeAllWeaponsAndAmmo(lookup, inventory, chest, out inventoryCount, out chestCount);
        Debug.Log("[V0.1] 전체 무기/탄약 지급 — 인벤토리 " + inventoryCount
                  + "개, 테스트 상자 " + chestCount + "개");
    }

    static void DistributeAllWeaponsAndAmmo(ItemCatalog lookup, PlayerInventory inventory,
                                            WorldContainer chest, out int inventoryCount,
                                            out int chestCount)
    {
        inventoryCount = 0;
        chestCount = 0;
        for (int i = 0; i < lookup.Count; i++)
        {
            var definition = lookup.Get(i);
            if (!definition) continue;

            bool isAmmo = definition.id.StartsWith("ammo_");
            if (!isAmmo && definition.handedness == WeaponHandedness.None && !definition.isShield) continue;

            int amount = isAmmo ? 60 : 1;
            int accepted = inventory.Add(definition, amount);
            inventoryCount += accepted;
            if (accepted >= amount) continue;

            int overflow = amount - accepted;
            chest.Add(definition, overflow);
            chestCount += overflow;
        }
    }

    WorldContainer FindOrCreateTestChest()
    {
        var containers = WorldContainer.All;
        for (int i = 0; i < containers.Count; i++)
            if (containers[i] && containers[i].DisplayName == TestChestName) return containers[i];

        var size = new Vector3(1f, 0.8f, 1f);
        var chest = WorldContainer.Spawn(TestChestName, FindTestChestPosition(size), size, null);
        chest.gameObject.name = TestChestName;
        return chest;
    }

    Vector3 FindTestChestPosition(Vector3 size)
    {
        var controller = GetComponent<CharacterController>();
        float groundY = transform.position.y;
        if (controller) groundY += controller.center.y - controller.height * 0.5f;

        var origin = new Vector3(transform.position.x, groundY, transform.position.z);
        for (int i = 0; i < 16; i++)
        {
            var direction = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.right;
            var position = origin + direction * 1.5f;
            var center = position + Vector3.up * (size.y * 0.5f);
            if (!Physics.CheckBox(center, size * 0.45f, Quaternion.identity, ~0,
                                  QueryTriggerInteraction.Ignore)) return position;
        }

        Debug.LogWarning("[V0.1] 테스트 무기 상자의 빈 배치 지점을 찾지 못해 플레이어 오른쪽에 둡니다.");
        return origin + Vector3.right * 1.5f;
    }

    /// <summary>
    /// 주무기와 방패를 각각 하나씩 장착한다.
    /// 무장 없이 시작하면 맨손 6뎀으로 좀비 4마리를 못 버티고 소지품이 전부 좀비에게 넘어간다.
    /// </summary>
    void EquipBestWeapons()
    {
        var inventory = GetComponent<PlayerInventory>();
        var equipment = GetComponent<PlayerEquipment>();
        if (!inventory || !equipment) return;

        // GDD 21항 "한손 무기 + 방패" 조합을 바로 시험할 수 있게 방패부터 장착한다.
        TryEquipFrom(inventory, equipment, EquipmentSlot.OffHand, true);
        TryEquipFrom(inventory, equipment, EquipmentSlot.MainHand, false);
    }

    /// <summary>shieldWanted 면 방패만, 아니면 공격력 있는 무기만 골라 그 칸에 끼운다.</summary>
    static bool TryEquipFrom(PlayerInventory inventory, PlayerEquipment equipment, EquipmentSlot slot,
                             bool shieldWanted)
    {
        var stacks = inventory.Stacks;
        for (int i = 0; i < stacks.Count; i++)
        {
            var definition = stacks[i].definition;
            if (!definition) continue;
            if (definition.isShield != shieldWanted) continue;
            if (!shieldWanted && definition.attackDamage <= 0f) continue;
            if (!PlayerEquipment.CanEquipTo(slot, definition)) continue;

            if (equipment.TryEquipTo(slot, stacks[i])) return true;
        }
        return false;
    }

    /// <summary>빌더가 SaveSystem 에 물려둔 카탈로그를 재사용한다.</summary>
    static ItemCatalog FindCatalog()
    {
        var save = FindFirstObjectByType<SaveSystem>();
        return save ? save.Catalog : null;
    }

}
