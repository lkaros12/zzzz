using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 기획 6항 방. 실내 판정의 기준이 되는 상자 하나.
///
/// 벽/창문과 독립이다 — 방은 "어디까지가 실내인가" 만 알고, 벽을 들고 있지 않다.
/// 벽이 이 방에 속하는지는 위치로 판단한다. 그래야 두 방이 벽 하나를 공유해도
/// 양쪽 모두가 그 벽을 자기 벽으로 본다 (부모 관계로 묶으면 한쪽만 갖게 된다).
/// </summary>
public class Room : MonoBehaviour
{
    static readonly List<Room> all = new List<Room>();
    public static IReadOnlyList<Room> All => all;

    [SerializeField] string displayName = "방";
    [Tooltip("벽 안쪽 공간의 크기. 위치는 이 오브젝트의 바닥 중심")]
    [SerializeField] Vector3 innerSize = new Vector3(10f, 3f, 8f);

    public string DisplayName => displayName;

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    public Bounds InnerBounds =>
        new Bounds(transform.position + Vector3.up * (innerSize.y * 0.5f), innerSize);

    public Vector3 Center => transform.position + Vector3.up * (innerSize.y * 0.5f);

    public bool Contains(Vector3 worldPoint) => InnerBounds.Contains(worldPoint);

    /// <summary>벽처럼 경계에 걸친 물건까지 포함해서 본다.</summary>
    public bool Encloses(Vector3 worldPoint, float margin)
    {
        var bounds = InnerBounds;
        bounds.Expand(margin * 2f);
        return bounds.Contains(worldPoint);
    }

    /// <summary>
    /// 문간은 벽 두께만큼 어느 방의 상자에도 들어가지 않는다. 그 폭을 메워서
    /// 문에 서 있어도 "실내" 로 보게 한다 — 안 메우면 문에 붙는 순간 실외로 잡혀
    /// 시야 음영이 옅어지고 벽 투명화가 풀린다 (사용자 지적: "문에 붙으면 밝아짐").
    /// 바깥벽 두께(0.25m) 보다 크고, 문 밖에 서 있는 거리보다는 작게 잡는다.
    /// </summary>
    const float DoorwayMargin = 0.3f;

    /// <summary>그 지점이 들어 있는 방. 실외면 null.</summary>
    public static Room At(Vector3 worldPoint)
    {
        for (int i = 0; i < all.Count; i++)
            if (all[i] && all[i].Contains(worldPoint)) return all[i];

        // 상자 안이 아니면 문간일 수 있다. 가장 가까운 방으로 붙인다 —
        // 첫 일치를 쓰면 문 하나를 공유하는 두 방 중 엉뚱한 쪽이 걸린다.
        Room best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i] || !all[i].Encloses(worldPoint, DoorwayMargin)) continue;

            float distance = (all[i].Center - worldPoint).sqrMagnitude;
            if (distance >= bestDistance) continue;

            best = all[i];
            bestDistance = distance;
        }

        return best;
    }

    public void Setup(string label, Vector3 size)
    {
        displayName = label;
        innerSize = size;
    }
}
