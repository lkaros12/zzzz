using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 인벤토리 격자 위의 아이템 블록 하나. 입력 판단은 전부 InventoryUI 로 넘긴다.
/// </summary>
public class InventoryItemView : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    public ItemStack Stack { get; private set; }

    InventoryUI owner;
    Image background;
    Text label;

    public void Initialize(InventoryUI inventoryUI, ItemStack stack, Image image, Text text)
    {
        owner = inventoryUI;
        Stack = stack;
        background = image;
        label = text;
    }

    public void SetTint(Color color)
    {
        if (background) background.color = color;
    }

    public void SetLabel(string text)
    {
        if (label) label.text = text;
    }

    public void OnBeginDrag(PointerEventData eventData) => owner.BeginDragItem(this, eventData);
    public void OnDrag(PointerEventData eventData) => owner.DragItem(this, eventData);
    public void OnEndDrag(PointerEventData eventData) => owner.EndDragItem(this, eventData);

    public void OnPointerEnter(PointerEventData eventData) => owner.SetHovered(this);
    public void OnPointerExit(PointerEventData eventData) => owner.ClearHovered(this);

    public void OnPointerClick(PointerEventData eventData)
    {
        // 우클릭 = 즉시 착용/사용. 스택 분할은 Shift+우클릭으로 옮겼다.
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            var keyboard = Keyboard.current;
            bool shift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

            if (shift) owner.RequestSplit(this);
            else owner.UseStack(this);
            return;
        }

        // 드래그로 끝난 클릭은 이동이므로 무시한다.
        // 퀵슬롯 선택은 바에서 하고(1~5 키 또는 슬롯 클릭), 여기서는 더블클릭 사용만 받는다.
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging) return;
        if (eventData.clickCount >= 2) owner.UseStack(this);
    }
}
