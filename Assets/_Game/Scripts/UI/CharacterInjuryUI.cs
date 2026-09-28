using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Tab 캐릭터 그림에 부위별 부상과 출혈을 표시한다.</summary>
public class CharacterInjuryUI : MonoBehaviour
{
    static readonly Color[] SeverityColors =
    {
        new Color32(0xD3, 0xD1, 0xC7, 0xFF),
        new Color32(0xEF, 0x9F, 0x27, 0xFF),
        new Color32(0xD8, 0x5A, 0x30, 0xFF),
        new Color32(0xA3, 0x2D, 0x2D, 0xFF)
    };

    [SerializeField] PlayerInjuries injuries;
    [SerializeField] GameObject panel;
    [SerializeField] Image[] bodyParts;
    [SerializeField] Outline[] bleedingOutlines;
    [SerializeField] Text summaryText;

    readonly StringBuilder summary = new StringBuilder();
    int lastVersion = -1;

    public void Bind(PlayerInjuries playerInjuries, GameObject panelObject, Image[] parts,
                     Outline[] outlines, Text summaryLabel)
    {
        injuries = playerInjuries;
        panel = panelObject;
        bodyParts = parts;
        bleedingOutlines = outlines;
        summaryText = summaryLabel;
        Refresh();
    }

    void Update()
    {
        if (!panel || !panel.activeSelf || !injuries || lastVersion == injuries.Version) return;
        Refresh();
    }

    void Refresh()
    {
        if (!injuries || bodyParts == null) return;

        lastVersion = injuries.Version;
        summary.Clear();
        for (int i = 0; i < bodyParts.Length && i < PlayerInjuries.PartCount; i++)
        {
            var part = (BodyPart)i;
            var severity = injuries.GetSeverity(part);
            if (bodyParts[i]) bodyParts[i].color = SeverityColors[(int)severity];
            if (bleedingOutlines != null && i < bleedingOutlines.Length && bleedingOutlines[i])
                bleedingOutlines[i].enabled = injuries.IsBleeding(part);

            if (severity == InjurySeverity.None) continue;
            if (summary.Length > 0) summary.Append(" · ");
            summary.Append(PlayerInjuries.PartName(part)).Append(' ').Append(PlayerInjuries.SeverityName(severity));
        }

        if (injuries.BleedingCount > 0)
        {
            if (summary.Length > 0) summary.Append(" · ");
            summary.Append("출혈 ").Append(injuries.BleedingCount);
        }

        if (summary.Length == 0) summary.Append("부상 없음");
        if (summaryText) summaryText.text = summary.ToString();
    }
}
