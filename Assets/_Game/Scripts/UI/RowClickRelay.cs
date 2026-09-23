using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 목록 한 줄의 좌/우클릭을 구분해서 넘긴다.
/// uGUI Button 은 좌클릭만 받으므로, 우클릭 = 전량 획득 같은 조작에는 이게 필요하다.
/// </summary>
public class RowClickRelay : MonoBehaviour, IPointerClickHandler
{
    public Action OnLeft;
    public Action OnRight;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            if (OnRight != null) OnRight();
            return;
        }

        if (eventData.button == PointerEventData.InputButton.Left && OnLeft != null) OnLeft();
    }
}
