using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GDD 10항 장비 슬롯 목록. 슬롯을 클릭하면 해제한다.
/// 장착은 인벤토리 격자에서 장비 아이템을 더블클릭한다 (소비 아이템과 같은 경로).
/// </summary>
public class EquipmentUI : MonoBehaviour
{
    // 주무기/보조무기는 하단 퀵슬롯 바가 담당하므로 여기서는 방어구 계열만 보여준다.
    static readonly EquipmentSlot[] Slots =
    {
        EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Legs, EquipmentSlot.Feet,
        EquipmentSlot.Backpack, EquipmentSlot.Misc
    };

    [SerializeField] PlayerEquipment equipment;
    [SerializeField] PlayerInjuries injuries;
    [Tooltip("인벤토리 패널. 열려 있을 때만 갱신한다.")]
    [SerializeField] GameObject panel;
    [SerializeField] RectTransform listRoot;
    [SerializeField] Text summaryText;
    [SerializeField] Sprite rowSprite;
    [SerializeField] float rowHeight = 34f;

    readonly List<GameObject> rows = new List<GameObject>();
    readonly StringBuilder builder = new StringBuilder();

    bool wasOpen;
    int lastVersion = -1;

    public void Bind(PlayerEquipment playerEquipment, PlayerInjuries playerInjuries, GameObject panelObject,
                     RectTransform root, Text summary, Sprite sprite)
    {
        equipment = playerEquipment;
        injuries = playerInjuries;
        panel = panelObject;
        listRoot = root;
        summaryText = summary;
        rowSprite = sprite;
    }

    void Update()
    {
        bool open = panel && panel.activeSelf;
        if (!open)
        {
            wasOpen = false;
            return;
        }

        if (wasOpen && equipment.Version == lastVersion) return;

        wasOpen = true;
        lastVersion = equipment.Version;
        Rebuild();
    }

    void Rebuild()
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i]) Destroy(rows[i]);
        rows.Clear();

        for (int i = 0; i < Slots.Length; i++) rows.Add(CreateRow(Slots[i], i));

        UpdateSummary();
    }

    GameObject CreateRow(EquipmentSlot slot, int index)
    {
        var definition = equipment.Get(slot);

        var go = new GameObject("EquipRow_" + slot, typeof(Image), typeof(Button));
        var rect = (RectTransform)go.transform;
        rect.SetParent(listRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(0f, -index * rowHeight);
        rect.sizeDelta = new Vector2(listRoot.rect.width, rowHeight - 4f);

        var image = go.GetComponent<Image>();
        image.sprite = rowSprite;
        image.type = Image.Type.Sliced;
        image.color = definition
            ? new Color(0.26f, 0.34f, 0.28f, 1f)
            : new Color(0.18f, 0.19f, 0.22f, 1f);

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        var capturedSlot = slot;
        button.onClick.AddListener(() => equipment.Unequip(capturedSlot));

        CreateRowLabel(rect, BuildRowLabel(slot, equipment.GetStack(slot)));
        return go;
    }

    /// <summary>GDD 11항 내구도와 GDD 10항 한손/양손을 슬롯 줄에 같이 보여준다.</summary>
    static string BuildRowLabel(EquipmentSlot slot, ItemStack stack)
    {
        string prefix = PlayerEquipment.SlotName(slot) + " : ";
        if (stack == null || !stack.definition) return prefix + "-";

        string label = prefix + stack.definition.displayName;

        if (stack.definition.handedness == WeaponHandedness.TwoHanded) label += " (양손)";
        else if (stack.definition.handedness == WeaponHandedness.OneHanded) label += " (한손)";

        if (stack.HasDurability)
            label += stack.IsBroken ? "  [파손]" : "  " + stack.durability + "/" + stack.definition.maxDurability;

        return label;
    }

    void CreateRowLabel(RectTransform parent, string content)
    {
        var go = new GameObject("Label", typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(10f, 2f);
        rect.offsetMax = new Vector2(-10f, -2f);

        var text = go.GetComponent<Text>();
        text.font = KoreanFont.Get();
        text.fontSize = 15;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = content;
    }

    /// <summary>GDD 6항 부위별 방어력 + GDD 10항 보정치를 그대로 보여준다.</summary>
    void UpdateSummary()
    {
        if (!summaryText) return;

        builder.Clear();
        builder.Append("방어  머리 ").Append(equipment.GetDefense(BodyPart.Head).ToString("0.#"))
               .Append(" / 몸통 ").Append(equipment.GetDefense(BodyPart.Torso).ToString("0.#"))
               .Append(" / 다리 ").Append(equipment.GetDefense(BodyPart.LeftLeg).ToString("0.#"))
               .Append('\n')
               .Append("이동 ").Append(MoveSpeedTotal().ToString("0.00")).Append("배")
               .Append("   스태미나 ").Append(equipment.StaminaCostMultiplier.ToString("0.00")).Append("배")
               .Append("   부상확률 ").Append(equipment.InjuryChanceMultiplier.ToString("0.00")).Append("배");

        summaryText.text = builder.ToString();
    }

    /// <summary>장비 보정과 다리 부상 패널티를 합친 실제 이동 배율.</summary>
    float MoveSpeedTotal() =>
        equipment.MoveSpeedMultiplier * (injuries ? injuries.MoveSpeedMultiplier : 1f);
}
