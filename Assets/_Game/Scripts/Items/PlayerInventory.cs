using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>GDD 11항: 여러 칸을 차지하는 아이템 묶음. origin 은 좌상단 칸.</summary>
public class ItemStack
{
    public ItemDefinition definition;
    public int quantity;
    public Vector2Int origin;

    /// <summary>
    /// GDD 11항 현재 내구도. 정의(공유 에셋) 가 아니라 개체 상태이므로 여기 있어야 한다 —
    /// 같은 손도끼 두 자루가 서로 다른 내구도를 가질 수 있어야 한다.
    /// </summary>
    public int durability;

    /// <summary>
    /// GDD 4항 장전된 탄. 내구도와 같은 이유로 개체 상태다 — 같은 권총 두 자루의 잔탄이 달라야 한다.
    /// </summary>
    public int loadedAmmo;

    public Vector2Int Size => definition ? definition.gridSize : Vector2Int.one;

    public bool HasDurability => definition && definition.maxDurability > 0;

    public bool UsesAmmo => definition && definition.UsesAmmo;
    public bool IsEmpty => UsesAmmo && loadedAmmo <= 0;
    public bool IsBroken => HasDurability && durability <= 0;

    /// <summary>내구도를 최대치로 초기화해서 만든다. 스택 생성은 전부 이 경로를 쓴다.</summary>
    public static ItemStack Create(ItemDefinition definition, int quantity, Vector2Int origin) => new ItemStack
    {
        definition = definition,
        quantity = quantity,
        origin = origin,
        durability = definition ? definition.maxDurability : 0,
        // 주워서 바로 쏠 수 있게 꽉 채워 준다. 루팅이 들어오면 여기서 줄이면 된다.
        loadedAmmo = definition ? definition.magazineSize : 0,
    };

    public bool Overlaps(Vector2Int otherOrigin, Vector2Int otherSize) =>
        origin.x < otherOrigin.x + otherSize.x && otherOrigin.x < origin.x + Size.x &&
        origin.y < otherOrigin.y + otherSize.y && otherOrigin.y < origin.y + Size.y;
}

/// <summary>
/// GDD 11항 칸 기반 인벤토리. 무게가 아니라 칸으로 제한한다.
/// 퀵슬롯 1~5 는 스택 리스트의 앞 5개를 가리킨다 — 정식 퀵슬롯 등록은 8단계.
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    [SerializeField, Min(1)] int width = 8;
    [SerializeField, Min(1)] int height = 6;

    readonly List<ItemStack> stacks = new List<ItemStack>();

    public int Width => width;
    public int Height => height;
    public IReadOnlyList<ItemStack> Stacks => stacks;

    /// <summary>내용(스택 추가/이동/분할/정렬) 이 바뀔 때마다 증가. UI 가 격자를 다시 만드는 기준.</summary>
    public int Version { get; private set; }

    public ItemStack GetStack(int index) => index >= 0 && index < stacks.Count ? stacks[index] : null;
    // ---- 배치 판정 ----

    public bool InBounds(Vector2Int origin, Vector2Int size) =>
        origin.x >= 0 && origin.y >= 0 && origin.x + size.x <= width && origin.y + size.y <= height;

    public bool CanPlace(Vector2Int origin, Vector2Int size, ItemStack ignore)
    {
        if (!InBounds(origin, size)) return false;

        for (int i = 0; i < stacks.Count; i++)
        {
            if (stacks[i] == ignore) continue;
            if (stacks[i].Overlaps(origin, size)) return false;
        }
        return true;
    }

    public ItemStack GetStackAt(Vector2Int cell)
    {
        for (int i = 0; i < stacks.Count; i++)
            if (stacks[i].Overlaps(cell, Vector2Int.one)) return stacks[i];
        return null;
    }

    bool TryFindFreeOrigin(Vector2Int size, out Vector2Int origin)
    {
        for (int y = 0; y + size.y <= height; y++)
        for (int x = 0; x + size.x <= width; x++)
        {
            var candidate = new Vector2Int(x, y);
            if (!CanPlace(candidate, size, null)) continue;

            origin = candidate;
            return true;
        }

        origin = default;
        return false;
    }

    // ---- 변경 ----

    /// <summary>GDD 12항: 넣을 수 있는 만큼만 넣고 실제로 넣은 수량을 반환. 나머지는 월드에 남는다.</summary>
    public int Add(ItemDefinition definition, int amount)
    {
        if (!definition || amount <= 0) return 0;
        int remaining = amount;

        // 같은 아이템 스택에 먼저 합친다.
        for (int i = 0; i < stacks.Count && remaining > 0; i++)
        {
            var stack = stacks[i];
            if (stack.definition != definition) continue;

            int room = definition.maxStack - stack.quantity;
            if (room <= 0) continue;

            int moved = Mathf.Min(room, remaining);
            stack.quantity += moved;
            remaining -= moved;
        }

        // 남으면 빈 칸을 찾는다. 자리가 없으면 그대로 남긴다.
        while (remaining > 0)
        {
            Vector2Int origin;
            if (!TryFindFreeOrigin(definition.gridSize, out origin)) break;

            int moved = Mathf.Min(definition.maxStack, remaining);
            stacks.Add(ItemStack.Create(definition, moved, origin));
            remaining -= moved;
        }

        int added = amount - remaining;
        if (added > 0) Version++;
        return added;
    }

    public bool TryMove(ItemStack stack, Vector2Int origin)
    {
        if (stack == null || !CanPlace(origin, stack.Size, stack)) return false;

        stack.origin = origin;
        Version++;
        return true;
    }

    /// <summary>같은 아이템에 겹쳐 놓으면 합친다. maxStack 을 넘는 수량은 원래 스택에 남는다.</summary>
    public bool TryMerge(ItemStack from, ItemStack into)
    {
        if (from == null || into == null || from == into) return false;
        if (from.definition != into.definition) return false;

        int room = into.definition.maxStack - into.quantity;
        if (room <= 0) return false;

        int moved = Mathf.Min(room, from.quantity);
        into.quantity += moved;
        from.quantity -= moved;
        if (from.quantity <= 0) stacks.Remove(from);

        Version++;
        return true;
    }

    public int TakeFrom(ItemStack stack, int amount)
    {
        if (stack == null || amount <= 0) return 0;

        // 인벤토리에 없는 스택(장착 중, 컨테이너 안 등) 을 깎으면 아이템이 복제된다.
        // 아래 stacks.Remove 가 비회원에게 조용히 실패하기 때문에 호출자는 성공한 줄 안다.
        // 실제로 장착한 무기를 합성하면 원본이 장착된 채로 결과물이 하나 더 생겼다.
        if (!stacks.Contains(stack)) return 0;

        int taken = Mathf.Min(amount, stack.quantity);
        stack.quantity -= taken;
        if (stack.quantity <= 0) stacks.Remove(stack);

        Version++;
        return taken;
    }

    /// <summary>GDD 11항 스택 분할. 새 스택을 놓을 빈 자리가 없으면 null.</summary>
    public ItemStack Split(ItemStack stack, int amount)
    {
        if (stack == null || amount <= 0 || amount >= stack.quantity) return null;

        Vector2Int origin;
        if (!TryFindFreeOrigin(stack.Size, out origin)) return null;

        stack.quantity -= amount;
        var half = ItemStack.Create(stack.definition, amount, origin);
        half.durability = stack.durability; // 나뉜 묶음도 같은 마모 상태를 물려받는다
        stacks.Add(half);

        Version++;
        return half;
    }

    /// <summary>
    /// 이미 있는 스택 객체를 빈 자리에 그대로 넣는다. 내구도 같은 개체 상태가 보존된다.
    /// 장비 해제와 저장 복원이 쓴다.
    /// </summary>
    public bool TryPlaceStack(ItemStack stack)
    {
        if (stack == null || !stack.definition || stack.quantity <= 0) return false;

        Vector2Int origin;
        if (!TryFindFreeOrigin(stack.Size, out origin)) return false;

        stack.origin = origin;
        stacks.Add(stack);
        Version++;
        return true;
    }

    /// <summary>저장 복원용. 저장된 격자 좌표에 스택을 그대로 놓는다. 자리가 겹치면 실패.</summary>
    public bool PlaceExact(ItemStack stack)
    {
        if (stack == null || !stack.definition || stack.quantity <= 0) return false;
        if (!CanPlace(stack.origin, stack.Size, null)) return false;

        stacks.Add(stack);
        Version++;
        return true;
    }

    /// <summary>GDD 8항 사망 시 소지품을 전부 잃는다.</summary>
    public void Clear()
    {
        if (stacks.Count == 0) return;

        stacks.Clear();
        Version++;
    }

    /// <summary>GDD 11항 자동 정렬. 부분 스택을 합치고 큰 아이템부터 좌상단으로 다시 채운다.</summary>
    public void AutoSort()
    {
        ConsolidateStacks();

        var snapshot = new Vector2Int[stacks.Count];
        for (int i = 0; i < stacks.Count; i++) snapshot[i] = stacks[i].origin;

        var ordered = new List<ItemStack>(stacks);
        ordered.Sort(CompareForSort);

        // 전부 격자 밖으로 빼두고 큰 것부터 다시 배치한다.
        for (int i = 0; i < stacks.Count; i++) stacks[i].origin = new Vector2Int(-100, -100);

        bool allPlaced = true;
        for (int i = 0; i < ordered.Count; i++)
        {
            Vector2Int origin;
            if (!TryFindFreeOrigin(ordered[i].Size, out origin)) { allPlaced = false; break; }

            ordered[i].origin = origin;
        }

        // 재배치가 실패하면 원래 배치로 되돌린다. 정렬 때문에 아이템을 잃는 일은 없어야 한다.
        if (!allPlaced)
        {
            for (int i = 0; i < stacks.Count; i++) stacks[i].origin = snapshot[i];
            return;
        }

        stacks.Clear();
        stacks.AddRange(ordered);
        Version++;
    }

    void ConsolidateStacks()
    {
        for (int i = 0; i < stacks.Count; i++)
        {
            var target = stacks[i];
            int room = target.definition.maxStack - target.quantity;

            for (int j = stacks.Count - 1; j > i && room > 0; j--)
            {
                var source = stacks[j];
                if (source.definition != target.definition) continue;

                int moved = Mathf.Min(room, source.quantity);
                target.quantity += moved;
                source.quantity -= moved;
                room -= moved;

                if (source.quantity <= 0) stacks.RemoveAt(j);
            }
        }
    }

    static int CompareForSort(ItemStack a, ItemStack b)
    {
        int areaA = a.Size.x * a.Size.y;
        int areaB = b.Size.x * b.Size.y;
        if (areaA != areaB) return areaB.CompareTo(areaA);

        int byId = string.Compare(a.definition.id, b.definition.id, StringComparison.Ordinal);
        return byId != 0 ? byId : b.quantity.CompareTo(a.quantity);
    }
}
