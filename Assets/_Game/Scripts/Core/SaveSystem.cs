using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GDD 19항 저장/로드. F5 저장, F9 로드.
/// GDD 21항에 저장 키가 없어 비어 있는 F5/F9 를 썼다.
///
/// 컨테이너(GDD 15항) 와 NPC 는 아직 없으므로 저장 항목에서 빠져 있다.
/// 추가할 때 SaveData 에 리스트를 하나 늘리고 Collect/Restore 에 한 블록씩 붙이면 된다.
/// </summary>
public class SaveSystem : MonoBehaviour
{
    const string FileName = "v01_save.json";

    [SerializeField] ItemCatalog catalog;
    [SerializeField] GameClock clock;
    [SerializeField] Material zombieMaterial;
    [SerializeField] Material corpseMaterial;
    [Tooltip("GDD 16항 좀비 종류 에셋. 저장된 typeId 를 되돌릴 때 쓴다")]
    [SerializeField] ZombieDefinition[] zombieTypes;

    PlayerStats stats;
    PlayerInjuries injuries;
    PlayerInventory inventory;
    PlayerEquipment equipment;
    PlayerQuickSlots quickSlots;
    CharacterController playerController;
    Transform player;

    /// <summary>UI/디버그용 마지막 결과 메시지.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    /// <summary>다른 컴포넌트가 아이템 조회에 재사용할 수 있게 노출한다.</summary>
    public ItemCatalog Catalog => catalog;

    public string SavePath => Path.Combine(Application.persistentDataPath, FileName);
    public bool HasSave => File.Exists(SavePath);

    public void Bind(ItemCatalog itemCatalog, GameClock gameClock, Material zombie, Material corpse) =>
        Bind(itemCatalog, gameClock, zombie, corpse, null);

    public void Bind(ItemCatalog itemCatalog, GameClock gameClock, Material zombie, Material corpse,
                     ZombieDefinition[] types)
    {
        catalog = itemCatalog;
        clock = gameClock;
        zombieMaterial = zombie;
        corpseMaterial = corpse;
        zombieTypes = types;
    }

    void Awake() => CachePlayer();

    void CachePlayer()
    {
        stats = FindFirstObjectByType<PlayerStats>();
        if (!stats) return;

        player = stats.transform;
        injuries = stats.GetComponent<PlayerInjuries>();
        inventory = stats.GetComponent<PlayerInventory>();
        equipment = stats.GetComponent<PlayerEquipment>();
        quickSlots = stats.GetComponent<PlayerQuickSlots>();
        playerController = stats.GetComponent<CharacterController>();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.f5Key.wasPressedThisFrame) Save();
        else if (keyboard.f9Key.wasPressedThisFrame) Load();
    }

    // ---- 저장 ----

    public bool Save()
    {
        if (!player) CachePlayer();
        if (!player || !catalog)
        {
            LastMessage = "저장 실패: 플레이어나 아이템 카탈로그가 없습니다";
            Debug.LogWarning("[V0.1] " + LastMessage);
            return false;
        }

        var data = Collect();
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));

        LastMessage = "저장 완료";
        Debug.Log("[V0.1] 저장: " + SavePath);
        return true;
    }

    SaveData Collect()
    {
        var data = new SaveData();

        if (clock)
        {
            data.day = clock.Day;
            data.minuteOfDay = clock.MinuteOfDay;
        }

        data.playerPosition = player.position;
        data.playerYaw = player.eulerAngles.y;

        if (stats)
        {
            data.hp = stats.Hp;
            data.stamina = stats.Stamina;
            data.hunger = stats.Hunger;
            data.thirst = stats.Thirst;
            data.fatigue = stats.Fatigue;
        }

        if (injuries)
        {
            for (int i = 0; i < PlayerInjuries.PartCount; i++)
            {
                data.injuries[i] = (int)injuries.GetSeverity((BodyPart)i);
                data.bleeding[i] = injuries.IsBleeding((BodyPart)i);
            }
        }

        if (inventory)
        {
            var stacks = inventory.Stacks;
            for (int i = 0; i < stacks.Count; i++) data.inventory.Add(ToStackSave(stacks[i]));
        }

        if (equipment)
            for (int slot = 1; slot <= PlayerEquipment.SlotCount; slot++)
            {
                var stack = equipment.GetStack((EquipmentSlot)slot);
                if (stack == null || !stack.definition) continue;

                data.equipment.Add(new EquipSave
                {
                    slot = slot, id = stack.definition.id,
                    durability = stack.durability, loadedAmmo = stack.loadedAmmo
                });
            }

        if (quickSlots)
            for (int i = 0; i < PlayerQuickSlots.ConsumableSlotCount; i++)
                data.quickSlots[i] = quickSlots.GetConsumableId(i);

        // GDD 25항: 컨테이너 내용물.
        foreach (var container in FindObjectsByType<WorldContainer>(FindObjectsSortMode.None))
        {
            var saved = new ContainerSave { key = container.SaveKey };
            var entries = container.Contents;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!entries[i].definition) continue;

                saved.contents.Add(new StackSave
                {
                    id = entries[i].definition.id,
                    quantity = entries[i].quantity,
                    durability = entries[i].durability,
                    loadedAmmo = entries[i].loadedAmmo,
                });
            }
            data.containers.Add(saved);
        }

        // GDD 19항: 월드에 떨어뜨린 아이템 상태도 저장한다.
        foreach (var item in FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
        {
            if (!item.Definition) continue;

            data.worldItems.Add(new WorldItemSave
            {
                id = item.Definition.id,
                quantity = item.Quantity,
                position = item.transform.position,
                durability = item.Durability,
                loadedAmmo = item.LoadedAmmo,
            });
        }

        foreach (var zombie in FindObjectsByType<Zombie>(FindObjectsSortMode.None))
        {
            if (zombie.IsDead) continue;

            var entry = new ZombieSave
            {
                typeId = zombie.Definition ? zombie.Definition.id : string.Empty,
                position = zombie.transform.position,
                yaw = zombie.transform.eulerAngles.y,
                health = zombie.Health,
                isPlayerCorpse = zombie.IsPlayerCorpse,
            };

            var carried = zombie.CarriedItems;
            for (int i = 0; i < carried.Count; i++)
            {
                if (!carried[i].definition) continue;

                entry.carried.Add(new StackSave
                {
                    id = carried[i].definition.id,
                    quantity = carried[i].quantity,
                    durability = carried[i].durability,
                    loadedAmmo = carried[i].loadedAmmo,
                });
            }

            data.zombies.Add(entry);
        }

        return data;
    }

    static StackSave ToStackSave(ItemStack stack) => new StackSave
    {
        loadedAmmo = stack.loadedAmmo,
        id = stack.definition ? stack.definition.id : string.Empty,
        quantity = stack.quantity,
        x = stack.origin.x,
        y = stack.origin.y,
        durability = stack.durability,
    };

    // ---- 로드 ----

    public bool Load()
    {
        if (!HasSave)
        {
            LastMessage = "저장 파일이 없습니다";
            Debug.LogWarning("[V0.1] " + LastMessage);
            return false;
        }

        if (!player) CachePlayer();
        if (!player || !catalog)
        {
            LastMessage = "로드 실패: 플레이어나 아이템 카탈로그가 없습니다";
            Debug.LogWarning("[V0.1] " + LastMessage);
            return false;
        }

        var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        if (data == null)
        {
            LastMessage = "저장 파일을 읽을 수 없습니다";
            Debug.LogWarning("[V0.1] " + LastMessage);
            return false;
        }

        Restore(data);

        LastMessage = "로드 완료 (DAY " + data.day + ")";
        Debug.Log("[V0.1] " + LastMessage);
        return true;
    }

    void Restore(SaveData data)
    {
        player.GetComponent<PlayerConsumer>()?.ResetMedicalState();
        if (clock) clock.LoadState(data.day, data.minuteOfDay);

        // CharacterController 가 붙어 있으면 transform 을 직접 옮기기 전에 끄는 편이 안전하다.
        if (playerController) playerController.enabled = false;
        player.position = data.playerPosition;
        player.rotation = Quaternion.Euler(0f, data.playerYaw, 0f);
        if (playerController) playerController.enabled = true;

        if (stats) stats.LoadState(data.hp, data.stamina, data.hunger, data.thirst, data.fatigue);

        if (injuries)
        {
            injuries.ClearAll();
            if (data.injuries != null)
                for (int i = 0; i < data.injuries.Length && i < PlayerInjuries.PartCount; i++)
                    injuries.Apply((BodyPart)i, (InjurySeverity)data.injuries[i]);
            if (data.bleeding != null)
                for (int i = 0; i < data.bleeding.Length && i < PlayerInjuries.PartCount; i++)
                    injuries.SetBleeding((BodyPart)i, data.bleeding[i]);
        }

        RestoreInventory(data);
        RestoreEquipment(data);
        RestoreQuickSlots(data);
        RestoreContainers(data);
        RestoreWorldItems(data);
        RestoreZombies(data);
    }

    void RestoreInventory(SaveData data)
    {
        if (!inventory) return;

        inventory.Clear();
        for (int i = 0; i < data.inventory.Count; i++)
        {
            var entry = data.inventory[i];
            var definition = catalog.Find(entry.id);
            if (!definition) continue;

            var stack = new ItemStack
            {
                definition = definition,
                quantity = entry.quantity,
                durability = entry.durability < 0 ? definition.maxDurability : entry.durability,
                loadedAmmo = entry.loadedAmmo < 0 ? definition.magazineSize : entry.loadedAmmo,
                origin = new Vector2Int(entry.x, entry.y),
            };

            // 저장된 좌표에 못 놓으면(격자 크기가 바뀐 경우 등) 빈 자리에라도 넣어 아이템을 잃지 않게 한다.
            if (!inventory.PlaceExact(stack)) inventory.TryPlaceStack(stack);
        }
    }

    void RestoreEquipment(SaveData data)
    {
        if (!equipment) return;

        equipment.StripAll();
        for (int i = 0; i < data.equipment.Count; i++)
        {
            var definition = catalog.Find(data.equipment[i].id);
            if (!definition) continue;

            int durability = data.equipment[i].durability < 0 ? definition.maxDurability : data.equipment[i].durability;
            equipment.LoadEquip((EquipmentSlot)data.equipment[i].slot, definition, durability);

            var loaded = equipment.GetStack((EquipmentSlot)data.equipment[i].slot);
            if (loaded != null)
                loaded.loadedAmmo = data.equipment[i].loadedAmmo < 0
                    ? definition.magazineSize : data.equipment[i].loadedAmmo;
        }
    }

    void RestoreQuickSlots(SaveData data)
    {
        if (!quickSlots || data.quickSlots == null) return;

        for (int i = 0; i < PlayerQuickSlots.ConsumableSlotCount && i < data.quickSlots.Length; i++)
            quickSlots.LoadConsumable(i, catalog.Find(data.quickSlots[i]));
    }

    /// <summary>GDD 25항 컨테이너 내용물 복원. 컨테이너 자체는 씬에 고정돼 있어 다시 만들지 않는다.</summary>
    void RestoreContainers(SaveData data)
    {
        if (data.containers == null) return;

        foreach (var container in FindObjectsByType<WorldContainer>(FindObjectsSortMode.None))
        {
            ContainerSave saved = null;
            for (int i = 0; i < data.containers.Count; i++)
                if (data.containers[i].key == container.SaveKey) { saved = data.containers[i]; break; }

            if (saved == null) continue;

            var entries = new List<WorldContainer.Entry>();
            for (int i = 0; i < saved.contents.Count; i++)
            {
                var definition = catalog.Find(saved.contents[i].id);
                if (!definition) continue;

                entries.Add(new WorldContainer.Entry
                {
                    definition = definition,
                    quantity = saved.contents[i].quantity,
                    durability = saved.contents[i].durability < 0
                        ? definition.maxDurability : saved.contents[i].durability,
                    loadedAmmo = saved.contents[i].loadedAmmo < 0
                        ? definition.magazineSize : saved.contents[i].loadedAmmo,
                });
            }

            container.LoadContents(entries);
        }
    }

    void RestoreWorldItems(SaveData data)
    {
        foreach (var item in FindObjectsByType<WorldItem>(FindObjectsSortMode.None)) Destroy(item.gameObject);

        for (int i = 0; i < data.worldItems.Count; i++)
        {
            var entry = data.worldItems[i];
            var definition = catalog.Find(entry.id);
            if (!definition) continue;

            WorldItem.Spawn(definition, entry.quantity, entry.position, Vector3.zero)
                     .SetDurability(entry.durability)
                     .SetAmmo(entry.loadedAmmo);
        }
    }

    void RestoreZombies(SaveData data)
    {
        foreach (var zombie in FindObjectsByType<Zombie>(FindObjectsSortMode.None)) Destroy(zombie.gameObject);

        for (int i = 0; i < data.zombies.Count; i++)
        {
            var entry = data.zombies[i];

            // Spawn 이 1.05 만큼 띄우므로 저장된 위치에서 그만큼 내려서 넣는다.
            var groundPosition = entry.position - Vector3.up * 1.05f;
            var zombie = Zombie.Spawn(groundPosition,
                FindZombieType(entry.typeId),
                entry.isPlayerCorpse ? corpseMaterial : zombieMaterial,
                entry.isPlayerCorpse);

            zombie.transform.rotation = Quaternion.Euler(0f, entry.yaw, 0f);
            zombie.LoadHealth(entry.health);

            for (int c = 0; c < entry.carried.Count; c++)
            {
                var definition = catalog.Find(entry.carried[c].id);
                if (definition)
                    zombie.AddCarried(definition, entry.carried[c].quantity,
                                      entry.carried[c].durability, entry.carried[c].loadedAmmo);
            }
        }
    }

    /// <summary>GDD 16항 종류 에셋을 id 로 찾는다. 못 찾으면 null 이고 좀비는 기본값으로 돈다.</summary>
    ZombieDefinition FindZombieType(string id)
    {
        if (string.IsNullOrEmpty(id) || zombieTypes == null) return null;

        for (int i = 0; i < zombieTypes.Length; i++)
            if (zombieTypes[i] && zombieTypes[i].id == id) return zombieTypes[i];

        return null;
    }

    /// <summary>테스트용. 저장 파일을 지운다.</summary>
    public void DeleteSave()
    {
        if (!HasSave) return;

        File.Delete(SavePath);
        LastMessage = "저장 파일 삭제";
    }
}
