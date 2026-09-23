using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 기획 2·3·7항 벽 투명화. 렌더링 전용이다 — 시야 판정(VisionField)과 완전히 분리돼 있고
/// 콜라이더도 건드리지 않는다. 벽을 끄거나 지우지 않고 알파만 내린다.
///
/// 흐려지는 것은 "플레이어가 들어가 있는 방의 벽" 뿐이다.
///  - 실외에 있으면 아무 벽도 흐려지지 않는다. 바깥에서 남의 집 속이 들여다보이면 안 된다.
///  - 실내에서도 다른 방의 벽은 그대로 둔다. 안 그러면 집 전체가 다 비쳐서 지금 어느 방에
///    있는지 읽히지 않는다 (기획 7항 "현재 있는 방의 내부를 쉽게 확인").
///
/// 그 안에서 두 가지 이유로 흐려진다.
///  1) 카메라와 플레이어 사이에 낀 벽 조각 — 실제로 가리는 조각만.
///  2) 그 방의 카메라 쪽 벽면 — 한 조각만 내려도 방이 안 보이므로 면 전체를 내린다.
/// </summary>
[RequireComponent(typeof(Camera))]
public class WallFader : MonoBehaviour
{
    [SerializeField] Transform target;
    [Tooltip("플레이어 몸 굵기. 이보다 얇게 잡으면 어깨를 가리는 벽을 놓친다")]
    [SerializeField] float castRadius = 0.7f;
    [Tooltip("방 경계에 걸친 벽까지 그 방의 벽으로 본다. 너무 크면 옆방 벽까지 흐려진다")]
    [SerializeField] float wallMargin = 0.45f;

    readonly RaycastHit[] buffer = new RaycastHit[24];
    readonly HashSet<WallSegment> roomWalls = new HashSet<WallSegment>();

    /// <summary>검증용. 이번 프레임에 흐려달라고 요청한 조각 수.</summary>
    public int FadedCount { get; private set; }
    /// <summary>검증용. 플레이어가 지금 들어가 있는 방.</summary>
    public Room CurrentRoom { get; private set; }

    public void SetTarget(Transform newTarget) => target = newTarget;

    void LateUpdate()
    {
        FadedCount = 0;
        roomWalls.Clear();
        CurrentRoom = null;
        if (!target) return;

        CurrentRoom = Room.At(target.position);
        if (!CurrentRoom) return;   // 실외에서는 벽을 흐리지 않는다

        CollectRoomWalls();
        FadeOccludingWalls();
        FadeFrontWalls();
    }

    /// <summary>지금 있는 방의 경계에 있는 벽 조각만 모은다.</summary>
    void CollectRoomWalls()
    {
        var walls = WallSegment.All;
        for (int i = 0; i < walls.Count; i++)
        {
            var wall = walls[i];
            if (wall && CurrentRoom.Encloses(wall.Center, wallMargin)) roomWalls.Add(wall);
        }
    }

    /// <summary>기획 2항: 카메라와 플레이어 사이에 실제로 낀 조각만.</summary>
    void FadeOccludingWalls()
    {
        Vector3 delta = target.position - transform.position;
        float distance = delta.magnitude;
        if (distance < 0.1f) return;

        Vector3 direction = delta / distance;

        // 굵은 구체로 훑는다. 얇은 선이면 플레이어 중심만 보고 어깨를 가리는 조각을 놓친다.
        int count = Physics.SphereCastNonAlloc(transform.position, castRadius, direction, buffer,
                                               distance, Vision.BlockerMask,
                                               QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var wall = buffer[i].collider ? buffer[i].collider.GetComponentInParent<WallSegment>() : null;
            if (!wall || !roomWalls.Contains(wall)) continue;

            wall.RequestFade();
            FadedCount++;
        }
    }

    /// <summary>
    /// 기획 7항: 눈앞 한 조각만 흐려도 방이 안 보인다 — 카메라 쪽 벽면 전체를 내린다.
    /// 반대쪽 벽은 그대로 둔다. 그래야 건물 구조가 남는다.
    /// </summary>
    void FadeFrontWalls()
    {
        Vector3 toCamera = -transform.forward;
        toCamera.y = 0f;
        toCamera.Normalize();

        Vector3 center = CurrentRoom.Center;

        foreach (var wall in roomWalls)
        {
            Vector3 offset = wall.Center - center;
            offset.y = 0f;

            // 방 중심에서 카메라 쪽에 있는 벽만. 뒤쪽 벽은 어차피 시야를 막지 않는다.
            if (Vector3.Dot(offset, toCamera) <= 0.2f) continue;

            wall.RequestFade();
            FadedCount++;
        }
    }
}
