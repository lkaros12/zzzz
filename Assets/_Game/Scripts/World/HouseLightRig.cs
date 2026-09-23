using UnityEngine;

/// <summary>
/// 방마다 달린 전등을 HouseLights 상태에 맞춰 켜고 끈다.
///
/// 조명 자체에는 그림자를 켜지 않는다. 방 11개에 광원을 달면 2048 그림자 아틀라스를
/// 그만큼 쪼개서 텍셀이 태양 그림자(1.46cm) 보다 몇 배로 커지고, 둘이 나란히 놓이면
/// 계단이 바로 눈에 띈다. 점광원은 6면이라 더 나쁘므로 아래로 쏘는 스포트를 쓴다.
/// 벽 가림은 어차피 시야 마스크(VisionFog) 가 하므로 조명 그림자가 없어도 방 밖은 검다.
/// </summary>
public class HouseLightRig : MonoBehaviour
{
    [SerializeField] Light[] lamps;
    [Tooltip("플레이어로부터 이 거리 안의 전등만 켠다. 먼 방은 시야 마스크가 어차피 가린다")]
    [SerializeField] float activeRange = 13f;
    [Tooltip("켤 전등을 다시 고르는 간격(초)")]
    [SerializeField] float refreshInterval = 0.25f;

    Transform player;
    float nextRefreshTime;

    /// <summary>검증용.</summary>
    public int LampCount => lamps != null ? lamps.Length : 0;

    /// <summary>검증용. 지금 실제로 켜져 있는 전등 수.</summary>
    public int ActiveCount { get; private set; }

    public void Bind(Light[] lights)
    {
        lamps = lights;
        nextRefreshTime = 0f;
    }

    void Update()
    {
        if (Time.time < nextRefreshTime) return;
        nextRefreshTime = Time.time + refreshInterval;

        if (lamps == null) return;

        if (!player)
        {
            var stats = FindFirstObjectByType<PlayerStats>();
            if (!stats) return;
            player = stats.transform;
        }

        // 전등 13개를 다 켜면 픽셀 조명이 3.41ms 를 먹는다 (실측: 7.39 → 10.80ms).
        // 먼 방은 음영에 덮여 보이지도 않으므로 켤 이유가 없다.
        float rangeSqr = activeRange * activeRange;
        int active = 0;

        for (int i = 0; i < lamps.Length; i++)
        {
            if (!lamps[i]) continue;

            bool on = HouseLights.On
                      && (lamps[i].transform.position - player.position).sqrMagnitude <= rangeSqr;

            lamps[i].enabled = on;
            if (on) active++;
        }

        ActiveCount = active;
    }
}
