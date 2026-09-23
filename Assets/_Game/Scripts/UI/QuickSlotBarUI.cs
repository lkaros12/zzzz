using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GDD 5항 하단 중앙 퀵슬롯 바. 앞 두 칸은 주무기/보조무기, 뒤 세 칸은 소모품.
/// 인벤토리에서 아이템을 끌어다 놓으면 무기는 장착되고 소모품은 등록된다.
/// </summary>
public class QuickSlotBarUI : MonoBehaviour
{
    static readonly Color WeaponEmptyTint = new Color(0.18f, 0.16f, 0.12f, 0.80f);
    static readonly Color WeaponFilledTint = new Color(0.40f, 0.32f, 0.18f, 0.92f);
    static readonly Color ConsumableEmptyTint = new Color(0.10f, 0.11f, 0.14f, 0.75f);
    static readonly Color ConsumableFilledTint = new Color(0.20f, 0.23f, 0.28f, 0.90f);

    [SerializeField] PlayerQuickSlots quickSlots;
    [SerializeField] PlayerInventory inventory;
    [SerializeField] PlayerEquipment equipment;
    [SerializeField] RectTransform barRoot;
    [SerializeField] Sprite slotSprite;
    [SerializeField] float slotSize = 76f;
    [SerializeField] float slotGap = 8f;
    [Tooltip("무기 칸과 소모품 칸 사이 간격. 두 묶음을 눈으로 분리한다")]
    [SerializeField] float groupGap = 30f;
    [Tooltip("드롭 판정을 칸 바깥으로 넓히는 여유(px). 칸 사이 틈에 떨어뜨려도 받아준다")]
    [SerializeField] float dropPadding = 24f;

    readonly RectTransform[] slotRects = new RectTransform[PlayerQuickSlots.SlotCount];
    readonly Image[] slotImages = new Image[PlayerQuickSlots.SlotCount];
    readonly Image[] swatches = new Image[PlayerQuickSlots.SlotCount];
    readonly Text[] labels = new Text[PlayerQuickSlots.SlotCount];

    int lastVersion = -1;
    int lastInventoryVersion = -1;
    int lastEquipmentVersion = -1;

    public void Bind(PlayerQuickSlots slots, PlayerInventory playerInventory,
                     PlayerEquipment playerEquipment, RectTransform root, Sprite sprite)
    {
        quickSlots = slots;
        inventory = playerInventory;
        equipment = playerEquipment;
        barRoot = root;
        slotSprite = sprite;
    }

    void Awake()
    {
        BuildSlots();
        Refresh();
    }

    void Update()
    {
        if (!quickSlots || !inventory) return;

        int equipVersion = equipment ? equipment.Version : 0;
        if (quickSlots.Version == lastVersion
            && inventory.Version == lastInventoryVersion
            && equipVersion == lastEquipmentVersion) return;

        lastVersion = quickSlots.Version;
        lastInventoryVersion = inventory.Version;
        lastEquipmentVersion = equipVersion;
        Refresh();
    }

    void BuildSlots()
    {
        if (!barRoot) return;

        float step = slotSize + slotGap;
        float totalWidth = step * PlayerQuickSlots.SlotCount - slotGap + groupGap;
        float startX = -totalWidth * 0.5f + slotSize * 0.5f;

        for (int i = 0; i < PlayerQuickSlots.SlotCount; i++)
        {
            // 무기 묶음이 끝나는 지점에서 한 번 더 벌린다.
            float extra = i >= PlayerQuickSlots.WeaponSlotCount ? groupGap : 0f;

            var go = new GameObject("QuickSlot_" + (i + 1), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(barRoot, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(startX + step * i + extra, 0f);
            rect.sizeDelta = new Vector2(slotSize, slotSize);

            var image = go.GetComponent<Image>();
            image.sprite = slotSprite;
            image.type = Image.Type.Sliced;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            int captured = i;
            button.onClick.AddListener(() => OnSlotClicked(captured));

            slotRects[i] = rect;
            slotImages[i] = image;
            swatches[i] = CreateSwatch(rect);
            labels[i] = CreateLabel(rect, i);
        }
    }

    /// <summary>무기 칸 클릭은 해제, 소모품 칸 클릭은 사용.</summary>
    void OnSlotClicked(int index)
    {
        if (PlayerQuickSlots.IsWeaponSlot(index)) quickSlots.Clear(index);
        else quickSlots.TryUseSlot(index);
    }

    Image CreateSwatch(RectTransform parent)
    {
        var go = new GameObject("Swatch", typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -8f);
        rect.sizeDelta = new Vector2(32f, 32f);

        var image = go.GetComponent<Image>();
        image.sprite = slotSprite;
        image.type = Image.Type.Sliced;
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    Text CreateLabel(RectTransform parent, int index)
    {
        var go = new GameObject("Label", typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(3f, 3f);
        rect.offsetMax = new Vector2(-3f, -3f);

        var text = go.GetComponent<Text>();
        text.font = KoreanFont.Get();
        text.fontSize = 12;
        text.alignment = TextAnchor.LowerCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        text.text = PlayerQuickSlots.SlotName(index);
        return text;
    }

    void Refresh()
    {
        if (!quickSlots) return;

        for (int i = 0; i < PlayerQuickSlots.SlotCount; i++)
        {
            bool weaponSlot = PlayerQuickSlots.IsWeaponSlot(i);
            var stack = quickSlots.GetDisplayedStack(i);
            var definition = quickSlots.GetDisplayed(i);

            if (slotImages[i])
                slotImages[i].color = definition
                    ? (weaponSlot ? WeaponFilledTint : ConsumableFilledTint)
                    : (weaponSlot ? WeaponEmptyTint : ConsumableEmptyTint);

            if (swatches[i])
            {
                swatches[i].enabled = definition;
                if (definition) swatches[i].color = definition.placeholderColor;
            }

            if (labels[i]) labels[i].text = BuildSlotLabel(i, definition, stack);
        }
    }

    string BuildSlotLabel(int index, ItemDefinition definition, ItemStack stack)
    {
        if (!definition) return PlayerQuickSlots.SlotName(index);

        string label = PlayerQuickSlots.SlotName(index) + "\n" + definition.displayName;

        if (PlayerQuickSlots.IsWeaponSlot(index))
        {
            if (stack != null && stack.HasDurability)
                label += stack.IsBroken ? "\n[파손]" : "\n" + stack.durability;

            return label;
        }

        int count = quickSlots.CountOf(index);
        return count > 1 ? label + "\nx" + count : label;
    }

    /// <summary>
    /// 화면 좌표가 어느 슬롯 위인지. 인벤토리에서 끌어다 놓을 때 InventoryUI 가 물어본다.
    ///
    /// 보이는 칸보다 dropPadding 만큼 넓게 받는다 — 칸 사이 틈이나 테두리 바로 밖에 놓았을 때
    /// 등록 대신 바닥에 던져지는 일이 있었다. 넓힌 영역이 겹치면 중심이 가까운 칸을 고른다.
    /// 어느 칸에도 안 걸리면 -1.
    /// </summary>
    public int SlotIndexAt(Vector2 screenPoint, Camera eventCamera)
    {
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < slotRects.Length; i++)
        {
            if (!slotRects[i]) continue;

            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(slotRects[i], screenPoint, eventCamera, out local))
                continue;

            var padded = slotRects[i].rect;
            padded.min -= new Vector2(dropPadding, dropPadding);
            padded.max += new Vector2(dropPadding, dropPadding);
            if (!padded.Contains(local)) continue;

            float distance = (local - slotRects[i].rect.center).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }

        return best;
    }
}
