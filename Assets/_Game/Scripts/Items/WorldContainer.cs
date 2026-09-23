using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GDD 24항 컨테이너. 냉장고/옷장/책상/상자/시체/차량이 각자 독립적인 인벤토리를 가진다.
///
/// E 로 가장 가까운 대상과 상호작용해 열고, Tab 화면에서 플레이어 인벤토리와 함께 보며
/// 원하는 것만 가져온다. 자동 전체 획득은 GDD 7항대로 제공하지 않는다.
///
/// ponytail: 컨테이너 쪽은 격자가 아니라 단순 목록이다. 기획서가 요구하는 것은
/// "독립 인벤토리 + 골라 가져오기" 이고, 컨테이너 안에서 배치를 정리할 이유는 아직 없다.
/// </summary>
public class WorldContainer : MonoBehaviour
{
    /// <summary>
    /// 직렬화되어야 한다 — readonly 필드나 비직렬화 구조체로 두면 씬을 저장하고 다시 열 때
    /// 컨테이너가 텅 비어 버린다 (실제로 겪었다). GDD 25항 저장 목록에도 컨테이너 내용물이 있다.
    /// </summary>
    [Serializable]
    public struct Entry
    {
        public ItemDefinition definition;
        public int quantity;
        public int durability;
        public int loadedAmmo;
    }

    // 열려 있는 컨테이너를 찾을 때마다 씬을 훑지 않도록 모아둔다.
    static readonly List<WorldContainer> all = new List<WorldContainer>();
    public static IReadOnlyList<WorldContainer> All => all;

    [SerializeField] string displayName = "상자";
    [Tooltip("이 거리 안에서 E 로 열 수 있다")]
    [SerializeField] float interactRange = 2.2f;

    [SerializeField] List<Entry> contents = new List<Entry>();

    public string DisplayName => displayName;
    public float InteractRange => interactRange;
    public IReadOnlyList<Entry> Contents => contents;
    public int Count => contents.Count;

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    public void Add(ItemDefinition definition, int quantity, int durability = -1, int loadedAmmo = -1)
    {
        if (!definition || quantity <= 0) return;

        int resolved = durability < 0 ? definition.maxDurability : durability;
        int resolvedAmmo = loadedAmmo < 0 ? definition.magazineSize : loadedAmmo;

        // 내구도가 없는 아이템은 같은 종류끼리 합친다. 무기는 개체마다 내구도가 달라 따로 둔다.
        if (definition.maxDurability <= 0)
        {
            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i].definition != definition) continue;

                var merged = contents[i];
                merged.quantity += quantity;
                contents[i] = merged;
                return;
            }
        }

        contents.Add(new Entry
        {
            definition = definition, quantity = quantity,
            durability = resolved, loadedAmmo = resolvedAmmo,
        });
    }

    /// <summary>
    /// 인벤토리의 스택을 컨테이너로 옮긴다. GDD 24항 컨테이너는 독립 인벤토리이므로
    /// 넣는 방향도 있어야 한다. 실제로 옮긴 수를 반환.
    ///
    /// 컨테이너 쪽은 칸 제한이 없는 목록이라 수량 제한은 걸지 않는다 —
    /// 제한이 필요해지면 maxRows 같은 상한을 여기서 본다.
    /// </summary>
    public int PutFrom(ItemStack stack, PlayerInventory inventory, int amount)
    {
        if (!inventory || stack == null || !stack.definition || amount <= 0) return 0;

        int taken = inventory.TakeFrom(stack, Mathf.Min(amount, stack.quantity));
        if (taken <= 0) return 0;

        Add(stack.definition, taken, stack.durability, stack.loadedAmmo);
        return taken;
    }

    /// <summary>
    /// 종류와 내구도로 줄을 찾는다. UI 가 인덱스를 들고 있으면 목록이 바뀐 뒤 엉뚱한 줄을
    /// 집어간다 — 그래서 클릭 시점에 다시 찾는다.
    /// </summary>
    public int IndexOf(ItemDefinition definition, int durability)
    {
        for (int i = 0; i < contents.Count; i++)
            if (contents[i].definition == definition && contents[i].durability == durability) return i;

        return -1;
    }

    /// <summary>종류/내구도로 찾아 옮긴다. 이미 없어졌으면 0.</summary>
    public int TakeInto(ItemDefinition definition, int durability, PlayerInventory inventory) =>
        TakeInto(IndexOf(definition, durability), inventory);

    /// <summary>
    /// 한 줄을 플레이어 인벤토리로 옮긴다. 자리가 부족하면 GDD 7항대로 가능한 수량만 가져가고
    /// 나머지는 컨테이너에 남긴다. 실제로 옮긴 수를 반환.
    /// </summary>
    public int TakeInto(int index, PlayerInventory inventory)
    {
        if (!inventory || index < 0 || index >= contents.Count) return 0;

        var entry = contents[index];
        if (!entry.definition || entry.quantity <= 0) return 0;

        int accepted = inventory.Add(entry.definition, entry.quantity);
        if (accepted <= 0) return 0;

        // 내구도/잔탄을 실어 준다. Add 는 최대치로 만들기 때문에 넣은 스택을 찾아 덮어써야 한다.
        ApplyInstanceState(inventory, entry);

        if (accepted >= entry.quantity) contents.RemoveAt(index);
        else
        {
            entry.quantity -= accepted;
            contents[index] = entry;
        }

        return accepted;
    }

    static void ApplyInstanceState(PlayerInventory inventory, Entry entry)
    {
        var definition = entry.definition;
        if (definition.maxDurability <= 0 && !definition.UsesAmmo) return;

        var stacks = inventory.Stacks;
        for (int i = stacks.Count - 1; i >= 0; i--)
        {
            if (stacks[i].definition != definition) continue;

            if (definition.maxDurability > 0) stacks[i].durability = entry.durability;
            if (definition.UsesAmmo) stacks[i].loadedAmmo = entry.loadedAmmo;
            return;
        }
    }

    /// <summary>저장 복원용. 내용물을 저장된 목록으로 교체한다.</summary>
    public void LoadContents(List<Entry> loaded)
    {
        contents.Clear();
        if (loaded != null) contents.AddRange(loaded);
    }

    /// <summary>저장 식별용. 같은 위치의 컨테이너를 다시 찾는 열쇠로 쓴다.</summary>
    public string SaveKey => DisplayName + "@" + transform.position.ToString("F1");

    /// <summary>플레이어가 이 컨테이너를 열 수 있는 거리인지.</summary>
    public bool IsInRange(Vector3 position) =>
        (transform.position - position).sqrMagnitude <= interactRange * interactRange;

    /// <summary>
    /// 3D 에셋이 없으므로 박스 프리미티브로 만든다. Zombie.Spawn 과 같은 이유로
    /// 공유 머티리얼을 받고 모드에 맞는 파괴 함수를 쓴다.
    /// </summary>
    public static WorldContainer Spawn(string label, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Container_" + label;
        go.transform.position = position + Vector3.up * (size.y * 0.5f);
        go.transform.localScale = size;

        var renderer = go.GetComponent<Renderer>();
        if (renderer && material) renderer.sharedMaterial = material;

        var container = go.AddComponent<WorldContainer>();
        container.displayName = label;
        return container;
    }
}
