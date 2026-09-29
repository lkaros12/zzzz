using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GDD 3항 이동 + 7항 회피/피격 경직.
/// WASD는 화면(카메라) 기준. GDD 3항은 월드 기준이었으나 플레이 감각 문제로 변경됨. 가속/감속 없음.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerStats))]
public class PlayerMotor : MonoBehaviour
{
    [Header("이동")]
    [SerializeField] float walkSpeed = 3.5f;
    [SerializeField] float runSpeed = 6.5f;
    [SerializeField] float runStaminaPerSecond = 12f;
    [SerializeField] float turnSpeed = 900f;

    [Header("점프 (Space)")]
    [Tooltip("점프 시작 속도. 도달 높이 ≈ speed^2 / (2 * |gravity|)")]
    [SerializeField] float jumpSpeed = 6.5f;
    // 점프는 스태미나를 쓰지 않는다 (사용자 요구). 달리기/회피만 쓴다.

    [Header("회피 (좌Ctrl)")]
    [SerializeField] float dodgeSpeed = 12f;
    [SerializeField] float dodgeMoveDuration = 0.25f;
    [SerializeField] float dodgeInvulnerableDuration = 0.5f;
    [SerializeField] float dodgeStaminaCost = 20f;

    [Header("피격 경직")]
    [SerializeField] float hitSlowDuration = 0.1f;
    [SerializeField] float hitSlowMultiplier = 0.6f;

    // 피격 후 무적시간. GDD 7항에는 없지만 GDD 23항의 "회피 가능성 확보" 를 위해 넣었다.
    // 이게 없으면 좀비 여러 마리가 각자 쿨다운으로 물어서 손쓸 틈이 없다.
    [Tooltip("피격 후 무적시간. 0 이면 연속 피격이 그대로 들어온다")]
    [SerializeField] float postHitInvulnerability = 0.8f;

    [SerializeField] float gravity = -20f;

    public float RunSpeed => runSpeed;

    [Header("소음 (GDD 18항)")]
    [Tooltip("걷기 소음. 기획서 예시(식칼 5) 보다 작게 둬서 발소리만으로는 멀리 안 퍼지게 한다")]
    [SerializeField] float walkNoise = 4f;
    [Tooltip("달리기 소음. 방망이 공격(10) 정도로 크다")]
    [SerializeField] float runNoise = 14f;
    [Tooltip("발소리를 내는 간격(초). 매 프레임 터뜨리면 낭비다")]
    [SerializeField] float footstepNoiseInterval = 0.45f;

    [Header("카메라 정렬")]
    [SerializeField] Transform cameraTransform; // 비어 있으면 Camera.main 을 사용

    CharacterController cc;
    PlayerStats stats;
    PlayerInjuries injuries;
    PlayerEquipment equipment;
    PlayerGrapple grapple;
    PlayerAim aim;
    PlayerShield shield;
    PlayerAssassinate assassinate;
    PlayerVault vault;

    Vector3 dodgeDirection;
    float dodgeEndTime, invulnerableUntil, hitSlowUntil, verticalVelocity;
    float nextFootstepNoiseTime;

    public bool IsRunning { get; private set; }
    public bool IsDodging => Time.time < dodgeEndTime;
    public bool IsInvulnerable => Time.time < invulnerableUntil;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        stats = GetComponent<PlayerStats>();
        injuries = GetComponent<PlayerInjuries>();
        equipment = GetComponent<PlayerEquipment>();
        grapple = GetComponent<PlayerGrapple>();

        // 씬 재빌드 없이 붙도록 런타임에 보강한다.
        aim = GetComponent<PlayerAim>();
        if (!aim) aim = gameObject.AddComponent<PlayerAim>();
        shield = GetComponent<PlayerShield>();
        if (!shield) shield = gameObject.AddComponent<PlayerShield>();

        // 조준 점선 + 커서 링. 씬 재빌드 없이 붙도록 런타임에 보강한다.
        if (!GetComponent<AimCursorFx>()) gameObject.AddComponent<AimCursorFx>();
        assassinate = GetComponent<PlayerAssassinate>();
        if (!assassinate) assassinate = gameObject.AddComponent<PlayerAssassinate>();

        // 기획 5항 창문 파쿠르. 씬 재빌드 없이 붙도록 런타임에 보강한다.
        vault = GetComponent<PlayerVault>();
        if (!vault) vault = gameObject.AddComponent<PlayerVault>();

        // 기획 8항 시야 판정. 음영을 그리는 쪽(VisionFog)이 이 값을 읽는다.
        if (!GetComponent<VisionField>()) gameObject.AddComponent<VisionField>();
    }

    void Update()
    {
        if (stats.IsDead) return;

        // 저항(그래플) 중에는 붙잡혀 있어서 이동도 회피도 못 한다.
        // GDD 19항: 암살 모션 3초 동안도 움직일 수 없다.
        bool held = (grapple && grapple.IsGrappled) || (assassinate && assassinate.IsAssassinating);
        Vector3 input = held ? Vector3.zero : AlignToCamera(ReadMoveInput());

        if (!held && !IsDodging && WasDodgePressed())
            TryDodge(input);

        if (!held && WasJumpPressed())
            TryJump();

        // 창문을 넘는 동안은 PlayerVault 가 몸을 직접 옮긴다. CharacterController 가
        // 꺼져 있어서 아래 cc.Move 를 그대로 부르면 에러가 난다.
        if (vault && vault.IsVaulting) return;

        Vector3 horizontal = IsDodging ? dodgeDirection * dodgeSpeed : WalkVelocity(input);

        // 지면에 붙어 있을 때 중력을 리셋하지 않으면 경사에서 튄다.
        if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = -1f;
        verticalVelocity += gravity * Time.deltaTime;

        cc.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);

        // 조준은 PlayerAim 이 마우스 방향으로 맞춘다. 조준이 없을 때만 이동 방향을 바라본다.
        if (!aim && horizontal.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(new Vector3(horizontal.x, 0f, horizontal.z));
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }
    }

    Vector3 WalkVelocity(Vector3 input)
    {
        bool moving = input.sqrMagnitude > 0.01f;
        bool holdingRun = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        // GDD 21항: 방어 중에는 달리기 불가.
        if (shield && shield.IsBlocking) holdingRun = false;

        // GDD 3항: 스태미나가 0이면 달리기 종료.
        IsRunning = holdingRun && moving && stats.Stamina > 0f;
        // GDD 10항: 장비가 스태미나 소비량을 보정한다.
        if (IsRunning) stats.SpendStamina(runStaminaPerSecond * Time.deltaTime * StaminaCostMultiplier);

        // GDD 5항: 달리기 중에는 허기/탈수/피곤함이 더 빨리 오른다.
        stats.IsExerting = IsRunning;

        float speed = IsRunning ? runSpeed : walkSpeed;
        if (Time.time < hitSlowUntil) speed *= hitSlowMultiplier;

        // GDD 6항: 다리 부상은 이동에 불이익.
        if (injuries) speed *= injuries.MoveSpeedMultiplier;

        // GDD 10항: 이동속도는 인벤토리 무게가 아니라 장비 고유 속성으로만 보정한다.
        if (equipment) speed *= equipment.MoveSpeedMultiplier;

        // GDD 21항: 방어 자세는 이동속도를 대폭 깎는다.
        if (shield) speed *= shield.MoveSpeedMultiplier;

        EmitFootstepNoise(moving);

        return input * speed;
    }

    void TryDodge(Vector3 input)
    {
        // GDD 21항: 방어 중에는 회피할 수 없다.
        if (shield && shield.IsBlocking) return;

        Vector3 direction = input.sqrMagnitude > 0.01f ? input : transform.forward;
        if (!stats.TrySpendStamina(dodgeStaminaCost * StaminaCostMultiplier)) return;

        dodgeDirection = direction;
        dodgeEndTime = Time.time + dodgeMoveDuration;
        invulnerableUntil = Time.time + dodgeInvulnerableDuration;
    }

    /// <summary>
    /// 점프. 땅에 붙어 있을 때만 되고, 방어 자세/암살 중에는 안 된다.
    /// 공중에서는 CharacterController 가 이미 중력을 적용하므로 수직 속도만 밀어준다.
    /// </summary>
    void TryJump()
    {
        if (shield && shield.IsBlocking) return;

        // 기획 5항: 넘을 수 있는 창문 앞이면 점프가 아니라 창문 넘기가 된다.
        if (vault && vault.TryVault()) return;

        if (!cc.isGrounded) return;

        verticalVelocity = jumpSpeed;
    }

    /// <summary>공중에 떠 있는지. 애니메이션/연출이 붙으면 쓸 수 있다.</summary>
    public bool IsAirborne => cc && !cc.isGrounded;

    float StaminaCostMultiplier => equipment ? equipment.StaminaCostMultiplier : 1f;

    /// <summary>
    /// GDD 7항 피격 경직 + 피격 후 무적. 모든 피격이 이 경로를 지난다 (Zombie.Bite).
    /// 무적 타이머는 회피와 공유하되 Max 로 합쳐 서로를 깎지 않게 한다.
    /// </summary>
    /// <summary>GDD 19항 암살 무적처럼, 지정한 시간만큼 무적을 보장한다 (짧아지지는 않는다).</summary>
    public void SetInvulnerableFor(float seconds) =>
        invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + seconds);

    /// <summary>GDD 18항: 걷기/달리기 소음. 매 프레임 터뜨리지 않고 일정 간격으로만 낸다.</summary>
    void EmitFootstepNoise(bool moving)
    {
        if (!moving) return;
        if (Time.time < nextFootstepNoiseTime) return;

        nextFootstepNoiseTime = Time.time + footstepNoiseInterval;
        Noise.Emit(transform.position, IsRunning ? runNoise : walkNoise);
    }

    public void ApplyHitStagger()
    {
        hitSlowUntil = Time.time + hitSlowDuration;
        invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + postHitInvulnerability);
    }

    /// <summary>GDD 3항은 월드 기준이었으나, 플레이 감각 문제로 화면(카메라) 기준으로 변경.</summary>
    Vector3 AlignToCamera(Vector3 raw)
    {
        if (raw.sqrMagnitude < 0.0001f) return Vector3.zero;
        if (!cameraTransform && Camera.main) cameraTransform = Camera.main.transform;
        if (!cameraTransform) return raw;

        // 아이소메트릭 카메라는 위를 내려보므로 forward 를 그대로 쓰면 XZ 성분이 짧아진다. yaw 만 사용.
        return Quaternion.Euler(0f, cameraTransform.eulerAngles.y, 0f) * raw;
    }

    /// <summary>사용자 요구로 Space 는 점프. 기획서 4항은 Space=회피였으나 변경됐다.</summary>
    static bool WasJumpPressed() =>
        Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

    /// <summary>
    /// 회피는 기획서 7·15항의 0.5초 무적을 가진 기능이라 버리지 않고 좌Ctrl 로 옮겼다.
    /// </summary>
    static bool WasDodgePressed() =>
        Keyboard.current != null && Keyboard.current.leftCtrlKey.wasPressedThisFrame;

    static Vector3 ReadMoveInput()
    {
        var k = Keyboard.current;
        if (k == null) return Vector3.zero;

        Vector3 v = Vector3.zero;
        if (k.wKey.isPressed) v.z += 1f;
        if (k.sKey.isPressed) v.z -= 1f;
        if (k.dKey.isPressed) v.x += 1f;
        if (k.aKey.isPressed) v.x -= 1f;
        return v.sqrMagnitude > 1f ? v.normalized : v;
    }
}
