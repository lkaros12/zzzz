using UnityEngine;

/// <summary>
/// 시야 판정 한 곳. 좀비 AI(17항 추적)와 플레이어 시야 음영이 같은 규칙을 쓴다.
///
/// "무엇이 시야를 막는가" 는 레이어로만 판단한다. 예전에는 Zombie 가 레이캐스트 결과를
/// 하나씩 훑으며 자기 자신/다른 좀비/월드 아이템을 걸러냈는데, 규칙이 그 한 곳에만 있어서
/// 다른 시스템이 같은 판정을 쓰려면 복사해야 했다.
///
/// 창문은 별도 컴포넌트가 아니라 "패널 콜라이더" 로 막는다 — 닫히면 켜고 열리면 끈다.
/// 그래서 열린 창문 너머는 여기서 아무 것도 안 해도 그냥 보인다 (기획 11항).
/// </summary>
public static class Vision
{
    /// <summary>
    /// 좀비 CharacterController 중심에서 눈까지의 높이.
    /// </summary>
    public const float EyeOffset = 1.15f - 1.2f * 0.5f;
    public const float PlayerEyeOffset = 0.5f;

    static int blockerMask;

    /// <summary>시야를 막는 레이어. 캐릭터와 바닥 아이템은 서로를 가리지 않는다.</summary>
    public static int BlockerMask
    {
        get
        {
            // 계산된 값은 0 이 될 수 없으므로 0 을 "아직 안 구함" 으로 쓴다.
            if (blockerMask != 0) return blockerMask;

            blockerMask = ~0;
            Drop("Character");
            Drop("WorldItem");
            Drop("Ignore Raycast");
            Drop("TransparentFX");
            Drop("UI");
            return blockerMask;
        }
    }

    static void Drop(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        if (layer >= 0) blockerMask &= ~(1 << layer);
    }

    /// <summary>두 눈 사이가 뚫려 있는지. 인자는 캡슐 중심 위치다.</summary>
    public static bool CanSee(Vector3 fromBody, Vector3 toBody, float maxDistance)
        => CanSee(fromBody, EyeOffset, toBody, EyeOffset, maxDistance);

    public static bool CanSee(Vector3 fromBody, float fromEyeOffset,
                              Vector3 toBody, float toEyeOffset, float maxDistance)
    {
        Vector3 from = fromBody + Vector3.up * fromEyeOffset;
        Vector3 to = toBody + Vector3.up * toEyeOffset;

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance > maxDistance) return false;
        if (distance < 0.01f) return true;

        return !Physics.Raycast(from, delta / distance, distance,
                                BlockerMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>한 방향으로 시야가 닿는 거리. 막히면 벽까지, 안 막히면 maxDistance.</summary>
    public static float SightDistance(Vector3 origin, Vector3 direction, float maxDistance)
    {
        RaycastHit hit;
        return Physics.Raycast(origin, direction, out hit, maxDistance,
                               BlockerMask, QueryTriggerInteraction.Ignore)
            ? hit.distance
            : maxDistance;
    }
}
