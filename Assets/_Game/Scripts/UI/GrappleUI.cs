using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 저항 게이지. 좀비에게 붙잡혔을 때만 나타난다.
/// 게이지가 낮을수록 붉어져서 위험을 바로 읽을 수 있게 한다.
/// </summary>
public class GrappleUI : MonoBehaviour
{
    [SerializeField] PlayerGrapple grapple;
    [SerializeField] GameObject panel;
    [SerializeField] Image fill;
    [SerializeField] Text label;

    [SerializeField] Color safeColor = new Color(0.35f, 0.80f, 0.45f);
    [SerializeField] Color dangerColor = new Color(0.90f, 0.25f, 0.20f);

    public void Bind(PlayerGrapple playerGrapple, GameObject panelObject, Image fillImage, Text text)
    {
        grapple = playerGrapple;
        panel = panelObject;
        fill = fillImage;
        label = text;
    }

    void Awake()
    {
        if (panel) panel.SetActive(false);
    }

    void Update()
    {
        if (!grapple || !panel) return;

        bool grappled = grapple.IsGrappled;
        if (panel.activeSelf != grappled) panel.SetActive(grappled);
        if (!grappled) return;

        float progress = Mathf.Clamp01(grapple.Progress);
        if (fill)
        {
            fill.fillAmount = progress;
            fill.color = Color.Lerp(dangerColor, safeColor, progress);
        }

        if (label) label.text = "물렸다! 좌클릭 연타로 저항";
    }
}
