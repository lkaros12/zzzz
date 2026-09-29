using UnityEngine;

/// <summary>플레이어 게임 상태를 Animator에 한 방향으로만 전달한다.</summary>
[RequireComponent(typeof(PlayerStats))]
[RequireComponent(typeof(PlayerCombat))]
[RequireComponent(typeof(PlayerEquipment))]
[RequireComponent(typeof(PlayerMotor))]
public class PlayerAnimator : MonoBehaviour
{
    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int AttackId = Animator.StringToHash("Attack");
    static readonly int AttackOneHandId = Animator.StringToHash("AttackOneHand");
    static readonly int DeadId = Animator.StringToHash("Dead");

    [Tooltip("PlayerMotor의 걷기 속도(m/s). 이 속도를 Speed=1로 본다.")]
    [SerializeField] float walkSpeedReference = 3.5f;
    [SerializeField] float speedSmoothing = 10f;

    PlayerStats stats;
    PlayerCombat combat;
    PlayerEquipment equipment;
    PlayerMotor motor;
    Animator animator;
    Vector3 lastPosition;
    float speed;
    float seenAttackTime;
    bool reportedDead;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        combat = GetComponent<PlayerCombat>();
        equipment = GetComponent<PlayerEquipment>();
        motor = GetComponent<PlayerMotor>();
        animator = GetComponentInChildren<Animator>();
        lastPosition = transform.position;
        seenAttackTime = combat.LastAttackTime;
        reportedDead = stats.IsDead;
    }

    void Update()
    {
        if (!animator) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;
        if (IsTeleport(delta, Time.deltaTime, motor.RunSpeed)) speed = 0f;
        else
        {
            float measured = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            speed = Mathf.Lerp(speed, measured / Mathf.Max(0.01f, walkSpeedReference),
                               1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));
        }
        animator.SetFloat(SpeedId, speed);

        bool dead = stats.IsDead;
        if (dead != reportedDead)
        {
            reportedDead = dead;
            animator.SetBool(DeadId, dead);
            if (dead)
            {
                animator.ResetTrigger(AttackId);
                animator.ResetTrigger(AttackOneHandId);
            }
            if (!dead) animator.Play("Idle", 0, 0f);
        }
        if (dead) return;

        if (combat.LastAttackTime > seenAttackTime)
        {
            seenAttackTime = combat.LastAttackTime;
            bool oneHanded = PlayerEquipment.IsOneHandedMelee(equipment.Get(EquipmentSlot.MainHand));
            animator.ResetTrigger(oneHanded ? AttackId : AttackOneHandId);
            animator.SetTrigger(oneHanded ? AttackOneHandId : AttackId);
        }
    }

    public static bool IsTeleport(Vector3 delta, float deltaTime, float runSpeed)
    {
        float limit = runSpeed * deltaTime * 3f;
        return deltaTime > 0f && delta.sqrMagnitude > limit * limit;
    }
}
