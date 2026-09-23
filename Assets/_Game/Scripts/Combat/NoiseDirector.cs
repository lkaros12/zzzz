using UnityEngine;

/// <summary>
/// GDD 26항 "소음에 따른 추가 적 유입". 누적 소음이 문턱을 넘으면 맵 밖에서 좀비가 들어온다.
///
/// GDD 18항은 "소리가 난 곳을 조사한다" 까지만 규정하므로, 유입 규칙(문턱/감쇠/쿨타임) 은
/// 여기서 정한 초기값이다 — GDD 27항 5번대로 플레이 테스트로 조정한다.
///
/// ponytail: 웨이브 스케줄러를 만들지 않았다. 누적값 하나와 쿨타임 하나로 충분하고,
/// 실제로 웨이브 패턴이 필요해지면 그때 상태 기계를 넣는다.
/// </summary>
public class NoiseDirector : MonoBehaviour
{
    [Tooltip("이 값을 넘으면 좀비가 유입된다. 산탄총(100) 두 방 정도")]
    [SerializeField] float spawnThreshold = 180f;

    [Tooltip("초당 감쇠량. 조용히 있으면 누적이 식는다")]
    [SerializeField] float decayPerSecond = 12f;

    [Tooltip("한 번에 들어오는 좀비 수")]
    [SerializeField, Min(1)] int spawnCount = 2;

    [Tooltip("유입 후 이만큼은 다시 유입되지 않는다")]
    [SerializeField] float cooldown = 20f;

    [Tooltip("플레이어로부터 이 거리 밖에서 생성한다 — 눈앞에 튀어나오면 반칙이다")]
    [SerializeField] float spawnDistance = 26f;

    [Tooltip("살아 있는 좀비가 이 수 이상이면 더 부르지 않는다")]
    [SerializeField] int maxAlive = 14;

    [SerializeField] ZombieDefinition[] spawnTypes;
    [SerializeField] Material fallbackMaterial;

    Transform player;
    float accumulated;
    float nextSpawnTime;

    /// <summary>검증/디버그용. 현재 누적 소음과 총 유입 수.</summary>
    public float Accumulated => accumulated;
    public int SpawnedTotal { get; private set; }

    public void Bind(ZombieDefinition[] types, Material fallback)
    {
        spawnTypes = types;
        fallbackMaterial = fallback;
    }

    void Awake() => Noise.Heard += OnNoise;
    void OnDestroy() => Noise.Heard -= OnNoise;

    void OnNoise(Vector3 position, float loudness) => accumulated += loudness;

    void Update()
    {
        if (accumulated <= 0f) return;

        accumulated = Mathf.Max(0f, accumulated - decayPerSecond * Time.deltaTime);
        if (accumulated < spawnThreshold) return;
        if (Time.time < nextSpawnTime) return;

        if (!player)
        {
            var stats = FindFirstObjectByType<PlayerStats>();
            if (!stats) return;
            player = stats.transform;
        }

        if (Zombie.AliveCount >= maxAlive) { accumulated = spawnThreshold * 0.5f; return; }

        SpawnWave();
    }

    void SpawnWave()
    {
        accumulated = 0f;
        nextSpawnTime = Time.time + cooldown;

        // 소리가 난 쪽이 아니라 임의 방향에서 들어오게 한다 — 어디서 올지 모르는 게 압박이 된다.
        for (int i = 0; i < spawnCount; i++)
        {
            float angle = Random.value * Mathf.PI * 2f;
            var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spawnDistance;
            Vector3 ground = player.position + offset;
            ground.y = 0f;

            var type = spawnTypes != null && spawnTypes.Length > 0
                ? spawnTypes[Random.Range(0, spawnTypes.Length)]
                : null;

            var zombie = Zombie.Spawn(ground, type, fallbackMaterial);

            // 들어온 좀비는 마지막 소음 지점을 조사하며 다가온다 (GDD 18항).
            zombie.Hear(Noise.LastPosition);
            SpawnedTotal++;
        }
    }

    /// <summary>테스트용. 누적 소음을 직접 밀어 넣는다.</summary>
    public void AddNoiseForTest(float amount) => accumulated += amount;
}
