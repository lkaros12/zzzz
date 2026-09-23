using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// GDD 11항 칸 기반 인벤토리 화면 + GDD 12항 "영역 밖으로 드래그하면 버리기".
/// 여는 키는 GDD 21항의 Tab. 4단계에서 이 패널에 "주변 아이템" 영역을 붙이면 GDD 13항 화면이 된다.
/// GDD 23항대로 배치 판정은 전부 PlayerInventory 가 하고 여기서는 그리기/입력만 담당한다.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    static readonly Color NormalTint = new Color(0.30f, 0.34f, 0.40f, 0.95f);
    static readonly Color DragTint = new Color(0.45f, 0.62f, 0.85f, 0.85f);
    static readonly Color HoverTint = new Color(0.40f, 0.48f, 0.56f, 0.95f);

    [SerializeField] PlayerInventory inventory;
    [SerializeField] PlayerInteractor interactor;
    [SerializeField] GameObject panel;
    [SerializeField] RectTransform gridRoot;
    [SerializeField] QuantityDialog quantityDialog;
    [SerializeField] Button sortButton;
    [SerializeField] Button useButton;
    [SerializeField] PlayerConsumer consumer;
    [SerializeField] PlayerEquipment equipment;
    [SerializeField] PlayerQuickSlots quickSlots;
    [SerializeField] QuickSlotBarUI quickSlotBar;
    [SerializeField] PlayerCrafting crafting;
    [SerializeField] NearbyItemsUI nearbyList;
    [SerializeField] Text hintText;
    [SerializeField] Sprite cellSprite;
    [SerializeField] float cellSize = 56f;

    readonly List<InventoryItemView> views = new List<InventoryItemView>();

    int lastVersion = -1;
    InventoryItemView dragging;
    InventoryItemView hovered;
    RectTransform draggingRect;
    Vector2 dragOffset;

    public void Bind(PlayerInventory playerInventory, PlayerInteractor playerInteractor, GameObject panelObject,
                     RectTransform grid, QuantityDialog dialog, Button sort, Button use,
                     PlayerConsumer playerConsumer, PlayerEquipment playerEquipment,
                     PlayerQuickSlots slots, QuickSlotBarUI bar,
                     Text hint, Sprite sprite, float cell)
    {
        useButton = use;
        consumer = playerConsumer;
        equipment = playerEquipment;
        quickSlots = slots;
        quickSlotBar = bar;
        if (playerEquipment) crafting = playerEquipment.GetComponent<PlayerCrafting>();
        if (!nearbyList) nearbyList = GetComponent<NearbyItemsUI>();
        inventory = playerInventory;
        interactor = playerInteractor;
        panel = panelObject;
        gridRoot = grid;
        quantityDialog = dialog;
        sortButton = sort;
        hintText = hint;
        cellSprite = sprite;
        cellSize = cell;
    }

    void Awake()
    {
        // Bind 는 빌더가 씬을 만들 때 한 번만 돈다. 그 뒤에 추가한 참조는 씬에 비어 있으므로
        // 런타임에 스스로 찾는다 — 안 그러면 합성/컨테이너 넣기가 조용히 아무 일도 안 한다.
        if (!crafting && interactor) crafting = interactor.GetComponent<PlayerCrafting>();
        if (!nearbyList) nearbyList = GetComponent<NearbyItemsUI>();

        if (sortButton) sortButton.onClick.AddListener(OnSortClicked);
        if (useButton) useButton.onClick.AddListener(OnUseClicked);
        BuildCellBackgrounds();
        if (panel) panel.SetActive(false);
    }

    /// <summary>패널이 열려 있는지. 카메라 줌 같은 월드 입력을 막는 데 쓴다.</summary>
    public bool IsOpen => panel && panel.activeSelf;

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.tabKey.wasPressedThisFrame) Toggle();
            else if (keyboard.escapeKey.wasPressedThisFrame) CloseTopmost();
            // E 로 사용하는 것은 인벤토리 화면이 열려 있을 때만. 인게임에서 E 는 줍기 전용이다.
            else if (keyboard.eKey.wasPressedThisFrame && panel && panel.activeSelf) OnUseClicked();
            // Q 도 화면이 열려 있을 때만. 인게임 Q 는 퀵슬롯 투척이라 서로 안 겹친다.
            else if (keyboard.qKey.wasPressedThisFrame && panel && panel.activeSelf) OnDropHovered();
        }

        if (!panel || !panel.activeSelf) return;

        if (inventory.Version != lastVersion) Rebuild();
    }

    void Toggle()
    {
        if (!panel) return;

        bool opening = !panel.activeSelf;
        panel.SetActive(opening);

        if (opening) ForceRefresh();
        else if (quantityDialog) quantityDialog.Close();
    }

    /// <summary>
    /// Esc 는 열려 있는 창을 위에서부터 하나 닫는다. 닫을 것이 있었으면 true.
    /// GDD 21항의 Esc(메뉴/일시정지)를 넣을 때 아무것도 안 열려 있을 때만 메뉴로 넘기면 된다.
    /// </summary>
    bool CloseTopmost()
    {
        if (quantityDialog && quantityDialog.IsOpen)
        {
            quantityDialog.Close();
            return true;
        }

        if (panel && panel.activeSelf)
        {
            Toggle();
            return true;
        }

        return false;
    }

    /// <summary>다음 Update 에서 데이터로부터 화면을 다시 만든다. 이동 실패 시 원위치 복귀도 이걸로 처리.</summary>
    void ForceRefresh() => lastVersion = -1;

    // ---- 그리기 ----

    void BuildCellBackgrounds()
    {
        if (!gridRoot) return;

        for (int y = 0; y < inventory.Height; y++)
        for (int x = 0; x < inventory.Width; x++)
        {
            var cell = new GameObject("Cell_" + x + "_" + y, typeof(Image));
            var rect = (RectTransform)cell.transform;
            rect.SetParent(gridRoot, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = CellToLocal(new Vector2Int(x, y));
            rect.sizeDelta = new Vector2(cellSize - 2f, cellSize - 2f);

            var image = cell.GetComponent<Image>();
            image.sprite = cellSprite;
            image.color = new Color(1f, 1f, 1f, 0.07f);
            image.raycastTarget = false;
        }
    }

    void Rebuild()
    {
        lastVersion = inventory.Version;

        hovered = null;

        for (int i = 0; i < views.Count; i++)
            if (views[i]) Destroy(views[i].gameObject);
        views.Clear();

        foreach (var stack in inventory.Stacks) views.Add(CreateView(stack));
    }

    InventoryItemView CreateView(ItemStack stack)
    {
        var go = new GameObject("Item_" + stack.definition.id, typeof(Image), typeof(InventoryItemView));
        var rect = (RectTransform)go.transform;
        rect.SetParent(gridRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = CellToLocal(stack.origin);
        rect.sizeDelta = new Vector2(stack.Size.x * cellSize - 4f, stack.Size.y * cellSize - 4f);

        var image = go.GetComponent<Image>();
        image.sprite = cellSprite;
        image.color = NormalTint;

        var label = CreateLabel(rect, stack);

        var view = go.GetComponent<InventoryItemView>();
        view.Initialize(this, stack, image, label);
        view.SetLabel(BuildLabel(stack));
        return view;
    }

    Text CreateLabel(RectTransform parent, ItemStack stack)
    {
        var go = new GameObject("Label", typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(4f, 4f);
        rect.offsetMax = new Vector2(-4f, -4f);

        var text = go.GetComponent<Text>();
        text.font = KoreanFont.Get();
        text.fontSize = 14;
        text.alignment = TextAnchor.UpperLeft;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    static string BuildLabel(ItemStack stack)
    {
        string label = stack.definition.displayName;
        if (stack.quantity > 1) label += "\nx" + stack.quantity;

        // GDD 11항 내구도. 파손된 무기는 바로 알아볼 수 있어야 한다.
        if (stack.HasDurability)
            label += stack.IsBroken ? "\n[파손]" : "\n" + stack.durability + "/" + stack.definition.maxDurability;

        return label;
    }

    Vector2 CellToLocal(Vector2Int cell) => new Vector2(cell.x * cellSize, -cell.y * cellSize);

    // ---- 드래그 ----

    public void BeginDragItem(InventoryItemView view, PointerEventData eventData)
    {
        if (quantityDialog && quantityDialog.IsOpen) return;

        dragging = view;
        draggingRect = (RectTransform)view.transform;
        draggingRect.SetAsLastSibling();
        view.SetTint(DragTint);

        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                gridRoot, eventData.position, eventData.pressEventCamera, out local))
            dragOffset = local - draggingRect.anchoredPosition;
        else
            dragOffset = Vector2.zero;
    }

    public void DragItem(InventoryItemView view, PointerEventData eventData)
    {
        if (dragging != view) return;

        Vector2 local;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                gridRoot, eventData.position, eventData.pressEventCamera, out local))
            draggingRect.anchoredPosition = local - dragOffset;
    }

    public void EndDragItem(InventoryItemView view, PointerEventData eventData)
    {
        if (dragging != view) return;

        dragging = null;
        view.SetTint(view == hovered ? HoverTint : NormalTint);
        if (hintText) hintText.text = string.Empty;

        // 퀵슬롯 바 위에 놓으면 버리지 않고 무기 장착 / 소모품 등록.
        if (quickSlotBar && quickSlots && view.Stack != null)
        {
            int slot = quickSlotBar.SlotIndexAt(eventData.position, eventData.pressEventCamera);
            if (slot >= 0)
            {
                var definition = view.Stack.definition;
                bool ok = quickSlots.TryAssign(slot, view.Stack);

                if (hintText)
                    hintText.text = ok
                        ? PlayerQuickSlots.SlotName(slot) + " 칸: " + definition.displayName
                        : definition.displayName + " 은 " + PlayerQuickSlots.SlotName(slot) + " 칸에 넣을 수 없습니다";

                ForceRefresh();
                return;
            }
        }

        // GDD 24항: 컨테이너 목록 위에 놓으면 그 안에 넣는다. 반대 방향(꺼내기) 은 목록 클릭.
        if (nearbyList && interactor && view.Stack != null &&
            nearbyList.ContainsScreenPoint(eventData.position, eventData.pressEventCamera))
        {
            var container = interactor.ViewContainer;
            if (container)
            {
                var stack = view.Stack;
                var definition = stack.definition;
                int amount = stack.quantity;
                int moved = container.PutFrom(stack, inventory, amount);

                if (hintText)
                    hintText.text = moved > 0
                        ? container.DisplayName + " 에 " + definition.displayName + " " + moved + "개 넣음"
                        : definition.displayName + " 을 넣지 못했습니다";

                ForceRefresh();
                return;
            }

            if (hintText) hintText.text = "가까운 컨테이너가 없습니다";
            ForceRefresh();
            return;
        }

        // GDD 12항: 인벤토리 영역 밖으로 드래그하면 버리기.
        bool insidePanel = RectTransformUtility.RectangleContainsScreenPoint(
            (RectTransform)panel.transform, eventData.position, eventData.pressEventCamera);

        if (!insidePanel)
        {
            RequestDrop(view);
            return;
        }

        // 블록의 좌상단이 놓인 칸을 기준으로 판정한다. 커서 위치보다 이쪽이 직관적이다.
        var target = new Vector2Int(
            Mathf.RoundToInt(draggingRect.anchoredPosition.x / cellSize),
            Mathf.RoundToInt(-draggingRect.anchoredPosition.y / cellSize));

        var occupant = inventory.GetStackAt(target);

        // GDD 12·13항: 재료를 무기 위에 놓으면 합성 또는 내구도 회복.
        // 합치기/이동보다 먼저 본다 — 같은 조작에서 합성이 의도인 경우가 더 구체적이다.
        if (crafting && occupant != null && crafting.TryApply(occupant, view.Stack))
        {
            if (hintText) hintText.text = crafting.LastMessage;
            ForceRefresh();
            return;
        }

        bool merged = occupant != null && occupant != view.Stack &&
                      inventory.TryMerge(view.Stack, occupant);

        if (!merged && !inventory.TryMove(view.Stack, target) && hintText)
            hintText.text = "그 자리에는 놓을 수 없습니다";

        ForceRefresh();
    }

    // ---- 버리기 / 분할 / 정렬 ----

    void RequestDrop(InventoryItemView view)
    {
        var stack = view.Stack;
        if (stack == null) { ForceRefresh(); return; }

        // GDD 12항: 수량 1 이면 즉시 투척, 2 이상이면 수량 선택창.
        if (stack.quantity <= 1)
        {
            DropToWorld(stack, 1);
            return;
        }

        if (!quantityDialog) { DropToWorld(stack, stack.quantity); return; }

        quantityDialog.Open("버릴 수량", stack.quantity, amount => DropToWorld(stack, amount));
        ForceRefresh();
    }

    void DropToWorld(ItemStack stack, int amount)
    {
        var definition = stack.definition;

        // 내구도/잔탄을 먼저 챙긴다. TakeFrom 이 스택을 리스트에서 지울 수 있고,
        // 안 챙기면 버렸다 줍는 것만으로 무기가 새것이 된다.
        int durability = stack.durability;
        int ammo = stack.loadedAmmo;

        int taken = inventory.TakeFrom(stack, amount);
        if (taken > 0 && interactor) interactor.ThrowToWorld(definition, taken, durability, ammo);

        ForceRefresh();
    }

    public void RequestSplit(InventoryItemView view)
    {
        var stack = view.Stack;
        if (stack == null || stack.quantity < 2 || !quantityDialog) return;

        // 전부 옮기면 분할이 아니므로 최대치는 quantity - 1.
        quantityDialog.Open("분할 수량", stack.quantity - 1, amount =>
        {
            if (inventory.Split(stack, amount) == null && hintText)
                hintText.text = "분할한 묶음을 놓을 빈 칸이 없습니다";

            ForceRefresh();
        });
    }

    /// <summary>
    /// GDD 11항 "사용" / GDD 10항 "장착". 더블클릭 · 사용 버튼 · E 가 모두 이 경로를 쓴다.
    /// 장비면 장착, 아니면 소비한다.
    /// </summary>
    void UseOrEquip(ItemStack stack)
    {
        if (BlockedByAssassination) return;

        if (stack == null || !stack.definition)
        {
            if (hintText) hintText.text = "선택된 아이템이 없습니다";
            return;
        }

        var definition = stack.definition;

        if (definition.equipSlot != EquipmentSlot.None)
        {
            if (hintText)
                hintText.text = equipment && equipment.TryEquip(stack)
                    ? definition.displayName + " 장착"
                    : definition.displayName + " 을 장착할 수 없습니다";
            else if (equipment)
                equipment.TryEquip(stack);
        }
        else if (consumer)
        {
            consumer.TryUse(stack);
            if (hintText) hintText.text = consumer.LastMessage;
        }

        ForceRefresh();
    }

    public void UseStack(InventoryItemView view) => UseOrEquip(view != null ? view.Stack : null);

    /// <summary>
    /// 커서가 올라간 아이템이 있으면 그것을 쓰고, 없으면 선택된 퀵슬롯 아이템을 쓴다.
    /// 클릭해서 선택하지 않아도 E 로 바로 쓸 수 있게 하는 경로.
    /// </summary>
    void OnUseClicked() => UseOrEquip(hovered && hovered.Stack != null ? hovered.Stack : null);

    /// <summary>GDD 19항: 암살 모션 중에는 장착/사용/버리기를 모두 막는다.</summary>
    bool BlockedByAssassination
    {
        get
        {
            var assassinate = interactor ? interactor.GetComponent<PlayerAssassinate>() : null;
            if (!assassinate || !assassinate.IsAssassinating) return false;

            if (hintText) hintText.text = "암살 중에는 조작할 수 없습니다";
            return true;
        }
    }

    /// <summary>
    /// GDD 8항 버리기. 인벤토리에서 커서가 가리키는 아이템을 Q 로 버린다.
    /// 화면 밖으로 드래그하는 것과 같은 경로라 수량 선택창도 그대로 뜬다.
    /// </summary>
    void OnDropHovered()
    {
        if (BlockedByAssassination) return;
        if (quantityDialog && quantityDialog.IsOpen) return;
        if (!hovered || hovered.Stack == null)
        {
            if (hintText) hintText.text = "버릴 아이템에 커서를 올려주세요";
            return;
        }

        RequestDrop(hovered);
    }

    public void SetHovered(InventoryItemView view)
    {
        if (hovered && hovered != view) hovered.SetTint(NormalTint);

        hovered = view;
        if (view) view.SetTint(HoverTint);
        if (hintText && view && view.Stack != null) hintText.text = HoverHint(view.Stack);
    }

    /// <summary>
    /// 커서가 빠져도 hovered 참조는 유지한다 — "사용/장착" 버튼과 E 가 마지막으로 가리킨 아이템을
    /// 대상으로 삼을 수 있어야 한다. 실제 해제는 Rebuild 에서 한다.
    /// </summary>
    public void ClearHovered(InventoryItemView view)
    {
        if (hovered != view) return;
        if (hintText) hintText.text = string.Empty;
    }

    static string HoverHint(ItemStack stack)
    {
        var definition = stack.definition;
        if (!definition) return string.Empty;

        if (definition.equipSlot != EquipmentSlot.None) return "[우클릭] " + definition.displayName + " 장착";
        if (definition.usable) return "[우클릭] " + definition.displayName + " 사용";

        return definition.displayName;
    }

    void OnSortClicked()
    {
        inventory.AutoSort();
        ForceRefresh();
    }
}
