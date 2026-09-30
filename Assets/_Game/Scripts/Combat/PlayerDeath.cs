using UnityEngine;

/// <summary>
/// GDD 8항 사망 / 좀비화 / 부활.
/// HP 가 0 이 되면 소지품과 장비를 전부 잃고, 그 자리에 소지품을 들고 있는 내 좀비가 생긴다.
/// 플레이어는 지정 스폰 지점 중 하나에서 새 생존자로 부활한다.
/// </summary>
public class PlayerDeath : MonoBehaviour
{
    [Tooltip("부활 지점. 비어 있으면 원점 주변에서 부활한다")]
    [SerializeField] Transform[] spawnPoints;

    [Header("내 좀비 모델")]
    [SerializeField] GameObject corpseModel;
    [SerializeField] float corpseModelScale = 1f;
    [SerializeField] Material[] corpseMaterials;
    [SerializeField] RuntimeAnimatorController corpseController;
    [SerializeField] Avatar corpseAvatar;

    PlayerStats stats;
    PlayerInventory inventory;
    PlayerEquipment equipment;
    PlayerInjuries injuries;
    PlayerGrapple grapple;
    CharacterController cc;

    /// <summary>몇 번 죽었는지. UI/디버그용.</summary>
    public int DeathCount { get; private set; }

    /// <summary>마지막으로 만들어진 내 좀비. 회수 대상.</summary>
    public Zombie LastCorpse { get; private set; }

    public void Bind(Transform[] points, GameObject model, float modelScale, Material[] materials,
                     RuntimeAnimatorController controller, Avatar avatar)
    {
        spawnPoints = points;
        corpseModel = model;
        corpseModelScale = modelScale;
        corpseMaterials = materials;
        corpseController = controller;
        corpseAvatar = avatar;
    }

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        inventory = GetComponent<PlayerInventory>();
        equipment = GetComponent<PlayerEquipment>();
        injuries = GetComponent<PlayerInjuries>();
        grapple = GetComponent<PlayerGrapple>();
        cc = GetComponent<CharacterController>();
    }

    void Update()
    {
        if (!stats || !stats.IsDead) return;
        Die();
    }

    void Die()
    {
        DeathCount++;
        GetComponent<PlayerConsumer>()?.ResetMedicalState();

        if (grapple) grapple.ForceRelease();

        SpawnCorpseWithBelongings();
        Respawn();
    }

    /// <summary>GDD 8항: 사망 시점의 소지품을 전부 보유한 좀비를 만든다.</summary>
    void SpawnCorpseWithBelongings()
    {
        var corpse = SpawnCorpse(transform.position);

        if (inventory)
        {
            var stacks = inventory.Stacks;
            for (int i = 0; i < stacks.Count; i++)
                corpse.AddCarried(stacks[i].definition, stacks[i].quantity,
                                  stacks[i].durability, stacks[i].loadedAmmo);

            inventory.Clear();
        }

        if (equipment)
        {
            var stripped = equipment.StripAll();
            for (int i = 0; i < stripped.Length; i++)
                corpse.AddCarried(stripped[i].definition, stripped[i].quantity,
                                  stripped[i].durability, stripped[i].loadedAmmo);
        }

        LastCorpse = corpse;
    }

    public Zombie SpawnCorpse(Vector3 position)
    {
        var body = cc ? cc : GetComponent<CharacterController>();
        return Zombie.Spawn(position, corpseModel, corpseModelScale, corpseMaterials,
                            corpseController, corpseAvatar,
                            body ? body.height : 1.2f, body ? body.radius : 0.26f);
    }

    /// <summary>GDD 8항: 랜덤한 지정 스폰 장소에서 새 생존자로 부활.</summary>
    void Respawn()
    {
        Vector3 position = PickSpawnPosition();

        // CharacterController 가 붙어 있으면 transform 을 직접 옮기기 전에 끄는 편이 안전하다.
        if (cc) cc.enabled = false;
        transform.position = position;
        if (cc) cc.enabled = true;

        if (stats) stats.ResetStats();
        if (injuries) injuries.ClearAll();

        Debug.Log("[V0.1] 사망 " + DeathCount + "회 — 소지품은 내 좀비가 들고 있다. 찾아서 처리하면 떨어진다.");
    }

    Vector3 PickSpawnPosition()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            var point = spawnPoints[Random.Range(0, spawnPoints.Length)];
            if (point) return point.position;
        }

        // 스폰 지점이 없으면 원점 주변으로.
        var offset = Random.insideUnitCircle * 6f;
        return new Vector3(offset.x, cc ? cc.height * 0.5f + 0.05f : 0.65f, offset.y);
    }
}
