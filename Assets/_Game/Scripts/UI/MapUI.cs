using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// GDD 4항 M "전체 지도" + GDD 25항 "기본 화면에는 미니맵을 표시하지 않는다".
///
/// 월드 XZ 좌표를 패널 좌표로 눌러 담아 점으로 그린다. 실제 지형 렌더링이 아니라
/// 위치 파악용 개요도다 — 타일맵/월드 텍스처가 생기면 배경만 교체하면 된다.
/// </summary>
public class MapUI : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] RectTransform mapRoot;
    [SerializeField] Sprite dotSprite;
    [SerializeField] Text legend;

    [Tooltip("지도가 담는 월드 반경(m). 이 안의 대상만 표시된다")]
    [SerializeField] float worldRadius = 40f;

    [SerializeField] Color playerColor = new Color(0.35f, 0.85f, 1f);
    [SerializeField] Color zombieColor = new Color(0.9f, 0.3f, 0.3f);
    [SerializeField] Color itemColor = new Color(1f, 0.9f, 0.4f);
    [SerializeField] Color containerColor = new Color(0.55f, 0.8f, 0.55f);

    readonly List<Image> dots = new List<Image>();
    Transform player;
    int used;

    public bool IsOpen => panel && panel.activeSelf;

    public void Bind(GameObject panelObject, RectTransform root, Sprite sprite, Text legendText)
    {
        panel = panelObject;
        mapRoot = root;
        dotSprite = sprite;
        legend = legendText;
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.mKey.wasPressedThisFrame) Toggle();
        else if (IsOpen && keyboard.escapeKey.wasPressedThisFrame) Close();

        if (IsOpen) Redraw();
    }

    public void Toggle()
    {
        if (!panel) return;

        panel.SetActive(!panel.activeSelf);
        if (panel.activeSelf) Redraw();
    }

    public void Close() { if (panel) panel.SetActive(false); }

    void Redraw()
    {
        if (!mapRoot) return;

        if (!player)
        {
            var stats = FindFirstObjectByType<PlayerStats>();
            if (!stats) return;
            player = stats.transform;
        }

        used = 0;

        // 플레이어를 중심에 두고 주변을 그린다.
        Vector3 center = player.position;

        foreach (var item in FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
            PlaceDot(item.transform.position, center, itemColor, 6f);

        var containers = WorldContainer.All;
        for (int i = 0; i < containers.Count; i++)
            if (containers[i]) PlaceDot(containers[i].transform.position, center, containerColor, 9f);

        var zombies = Zombie.All;
        int zombieCount = 0;
        for (int i = 0; i < zombies.Count; i++)
        {
            if (!zombies[i] || zombies[i].IsDead) continue;
            if (PlaceDot(zombies[i].transform.position, center, zombieColor, 9f)) zombieCount++;
        }

        // 플레이어는 마지막에 그려서 제일 위에 오게 한다.
        PlaceDot(center, center, playerColor, 12f);

        // 남은 점은 숨긴다 (Destroy 대신 재사용 — 매 프레임 생성하면 가비지가 쌓인다).
        for (int i = used; i < dots.Count; i++) dots[i].enabled = false;

        if (legend)
            legend.text = "[ 전체 지도 ]  반경 " + worldRadius + "m   M 또는 Esc 로 닫기\n"
                          + "● 나   ● 좀비 " + zombieCount + "   ● 물건   ● 컨테이너";
    }

    /// <summary>반경 밖이면 그리지 않고 false.</summary>
    bool PlaceDot(Vector3 world, Vector3 center, Color color, float size)
    {
        Vector3 offset = world - center;
        offset.y = 0f;
        if (offset.magnitude > worldRadius) return false;

        float half = Mathf.Min(mapRoot.rect.width, mapRoot.rect.height) * 0.5f;
        var local = new Vector2(offset.x / worldRadius * half, offset.z / worldRadius * half);

        var dot = GetDot();
        dot.color = color;
        dot.enabled = true;

        var rect = (RectTransform)dot.transform;
        rect.anchoredPosition = local;
        rect.sizeDelta = new Vector2(size, size);
        return true;
    }

    Image GetDot()
    {
        if (used < dots.Count) return dots[used++];

        var go = new GameObject("Dot", typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(mapRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);

        var image = go.GetComponent<Image>();
        image.sprite = dotSprite;
        image.raycastTarget = false;

        dots.Add(image);
        used++;
        return image;
    }
}
