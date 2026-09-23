using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// GDD 13항 주변 물건 목록. 여러 개일 때 원하는 것만 골라 줍는다 —
/// "자동 전체 획득 금지" 가 명시돼 있으므로 한 번 클릭에 한 아이템만 처리한다.
/// </summary>
public class NearbyItemsUI : MonoBehaviour
{
    [SerializeField] PlayerInteractor interactor;
    [SerializeField] PlayerInventory inventory;
    [Tooltip("인벤토리 패널. 열려 있을 때만 목록을 갱신한다.")]
    [SerializeField] GameObject panel;
    [SerializeField] RectTransform listRoot;
    [SerializeField] Text emptyText;
    [SerializeField] Sprite rowSprite;
    [SerializeField] float rowHeight = 38f;

    readonly List<GameObject> rows = new List<GameObject>();

    bool wasOpen;
    int lastSignature;
    int scrollOffset;
    int lastTotal;

    void Awake()
    {
        // Bind 는 빌더가 씬을 만들 때 한 번만 돈다. 뒤에 추가한 참조는 씬에 비어 있으므로
        // 런타임에 스스로 찾는다.
        if (!inventory && interactor) inventory = interactor.GetComponent<PlayerInventory>();
    }

    /// <summary>화면 좌표가 이 목록 위인지. 인벤토리에서 컨테이너로 드래그할 때 InventoryUI 가 묻는다.</summary>
    public bool ContainsScreenPoint(Vector2 screenPoint, Camera eventCamera) =>
        listRoot && RectTransformUtility.RectangleContainsScreenPoint(listRoot, screenPoint, eventCamera);

    public void Bind(PlayerInteractor playerInteractor, GameObject panelObject,
                     RectTransform root, Text empty, Sprite sprite)
    {
        interactor = playerInteractor;
        if (playerInteractor) inventory = playerInteractor.GetComponent<PlayerInventory>();
        panel = panelObject;
        listRoot = root;
        emptyText = empty;
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

        HandleScroll();

        // 매 프레임 버튼을 다시 만들면 클릭이 씹히고 가비지도 생긴다. 목록이 바뀔 때만 다시 만든다.
        int signature = BuildSignature() * 31 + scrollOffset;
        if (wasOpen && signature == lastSignature) return;

        wasOpen = true;
        lastSignature = signature;
        Rebuild();
    }

    /// <summary>
    /// 목록이 영역보다 길면 넘치는 줄은 아예 그려지지 않아 가져올 방법이 없었다.
    /// 휠로 위아래로 넘긴다 — 카메라 줌은 UI 위에서 휠을 무시하므로 서로 안 겹친다.
    /// ponytail: ScrollRect 를 붙이지 않았다. 줄이 몇 개 넘칠 뿐이고 오프셋 하나로 충분하다.
    /// </summary>
    void HandleScroll()
    {
        if (lastTotal <= VisibleRowCount) { scrollOffset = 0; return; }

        var mouse = Mouse.current;
        if (mouse == null) return;

        // 목록 위에 커서가 있을 때만 넘긴다.
        if (!listRoot || !RectTransformUtility.RectangleContainsScreenPoint(
                listRoot, mouse.position.ReadValue(), null)) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.01f) return;

        int max = Mathf.Max(0, lastTotal - VisibleRowCount);
        scrollOffset = Mathf.Clamp(scrollOffset - (int)Mathf.Sign(scroll), 0, max);
    }

    /// <summary>한 번에 그릴 수 있는 줄 수. 넘침 안내 줄 한 칸을 남겨둔다.</summary>
    int VisibleRowCount => Mathf.Max(1, Mathf.FloorToInt(listRoot.rect.height / rowHeight) - 1);

    int BuildSignature()
    {
        int hash = 17;

        // GDD 24항: 컨테이너가 열려 있으면 그 내용을 보여준다.
        var container = interactor.ViewContainer;
        if (container)
        {
            hash = hash * 31 + container.GetInstanceID();
            var entries = container.Contents;
            for (int i = 0; i < entries.Count; i++)
            {
                hash = hash * 31 + (entries[i].definition ? entries[i].definition.id.GetHashCode() : 0);
                hash = hash * 31 + entries[i].quantity;
            }
            return hash;
        }

        var items = interactor.NearbyItems;
        for (int i = 0; i < items.Count; i++)
        {
            hash = hash * 31 + items[i].GetInstanceID();
            hash = hash * 31 + items[i].Quantity;
        }
        return hash;
    }

    void Rebuild()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (!rows[i]) continue;

            // Destroy 는 프레임 끝까지 미뤄져서 낡은 줄이 그 프레임에 계속 클릭될 수 있다.
            // 먼저 꺼서 입력 대상에서 빼낸다.
            rows[i].SetActive(false);
            Destroy(rows[i]);
        }
        rows.Clear();

        int visible = VisibleRowCount;

        var container = interactor.ViewContainer;
        if (container)
        {
            var entries = container.Contents;
            lastTotal = entries.Count;
            if (emptyText)
            {
                emptyText.enabled = entries.Count == 0;
                emptyText.text = container.DisplayName + " 이(가) 비었습니다";
            }

            ClampOffset(visible);
            int count = Mathf.Min(entries.Count - scrollOffset, visible);
            for (int i = 0; i < count; i++) rows.Add(CreateContainerRow(container, scrollOffset + i, i));

            AddOverflowHint(entries.Count, visible, count);
            return;
        }

        var items = interactor.NearbyItems;
        lastTotal = items.Count;
        if (emptyText)
        {
            emptyText.enabled = items.Count == 0;
            emptyText.text = "주변에 아무것도 없습니다";
        }

        ClampOffset(visible);
        int shown = Mathf.Min(items.Count - scrollOffset, visible);
        for (int i = 0; i < shown; i++) rows.Add(CreateRow(items[scrollOffset + i], i));

        AddOverflowHint(items.Count, visible, shown);
    }

    void ClampOffset(int visible)
    {
        scrollOffset = Mathf.Clamp(scrollOffset, 0, Mathf.Max(0, lastTotal - visible));
    }

    /// <summary>위/아래로 남은 줄 수를 알려준다. 넘치는 줄이 없으면 아무것도 만들지 않는다.</summary>
    void AddOverflowHint(int total, int visible, int shown)
    {
        if (total <= visible) return;

        int above = scrollOffset;
        int below = total - scrollOffset - shown;

        string text = "휠로 스크롤";
        if (above > 0) text = "▲ " + above + "개  " + text;
        if (below > 0) text += "  ▼ " + below + "개";

        var go = CreateRowObject("OverflowHint", shown, new Color(0.14f, 0.16f, 0.20f, 1f));
        CreateRowLabel((RectTransform)go.transform, text);
        rows.Add(go);
    }

    /// <summary>GDD 24항 컨테이너 한 줄. 클릭하면 그 아이템만 가져온다.</summary>
    GameObject CreateContainerRow(WorldContainer container, int index, int row)
    {
        var entry = container.Contents[index];
        var go = CreateRowObject("ContainerRow_" + index, row, new Color(0.30f, 0.26f, 0.18f, 1f));

        string label = entry.definition ? entry.definition.displayName : "?";
        if (entry.quantity > 1) label += " x" + entry.quantity;
        if (entry.definition && entry.definition.maxDurability > 0)
            label += "  [" + entry.durability + "/" + entry.definition.maxDurability + "]";

        // 인덱스가 아니라 종류+내구도로 기억한다. 목록이 갱신되기 전에 두 번 눌려도
        // 엉뚱한 줄이 들어오지 않는다.
        var captured = container;
        var capturedDefinition = entry.definition;
        int capturedDurability = entry.durability;

        // 좌클릭도 우클릭도 전량 획득. 사용자 요구: "우클릭을 누르면 수량이 얼마든 바로 인벤토리에"
        var relay = go.AddComponent<RowClickRelay>();
        relay.OnLeft = () => OnContainerRowClicked(captured, capturedDefinition, capturedDurability);
        relay.OnRight = relay.OnLeft;

        CreateRowLabel((RectTransform)go.transform, label);
        return go;
    }

    void OnContainerRowClicked(WorldContainer container, ItemDefinition definition, int durability)
    {
        // 참조를 지연 해석한다. Bind 가 돌기 전에 만들어진 씬에서는 직렬화된 값이 비어 있어서,
        // 여기 의존하면 가져오기가 조용히 실패한다 (실제로 겪었다).
        if (!inventory && interactor) inventory = interactor.GetComponent<PlayerInventory>();

        if (container && inventory) container.TakeInto(definition, durability, inventory);

        wasOpen = false; // 다음 프레임에 목록을 다시 만든다
    }

    /// <summary>줄 껍데기. 주변 물건과 컨테이너 줄이 같은 모양을 쓴다.</summary>
    GameObject CreateRowObject(string name, int index, Color tint)
    {
        var go = new GameObject(name, typeof(Image), typeof(Button));
        var rect = (RectTransform)go.transform;
        rect.SetParent(listRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(0f, -index * rowHeight);
        rect.sizeDelta = new Vector2(listRoot.rect.width, rowHeight - 4f);

        var image = go.GetComponent<Image>();
        image.sprite = rowSprite;
        image.type = Image.Type.Sliced;
        image.color = tint;

        go.GetComponent<Button>().targetGraphic = image;
        return go;
    }

    GameObject CreateRow(WorldItem item, int index)
    {
        var go = CreateRowObject("NearbyRow_" + index, index, new Color(0.22f, 0.26f, 0.32f, 1f));

        var captured = item;

        var relay = go.AddComponent<RowClickRelay>();
        relay.OnLeft = () => OnRowClicked(captured);
        relay.OnRight = relay.OnLeft;

        CreateRowLabel((RectTransform)go.transform, item.Label);
        return go;
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
        text.fontSize = 16;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = content;
    }

    void OnRowClicked(WorldItem item)
    {
        // 다른 경로로 이미 사라진 아이템일 수 있다.
        if (item) interactor.TryPickup(item);

        wasOpen = false; // 다음 프레임에 목록을 다시 만든다
    }
}
