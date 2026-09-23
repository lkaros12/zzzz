using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// GDD 4항 Z "캐릭터 상태". 생존 수치와 부위별 부상, 장비 보정을 한 화면에 모아 보여준다.
/// 읽기 전용이다 — GDD 23항대로 로직은 건드리지 않는다.
/// </summary>
public class StatusPanelUI : MonoBehaviour
{
    [SerializeField] PlayerStats stats;
    [SerializeField] PlayerInjuries injuries;
    [SerializeField] PlayerEquipment equipment;
    [SerializeField] GameObject panel;
    [SerializeField] Text body;

    readonly StringBuilder builder = new StringBuilder();

    public bool IsOpen => panel && panel.activeSelf;

    public void Bind(PlayerStats playerStats, PlayerInjuries playerInjuries, PlayerEquipment playerEquipment,
                     GameObject panelObject, Text bodyText)
    {
        stats = playerStats;
        injuries = playerInjuries;
        equipment = playerEquipment;
        panel = panelObject;
        body = bodyText;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.zKey.wasPressedThisFrame) Toggle();
        else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame) Close();

        if (IsOpen) Refresh();
    }

    public void Toggle()
    {
        if (!panel) return;

        panel.SetActive(!panel.activeSelf);
        if (panel.activeSelf) Refresh();
    }

    public void Close() { if (panel) panel.SetActive(false); }

    void Refresh()
    {
        if (!body || !stats) return;

        builder.Length = 0;
        builder.Append("[ 캐릭터 상태 ]   Z 또는 Esc 로 닫기\n\n");
        builder.Append("체력      ").Append(Bar(stats.Hp, stats.MaxHp)).Append('\n');
        builder.Append("스태미나  ").Append(Bar(stats.Stamina, stats.MaxStamina)).Append('\n');
        builder.Append("허기      ").Append(Bar(stats.Hunger, 100f)).Append('\n');
        builder.Append("탈수      ").Append(Bar(stats.Thirst, 100f)).Append('\n');
        builder.Append("피곤함    ").Append(Bar(stats.Fatigue, 100f)).Append("\n\n");

        if (injuries)
        {
            builder.Append("[ 부상 ]\n");
            for (int i = 0; i < PlayerInjuries.PartCount; i++)
            {
                var part = (BodyPart)i;
                var severity = injuries.GetSeverity(part);
                builder.Append(PlayerInjuries.PartName(part).PadRight(6))
                       .Append(PlayerInjuries.SeverityName(severity)).Append('\n');
            }
            builder.Append('\n');
        }

        if (equipment)
        {
            builder.Append("[ 장비 보정 ]\n");
            builder.Append("이동속도 x").Append(equipment.MoveSpeedMultiplier.ToString("0.00")).Append('\n');
            builder.Append("스태미나 x").Append(equipment.StaminaCostMultiplier.ToString("0.00")).Append('\n');
            builder.Append("부상확률 x").Append(equipment.InjuryChanceMultiplier.ToString("0.00"));
        }

        body.text = builder.ToString();
    }

    /// <summary>글자로 만든 게이지. 스프라이트 바를 또 만들 이유가 없다.</summary>
    static string Bar(float value, float max)
    {
        if (max <= 0f) return "-";

        int filled = Mathf.Clamp(Mathf.RoundToInt(value / max * 20f), 0, 20);
        return new string('|', filled) + new string('.', 20 - filled)
               + "  " + Mathf.RoundToInt(value) + " / " + Mathf.RoundToInt(max);
    }
}
