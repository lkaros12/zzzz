using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>좀비 경계 상태. GDD 17항 (추적 → 마지막 목격 위치 조사 → 경계 해제).</summary>
public enum ZombieAlert
{
    /// <summary>플레이어를 모른다. 이 상태에서만 암살당한다 (GDD 19항).</summary>
    Idle = 0,
    /// <summary>소리를 들었거나 시야에서 놓쳤다. 특정 지점을 조사한다.</summary>
    Investigating = 1,
    /// <summary>플레이어를 보고 있다.</summary>
    Chasing = 2,
}

/// <summary>
/// GDD 7·16·17·18·20·22항 적 + GDD 8항 플레이어 좀비.
/// 이동은 CharacterController 가 하고 방향만 NavMesh 경로에서 받아온다 —
/// NavMeshAgent 로 갈아타면 넉백/돌진/추락 복구를 전부 다시 만들어야 한다.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class Zombie : MonoBehaviour
{
    public struct Carried
    {
        public ItemDefinition definition;
        public int quantity;
        public int durability;
        public int loadedAmmo;
    }

    // Noise 가 매번 FindObjectsByType 을 돌지 않도록 살아 있는 좀비를 모아둔다.
    static readonly List<Zombie> all = new List<Zombie>();
    public static IReadOnlyList<Zombie> All => all;

    [Tooltip("GDD 16항 종류별 수치. 비어 있으면 아래 기본값을 쓴다")]
    [SerializeField] ZombieDefinition definition;

    [Header("기본값 (종류 에셋이 없을 때)")]
    [SerializeField] float maxHealth = 100f;
    [SerializeField] float moveSpeed = 2.5f;
    [SerializeField] float damageMin = 15f;
    [SerializeField] float damageMax = 25f;

    [Header("이동")]
    [SerializeField] float turnSpeed = 420f;
    [SerializeField] float gravity = -20f;

    [Header("감지 (GDD 17항)")]
    [SerializeField] float visionRange = 16f;
    [Tooltip("정면 시야 반각. 이 밖에서 접근하면 보지 못한다")]
    [SerializeField] float visionHalfAngle = 55f;
    [Tooltip("GDD 17항: 마지막 목격 위치에서 이만큼 못 찾으면 경계를 해제한다")]
    [SerializeField] float searchDuration = 5f;
    [Tooltip("조사 지점에 이만큼 가까워지면 도착으로 본다")]
    [SerializeField] float investigateArriveDistance = 1.2f;

    [Header("길찾기 (NavMesh)")]
    [Tooltip("끄면 예전처럼 직선 추적만 한다. A/B 비교용")]
    [SerializeField] bool useNavMesh = true;
    [Tooltip("경로 재계산 간격(초)")]
    [SerializeField] float repathInterval = 0.35f;
    [Tooltip("목표가 이만큼 움직이면 간격을 기다리지 않고 즉시 다시 계산한다")]
    [SerializeField] float repathDistance = 1.5f;
    [Tooltip("꺾임점에 이만큼 가까워지면 다음 꺾임점을 본다")]
    [SerializeField] float cornerArriveDistance = 0.6f;

    [Header("무리 행동 (좀보이드 참고)")]
    [Tooltip("플레이어를 발견했을 때 내는 신음의 소음값. 주변 좀비가 몰려온다")]
    [SerializeField] float groanNoise = 26f;
    [Tooltip("신음 간격(초). 무한 연쇄를 막는다")]
    [SerializeField] float groanInterval = 4f;

    [Header("유휴 배회 (좀보이드 참고)")]
    [Tooltip("가만히 서 있지 않고 이 반경 안을 느리게 돌아다닌다")]
    [SerializeField] float wanderRadius = 6f;
    [SerializeField] float wanderIntervalMin = 3f;
    [SerializeField] float wanderIntervalMax = 8f;
    [Tooltip("배회 속도 배수. 추적보다 훨씬 느리게")]
    [SerializeField, Range(0f, 1f)] float wanderSpeedMultiplier = 0.35f;
    [Tooltip("소리를 조사할 때 그 지점 주변을 이 반경으로 서성인다")]
    [SerializeField] float searchRadius = 2.5f;
    [Tooltip("서성일 때 다음 지점을 고르는 간격. 유휴 배회보다 짧게 둬서 부지런히 움직이게 한다")]
    [SerializeField] float searchStepInterval = 1.4f;

    [Header("공성 (실내로 못 들어갈 때)")]
    [Tooltip("두드릴 개구부는 플레이어로부터 이 거리 안이어야 한다")]
    [SerializeField] float breachRadiusFromPlayer = 8f;
    [Tooltip("두드릴 개구부는 자기로부터도 이 거리 안이어야 한다. 맵 반대편에서 몰려오면 안 된다")]
    [SerializeField] float breachRadiusFromSelf = 14f;
    [Tooltip("두드릴 수 있는 거리. WindowFrame.vaultRange(1.5) 보다 작아야 붙은 자리에서 바로 넘을 수 있다")]
    [SerializeField] float poundRange = 1.3f;
    [Tooltip("개구부 앞 몇 m 에 서는지. 문간 안쪽을 목표로 잡으면 닫힌 문의 카빙에 걸려 경로가 끊긴다")]
    [SerializeField] float siegeStandDistance = 0.9f;
    [Tooltip("두드리는 소음값. 주변 좀비가 몰려온다 (사용자 요구)")]
    [SerializeField] float poundNoise = 26f;
    [Tooltip("두드리는 소리 간격(초)")]
    [SerializeField] float poundNoiseInterval = 1.1f;
    [Tooltip("두드림 소리를 듣고 합류하는 반경")]
    [SerializeField] float siegeCallRadius = 18f;
    [Tooltip("플레이어까지 걸어갈 수 있는지 다시 계산하는 간격(초)")]
    [SerializeField] float reachCheckInterval = 0.5f;
    [Tooltip("창문을 넘는 데 걸리는 시간(초)")]
    [SerializeField] float vaultDuration = 0.6f;
    [Tooltip("넘는 동안 몸이 뜨는 높이")]
    [SerializeField] float vaultArc = 0.45f;

    // ---- 성능: 좀보이드 "Tiered Zombie Updates" 와 같은 방식 ----
    //
    // 실측: 좀비 201마리에서 BehaviourUpdate 가 6.78ms, 프레임 14.81ms — AI 가 절반이다.
    // 멀리 있는 좀비는 몇 프레임에 한 번만 돌리고, 건너뛴 시간은 다음 갱신에 몰아서 쓴다
    // (그래야 느려지지 않는다). 위상을 흩뿌려서 같은 등급이 한 프레임에 몰리지 않게 한다.
    [Header("성능 (거리별 갱신 등급)")]
    [Tooltip("이 거리 안은 매 프레임 갱신한다. 화면에 보이는 범위보다 넉넉하게")]
    [SerializeField] float fullUpdateRange = 26f;
    [Tooltip("이 거리까지는 midUpdateStride 프레임마다 한 번만 갱신한다")]
    [SerializeField] float midUpdateRange = 50f;
    [SerializeField, Min(1)] int midUpdateStride = 4;
    [Tooltip("더 먼 좀비의 갱신 간격(프레임)")]
    [SerializeField, Min(1)] int farUpdateStride = 12;
    [Tooltip("이 거리 밖에서는 렌더러와 애니메이터를 끈다. 화면 밖 좀비를 그릴 이유가 없다 — "
           + "좀비 202마리가 드로우콜을 711→2,509 로 올렸다 (실측)")]
    [SerializeField] float visualRange = 34f;

    Renderer[] visuals;
    Animator[] animators;
    bool visualsShown = true;

    /// <summary>
    /// 한 프레임에 허용할 NavMesh 경로 계산 수.
    ///
    /// 좀보이드는 길찾기를 워커 스레드에서 예산을 두고 돌린다. Unity 의 CalculatePath 는
    /// 메인 스레드 동기 호출이라 스레드로 뺄 수 없으므로 예산만 가져왔다.
    /// 예산을 못 받은 좀비는 지난 경로를 그대로 쓰고 다음 프레임에 다시 시도한다.
    /// </summary>
    const int PathBudgetPerFrame = 12;

    static int pathBudgetFrame = -1;
    static int pathsThisFrame;

    /// <summary>검증용. 지난 프레임에 실제로 계산된 경로 수.</summary>
    public static int PathsLastFrame { get; private set; }

    static bool TakePathBudget()
    {
        if (pathBudgetFrame != Time.frameCount)
        {
            PathsLastFrame = pathsThisFrame;
            pathBudgetFrame = Time.frameCount;
            pathsThisFrame = 0;
        }

        if (pathsThisFrame >= PathBudgetPerFrame) return false;

        pathsThisFrame++;
        return true;
    }

    int updatePhase;
    float skippedTime;

    /// <summary>이번 갱신이 대신하는 시간. 건너뛴 프레임만큼 쌓인다 (Time.deltaTime 자리에 쓴다).</summary>
    float tickDelta;

    // 모든 거리 판정은 y 를 눌러서 수평으로만 재기 때문에, 바닥을 뚫고 빠진 좀비가
    // 수직으로 1100m 아래에 있으면서도 플레이어를 계속 물었다. 수직 차이도 본다.
    [Tooltip("플레이어와 높이 차가 이보다 크면 감지/공격이 통하지 않는다")]
    [SerializeField] float verticalTolerance = 3f;

    [Header("공격 (GDD 7항: 적의 판정은 빡빡하게)")]
    [SerializeField] float attackRange = 1.25f;
    [Tooltip("이 각도 안에 플레이어가 있어야 공격이 나간다")]
    [SerializeField] float attackHalfAngle = 45f;
    // 사용자 결정: 좀비마다 각자 3초 쿨다운. 둘러싸이면 그만큼 위험해진다.
    [SerializeField] float attackInterval = 3f;

    [Tooltip("붙었을 때 즉시 물릴 확률. 실패하면 저항(그래플) 로 넘어간다.")]
    [SerializeField, Range(0f, 1f)] float instantBiteChance = 0.45f;

    [Tooltip("플레이어가 저항에 성공하면 이만큼 비틀거린다")]
    [SerializeField] float escapeStunDuration = 1.6f;

    [Header("피격 반응 (GDD 20항)")]
    [Tooltip("무기의 넉백 거리를 이 시간에 걸쳐 밀어낸다. 짧을수록 튕겨나가는 느낌. 거리는 프레임이 튀어도 보장된다")]
    [SerializeField] float knockbackDuration = 0.12f;

    [Header("돌진 준비 연출 (GDD 22항)")]
    [Tooltip("준비 모션 대신 몸 색을 이 색으로 물들인다. 3D 애니메이션이 없어서 색으로 예고한다")]
    [SerializeField] Color chargeWarningColor = new Color(1f, 0.45f, 0.1f);

    [Header("추락 복구")]
    [Tooltip("이 높이보다 아래로 빠지면 지면 위로 되돌린다")]
    [SerializeField] float fallRecoveryY = -8f;
    [Tooltip("복구 시 놓아줄 높이. 캡슐 중심이 지면 위에 오게 한다")]
    [SerializeField] float groundRecoveryHeight = 1.05f;

    [Header("사망")]
    [SerializeField] float corpseDropForce = 2.2f;

    readonly List<Carried> carried = new List<Carried>();

    CharacterController cc;
    Transform player;
    PlayerMotor playerMotor;
    PlayerInjuries playerInjuries;
    PlayerGrapple playerGrapple;
    PlayerShield playerShield;
    PlayerAssassinate playerAssassinate;

    float nextAttackTime;
    float stunnedUntil;
    Vector3 knockbackDirection;
    float knockbackSpeed;
    float knockbackRemaining;
    float verticalVelocity;
    HitFlash flash;

    Vector3 investigatePoint;
    float searchEndTime;

    float nextGroanTime;
    Vector3 wanderTarget;
    NavMeshPath wanderPath;
    float nextWanderTime;

    NavMeshPath path;
    readonly Vector3[] corners = new Vector3[24];
    int cornerCount;
    Vector3 pathTarget;
    float nextPathTime;

    WindowFrame siegeTarget;
    float nextPoundNoiseTime;
    float nextReachCheckTime;
    float nextBreachPickTime;
    bool playerReachable = true;
    NavMeshPath reachPath;

    WindowFrame vaultWindow;
    Vector3 vaultFrom, vaultTo;
    float vaultStartTime = -1f;

    Vector3 chargeDirection;
    float chargeWindupEndTime;
    float chargeRemaining;
    float nextChargeTime;
    bool chargeTriggered;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsGrappling { get; private set; }

    /// <summary>GDD 17항 경계 상태. GDD 19항 암살은 Chasing 이 아닐 때만 가능하다.</summary>
    public ZombieAlert Alert { get; private set; }

    /// <summary>GDD 19항 암살 조건. 플레이어를 인식하고 있으면 암살할 수 없다.</summary>
    public bool IsAwareOfPlayer => Alert == ZombieAlert.Chasing;

    /// <summary>GDD 19항: 암살 중에는 얼어붙고, 종료 시 즉사한다.</summary>
    public bool IsBeingAssassinated { get; private set; }

    /// <summary>GDD 21항: 마지막 공격이 강한 일반 공격이었는지 (검증/연출용).</summary>
    public bool LastSwingWasHeavy { get; private set; }

    /// <summary>GDD 22항 돌진 중(준비 모션 포함) 인지. 남은 거리가 있으면 준비든 질주든 돌진 상태다.</summary>
    public bool IsCharging => chargeRemaining > 0f;

    /// <summary>준비 모션 구간인지. 이때는 제자리에서 조준만 한다.</summary>
    public bool IsChargeWindup => IsCharging && Time.time < chargeWindupEndTime;

    public ZombieDefinition Definition => definition;

    /// <summary>
    /// 마지막으로 공격을 시작한 시각. ZombieAnimator 가 이 값이 바뀌는 순간 공격 모션을 낸다.
    /// 공격 판정 지점이 두 군데(물기/돌진) 라서 MarkAttack 한 곳에서만 갱신한다.
    /// </summary>
    public float LastAttackTime { get; private set; }

    /// <summary>마지막으로 피해를 입은 시각. 피격 연출을 붙일 때 쓴다.</summary>
    public float LastHitTime { get; private set; }

    /// <summary>살아 있는 좀비 수. 시체가 잠깐 남아 있어도 스폰 상한이 어긋나지 않게 한다.</summary>
    public static int AliveCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i] && !all[i].IsDead) n++;

            return n;
        }
    }

    /// <summary>공격 쿨다운과 공격 모션 신호를 함께 갱신한다. 공격을 시작하는 모든 경로가 여기를 지난다.</summary>
    void MarkAttack()
    {
        nextAttackTime = Time.time + attackInterval;
        LastAttackTime = Time.time;
    }
    public bool CanBeAssassinated => definition ? definition.canBeAssassinated : true;

    /// <summary>GDD 8항 플레이어 좀비인지. 이름/색을 다르게 하고 소지품을 들고 있다.</summary>
    public bool IsPlayerCorpse { get; private set; }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!all.Contains(this)) all.Add(this);

        // 갱신 위상. 같은 등급의 좀비가 같은 프레임에 몰리지 않게 흩뿌린다.
        updatePhase = GetInstanceID() & 1023;

        ApplyDefinition();
        Health = maxHealth;
        CachePlayer();
    }

    // 등록/해제는 존재 기준(Awake/OnDestroy) 이다. OnEnable/OnDisable 로 두면 컴포넌트를
    // 잠깐 끄기만 해도 등록부에서 빠져서, 청각·암살·지도가 그 좀비를 없는 것으로 취급한다.
    void OnDestroy() => all.Remove(this);

    /// <summary>종류 에셋의 수치를 인스턴스 필드로 복사한다. 이후 로직은 에셋 유무를 신경 쓰지 않는다.</summary>
    void ApplyDefinition()
    {
        if (!definition) return;

        maxHealth = definition.maxHealth;
        moveSpeed = definition.moveSpeed;
        damageMin = definition.damageMin;
        damageMax = definition.damageMax;
        visionRange = definition.visionRange;
        visionHalfAngle = definition.visionHalfAngle;
    }

    void CachePlayer()
    {
        var stats = FindFirstObjectByType<PlayerStats>();
        if (!stats) return;

        player = stats.transform;
        playerMotor = stats.GetComponent<PlayerMotor>();
        playerInjuries = stats.GetComponent<PlayerInjuries>();
        playerGrapple = stats.GetComponent<PlayerGrapple>();
        playerShield = stats.GetComponent<PlayerShield>();
        playerAssassinate = stats.GetComponent<PlayerAssassinate>();
    }

    void Update()
    {
        if (IsDead) return;

        // GDD 19항: 암살 모션 동안 대상은 아무것도 하지 못한다.
        if (IsBeingAssassinated) return;

        if (!player) { CachePlayer(); return; }

        float playerDistanceSqr = (player.position - transform.position).sqrMagnitude;
        ApplyVisualLod(playerDistanceSqr <= visualRange * visualRange);

        // 좀보이드식 거리별 갱신 등급. 멀리 있는 좀비를 매 프레임 돌리면
        // 200마리에서 AI 가 프레임의 절반을 먹는다 (실측).
        skippedTime += Time.deltaTime;
        if (!ShouldTickNow(playerDistanceSqr)) return;

        // 상한을 둔다. 에디터가 한 번 멈췄다 돌아올 때 tickDelta 가 몇 초가 되면
        // 좀비가 벽을 뚫고 순간이동한다.
        tickDelta = Mathf.Min(skippedTime, 0.25f);
        skippedTime = 0f;

        // 창문을 넘는 동안은 CharacterController 가 꺼져 있다. RecoverIfFallen 이
        // 몸을 지면으로 되돌려 버리면 넘다가 바닥으로 끌려 내려간다.
        if (IsVaultingThrough) { UpdateVault(); return; }

        RecoverIfFallen();

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        UpdateAlert(toPlayer, distance);

        // 돌진은 경직/그래플보다 우선한다 — 이미 발동한 돌진은 끝까지 간다.
        if (IsCharging) { UpdateCharge(); return; }

        // 그래플 중이면 붙어 있기만 한다. 비틀거리는 동안도 멈춘다.
        bool canAct = !IsGrappling && Time.time >= stunnedUntil;
        if (!canAct) { MoveTowards(Vector3.zero); return; }

        // 사용자 요구: 실내 플레이어에게 걸어갈 길이 없으면 문/창문을 두드린다.
        // 추적/조사보다 앞에 둔다 — 안 그러면 벽에 코를 박고 밀기만 한다.
        if (WantsSiege()) { UpdateSiege(); return; }

        if (Alert == ZombieAlert.Chasing)
        {
            FaceTowards(toPlayer);

            if (TryStartCharge(distance)) return;

            if (distance > attackRange * 0.9f)
                MoveTowards(SteerDirection(player.position, toPlayer.normalized));
            else TryAttack(toPlayer, distance);
            return;
        }

        if (Alert == ZombieAlert.Investigating) { UpdateInvestigate(); return; }

        UpdateIdleWander();
    }

    /// <summary>
    /// 이번 프레임에 갱신할 좀비인지. 등급은 플레이어와의 거리로 정하고,
    /// instance id 로 위상을 흩뿌려 같은 등급이 한 프레임에 몰리지 않게 한다
    /// (몰리면 평균은 같아도 주기적으로 프레임이 튄다).
    ///
    /// 붙어 있거나 공성·돌진 중이면 등급을 내리지 않는다 — 판정이 늦으면 눈에 보인다.
    /// </summary>
    bool ShouldTickNow(float distanceSqr)
    {
        if (siegeTarget || IsGrappling || IsCharging || IsVaultingThrough) return true;
        if (distanceSqr <= fullUpdateRange * fullUpdateRange) return true;

        int stride = distanceSqr <= midUpdateRange * midUpdateRange ? midUpdateStride : farUpdateStride;
        return (Time.frameCount + updatePhase) % stride == 0;
    }

    /// <summary>
    /// 멀리 있는 좀비는 그리지도, 애니메이션을 돌리지도 않는다.
    ///
    /// AI 를 등급제로 줄여도 프레임이 거의 안 줄었던 이유가 이것이었다 — 좀비 202마리가
    /// 렌더러 1,010개로 드로우콜을 711 → 2,509, setPass 를 40 → 655 로 올렸다.
    /// 좀보이드가 화면 밖 좀비를 덜 그리는 것과 같은 처방이다.
    ///
    /// 콜라이더와 등록부는 건드리지 않는다. 안 보여도 소리는 듣고 물기도 한다.
    /// </summary>
    void ApplyVisualLod(bool show)
    {
        if (show == visualsShown && visuals != null) return;

        // Spawn 이 Awake 뒤에 모델을 붙이므로 첫 호출에서 모은다.
        if (visuals == null)
        {
            visuals = GetComponentsInChildren<Renderer>(true);
            animators = GetComponentsInChildren<Animator>(true);
        }

        visualsShown = show;
        for (int i = 0; i < visuals.Length; i++) if (visuals[i]) visuals[i].enabled = show;
        for (int i = 0; i < animators.Length; i++) if (animators[i]) animators[i].enabled = show;
    }

    /// <summary>
    /// 바닥 밑으로 빠졌으면 지면 위로 되돌린다. 파괴하지 않는 이유는 시체 좀비가 들고 있는
    /// 소지품을 잃지 않기 위해서다 (GDD 8항 회수 루프).
    /// </summary>
    void RecoverIfFallen()
    {
        if (transform.position.y > fallRecoveryY) return;

        var recovered = new Vector3(transform.position.x, groundRecoveryHeight, transform.position.z);
        cc.enabled = false;
        transform.position = recovered;
        cc.enabled = true;

        verticalVelocity = 0f;
        knockbackRemaining = 0f;
        chargeRemaining = 0f;
        chargeWindupEndTime = 0f;

        // 붙잡고 있었다면 놓아준다 — 뚫고 빠진 채로 계속 물고 있으면 안 된다.
        if (IsGrappling && playerGrapple) playerGrapple.ForceRelease();
    }

    // ---- GDD 17항 감지 / 경계 ----

    /// <summary>시야 판정과 경계 상태 전이. 청각 입력은 Hear 가 따로 넣는다.</summary>
    void UpdateAlert(Vector3 toPlayer, float distance)
    {
        if (CanSeePlayer(toPlayer, distance))
        {
            // 새로 발견한 순간에만 신음한다 — 이미 추적 중이면 조용히 따라온다.
            if (Alert != ZombieAlert.Chasing) Groan();

            Alert = ZombieAlert.Chasing;
            investigatePoint = player.position;
            return;
        }

        // GDD 17항: 시야에서 놓치면 마지막으로 봤던 위치로 이동한다.
        if (Alert == ZombieAlert.Chasing) StartInvestigating(investigatePoint);
    }

    bool CanSeePlayer(Vector3 toPlayer, float distance)
    {
        if (distance > visionRange || distance < 0.01f) return false;
        if (!IsReachableHeight) return false;

        // GDD 17항: 정면 시야만. 뒤에서 접근하면 알아차리지 못한다.
        if (Vector3.Angle(transform.forward, toPlayer) > visionHalfAngle) return false;

        // 좀보이드처럼 벽에 가려지면 못 본다.
        return HasSightLine();
    }

    /// <summary>GDD 18항: 소리가 난 지점을 조사한다. 이미 추적 중이면 무시한다.</summary>
    public bool Hear(Vector3 position) => Hear(position, false);

    /// <summary>
    /// 실제로 조사에 들어갔으면 true.
    ///
    /// 사용자 요구: 실내에 있는 플레이어는 소리로 발각되지 않는다. 다른 방/실외에서 난
    /// 소리는 벽을 넘지 못한다 — 총성과 개구부가 부서지는 소리(piercesWalls) 만 예외다.
    /// </summary>
    public bool Hear(Vector3 position, bool piercesWalls)
    {
        if (IsDead || IsBeingAssassinated) return false;
        if (Alert == ZombieAlert.Chasing) return false;
        if (!piercesWalls && IsMuffledByWalls(position)) return false;

        StartInvestigating(position);
        return true;
    }

    /// <summary>소리가 난 방과 내가 있는 방이 다르면 벽이 막는다. 둘 다 실외면 막히지 않는다.</summary>
    bool IsMuffledByWalls(Vector3 position)
    {
        var source = Room.At(position);
        if (!source) return false;

        return source != Room.At(transform.position);
    }

    void StartInvestigating(Vector3 position)
    {
        Alert = ZombieAlert.Investigating;
        investigatePoint = position;
        searchEndTime = 0f; // 도착한 뒤부터 5초를 센다
    }

    void UpdateInvestigate()
    {
        Vector3 toPoint = investigatePoint - transform.position;
        toPoint.y = 0f;

        if (toPoint.magnitude > investigateArriveDistance)
        {
            var steer = SteerDirection(investigatePoint, toPoint.normalized);
            FaceTowards(steer);
            MoveTowards(steer);
            return;
        }

        // 도착했다. GDD 17항: 여기서 5초간 못 찾으면 경계 해제.
        if (searchEndTime <= 0f) searchEndTime = Time.time + searchDuration;

        // 그 자리를 중심으로 서성이며 둘러본다. 예전에는 제자리에서 계속 회전했는데,
        // 팽이처럼 보여서 좀비답지 않았다 (사용자 지적).
        WanderAround(investigatePoint, searchRadius, searchStepInterval);

        if (Time.time >= searchEndTime) Alert = ZombieAlert.Idle;
    }

    // ---- 공성: 두드리기 → 부서짐 → 넘어 들어오기 (사용자 요구) ----

    /// <summary>검증용. 지금 붙어 있는 개구부.</summary>
    public WindowFrame SiegeTarget => siegeTarget;

    public bool IsVaultingThrough => vaultStartTime >= 0f;

    /// <summary>
    /// 공성을 할 상황인지. 한 번 개구부를 물면 뚫릴 때까지 놓지 않는다 —
    /// 벽에 가려 시야가 끊기는 건 당연하므로 Alert 가 떨어져도 계속 두드려야 한다.
    /// </summary>
    bool WantsSiege()
    {
        if (!siegeTarget) siegeTarget = null;   // 파괴된 개구부 정리

        // 걸어서 갈 수 있게 됐으면 공성을 접는다 (문이 부서졌거나 열렸거나 플레이어가 나왔다).
        // 창문은 부서져도 창턱 때문에 길이 안 열리니, 넘을 대상은 그대로 들고 있는다.
        if (PlayerIsReachable() && (!siegeTarget || !siegeTarget.Passable)) siegeTarget = null;

        if (siegeTarget) return true;

        // 플레이어를 전혀 모르면 공성하지 않는다. 조사 상태도 포함한다 —
        // 쫓다가 문이 닫히는 순간 시야가 끊겨 Chasing 이 바로 풀리기 때문이다 (사용자 요구).
        if (Alert == ZombieAlert.Idle) return false;
        if (PlayerIsReachable()) return false;

        // 매 프레임 후보를 훑으면 개구부마다 경로 계산이 돌아간다. 판정 주기에 맞춰 띄운다.
        if (Time.time < nextBreachPickTime) return false;
        nextBreachPickTime = Time.time + reachCheckInterval;

        siegeTarget = PickBreachPoint();
        return siegeTarget != null;
    }

    /// <summary>
    /// 두드릴 개구부 고르기. 플레이어에 가까운 것을 고르되 내가 걸어갈 수 있는 것만 쓴다.
    ///
    /// 거리만 보면 실외 좀비가 집 안쪽 욕실문을 골라서 현관에 서서 헛손질한다
    /// (실측: 3마리 전부 Door_Bathroom 을 물고 두드리는수 0 이었다).
    /// </summary>
    WindowFrame PickBreachPoint()
    {
        WindowFrame best = null;
        float bestDistance = breachRadiusFromPlayer * breachRadiusFromPlayer;
        float selfLimit = breachRadiusFromSelf * breachRadiusFromSelf;

        var openings = WindowFrame.All;
        for (int i = 0; i < openings.Count; i++)
        {
            var opening = openings[i];
            if (!opening || opening.IsBroken) continue;
            if ((opening.Center - transform.position).sqrMagnitude > selfLimit) continue;

            float distance = (opening.Center - player.position).sqrMagnitude;
            if (distance >= bestDistance) continue;

            Vector3 stand = StandAt(opening, 0f);
            if (!CanWalkTo(stand)) continue;

            best = opening;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>플레이어까지 NavMesh 경로가 끝까지 이어지는지. 매 프레임 재지 않는다.</summary>
    bool PlayerIsReachable()
    {
        if (Time.time < nextReachCheckTime) return playerReachable;

        // 예산을 못 받으면 지난 판정을 그대로 쓴다. 공성 판단은 0.5초 늦어도 눈에 안 보인다.
        if (!TakePathBudget()) return playerReachable;

        nextReachCheckTime = Time.time + reachCheckInterval;
        if (reachPath == null) reachPath = new NavMeshPath();

        playerReachable = player
            && NavMesh.CalculatePath(transform.position, player.position, NavMesh.AllAreas, reachPath)
            && reachPath.status == NavMeshPathStatus.PathComplete;

        return playerReachable;
    }

    /// <summary>
    /// 붙어 설 자리. 개구부 중심은 창턱 위(공중) 이고 문간 안쪽은 카빙에 걸리므로
    /// 내가 있는 쪽 바깥 발밑을 쓴다.
    ///
    /// 모두가 같은 점을 노리면 서로 밀려서 한 마리만 붙는다 (실측: 2번째가 1.52m 로 빠졌다).
    /// 구멍 폭 안에서 좀비마다 다른 칸을 쓴다 — instance id 로 고정해서 매 프레임 흔들리지 않게.
    /// </summary>
    Vector3 StandAt(WindowFrame opening, float lateral)
    {
        Vector3 stand = opening.StandPoint(transform.position, siegeStandDistance, lateral);
        stand.y = transform.position.y;
        return stand;
    }

    Vector3 SiegeStand()
    {
        float half = Mathf.Max(0f, siegeTarget.OpeningWidth * 0.5f - cc.radius - 0.1f);
        float slot = half <= 0f ? 0f : ((GetInstanceID() & 1023) / 1023f * 2f - 1f) * half;

        return StandAt(siegeTarget, slot);
    }

    void UpdateSiege()
    {
        Vector3 approach = SiegeStand();

        Vector3 toOpening = approach - transform.position;
        toOpening.y = 0f;

        if (!siegeTarget.IsAtOpening(transform.position, poundRange))
        {
            var steer = SteerDirection(approach, toOpening.normalized);
            FaceTowards(steer);
            MoveTowards(steer);
            return;
        }

        FaceTowards(toOpening);

        // 뚫려 있으면 넘어 들어간다. 문은 길이 열렸으니 평소 추적으로 돌아간다.
        if (siegeTarget.Passable)
        {
            if (siegeTarget.CanVault(transform.position)) { StartVault(siegeTarget); return; }

            // 창문인데 아직 넘을 자리가 아니면 조금 더 붙는다. 여기서 놓아버리면
            // 추적 → 길 막힘 → 다시 공성 을 무한히 반복한다.
            if (!siegeTarget.Vaultable) { siegeTarget = null; return; }

            MoveTowards(SteerDirection(approach, toOpening.normalized));
            return;
        }

        MoveTowards(Vector3.zero);

        siegeTarget.Pound(this);

        if (Time.time < nextPoundNoiseTime) return;
        nextPoundNoiseTime = Time.time + poundNoiseInterval;

        // 두드리는 소리는 벽을 넘는다. 주변 좀비를 같은 개구부로 불러 모은다.
        Noise.Emit(transform.position, poundNoise, true);
        CallOthersToSiege();
    }

    /// <summary>같이 두드릴 좀비를 부른다. 창문 2마리, 문 3마리가 모여야 부서진다.</summary>
    void CallOthersToSiege()
    {
        float radiusSqr = siegeCallRadius * siegeCallRadius;

        for (int i = 0; i < all.Count; i++)
        {
            var other = all[i];
            if (!other || other == this || other.IsDead) continue;
            if ((other.transform.position - transform.position).sqrMagnitude > radiusSqr) continue;

            other.JoinSiege(siegeTarget);
        }
    }

    /// <summary>두드리는 소리를 듣고 합류한다. 이미 붙어 있거나 걸어 들어갈 수 있으면 무시한다.</summary>
    public void JoinSiege(WindowFrame opening)
    {
        if (IsDead || IsBeingAssassinated || IsVaultingThrough) return;
        if (!opening || opening.IsBroken || siegeTarget) return;
        if (PlayerIsReachable()) return;

        siegeTarget = opening;
    }

    void StartVault(WindowFrame window)
    {
        vaultWindow = window;
        vaultFrom = transform.position;
        vaultTo = window.VaultTarget(vaultFrom, cc.height * 0.5f);
        vaultStartTime = Time.time;

        // 넘는 동안 이 창이 닫히지 않게 잠근다 (플레이어 파쿠르와 같은 규칙).
        window.BeginVault();
        cc.enabled = false;
    }

    void UpdateVault()
    {
        float t = Mathf.Clamp01((Time.time - vaultStartTime) / vaultDuration);

        Vector3 position = Vector3.Lerp(vaultFrom, vaultTo, t);
        position.y += Mathf.Sin(t * Mathf.PI) * vaultArc;
        transform.position = position;

        if (t < 1f) return;

        vaultStartTime = -1f;
        cc.enabled = true;
        verticalVelocity = 0f;

        if (vaultWindow) { vaultWindow.EndVault(); vaultWindow = null; }

        // 넘어 들어왔으니 공성 끝. 다음 프레임부터 평소 추적이다.
        siegeTarget = null;
        nextReachCheckTime = 0f;
    }

    /// <summary>
    /// 시야 판정. 좀보이드처럼 벽에 가려지면 보지 못한다 —
    /// 각도와 거리만 보면 건물 안에 있는 플레이어를 벽 뚫고 인식한다.
    ///
    /// 판정 자체는 Vision 이 한다. 플레이어 시야 음영과 같은 규칙을 써야
    /// "내 눈에 안 보이는 좀비는 나도 못 본다" 가 성립한다.
    /// </summary>
    bool HasSightLine() =>
        player && Vision.CanSee(transform.position, Vision.EyeOffset,
                                player.position, Vision.PlayerEyeOffset, visionRange);

    /// <summary>
    /// 좀보이드식 무리 전파. 플레이어를 발견한 순간 신음을 내서 주변 좀비를 끌어온다.
    /// 한 마리가 본 것이 무리로 번지는 게 좀보이드 전투 압박의 핵심이다.
    ///
    /// 쿨타임을 둬서 신음이 신음을 부르는 무한 연쇄는 막는다 — 들은 좀비는 조사 상태로만 가고
    /// 직접 보지 않으면 다시 신음하지 않는다.
    /// </summary>
    void Groan()
    {
        if (Time.time < nextGroanTime) return;

        nextGroanTime = Time.time + groanInterval;
        Noise.Emit(transform.position, groanNoise);
    }

    /// <summary>
    /// 좀보이드식 유휴 배회. 가만히 서 있으면 허수아비처럼 보이고, 플레이어가
    /// 좀비 위치를 외워버린다. 느리게 주변을 돌아다니게 한다.
    /// </summary>
    void UpdateIdleWander() =>
        WanderAround(transform.position, wanderRadius, Random.Range(wanderIntervalMin, wanderIntervalMax));

    /// <summary>
    /// 중심점 주변을 어슬렁거린다. 유휴 배회와 소리 조사 둘 다 이걸 쓴다.
    ///
    /// 갈 수 없는 지점을 고르면 벽에 붙어 밀면서 방향만 바꿔 제자리에서 도는 것처럼 보인다.
    /// 그래서 경로가 끝까지 이어지는 지점만 고른다.
    /// </summary>
    void WanderAround(Vector3 center, float radius, float interval)
    {
        if (Time.time >= nextWanderTime)
        {
            nextWanderTime = Time.time + interval;

            Vector2 offset = Random.insideUnitCircle * radius;
            var candidate = center + new Vector3(offset.x, 0f, offset.y);

            NavMeshHit hit;
            wanderTarget = NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas)
                ? hit.position
                : transform.position;

            if (!CanWalkTo(wanderTarget)) wanderTarget = transform.position;
        }

        Vector3 toTarget = wanderTarget - transform.position;
        toTarget.y = 0f;
        if (toTarget.magnitude < 0.5f) { MoveTowards(Vector3.zero); return; }

        var steer = SteerDirection(wanderTarget, toTarget.normalized);
        FaceTowards(steer);
        MoveTowards(steer * wanderSpeedMultiplier);
    }

    /// <summary>그 지점까지 경로가 끝까지 이어지는지. 막힌 곳을 고르면 벽을 밀게 된다.</summary>
    bool CanWalkTo(Vector3 target)
    {
        if (wanderPath == null) wanderPath = new NavMeshPath();

        // 예산을 못 받으면 이번에는 그 지점을 고르지 않는다. 배회는 한 박자 쉬어도 되고,
        // 공성 대상 고르기는 0.5초 뒤에 다시 시도한다.
        if (!TakePathBudget()) return false;

        return NavMesh.CalculatePath(transform.position, target, NavMesh.AllAreas, wanderPath)
               && wanderPath.status == NavMeshPathStatus.PathComplete;
    }

    // ---- 길찾기 (NavMesh) ----

    /// <summary>
    /// 목표까지 실제로 걸어갈 방향. NavMesh 가 구워져 있으면 경로의 다음 꺾임점 쪽을,
    /// 없으면 예전처럼 직선 방향을 돌려준다.
    ///
    /// NavMeshAgent 로 갈아타지 않고 경로만 빌려 쓴다 — 이동 자체는 CharacterController 가
    /// 계속 담당해야 넉백/돌진/추락 복구가 그대로 동작한다.
    /// </summary>
    Vector3 SteerDirection(Vector3 target, Vector3 fallbackDirection)
    {
        if (!useNavMesh) return fallbackDirection;

        if (path == null) path = new NavMeshPath();

        // 매 프레임 경로를 다시 계산하면 비싸다. 간격을 두고, 목표가 크게 움직이면 즉시 갱신한다.
        bool targetMoved = (target - pathTarget).sqrMagnitude > repathDistance * repathDistance;
        // 예산을 못 받으면 지난 경로를 그대로 쓰고 다음 프레임에 다시 시도한다
        // (nextPathTime 을 밀지 않는다). 좀보이드가 길찾기를 예산제로 돌리는 것과 같은 이유다.
        if ((Time.time >= nextPathTime || targetMoved) && TakePathBudget())
        {
            nextPathTime = Time.time + repathInterval;
            pathTarget = target;

            cornerCount = 0;
            if (NavMesh.CalculatePath(transform.position, target, NavMesh.AllAreas, path))
                cornerCount = path.GetCornersNonAlloc(corners);
        }

        // 경로가 없으면 직선으로 밀어붙인다 (NavMesh 밖에 서 있는 경우 등).
        if (cornerCount < 2) return fallbackDirection;

        // corners[0] 은 현재 위치다. 도착한 꺾임점은 건너뛴다.
        int index = 1;
        while (index < cornerCount - 1)
        {
            Vector3 flat = corners[index] - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > cornerArriveDistance * cornerArriveDistance) break;
            index++;
        }

        Vector3 toCorner = corners[index] - transform.position;
        toCorner.y = 0f;
        if (toCorner.sqrMagnitude < 0.0001f) return fallbackDirection;

        return toCorner.normalized;
    }

    // ---- GDD 22항 돌진 ----

    bool TryStartCharge(float distance)
    {
        if (!definition || !definition.canCharge) return false;
        if (distance > definition.chargeTriggerRange) return false;

        // GDD 22항: 첫 조우에는 바로, 이후에는 개별 쿨타임.
        if (chargeTriggered && Time.time < nextChargeTime) return false;

        chargeTriggered = true;
        nextChargeTime = Time.time + definition.chargeCooldown;
        chargeWindupEndTime = Time.time + definition.chargeWindup;

        // 거리를 여기서 장전한다. 준비 구간에는 소비하지 않고, IsCharging 을 켜두는 역할도 한다.
        chargeRemaining = definition.chargeDistance;

        // 준비 모션 대신 몸을 물들여 예고한다 (GDD 22항: 보고 대응할 수 있어야 한다).
        if (!flash) flash = gameObject.AddComponent<HitFlash>();
        flash.Play(chargeWarningColor, definition.chargeWindup);
        return true;
    }

    void UpdateCharge()
    {
        // 준비 모션 중에는 플레이어를 계속 조준하되 움직이지 않는다.
        if (Time.time < chargeWindupEndTime)
        {
            Vector3 aim = player.position - transform.position;
            aim.y = 0f;
            if (aim.sqrMagnitude > 0.01f)
            {
                FaceTowards(aim);
                chargeDirection = aim.normalized;
            }

            MoveTowards(Vector3.zero);
            return;
        }

        float step = Mathf.Min(chargeRemaining, definition.chargeSpeed * tickDelta);
        chargeRemaining -= step;

        if (tickDelta > 0f) MoveRaw(chargeDirection * (step / tickDelta));

        TryChargeHit();

        // GDD 22항: 돌진 종료 후 2초 경직 — 잘 피했으면 반격 기회가 된다.
        if (chargeRemaining <= 0f)
        {
            chargeRemaining = 0f;
            stunnedUntil = Time.time + definition.chargeRecovery;
        }
    }

    void TryChargeHit()
    {
        if (Time.time < nextAttackTime) return;

        if (!IsReachableHeight) return;
        if (!CanStrikePlayer) return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.magnitude > attackRange + 0.4f) return;

        MarkAttack();

        if (playerMotor && playerMotor.IsInvulnerable) return;

        // 돌진은 붙잡지 않고 그대로 들이받는다. 피해도 물기보다 크다 (GDD 22항).
        Strike(definition ? definition.chargeBlockImpact : 1.5f,
               definition ? Random.Range(definition.chargeDamageMin, definition.chargeDamageMax)
                          : Random.Range(damageMin, damageMax));

        // 부딪히면 그 자리에서 멈춘다.
        chargeRemaining = 0f;
        stunnedUntil = Time.time + definition.chargeRecovery;
    }

    void FaceTowards(Vector3 direction)
    {
        var look = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.z));
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * tickDelta);
    }

    void MoveTowards(Vector3 direction)
    {
        Vector3 horizontal = direction * moveSpeed;

        // GDD 20항 넉백. 밀리는 동안은 추적 이동을 덮어쓴다.
        // 남은 시간이 아니라 남은 "거리" 로 관리한다 — 시간 창으로 재면 프레임이 한 번 튈 때
        // 창이 통째로 지나가 넉백이 아예 안 들어간다 (실제로 겪었다).
        if (knockbackRemaining > 0f && tickDelta > 0f)
        {
            float step = Mathf.Min(knockbackRemaining, knockbackSpeed * tickDelta);
            knockbackRemaining -= step;
            horizontal = knockbackDirection * (step / tickDelta);
        }

        MoveRaw(horizontal);
    }

    /// <summary>중력만 얹어 그대로 움직인다. 돌진은 넉백/이동속도 보정을 받지 않는다.</summary>
    void MoveRaw(Vector3 horizontal)
    {
        if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -1f;
        verticalVelocity += gravity * tickDelta;

        // 낙하 거리는 tickDelta 의 제곱으로 커진다 (속도도 tickDelta 로 늘고 거리도 곱해지므로).
        // 갱신 등급이 내려간 좀비는 tickDelta 가 12배라 낙하가 144배가 되고, 수직 성분이
        // 수평을 눌러버려서 제자리에 붙는다 (실측: 55m 밖 좀비가 10.29초에 0.22m).
        // 땅에 붙어 있을 때는 지면에 붙일 만큼만 내린다.
        float fall = verticalVelocity * tickDelta;
        if (cc.isGrounded) fall = Mathf.Max(fall, -0.05f);

        cc.Move(horizontal * tickDelta + Vector3.up * fall);
    }

    /// <summary>
    /// 때릴 수 있는 상태인지. 벽을 사이에 두고는 물 수 없다 —
    /// 거리와 각도만 보면 벽 너머 플레이어를 때린다 (플레이어 쪽 암살/근접도 같은 구멍이 있었다).
    /// </summary>
    bool CanStrikePlayer =>
        player && Vision.CanSee(transform.position, Vision.EyeOffset,
                                player.position, Vision.PlayerEyeOffset, attackRange + 0.5f);

    /// <summary>플레이어와 높이 차가 납득할 범위인지. 바닥을 뚫고 빠진 좀비를 걸러낸다.</summary>
    bool IsReachableHeight =>
        player && Mathf.Abs(player.position.y - transform.position.y) <= verticalTolerance;

    void TryAttack(Vector3 toPlayer, float distance)
    {
        if (Time.time < nextAttackTime) return;
        if (!IsReachableHeight) return;
        if (!CanStrikePlayer) return;

        // GDD 7항: 적의 공격은 빡빡하게 — 실제 사거리와 각도 안에 들어와야 한다.
        if (distance > attackRange) return;
        if (Vector3.Angle(transform.forward, toPlayer) > attackHalfAngle) return;

        MarkAttack();

        // GDD 7항 회피 무적시간.
        if (playerMotor && playerMotor.IsInvulnerable) return;

        // GDD 21항 표: 일반 공격(낮음) 과 강한 일반 공격(중간) 이 갈린다.
        bool heavySwing = definition && Random.value < definition.heavySwingChance;
        float impact = definition
            ? (heavySwing ? definition.heavySwingBlockImpact : definition.blockImpact)
            : 0.3f;

        // GDD 21항: 방패로 막히면 물기도 저항도 발생하지 않는다.
        if (IsBlocked(impact)) return;

        LastSwingWasHeavy = heavySwing;

        // 붙었다: 일정 확률로 즉시 물고, 아니면 저항 상태로 넘긴다.
        // 강한 공격은 붙잡지 않고 그대로 큰 피해를 준다.
        if (heavySwing || Random.value < instantBiteChance)
        {
            Bite(Random.Range(damageMin, damageMax) * (heavySwing && definition ? definition.heavySwingDamageMultiplier : 1f));
            return;
        }

        if (playerGrapple && playerGrapple.Begin(this)) IsGrappling = true;
        else Bite();
    }

    /// <summary>GDD 21항 방패 판정. 막혔으면 true — 이 공격은 없던 일이 된다.</summary>
    bool IsBlocked(float blockImpact) =>
        playerShield && playerShield.TryBlock(transform.position, blockImpact);

    /// <summary>돌진 적중. 붙잡지 않고 그대로 들이받는다. 막히면 피해가 없다.</summary>
    void Strike(float blockImpact, float damage)
    {
        if (IsBlocked(blockImpact)) return;

        Bite(damage);
    }

    /// <summary>물기. 부위는 GDD 6항대로 먼저 판정하고 방어력/부상은 ResolveHit 이 처리한다.</summary>
    public void Bite() => Bite(Random.Range(damageMin, damageMax));

    /// <summary>피해량을 지정해 문다. 돌진처럼 일반 공격과 피해가 다른 경로가 쓴다.</summary>
    public void Bite(float damage)
    {
        if (playerInjuries) playerInjuries.ResolveBite(RollHitPart(), damage);
        if (playerMotor) playerMotor.ApplyHitStagger();
    }

    public void OnGrappleEnded(bool playerEscaped)
    {
        IsGrappling = false;
        nextAttackTime = Time.time + attackInterval;

        if (playerEscaped) stunnedUntil = Time.time + escapeStunDuration;
    }

    // ---- GDD 19항 암살 ----

    /// <summary>암살 모션 시작. 대상을 얼리고 플레이어를 바라보게 세운다.</summary>
    public void BeginAssassinated()
    {
        IsBeingAssassinated = true;

        if (IsGrappling && playerGrapple) playerGrapple.ForceRelease();
        IsGrappling = false;
        knockbackRemaining = 0f;
        chargeRemaining = 0f;
        chargeWindupEndTime = 0f;
    }

    /// <summary>GDD 19항: 모션 종료 후 즉사.</summary>
    public void FinishAssassinated()
    {
        IsBeingAssassinated = false;
        Health = 0f;
        Die();
    }

    /// <summary>넉백/경직 없는 피해 (도트딜, 디버그 등).</summary>
    public void TakeDamage(float amount) => TakeDamage(amount, transform.position, 0f, 0f);

    /// <summary>
    /// GDD 20항 피격. 무기의 넉백(m) 과 경직(초) 을 함께 받는다.
    ///
    /// 넉백은 새 값이 기존 넉백을 덮어쓰고, 경직은 중첩되지 않고 더 긴 쪽으로 갱신된다.
    /// 붙잡고 있는 동안은 밀리지 않는다 — 물고 있으면서 뒤로 미끄러지면 이상하다.
    /// </summary>
    public void TakeDamage(float amount, Vector3 attackerPosition, float knockback, float staggerDuration)
    {
        if (IsDead || amount <= 0f) return;
        if (IsBeingAssassinated) return; // 암살 연출 중에는 다른 피해를 받지 않는다

        Health = Mathf.Max(0f, Health - amount);

        // 맞았으면 소리가 난 것과 같다 — 뒤에서 때리면 그때부터 인식한다.
        if (Alert != ZombieAlert.Chasing) StartInvestigating(attackerPosition);

        // 피격 피드백이 없으면 데미지가 들어가는지 눈으로 알 수 없다.
        LastHitTime = Time.time;
        if (!flash) flash = gameObject.AddComponent<HitFlash>();
        flash.Play();

        if (staggerDuration > 0f)
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + staggerDuration);

        bool knockbackImmune = definition && definition.immuneToKnockback;
        if (knockback > 0f && !knockbackImmune && !IsGrappling && knockbackDuration > 0f)
        {
            Vector3 away = transform.position - attackerPosition;
            away.y = 0f;

            // 정확히 겹쳐 있으면 방향이 없다. 좀비가 바라보는 반대쪽으로 밀어낸다.
            if (away.sqrMagnitude < 0.0001f) away = -transform.forward;

            knockbackDirection = away.normalized;
            knockbackSpeed = knockback / knockbackDuration;
            knockbackRemaining = knockback; // 새 넉백이 남은 넉백을 갱신한다 (GDD 20항)
        }

        if (Health > 0f) return;

        Die();
    }

    void Die()
    {
        IsDead = true;

        if (IsGrappling && playerGrapple) playerGrapple.ForceRelease();
        IsGrappling = false;

        // 넘는 중에 죽으면 창문이 영구히 잠긴 채로 남는다 — 잠금을 풀어준다.
        if (vaultWindow) { vaultWindow.EndVault(); vaultWindow = null; }
        vaultStartTime = -1f;
        siegeTarget = null;

        if (playerAssassinate) playerAssassinate.OnTargetLost(this);

        DropCarried();

        // 사망 애니메이션이 있으면 그게 끝날 때까지 시체를 남긴다. Update 는 IsDead 에서
        // 바로 빠지고, 전투/소음/암살/지도도 모두 IsDead 를 걸러 보므로 끼어들지 않는다.
        float linger = definition && definition.model ? definition.deathLinger : 0f;
        if (linger <= 0f || !Application.isPlaying) { Destroy(gameObject); return; }

        if (cc) cc.enabled = false;   // 시체를 밀고 다니지 않도록
        Destroy(gameObject, linger);
    }

    /// <summary>GDD 8항: 플레이어 좀비가 들고 있던 소지품을 월드에 떨어뜨린다 (회수 경로).</summary>
    void DropCarried()
    {
        for (int i = 0; i < carried.Count; i++)
        {
            var entry = carried[i];
            if (!entry.definition || entry.quantity <= 0) continue;

            Vector3 origin = transform.position + Vector3.up * 0.6f;
            Vector3 velocity = new Vector3(Random.Range(-1f, 1f), 1.1f, Random.Range(-1f, 1f)).normalized * corpseDropForce;
            WorldItem.Spawn(entry.definition, entry.quantity, origin, velocity)
                     .SetDurability(entry.durability)
                     .SetAmmo(entry.loadedAmmo);
        }
        carried.Clear();
    }

    public void AddCarried(ItemDefinition definition, int quantity, int durability) =>
        AddCarried(definition, quantity, durability, -1);

    /// <summary>내구도와 잔탄을 같이 받는다. 둘 다 개체 상태라 회수 루프에서 보존되어야 한다.</summary>
    public void AddCarried(ItemDefinition definition, int quantity, int durability, int loadedAmmo)
    {
        if (!definition || quantity <= 0) return;

        carried.Add(new Carried
        {
            definition = definition,
            quantity = quantity,
            durability = durability,
            loadedAmmo = loadedAmmo < 0 ? definition.magazineSize : loadedAmmo,
        });
    }

    public int CarriedCount => carried.Count;

    /// <summary>저장용. 들고 있는 소지품 목록.</summary>
    public IReadOnlyList<Carried> CarriedItems => carried;

    /// <summary>저장 복원용. 체력을 저장된 값으로 되돌린다.</summary>
    public void LoadHealth(float savedHealth) => Health = Mathf.Max(1f, savedHealth);

    /// <summary>부위별 가중치. 몸통이 가장 잘 맞고 머리가 가장 안 맞는다.</summary>
    static BodyPart RollHitPart()
    {
        float roll = Random.value;
        if (roll < 0.35f) return BodyPart.Torso;
        if (roll < 0.50f) return BodyPart.LeftArm;
        if (roll < 0.65f) return BodyPart.RightArm;
        if (roll < 0.78f) return BodyPart.LeftLeg;
        if (roll < 0.91f) return BodyPart.RightLeg;
        return BodyPart.Head;
    }

    /// <summary>
    /// 3D 에셋이 없으므로 캡슐 프리미티브로 만든다.
    /// 빌더(에디트 모드) 와 런타임 양쪽에서 호출되므로 모드에 맞는 파괴 함수를 쓰고,
    /// 색은 material(인스턴스) 이 아니라 공유 머티리얼 에셋으로 받는다 — 에디트 모드에서 머티리얼이 누출된다.
    /// </summary>
    public static Zombie Spawn(Vector3 position, Material material, bool playerCorpse = false) =>
        Spawn(position, null, material, playerCorpse);

    /// <summary>GDD 16항 종류를 지정해 생성한다. 종류 에셋이 머티리얼을 들고 있으면 그것을 쓴다.</summary>
    public static Zombie Spawn(Vector3 position, ZombieDefinition type, Material material, bool playerCorpse = false)
    {
        // 플레이어 좀비는 전용 색을 유지한다 (GDD 8항). 그 외에는 종류 색을 쓴다.
        var chosen = material;
        if (!playerCorpse && type && type.placeholderMaterial) chosen = type.placeholderMaterial;

        // 플레이어 좀비는 "내 시체" 라는 걸 색으로 알려야 해서 캡슐을 그대로 쓴다.
        var model = playerCorpse || !type ? null : type.model;

        // 콜라이더 크기는 종류 데이터에서 온다. 모델이 없으면 기존 캡슐 치수 그대로.
        float height = model ? Mathf.Max(0.4f, type.bodyHeight) : 2f;
        float radius = model ? Mathf.Clamp(type.bodyRadius, 0.05f, height * 0.5f - 0.01f) : 0.5f;
        float centerHeight = height * 0.5f;

        GameObject go;
        if (model)
        {
            go = new GameObject();

            var visual = Instantiate(model, go.transform, false);
            visual.name = "Model";
            // 모델 원점은 발밑이고 콜라이더 원점은 몸 중심이다. 그 차이만큼 내려 붙인다.
            visual.transform.localPosition = Vector3.down * centerHeight;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * Mathf.Max(0.01f, type.modelScale);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);

            var primitiveCollider = go.GetComponent<CapsuleCollider>();
            if (Application.isPlaying) Destroy(primitiveCollider);
            else DestroyImmediate(primitiveCollider);

            var meshRenderer = go.GetComponent<Renderer>();
            if (meshRenderer && chosen) meshRenderer.sharedMaterial = chosen;
        }

        go.name = playerCorpse ? "PlayerZombie" : (type ? "Zombie_" + type.id : "Zombie");
        go.transform.position = position + Vector3.up * centerHeight;

        int layer = LayerMask.NameToLayer("Character");
        // 모델의 자식까지 옮긴다. 안 하면 스킨드 메쉬가 Default 로 남아 시야를 막는다.
        if (layer >= 0) SetLayerRecursive(go.transform, layer);

        var cc = go.AddComponent<CharacterController>();
        cc.center = Vector3.zero;
        cc.height = height;
        cc.radius = radius;
        // NavMesh 가 올라갈 수 있다고 보는 높이와 맞춘다. 낮게 두면 경로는 턱을 올라가라
        // 하고 물리가 막아서 계단 옆에 영구 정지한다 (실제로 겪었다).
        // 다만 몸보다 큰 stepOffset 은 물리가 거부하므로 몸 높이로 한 번 깎는다.
        cc.stepOffset = Mathf.Min(NavMesh.GetSettingsByID(0).agentClimb, height * 0.75f);

        // 모델에는 진짜 얼굴이 있으니 플레이스홀더 눈/입을 붙이지 않는다.
        if (!model) CapsuleFace.Build(go.transform, chosen);

        var zombie = go.AddComponent<Zombie>();
        zombie.definition = type;
        zombie.IsPlayerCorpse = playerCorpse;

        // AddComponent 가 Awake 를 먼저 돌려버려서, 그 시점엔 definition 이 아직 null 이다.
        // 그래서 종류 수치를 여기서 한 번 더 반영한다 — 이게 없으면 런타임 생성 좀비가
        // 전부 기본값(체력 100 등) 으로 돈다. 에디트 모드는 Awake 가 아예 안 돌아서도 필요하다.
        zombie.ApplyDefinition();
        zombie.Health = zombie.maxHealth;
        // 추락 복구 높이도 몸 크기를 따라가야 한다. 2m 캡슐 기준값을 그대로 쓰면 작은 몸이 공중에 뜬다.
        zombie.groundRecoveryHeight = centerHeight;

        if (model)
        {
            var animator = go.GetComponentInChildren<Animator>();
            if (animator)
            {
                // 이동은 CharacterController 가 한다. 루트 모션을 켜면 두 번 움직인다.
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                if (type.animatorController) animator.runtimeAnimatorController = type.animatorController;
            }
            go.AddComponent<ZombieAnimator>().SetWalkSpeedReference(zombie.moveSpeed);
        }

        return zombie;
    }

    static void SetLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursive(root.GetChild(i), layer);
    }
}
