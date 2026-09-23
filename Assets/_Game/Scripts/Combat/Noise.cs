using UnityEngine;

/// <summary>
/// GDD 18항 청각 시스템. 소리를 낸 쪽이 Emit 을 부르고, 들을 수 있는 좀비가 그 지점을 조사한다.
///
/// 소음값(GDD 18항 예시: 식칼 5 ~ 산탄총 100) 을 거리로 환산해서 전파한다.
/// ponytail: 전파 대상을 Zombie 의 정적 등록부에서 훑는다. 공간 분할은 좀비 수가 실제로
/// 문제가 될 때 넣는다 — 프로토타입 규모(수십 마리) 에서는 선형 훑기가 더 싸다.
/// </summary>
public static class Noise
{
    /// <summary>소음이 발생할 때마다 호출된다. NoiseDirector 가 누적해서 추가 유입을 판단한다.</summary>
    public static event System.Action<Vector3, float> Heard;

    /// <summary>소음값 1 당 전파 거리(m). 권총 40 → 14m, 산탄총 100 → 35m, 식칼 5 → 1.75m.</summary>
    public const float RangePerUnit = 0.35f;

    /// <summary>UI/디버그용. 마지막으로 발생한 소음의 크기와 위치.</summary>
    public static float LastLoudness { get; private set; }
    public static Vector3 LastPosition { get; private set; }

    /// <summary>들은 좀비 수를 반환한다 (검증용).</summary>
    public static int Emit(Vector3 position, float loudness) => Emit(position, loudness, false);

    /// <summary>
    /// piercesWalls: 벽을 넘어가는 소리인지.
    ///
    /// 사용자 요구로 실내 플레이어는 소리로 발각되지 않는다 — 걸음/근접공격/재장전은
    /// 방 밖으로 새지 않는다. 총성과 문이 부서지는 소리만 예외로 벽을 넘는다.
    /// 판정은 듣는 쪽(Zombie.Hear) 이 한다. 여기서는 플래그만 전달한다.
    /// </summary>
    public static int Emit(Vector3 position, float loudness, bool piercesWalls)
    {
        if (loudness <= 0f) return 0;

        LastLoudness = loudness;
        LastPosition = position;

        if (Heard != null) Heard(position, loudness);

        float range = loudness * RangePerUnit;
        float rangeSqr = range * range;
        int heard = 0;

        var all = Zombie.All;
        for (int i = 0; i < all.Count; i++)
        {
            var zombie = all[i];
            if (!zombie || zombie.IsDead) continue;
            if ((zombie.transform.position - position).sqrMagnitude > rangeSqr) continue;

            if (!zombie.Hear(position, piercesWalls)) continue;
            heard++;
        }

        return heard;
    }
}
