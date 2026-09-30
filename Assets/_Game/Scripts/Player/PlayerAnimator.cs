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
    static readonly int AttackTwoHandId = Animator.StringToHash("AttackTwoHand");
    static readonly int AttackSpeedId = Animator.StringToHash("AttackSpeed");
    static readonly int AimPoseId = Animator.StringToHash("AimPose");
    static readonly int FireId = Animator.StringToHash("Fire");
    static readonly int FireSpeedId = Animator.StringToHash("FireSpeed");
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
    float attackClipLength = 1f;
    float attackOneHandClipLength = 1f;
    float attackTwoHandClipLength = 1f;
    float pistolFireClipLength = 1f;
    float rifleFireClipLength = 1f;
    ItemDefinition lastWeapon;
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
        if (animator && animator.runtimeAnimatorController)
        {
            var clips = animator.runtimeAnimatorController.animationClips;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i].name == "Attack") attackClipLength = clips[i].length;
                else if (clips[i].name == "AttackOneHand") attackOneHandClipLength = clips[i].length;
                else if (clips[i].name == "AttackTwoHand") attackTwoHandClipLength = clips[i].length;
                else if (clips[i].name == "PistolFire") pistolFireClipLength = clips[i].length;
                else if (clips[i].name == "RifleFire") rifleFireClipLength = clips[i].length;
            }
        }
        lastPosition = transform.position;
        seenAttackTime = combat.LastAttackTime;
        lastWeapon = equipment.Get(EquipmentSlot.MainHand);
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

        var weapon = equipment.Get(EquipmentSlot.MainHand);
        int aimPose = AimPoseFor(weapon, equipment.GetStack(EquipmentSlot.OffHand));
        bool dead = stats.IsDead;
        if (weapon != lastWeapon)
        {
            lastWeapon = weapon;
            animator.ResetTrigger(FireId);
        }
        animator.SetInteger(AimPoseId, aimPose);
        bool firearmAttack = combat.IsAttacking && (aimPose == 1 || aimPose == 2);
        if (animator.layerCount > 1)
            animator.SetLayerWeight(1, ShouldUseUpperBody(combat.IsAiming,
                combat.IsAttacking && !firearmAttack, aimPose, dead) ? 1f : 0f);

        if (dead != reportedDead)
        {
            reportedDead = dead;
            animator.SetBool(DeadId, dead);
            if (dead)
            {
                animator.ResetTrigger(AttackId);
                animator.ResetTrigger(AttackOneHandId);
                animator.ResetTrigger(AttackTwoHandId);
                animator.ResetTrigger(FireId);
                if (animator.layerCount > 1) animator.SetLayerWeight(1, 0f);
            }
            if (!dead) animator.Play("Locomotion", 0, 0f);
        }
        if (dead) return;

        if (combat.LastAttackTime > seenAttackTime)
        {
            seenAttackTime = combat.LastAttackTime;
            if (aimPose == 1 || aimPose == 2)
            {
                animator.SetFloat(FireSpeedId, CalculateAttackSpeed(
                    aimPose == 1 ? pistolFireClipLength : rifleFireClipLength, combat.AttackInterval));
                animator.SetTrigger(FireId);
                return;
            }
            animator.ResetTrigger(FireId);
            if (weapon && weapon.ranged) return; // 활 모션은 아직 없다.

            bool oneHanded = PlayerEquipment.IsOneHandedMelee(weapon);
            bool twoHanded = PlayerEquipment.IsTwoHandedMelee(weapon);
            int trigger = oneHanded ? AttackOneHandId : twoHanded ? AttackTwoHandId : AttackId;
            float clipLength = oneHanded ? attackOneHandClipLength
                              : twoHanded ? attackTwoHandClipLength : attackClipLength;
            animator.SetFloat(AttackSpeedId, CalculateAttackSpeed(clipLength, combat.AttackInterval));
            animator.ResetTrigger(AttackId);
            animator.ResetTrigger(AttackOneHandId);
            animator.ResetTrigger(AttackTwoHandId);
            animator.SetTrigger(trigger);
        }
    }

    public static int AimPoseFor(ItemDefinition weapon, ItemStack offHand)
    {
        bool shield = offHand != null && offHand.definition && offHand.definition.isShield && !offHand.IsBroken;
        if (shield && (!weapon || PlayerEquipment.IsOneHandedMelee(weapon))) return 6;
        if (!weapon) return 3;
        if (weapon.ranged)
        {
            if (weapon.ammoId == "ammo_arrow") return 0;
            return weapon.handedness == WeaponHandedness.OneHanded ? 1 : 2;
        }
        if (PlayerEquipment.IsOneHandedMelee(weapon)) return 4;
        return PlayerEquipment.IsTwoHandedMelee(weapon) ? 5 : 3;
    }

    public static bool ShouldUseUpperBody(bool isAiming, bool meleeAttacking, int aimPose, bool dead) =>
        isAiming && !meleeAttacking && aimPose > 0 && !dead;

    public static float CalculateAttackSpeed(float clipLength, float interval)
        => Mathf.Max(1f, clipLength / interval);

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
