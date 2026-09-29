using UnityEngine;

/// <summary>플레이어 게임 상태를 Animator에 한 방향으로만 전달한다.</summary>
[RequireComponent(typeof(PlayerStats))]
[RequireComponent(typeof(PlayerCombat))]
[RequireComponent(typeof(PlayerEquipment))]
[RequireComponent(typeof(PlayerMotor))]
public class PlayerAnimator : MonoBehaviour
{
    static readonly int MoveXId = Animator.StringToHash("MoveX");
    static readonly int MoveZId = Animator.StringToHash("MoveZ");
    static readonly int AttackId = Animator.StringToHash("Attack");
    static readonly int AttackOneHandId = Animator.StringToHash("AttackOneHand");
    static readonly int DeadId = Animator.StringToHash("Dead");

    [Tooltip("PlayerMotor의 걷기 속도(m/s). 이 속도를 이동 블렌드 1로 본다.")]
    [SerializeField] float walkSpeedReference = 3.5f;
    [SerializeField] float speedSmoothing = 10f;

    PlayerStats stats;
    PlayerCombat combat;
    PlayerEquipment equipment;
    PlayerMotor motor;
    PlayerAim aim;
    PlayerGrapple grapple;
    PlayerAssassinate assassinate;
    Animator animator;
    Vector3 lastPosition;
    Vector2 movement;
    float seenAttackTime;
    bool reportedDead;

    void Awake()
    {
        stats = GetComponent<PlayerStats>();
        combat = GetComponent<PlayerCombat>();
        equipment = GetComponent<PlayerEquipment>();
        motor = GetComponent<PlayerMotor>();
        aim = GetComponent<PlayerAim>();
        grapple = GetComponent<PlayerGrapple>();
        assassinate = GetComponent<PlayerAssassinate>();
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
        if (IsTeleport(delta, Time.deltaTime, motor.RunSpeed)) movement = Vector2.zero;
        else
        {
            Vector3 local = Time.deltaTime > 0f
                ? transform.InverseTransformDirection(delta) / (Time.deltaTime * Mathf.Max(0.01f, walkSpeedReference))
                : Vector3.zero;
            movement = Vector2.Lerp(movement, new Vector2(local.x, local.z),
                                    1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));
        }
        animator.SetFloat(MoveXId, movement.x);
        animator.SetFloat(MoveZId, movement.y);

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
            if (!dead) animator.Play("Locomotion", 0, 0f);
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

    public void ApplyLookAtIK()
    {
        if (!animator) return;

        bool blocked = stats.IsDead || (grapple && grapple.IsGrappled)
                                  || (assassinate && assassinate.IsAssassinating)
                                  || !aim || aim.AimPoint == Vector3.zero;
        animator.SetLookAtWeight(blocked ? 0f : 1f, 0.2f, 1f, 0f, 0.5f);
        if (blocked) return;

        Vector3 lookAt = aim.AimPoint;
        lookAt.y = transform.position.y + Vision.PlayerEyeOffset;
        animator.SetLookAtPosition(lookAt);
    }
}
