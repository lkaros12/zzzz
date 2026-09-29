using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// GDD 7항 플레이어 공격. 좌클릭.
/// 판정은 널널하게 — 무기 사거리에 여유를 더하고 넓은 부채꼴 안의 적을 때린다.
/// </summary>
public class PlayerCombat : MonoBehaviour
{
    [Header("맨손 (주무기가 없을 때)")]
    [SerializeField] float unarmedDamage = 6f;
    [SerializeField] float unarmedRange = 1.5f;
    [SerializeField] float unarmedInterval = 0.85f;
    [SerializeField] float unarmedStaminaCost = 6f;
    [Tooltip("GDD 18항 맨손 타격 소음. 무기보다 조용하다")]
    [SerializeField] float unarmedNoise = 3f;
    [Tooltip("GDD 18항 재장전 소음. 총성보다는 훨씬 작다")]
    [SerializeField] float reloadNoise = 12f;

    [Header("판정 (GDD 7항: 플레이어 공격은 널널하게)")]
    [Tooltip("부채꼴 반각. 클수록 옆에 있는 적도 맞는다")]
    [SerializeField] float attackHalfAngle = 65f;
    [Tooltip("무기 사거리에 더해주는 여유")]
    [SerializeField] float rangePadding = 0.5f;
    [Tooltip("총알 굵기. 클수록 조준이 빗나가도 맞는다")]
    [SerializeField] float hitscanRadius = 0.5f;
    [Tooltip("가림 판정을 하는 어깨 높이. 낮은 엄폐물 위로 넘겨 쏠 수 있어야 한다")]
    [SerializeField] float aimHeight = 0.35f;

    [Tooltip("저항이 풀린 직후 이 시간 동안은 좌클릭을 공격으로 받지 않는다")]
    [SerializeField] float releaseLockout = 0.2f;

    PlayerStats stats;
    PlayerInjuries injuries;
    PlayerEquipment equipment;
    PlayerGrapple grapple;
    PlayerAssassinate assassinate;
    PlayerInventory inventory;
    PlayerAim aim;

    readonly Collider[] overlapBuffer = new Collider[32];
    readonly RaycastHit[] hitBuffer = new RaycastHit[32];
    static int worldItemLayer = int.MinValue;
    readonly List<Zombie> candidates = new List<Zombie>();
    AttackSwingFx swingFx;
    Comparison<Zombie> distanceComparison;
    Vector3 sortOrigin;
    // GDD 20항: 이번 공격이 실을 넉백/경직. 판정 메서드 인자를 늘리지 않기 위해 sortOrigin 과 같은 방식으로 둔다.
    float pendingKnockback;
    float pendingStagger;
    readonly float[] nextAttackTime = new float[2];
    float reloadEndTime;

    /// <summary>UI/디버그용. 마지막 공격에 맞은 대상 수.</summary>
    public int LastAttackHitCount { get; private set; }

    /// <summary>실제로 공격을 시작한 마지막 시각. 애니메이션은 이 값을 읽기만 한다.</summary>
    public float LastAttackTime { get; private set; }

    public bool IsAttacking => Time.time < nextAttackTime[0] || Time.time < nextAttackTime[1];

    /// <summary>UI 에 띄우는 마지막 결과 메시지 (파손 등).</summary>
    public string LastMessage { get; private set; } = string.Empty;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        injuries = GetComponent<PlayerInjuries>();
        equipment = GetComponent<PlayerEquipment>();
        grapple = GetComponent<PlayerGrapple>();
        assassinate = GetComponent<PlayerAssassinate>();
        inventory = GetComponent<PlayerInventory>();
        aim = GetComponent<PlayerAim>();
        distanceComparison = CompareByDistance; // 매 공격마다 델리게이트를 새로 만들지 않도록 캐시

        // 씬을 재빌드하지 않아도 되도록 런타임에 붙인다.
        swingFx = GetComponent<AttackSwingFx>();
        if (!swingFx) swingFx = gameObject.AddComponent<AttackSwingFx>();
    }

    void Update()
    {
        // 저항 중에는 좌클릭이 연타 입력이므로 공격을 내지 않는다.
        if (grapple && grapple.IsGrappled) return;

        // 저항을 풀어낸 그 클릭이 곧바로 공격으로 이어지면 스태미나가 억울하게 빠진다.
        if (grapple && Time.time - grapple.LastReleaseTime < releaseLockout) return;
        if (stats && stats.IsDead) return;

        // GDD 19항: 암살 모션 3초 동안은 공격 입력을 받지 않는다.
        if (assassinate && assassinate.IsAssassinating) return;

        var keyboard = Keyboard.current;
        if (keyboard != null) HandleReloadInput(keyboard);

        // GDD 4항: 재장전 중에는 공격이 나가지 않는다.
        if (IsReloading) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        bool left = mouse.leftButton.wasPressedThisFrame;
        bool right = mouse.rightButton.wasPressedThisFrame;
        if (!left && !right) return;

        // 클릭을 Mouse.current 로 직접 읽기 때문에 uGUI 가 그 클릭을 먹었는지 알 수 없다.
        // 인벤토리 패널의 우클릭 착용이 공격으로 새지 않게 여기서 걸러낸다.
        if (IsPointerOverUI()) return;

        // 좌클릭 = 주무기, 우클릭 = 보조무기 (GDD 4항 "우클릭 = 보조 행동").
        TryAttack(left ? EquipmentSlot.MainHand : EquipmentSlot.OffHand);
    }

    static bool IsPointerOverUI() =>
        EventSystem.current && EventSystem.current.IsPointerOverGameObject();

    /// <summary>
    /// GDD 4항 R 재장전. 주무기의 탄약을 인벤토리에서 채운다.
    /// 재장전 중에는 공격이 나가지 않고, 탄약이 없으면 시작하지 않는다.
    /// </summary>
    void HandleReloadInput(Keyboard keyboard)
    {
        if (!keyboard.rKey.wasPressedThisFrame) return;

        // 사용자 요구: 한 발이라도 쐈으면 R 로 즉시 재장전. 탄창이 빌 때까지 기다리지 않는다.
        TryReload(EquipmentSlot.MainHand);
    }

    public bool TryReload(EquipmentSlot hand)
    {
        var stack = equipment ? equipment.GetStack(hand) : null;
        if (stack == null || !stack.UsesAmmo) return false;

        var weapon = stack.definition;
        if (stack.loadedAmmo >= weapon.magazineSize)
        {
            LastMessage = weapon.displayName + " 은(는) 이미 장전되어 있습니다";
            return false;
        }

        int need = weapon.magazineSize - stack.loadedAmmo;
        int loaded = TakeAmmo(weapon.ammoId, need);
        if (loaded <= 0)
        {
            LastMessage = "탄약이 없습니다";
            return false;
        }

        stack.loadedAmmo += loaded;
        reloadEndTime = Time.time + weapon.reloadTime;

        // 재장전도 소리를 낸다 — 조용히 숨어서 할 수 있는 행동이 아니다 (GDD 18항).
        Noise.Emit(transform.position, reloadNoise);

        LastMessage = weapon.displayName + " 재장전 (" + stack.loadedAmmo + "/" + weapon.magazineSize + ")";
        return true;
    }

    /// <summary>인벤토리에서 해당 탄약을 최대 amount 만큼 꺼낸다. 실제로 꺼낸 수를 반환.</summary>
    int TakeAmmo(string ammoId, int amount)
    {
        if (!inventory || string.IsNullOrEmpty(ammoId)) return 0;

        int taken = 0;
        while (taken < amount)
        {
            ItemStack found = null;
            var stacks = inventory.Stacks;
            for (int i = 0; i < stacks.Count; i++)
                if (stacks[i].definition && stacks[i].definition.id == ammoId) { found = stacks[i]; break; }

            if (found == null) break;

            int got = inventory.TakeFrom(found, amount - taken);
            if (got <= 0) break;
            taken += got;
        }

        return taken;
    }

    /// <summary>UI 용. 주무기의 잔탄 / 최대. 탄약을 안 쓰는 무기면 false.</summary>
    public bool TryGetAmmo(EquipmentSlot hand, out int loaded, out int capacity)
    {
        loaded = 0;
        capacity = 0;

        var stack = equipment ? equipment.GetStack(hand) : null;
        if (stack == null || !stack.UsesAmmo) return false;

        loaded = stack.loadedAmmo;
        capacity = stack.definition.magazineSize;
        return true;
    }

    /// <summary>재장전 중인지. HUD 와 공격 차단에 쓴다.</summary>
    public bool IsReloading => Time.time < reloadEndTime;

    void TryAttack(EquipmentSlot hand)
    {
        var weaponStack = equipment ? equipment.GetStack(hand) : null;
        var weapon = weaponStack != null ? weaponStack.definition : null;
        bool useWeapon = weapon && weapon.attackDamage > 0f;

        // 보조무기 슬롯이 비어 있으면 우클릭은 아무것도 하지 않는다. 맨손은 주무기 쪽만.
        if (!useWeapon && hand == EquipmentSlot.OffHand) return;

        // GDD 11항: 내구도 0 이면 파손 상태로 공격에 쓸 수 없다.
        if (useWeapon && weaponStack.IsBroken)
        {
            LastMessage = weapon.displayName + " 이(가) 파손되어 쓸 수 없습니다";
            return;
        }

        float interval = useWeapon ? weapon.attackInterval : unarmedInterval;

        // 손별로 쿨다운을 따로 둔다 — 주무기와 보조무기를 번갈아 쓸 수 있어야 한다.
        int handIndex = hand == EquipmentSlot.MainHand ? 0 : 1;
        if (Time.time < nextAttackTime[handIndex]) return;

        // 사용자 요구: 다 쏘고 좌클릭하면 알아서 재장전한다. 탄약이 없으면 그때만 경고.
        if (useWeapon && weaponStack.UsesAmmo && weaponStack.loadedAmmo <= 0)
        {
            if (!TryReload(hand)) LastMessage = weapon.displayName + " 탄약이 없습니다";
            return;
        }

        float staminaCost = useWeapon ? weapon.attackStaminaCost : unarmedStaminaCost;
        if (stats && !stats.TrySpendStamina(staminaCost)) return;

        if (useWeapon && weaponStack.UsesAmmo) weaponStack.loadedAmmo--;

        nextAttackTime[handIndex] = Time.time + interval;
        LastAttackTime = Time.time;

        float range = (useWeapon ? weapon.attackRange : unarmedRange) + rangePadding;
        float damage = useWeapon ? weapon.attackDamage : unarmedDamage;

        // GDD 6항: 팔 부상은 공격 효율을 깎는다.
        if (injuries) damage *= injuries.AttackEfficiency;

        // GDD 10항: 양손 무기만 다중타격. 한손과 맨손은 단일 대상. 원거리는 자기 maxTargets 를 그대로 쓴다.
        bool twoHanded = useWeapon && weapon.handedness == WeaponHandedness.TwoHanded;
        int maxTargets = useWeapon && (twoHanded || weapon.ranged) ? Mathf.Max(1, weapon.maxTargets) : 1;

        // GDD 20항: 맞은 좀비를 밀어내고 경직시킨다. 맨손은 둘 다 없다.
        pendingKnockback = useWeapon ? weapon.knockback : 0f;
        pendingStagger = useWeapon ? weapon.staggerDuration : 0f;

        // 무기별 부채꼴. 총은 좁고 산탄총은 넓다.
        float halfAngle = useWeapon && weapon.attackSpread > 0f ? weapon.attackSpread : attackHalfAngle;

        // 근접은 판정과 같은 부채꼴로 휘두른다. 총은 즉발이라 HitScan 안에서 직선 궤적을 그린다.
        if (swingFx && !(useWeapon && weapon.ranged)) swingFx.Play(range, halfAngle, twoHanded);

        // GDD 18항: 공격은 소리를 낸다. 맞았든 빗나갔든 소음은 발생한다.
        // 사용자 요구: 실내에서 낸 소리는 벽을 넘지 않지만 총성만은 예외다.
        bool gunshot = useWeapon && weapon.ranged && weaponStack.UsesAmmo;
        Noise.Emit(transform.position, useWeapon ? weapon.noise : unarmedNoise, gunshot);

        LastAttackHitCount = useWeapon && weapon.ranged
            ? HitScan(range, halfAngle, damage, maxTargets)
            : HitTargetsInArc(range, halfAngle, damage, maxTargets);

        // GDD 11항: 적에게 적중했을 때만 감소하고, 여러 명을 맞혀도 공격 1회당 1회만 감소한다.
        if (LastAttackHitCount > 0 && useWeapon && weaponStack.HasDurability)
        {
            weaponStack.durability = Mathf.Max(0, weaponStack.durability - 1);
            if (weaponStack.IsBroken) LastMessage = weapon.displayName + " 이(가) 파손되었습니다";
        }
    }

    /// <summary>
    /// 총/활 판정. 조준선을 따라 즉시 날아가는 히트스캔.
    ///
    /// 근접처럼 "부채꼴 안의 아무나" 가 아니라 조준선에 걸린 적만 맞는다. 대신 얇은 선이 아니라
    /// 굵은 원기둥(SphereCast) 으로 쏴서 조금 빗나가도 맞게 한다 — 권총이 빡빡하다는 피드백 반영.
    /// 산탄총은 여러 발을 부채꼴 양끝까지 균등하게 뿌린다.
    /// 엄폐물이 더 가까우면 그 뒤의 적은 맞지 않는다.
    /// </summary>
    int HitScan(float range, float halfAngle, float damage, int pellets)
    {
        candidates.Clear();

        for (int p = 0; p < pellets; p++)
        {
            // 한 발이면 조준선 그대로, 여러 발이면 -halfAngle ~ +halfAngle 을 균등 분할.
            float angle = pellets <= 1 ? 0f : Mathf.Lerp(-halfAngle, halfAngle, p / (float)(pellets - 1));
            var direction = Quaternion.AngleAxis(angle, Vector3.up) * AimDirection();

            float travel;
            var zombie = FirstZombieAlong(direction, range, out travel);
            if (zombie && !candidates.Contains(zombie)) candidates.Add(zombie);

            // 보이는 선과 판정을 같은 값으로 그린다 — 어긋나면 "맞췄는데 안 맞음" 이 생긴다.
            if (swingFx) swingFx.PlayTracer(transform.position, transform.position + direction * travel);
        }

        for (int i = 0; i < candidates.Count; i++) Hit(candidates[i], damage);
        return candidates.Count;
    }

    /// <summary>
    /// 총알이 나가는 방향. 커서 링이 찍힌 지점을 향한다 — 평평한 transform.forward 로 쏘면
    /// 플랫폼 위나 벽 위를 겨눠도 총알은 수평으로만 날아가서 빗나간다.
    /// </summary>
    Vector3 AimDirection()
    {
        if (!aim) return transform.forward;

        Vector3 muzzle = transform.position;
        Vector3 delta = aim.AimPoint - muzzle;
        return delta.sqrMagnitude > 0.01f ? delta.normalized : transform.forward;
    }

    /// <summary>한 발이 처음 맞히는 좀비. 엄폐물에 가려져 있으면 제외한다.</summary>
    Zombie FirstZombieAlong(Vector3 direction, float range, out float travel)
    {
        travel = range;

        // 굵은 구체로 "대충 겨눴는지" 를 보고, 가림은 아래 HasLineOfSight 가 얇은 선으로 따로 본다.
        // 굵은 구체로 엄폐 판정까지 하면 높이 1m 블록이 반지름 0.5 구체에 걸려서
        // 그 뒤에 서 있는 2m 좀비를 못 맞힌다 (실제로 겪었다).
        int count = Physics.SphereCastNonAlloc(transform.position, hitscanRadius, direction, hitBuffer,
                                               range, ~0, QueryTriggerInteraction.Ignore);

        Zombie nearest = null;
        float nearestDistance = float.MaxValue;
        float blockedAt = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var collider = hitBuffer[i].collider;
            if (!collider || collider.transform == transform) continue; // 자기 자신

            var zombie = collider.GetComponent<Zombie>();
            if (!zombie || zombie.IsDead)
            {
                // 궤적을 어디까지 그릴지에만 쓴다 (엄폐 판정은 HasLineOfSight 가 따로 한다).
                if (collider.gameObject.layer != WorldItemLayer()
                    && hitBuffer[i].distance > 0f && hitBuffer[i].distance < blockedAt)
                    blockedAt = hitBuffer[i].distance;
                continue;
            }

            // 시작 지점에 겹친 콜라이더는 distance 가 0 으로 보고된다 (Unity 규약).
            float distance = hitBuffer[i].distance;
            if (distance >= nearestDistance) continue;
            if (!HasLineOfSight(zombie)) continue;

            nearest = zombie;
            nearestDistance = distance;
        }

        // 궤적 끝: 맞은 좀비 → 엄폐물 → 최대 사거리 순으로 가까운 것
        if (nearest && nearestDistance < travel) travel = nearestDistance;
        if (blockedAt < travel) travel = blockedAt;

        return nearest;
    }

    /// <summary>
    /// 가림 판정. 어깨 높이에서 대상 어깨 높이로 얇은 선을 쏴서, 사이에 엄폐물이 있으면 false.
    /// 다른 좀비는 엄폐물로 보지 않는다 — 앞에 선 좀비는 거리 비교로 이미 우선된다.
    /// </summary>
    bool HasLineOfSight(Zombie target)
    {
        Vector3 from = transform.position + Vector3.up * aimHeight;
        Vector3 to = target.transform.position + Vector3.up * aimHeight;

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.01f) return true;

        int count = Physics.RaycastNonAlloc(from, delta / distance, hitBuffer, distance,
                                            ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            var collider = hitBuffer[i].collider;
            if (!collider) continue;
            if (collider.transform == transform) continue;              // 자기 자신
            if (collider.GetComponentInParent<Zombie>()) continue;      // 좀비는 막지 않는다
            if (collider.gameObject.layer == WorldItemLayer()) continue; // 떨어진 물건도 막지 않는다

            return false;
        }

        return true;
    }

    static int WorldItemLayer()
    {
        if (worldItemLayer == int.MinValue) worldItemLayer = LayerMask.NameToLayer("WorldItem");
        return worldItemLayer;
    }

    /// <summary>부채꼴 안의 적을 가까운 순으로 최대 maxTargets 명까지 때린다. 맞은 수를 반환.</summary>
    int HitTargetsInArc(float range, float halfAngle, float damage, int maxTargets)
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, range, overlapBuffer);

        candidates.Clear();
        for (int i = 0; i < count; i++)
        {
            var zombie = overlapBuffer[i].GetComponent<Zombie>();
            if (!zombie || zombie.IsDead || candidates.Contains(zombie)) continue;

            Vector3 toTarget = zombie.transform.position - transform.position;
            toTarget.y = 0f;
            if (Vector3.Angle(aim ? aim.AimDirection : transform.forward, toTarget) > halfAngle) continue;

            // 벽 너머는 못 때린다. 거리와 각도만 보면 벽을 통과해 맞는다 (히트스캔은 이미 본다).
            if (!HasLineOfSight(zombie)) continue;

            candidates.Add(zombie);
        }

        if (candidates.Count == 0) return 0;

        sortOrigin = transform.position;
        candidates.Sort(distanceComparison);

        int hits = Mathf.Min(maxTargets, candidates.Count);
        for (int i = 0; i < hits; i++) Hit(candidates[i], damage);

        return hits;
    }

    void Hit(Zombie zombie, float damage) =>
        zombie.TakeDamage(damage, transform.position, pendingKnockback, pendingStagger);

    int CompareByDistance(Zombie a, Zombie b) =>
        (a.transform.position - sortOrigin).sqrMagnitude
        .CompareTo((b.transform.position - sortOrigin).sqrMagnitude);
}
