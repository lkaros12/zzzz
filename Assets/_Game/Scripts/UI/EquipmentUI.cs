using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Tab 캐릭터 그림 양옆의 장비 슬롯. 슬롯을 클릭하면 해제한다.</summary>
public class EquipmentUI : MonoBehaviour
{
    static readonly EquipmentSlot[] Slots =
    {
        EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Legs, EquipmentSlot.Feet,
        EquipmentSlot.Backpack, EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Misc
    };

    [SerializeField] PlayerEquipment equipment;
    [SerializeField] PlayerInjuries injuries;
    [Tooltip("인벤토리 패널. 열려 있을 때만 갱신한다.")]
    [SerializeField] GameObject panel;
    [SerializeField] Button[] slotButtons;
    [SerializeField] Text summaryText;

    readonly StringBuilder builder = new StringBuilder();
    bool wasOpen;
    bool listenersAdded;
    int lastVersion = -1;

    public void Bind(PlayerEquipment playerEquipment, PlayerInjuries playerInjuries, GameObject panelObject,
                     Button[] buttons, Text summary)
    {
        equipment = playerEquipment;
        injuries = playerInjuries;
        panel = panelObject;
        slotButtons = buttons;
        summaryText = summary;
        Rebuild();
    }

    void Start() => AddListeners();

    void AddListeners()
    {
        if (listenersAdded || slotButtons == null) return;
        listenersAdded = true;

        for (int i = 0; i < slotButtons.Length && i < Slots.Length; i++)
        {
            if (!slotButtons[i]) continue;
            var slot = Slots[i];
            slotButtons[i].onClick.AddListener(() => equipment.Unequip(slot));
        }
    }

    void Update()
    {
        bool open = panel && panel.activeSelf;
        if (!open)
        {
            wasOpen = false;
            return;
        }

        if (!equipment || (wasOpen && equipment.Version == lastVersion)) return;

        wasOpen = true;
        lastVersion = equipment.Version;
        Rebuild();
    }

    void Rebuild()
    {
        if (!equipment || slotButtons == null) return;

        for (int i = 0; i < slotButtons.Length && i < Slots.Length; i++)
        {
            var button = slotButtons[i];
            if (!button) continue;

            var stack = equipment.GetStack(Slots[i]);
            var image = button.GetComponent<Image>();
            if (image)
                image.color = stack != null && stack.definition
                    ? new Color(0.26f, 0.34f, 0.28f, 1f)
                    : new Color(0.18f, 0.19f, 0.22f, 1f);

            var label = button.GetComponentInChildren<Text>(true);
            if (label) label.text = BuildRowLabel(Slots[i], stack);
        }

        UpdateSummary();
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

    float MoveSpeedTotal() =>
        equipment.MoveSpeedMultiplier * (injuries ? injuries.MoveSpeedMultiplier : 1f);
}
