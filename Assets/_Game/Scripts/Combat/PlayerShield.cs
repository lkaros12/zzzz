using UnityEngine;

/// <summary>
/// GDD 21항 방패. 방패 슬롯에 장착하고 우클릭 조준으로 방어 자세를 취한다.
/// </summary>
public class PlayerShield : MonoBehaviour
{
    [Tooltip("GDD 21항: 방어 중 이동속도 감소. 초기 제안값 70% 감소")]
    [SerializeField, Range(0f, 1f)] float blockMoveMultiplier = 0.3f;

    [Tooltip("GDD 21항: 방패가 막아내는 정면 범위(전체 각도). 뒤에서 오는 공격은 못 막는다")]
    [SerializeField] float blockAngle = 120f;

    PlayerEquipment equipment;
    PlayerStats stats;
    PlayerGrapple grapple;
    PlayerAssassinate assassinate;
    PlayerAim aim;
    PlayerCombat combat;

    float readyAgainAt;

    /// <summary>UI/디버그용 마지막 방어 결과 메시지.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    /// <summary>검증용. 막아낸 횟수.</summary>
    public int BlockCount { get; private set; }

    void Awake()
    {
        equipment = GetComponent<PlayerEquipment>();
        stats = GetComponent<PlayerStats>();
        grapple = GetComponent<PlayerGrapple>();
        assassinate = GetComponent<PlayerAssassinate>();
        aim = GetComponent<PlayerAim>();
        combat = GetComponent<PlayerCombat>();
    }

    /// <summary>방패 슬롯에 든 방패. 없으면 null.</summary>
    public ItemStack ShieldStack
    {
        get
        {
            var stack = equipment ? equipment.GetStack(EquipmentSlot.OffHand) : null;
            return stack != null && stack.definition && stack.definition.isShield ? stack : null;
        }
    }

    public bool HasShield => ShieldStack != null;

    /// <summary>GDD 21항: 충격 후딜 중에는 방패를 다시 들 수 없다.</summary>
    public bool IsRecovering => Time.time < readyAgainAt;

    /// <summary>우클릭을 누르고 있고, 방패가 멀쩡하고, 후딜이 아닐 때만 방어 자세다.</summary>
    public bool IsBlocking { get; private set; }

    /// <summary>GDD 21항: 방어 중 이동속도 감소. PlayerMotor 가 곱한다.</summary>
    public float MoveSpeedMultiplier => IsBlocking ? blockMoveMultiplier : 1f;

    void Update()
    {
        IsBlocking = false;

        var shield = ShieldStack;
        if (shield == null || shield.IsBroken) return;
        if (stats && stats.IsDead) return;
        if (grapple && grapple.IsGrappled) return; // 붙잡히면 방패를 들 수 없다
        if (assassinate && assassinate.IsAssassinating) return; // GDD 19항: 암살 중에는 다른 동작 없음
        if (IsRecovering) return;

        if (!combat || !combat.IsAiming || combat.IsAttacking) return;

        IsBlocking = true;
    }

    /// <summary>
    /// GDD 21항 방어 판정. 막았으면 true 이고 그 공격은 플레이어에게 들어가지 않는다.
    ///
    /// 막아낸 뒤에는 충격량만큼 방패를 다시 들 수 없다 — 충격량이 큰 공격일수록 길다.
    /// 내구도는 실제로 막았을 때만 줄어든다 (허공 방어 자세로는 줄지 않는다).
    /// </summary>
    public bool TryBlock(Vector3 attackerPosition, float impact)
    {
        if (!IsBlocking || (combat && combat.IsAttacking)) return false;

        // 후딜 검사를 여기서도 한다. Update 만 믿으면 같은 프레임 안의 호출 순서에 따라 뚫린다.
        if (IsRecovering) return false;

        var shield = ShieldStack;
        if (shield == null || shield.IsBroken) return false;

        Vector3 toAttacker = attackerPosition - transform.position;
        toAttacker.y = 0f;

        // GDD 21항: 정면 120° 범위만. 뒤에서 오는 공격은 방어할 수 없다.
        Vector3 blockDirection = aim ? aim.AimDirection : transform.forward;
        if (Vector3.Angle(blockDirection, toAttacker) > blockAngle * 0.5f) return false;

        IsBlocking = false;
        readyAgainAt = Time.time + impact;
        BlockCount++;

        if (shield.HasDurability)
        {
            shield.durability = Mathf.Max(0, shield.durability - 1);
            if (shield.IsBroken)
            {
                LastMessage = shield.definition.displayName + " 이(가) 파손되었습니다";
                return true;
            }
        }

        LastMessage = "방어 성공 (재방어 " + impact.ToString("0.0") + "초)";
        return true;
    }
}
