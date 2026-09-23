using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GDD 14항 E 상호작용(가장 가까운 대상) + GDD 12항 Q 투척.
/// 주변 물건 전체 목록은 GDD 13항 Tab 화면이 쓴다 — 정밀 선택은 그쪽 담당.
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public class PlayerInteractor : MonoBehaviour
{
    [Header("줍기")]
    // 2m 면 지름 4m 라 방 하나를 통째로 덮어서 Tab 에 방 안 아이템이 다 떴다 (사용자 지적).
    [SerializeField] float pickupRadius = 1.2f;
    [Tooltip("E 를 누르고 있을 때 연속 줍기가 시작되기까지의 시간")]
    [SerializeField] float holdInitialDelay = 0.3f;
    [Tooltip("연속 줍기 간격")]
    [SerializeField] float holdRepeatInterval = 0.12f;

    [Header("투척")]
    [SerializeField] float throwSpeed = 4f;
    [Tooltip("전방 대비 위로 던지는 비율. 클수록 높이 뜬다.")]
    [SerializeField] float throwUpwardRatio = 0.45f;
    [SerializeField] float throwOriginForward = 0.7f;
    [SerializeField] float throwOriginHeight = 0.2f;

    PlayerInventory inventory;
    PlayerQuickSlots quickSlots;
    PlayerAssassinate assassinate;
    PlayerEquipment equipment;
    SurveyHighlight survey;
    [Tooltip("GDD 8항: 2개 이상 던질 때 띄우는 수량 선택창")]
    [SerializeField] QuantityDialog quantityDialog;
    readonly Collider[] overlapBuffer = new Collider[32];
    readonly List<WorldItem> nearbyItems = new List<WorldItem>();
    Comparison<WorldItem> distanceComparison;
    Vector3 sortOrigin;
    float nextHoldPickupTime;

    /// <summary>GDD 13항 Tab 화면용. 가까운 순으로 정렬돼 있다.</summary>
    public IReadOnlyList<WorldItem> NearbyItems => nearbyItems;

    public WorldItem NearestItem { get; private set; }

    /// <summary>GDD 8항 수량 선택창 연결. 빌더가 HUD 를 만든 뒤 꽂아준다.</summary>
    public void BindQuantityDialog(QuantityDialog dialog) => quantityDialog = dialog;

    /// <summary>GDD 8항 투척 결과 메시지. HUD 프롬프트가 잠깐 띄운다.</summary>
    public string ThrowMessage { get; private set; } = string.Empty;

    /// <summary>ThrowMessage 가 갱신된 시각. 메시지를 얼마나 띄울지 UI 가 판단한다.</summary>
    public float ThrowMessageTime { get; private set; }

    void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        quickSlots = GetComponent<PlayerQuickSlots>();
        assassinate = GetComponent<PlayerAssassinate>();
        equipment = GetComponent<PlayerEquipment>();

        // GDD 4항 F 주변 조사. 사용자 요구로 키 할당은 빼두고 컴포넌트만 남겨뒀다 —
        // 강조 방식을 정한 뒤 다시 붙인다. 호출하려면 survey.Survey() 를 쓰면 된다.
        survey = GetComponent<SurveyHighlight>();
        distanceComparison = CompareByDistance; // 매 프레임 델리게이트를 새로 만들지 않도록 캐시
    }

    void Update()
    {
        RefreshNearbyItems();

        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // GDD 19항: 암살 모션 중에는 어떤 조작도 받지 않는다. 가장 먼저 걸러낸다 —
        // 뒤쪽에 두면 숫자키 소모품과 Q 투척이 그 앞에서 처리돼 버린다 (실제로 그랬다).
        if (assassinate && assassinate.IsAssassinating) return;

        UpdateNearestContainer();
        UpdateNearestWindow();

        HandleQuickSlotKeys(keyboard);
        HandleThrowInput(keyboard);

        // GDD 19항: E 는 암살이 줍기보다 우선한다. 조건이 맞을 때만 가로챈다.
        if (keyboard.eKey.wasPressedThisFrame && assassinate && assassinate.TryStart()) return;

        // 커튼은 C. 좀비가 실내 플레이어를 발견하는 조건이라 여닫는 조작이 필요하다 (사용자 요구).
        if (keyboard.cKey.wasPressedThisFrame && NearestWindow) NearestWindow.ToggleCurtain();

        // 전등은 L. 켜면 지금 있는 방 전체가 보이고, 끄면 부채꼴 시야만 남는다 (사용자 요구).
        if (keyboard.lKey.wasPressedThisFrame) HouseLights.Toggle();

        // 기획 4항: E 로 창문을 여닫는다. 컨테이너/아이템보다 가까울 때만 가로챈다.
        if (keyboard.eKey.wasPressedThisFrame && TryToggleWindow()) return;

        HandlePickupInput(keyboard);
    }

    void RefreshNearbyItems()
    {
        nearbyItems.Clear();
        NearestItem = null;

        int count = Physics.OverlapSphereNonAlloc(transform.position, pickupRadius, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            var item = overlapBuffer[i].GetComponent<WorldItem>();
            if (!item || !item.Definition) continue;

            nearbyItems.Add(item);
        }

        if (nearbyItems.Count == 0) return;

        sortOrigin = transform.position;
        nearbyItems.Sort(distanceComparison);
        NearestItem = nearbyItems[0];
    }

    int CompareByDistance(WorldItem a, WorldItem b) =>
        (a.transform.position - sortOrigin).sqrMagnitude
        .CompareTo((b.transform.position - sortOrigin).sqrMagnitude);

    /// <summary>사거리 안의 가장 가까운 컨테이너.</summary>
    public WorldContainer NearestContainer { get; private set; }

    /// <summary>
    /// Tab 화면이 보여줄 컨테이너. 사용자 요구대로 E 는 문과 창문 전용이고,
    /// 컨테이너는 사거리 안에 들어오면 Tab 화면에서 바로 내용을 보고 가져온다.
    /// </summary>
    public WorldContainer ViewContainer => NearestContainer;

    void UpdateNearestContainer()
    {
        NearestContainer = null;
        float best = float.MaxValue;

        var all = WorldContainer.All;
        for (int i = 0; i < all.Count; i++)
        {
            var container = all[i];
            if (!container || !container.IsInRange(transform.position)) continue;

            float distance = (container.transform.position - transform.position).sqrMagnitude;
            if (distance < best) { best = distance; NearestContainer = container; }
        }

    }


    /// <summary>
    /// 기획 4항: E 가 여닫을 창문. 컨테이너나 바닥 아이템이 더 가까우면 null 이다 —
    /// "E 는 가장 가까운 대상이 임자" 라는 규칙을 여기 한 곳에만 둔다 (UI 도 이걸 읽는다).
    /// </summary>
    public WindowFrame NearestWindow { get; private set; }

    void UpdateNearestWindow()
    {
        NearestWindow = WindowFrame.Nearest(transform.position);
        if (!NearestWindow) return;

        if (Closer(NearestContainer ? NearestContainer.transform : null, NearestWindow.transform)
            || Closer(NearestItem ? NearestItem.transform : null, NearestWindow.transform))
            NearestWindow = null;
    }

    bool TryToggleWindow()
    {
        if (!NearestWindow) return false;

        NearestWindow.Toggle();
        return true;
    }

    /// <summary>a 가 b 보다 가까운가. a 가 없으면 false.</summary>
    bool Closer(Transform a, Transform b)
    {
        if (!a || !b) return false;

        return (a.position - transform.position).sqrMagnitude
             < (b.position - transform.position).sqrMagnitude;
    }

    /// <summary>
    /// GDD 4항 "1~5 퀵슬롯 선택". 처음 누르면 선택, 같은 키를 다시 누르면 사용한다.
    /// 선택은 GDD 8항 Q 투척의 대상이 된다.
    /// </summary>
    void HandleQuickSlotKeys(Keyboard keyboard)
    {
        if (!quickSlots) return;

        for (int i = 0; i < PlayerQuickSlots.SlotCount; i++)
        {
            var key = (Key)((int)Key.Digit1 + i);
            if (keyboard[key].wasPressedThisFrame) quickSlots.PressSlot(i);
        }
    }

    /// <summary>
    /// GDD 14항 E: 줍기 전용. 꾹 누르면 연속으로 줍는다.
    /// 아이템 사용은 인벤토리 화면에서만 한다 — 인게임에서 E 로 소비되면 실수로 먹게 된다.
    /// </summary>
    void HandlePickupInput(Keyboard keyboard)
    {
        if (keyboard.eKey.wasPressedThisFrame)
        {
            TryPickup(NearestItem);
            nextHoldPickupTime = Time.time + holdInitialDelay;
            return;
        }

        bool holding = keyboard.eKey.isPressed;
        if (!holding) return;
        if (Time.time < nextHoldPickupTime) return;

        TryPickup(NearestItem);
        nextHoldPickupTime = Time.time + holdRepeatInterval;
    }

    /// <summary>
    /// GDD 13항 선택 획득. 일부라도 가져갔으면 true.
    /// Tab 화면에서 목록의 특정 아이템을 골랐을 때도 이 경로를 쓴다.
    /// </summary>
    public bool TryPickup(WorldItem item)
    {
        if (!item || !item.Definition) return false;

        int added = inventory.Add(item.Definition, item.Quantity);
        if (added <= 0) return false;

        // GDD 11항: 바닥의 마모 상태와 잔탄을 인벤토리로 옮긴다.
        // 무기는 스택되지 않으므로 방금 들어간 스택을 찾는다.
        ApplyPickedState(item);

        // GDD 12항: 공간이 부족하면 획득 가능한 수량만 가져가고 나머지는 월드에 남긴다.
        if (added >= item.Quantity)
        {
            // 파괴는 프레임 끝에 일어나므로 같은 프레임에 또 집지 않도록 목록에서 먼저 뺀다.
            nearbyItems.Remove(item);
            if (NearestItem == item) NearestItem = nearbyItems.Count > 0 ? nearbyItems[0] : null;

            Destroy(item.gameObject);
        }
        else
        {
            item.SetQuantity(item.Quantity - added);
        }

        return true;
    }

    /// <summary>주운 무기의 내구도를 복원한다. 스택되는 소모품은 내구도가 없어 영향이 없다.</summary>
    void ApplyPickedState(WorldItem item)
    {
        var definition = item.Definition;
        if (!definition) return;
        if (definition.maxDurability <= 0 && !definition.UsesAmmo) return;

        var stacks = inventory.Stacks;
        for (int i = stacks.Count - 1; i >= 0; i--)
        {
            if (stacks[i].definition != definition) continue;

            if (definition.maxDurability > 0) stacks[i].durability = item.Durability;
            if (definition.UsesAmmo) stacks[i].loadedAmmo = item.LoadedAmmo;
            return;
        }
    }

    /// <summary>
    /// GDD 8항 Q 투척. 선택한 퀵슬롯 아이템을 던진다.
    /// 1개면 즉시, 2개 이상이면 수량 선택창을 띄운다 (인벤토리 드래그-아웃과 같은 창).
    /// 무기 칸을 선택했으면 장착을 해제하고 그 무기를 던진다.
    /// </summary>
    void HandleThrowInput(Keyboard keyboard)
    {
        if (!keyboard.qKey.wasPressedThisFrame) return;
        if (!quickSlots) return;

        int index = quickSlots.SelectedIndex;
        if (index < 0) { ThrowMessage = "던질 퀵슬롯을 먼저 고르세요 (1~5)"; return; }

        var stack = quickSlots.SelectedStack;
        if (stack == null || !stack.definition) { ThrowMessage = PlayerQuickSlots.SlotName(index) + " 칸이 비어 있습니다"; return; }

        var definition = stack.definition;

        // 무기 칸: 장착 중인 무기를 던진다. 해제하면 인벤토리로 돌아오므로 거기서 꺼낸다.
        if (PlayerQuickSlots.IsWeaponSlot(index))
        {
            int durability = stack.durability;
            int ammo = stack.loadedAmmo;
            if (!equipment || !equipment.Unequip(PlayerQuickSlots.WeaponSlotFor(index))) return;

            var returned = FindStack(definition);
            if (returned != null) inventory.TakeFrom(returned, 1);

            ThrowToWorld(definition, 1, durability, ammo);
            SetThrowMessage(definition.displayName + " 투척");
            return;
        }

        int total = quickSlots.CountOf(index);
        if (total <= 1)
        {
            ThrowSelectedAmount(definition, 1);
            return;
        }

        if (!quantityDialog) { ThrowSelectedAmount(definition, total); return; }

        var captured = definition;
        quantityDialog.Open(captured.displayName + " 투척", total, amount => ThrowSelectedAmount(captured, amount));
    }

    /// <summary>인벤토리에서 해당 종류를 amount 만큼 꺼내 던진다. 여러 스택에 걸쳐 있어도 모은다.</summary>
    void ThrowSelectedAmount(ItemDefinition definition, int amount)
    {
        if (!definition || amount <= 0) return;

        int remaining = amount;
        int thrown = 0;
        int durability = -1;
        int ammo = -1;

        while (remaining > 0)
        {
            var stack = FindStack(definition);
            if (stack == null) break;

            if (durability < 0) { durability = stack.durability; ammo = stack.loadedAmmo; }

            int take = Mathf.Min(remaining, stack.quantity);
            int taken = inventory.TakeFrom(stack, take);
            if (taken <= 0) break;

            remaining -= taken;
            thrown += taken;
        }

        if (thrown <= 0) { ThrowMessage = definition.displayName + " 이(가) 없습니다"; return; }

        ThrowToWorld(definition, thrown, durability, ammo);
        SetThrowMessage(definition.displayName + " " + thrown + "개 투척");
    }

    void SetThrowMessage(string message)
    {
        ThrowMessage = message;
        ThrowMessageTime = Time.time;
    }

    ItemStack FindStack(ItemDefinition definition)
    {
        var stacks = inventory.Stacks;
        for (int i = 0; i < stacks.Count; i++)
            if (stacks[i].definition == definition) return stacks[i];

        return null;
    }

    /// <summary>GDD 12항 월드 투척. 인벤토리 UI 의 버리기도 같은 경로를 쓴다.</summary>
    public void ThrowToWorld(ItemDefinition definition, int quantity) =>
        ThrowToWorld(definition, quantity, -1, -1);

    public void ThrowToWorld(ItemDefinition definition, int quantity, int durability) =>
        ThrowToWorld(definition, quantity, durability, -1);

    /// <summary>내구도와 잔탄을 실어 던진다. -1 이면 최대치 (둘 다 개체 상태다).</summary>
    public void ThrowToWorld(ItemDefinition definition, int quantity, int durability, int loadedAmmo)
    {
        if (!definition || quantity <= 0) return;

        // 바닥에 내려놓는 게 아니라 실제로 던진다. 예전에는 전방 지점을 아래로 레이캐스트해서 잡았는데,
        // 먼저 떨어진 아이템의 윗면에 맞아서 같은 자리에 탑이 쌓였다.
        Vector3 origin = transform.position
                         + transform.forward * throwOriginForward
                         + Vector3.up * throwOriginHeight;
        Vector3 velocity = (transform.forward + Vector3.up * throwUpwardRatio).normalized * throwSpeed;

        WorldItem.Spawn(definition, quantity, origin, velocity).SetDurability(durability).SetAmmo(loadedAmmo);
    }
}
