using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GDD 12항 수량 선택창. 슬라이더 + −/+ 버튼.
/// 레거시 InputField 는 신규 Input System 에서 불안정하고 TMP 폰트 에셋도 없어 숫자 입력은 버튼으로 대체.
/// </summary>
public class QuantityDialog : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] Text titleText;
    [SerializeField] Text valueText;
    [SerializeField] Slider slider;
    [SerializeField] Button minusButton;
    [SerializeField] Button plusButton;
    [SerializeField] Button confirmButton;
    [SerializeField] Button cancelButton;

    Action<int> onConfirm;

    public bool IsOpen => panel && panel.activeSelf;

    public void Bind(GameObject panelObject, Text title, Text value, Slider valueSlider,
                     Button minus, Button plus, Button confirm, Button cancel)
    {
        panel = panelObject;
        titleText = title;
        valueText = value;
        slider = valueSlider;
        minusButton = minus;
        plusButton = plus;
        confirmButton = confirm;
        cancelButton = cancel;
    }

    void Awake()
    {
        // 에디터에서 persistent listener 를 직렬화하는 대신 런타임에 연결한다.
        if (slider) slider.onValueChanged.AddListener(OnSliderChanged);
        if (minusButton) minusButton.onClick.AddListener(() => Step(-1));
        if (plusButton) plusButton.onClick.AddListener(() => Step(1));
        if (confirmButton) confirmButton.onClick.AddListener(Confirm);
        if (cancelButton) cancelButton.onClick.AddListener(Close);

        Close();
    }

    public void Open(string title, int maxAmount, Action<int> confirm)
    {
        if (maxAmount < 1 || !panel) return;

        onConfirm = confirm;
        if (titleText) titleText.text = title;

        slider.wholeNumbers = true;
        slider.minValue = 1;
        slider.maxValue = maxAmount;
        slider.value = maxAmount;

        panel.SetActive(true);
        UpdateValueText();
    }

    public void Step(int delta)
    {
        if (!slider) return;
        slider.value = Mathf.Clamp(slider.value + delta, slider.minValue, slider.maxValue);
    }

    public void Confirm()
    {
        int amount = slider ? Mathf.RoundToInt(slider.value) : 1;

        // 콜백이 또 창을 열 수 있으므로 닫기 전에 들고 있는다.
        var callback = onConfirm;
        Close();
        if (callback != null) callback(amount);
    }

    public void Close()
    {
        onConfirm = null;
        if (panel) panel.SetActive(false);
    }

    void OnSliderChanged(float _) => UpdateValueText();

    void UpdateValueText()
    {
        if (valueText && slider) valueText.text = Mathf.RoundToInt(slider.value).ToString();
    }
}
