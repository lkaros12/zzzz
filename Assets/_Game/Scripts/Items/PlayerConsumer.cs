using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GDD 11항 인벤토리 "사용" + GDD 6항 치료. 음식은 즉시, 치료 도구는 감은 뒤 소비한다.
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public class PlayerConsumer : MonoBehaviour
{
    PlayerInventory inventory;
    PlayerStats stats;
    PlayerInjuries injuries;
    PlayerAssassinate assassinate;
    PlayerGrapple grapple;

    ItemStack treatmentStack;
    ItemDefinition treatmentDefinition;
    float treatmentElapsed;
    int treatmentBiteVersion;

    /// <summary>UI 에 그대로 띄우는 마지막 결과 메시지.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    /// <summary>메시지가 갱신된 시각. UI 가 일정 시간만 띄우는 데 쓴다.</summary>
    public float LastMessageTime { get; private set; } = -999f;

    public float RecoveryRemaining { get; private set; }
    public float RecoverySecondsRemaining { get; private set; }
    public bool IsTreating => treatmentDefinition != null;

    public void ResetMedicalState()
    {
        ClearTreatment();
        RecoveryRemaining = 0f;
        RecoverySecondsRemaining = 0f;
    }

    void SetMessage(string message)
    {
        LastMessage = message;
        LastMessageTime = Time.time;
    }

    void Awake() => EnsureComponents();

    void EnsureComponents()
    {
        inventory = GetComponent<PlayerInventory>();
        stats = GetComponent<PlayerStats>();
        injuries = GetComponent<PlayerInjuries>();
        assassinate = GetComponent<PlayerAssassinate>();
        grapple = GetComponent<PlayerGrapple>();
    }

    void Update()
    {
        TickRecovery();
        if (!IsTreating) return;

        if (HasMoveInput() || (grapple && grapple.IsGrappled)
            || (injuries && injuries.BiteVersion != treatmentBiteVersion))
        {
            CancelTreatment("치료 취소");
            return;
        }

        treatmentElapsed += Time.deltaTime;
        SetMessage(treatmentDefinition.displayName + " 감는 중 "
                   + Mathf.Min(treatmentElapsed, treatmentDefinition.treatmentDuration).ToString("0.0")
                   + " / " + treatmentDefinition.treatmentDuration.ToString("0.0") + "초");

        if (treatmentElapsed >= treatmentDefinition.treatmentDuration) CompleteTreatment();
    }

    public bool TryUse(ItemStack stack)
    {
        if (!inventory) EnsureComponents();

        // GDD 19항: 암살 모션 중에는 다른 동작을 하지 못한다.
        // 소모품 사용은 퀵슬롯/인벤토리/커서 E 가 모두 여기로 모이므로 한 군데서 막는다.
        if (assassinate && assassinate.IsAssassinating)
        {
            SetMessage("암살 중에는 사용할 수 없습니다");
            return false;
        }

        if (stack == null)
        {
            SetMessage("선택된 아이템이 없습니다");
            return false;
        }

        var definition = stack.definition;
        if (!definition || !definition.usable)
        {
            SetMessage((definition ? definition.displayName : "이 아이템") + " 은 사용할 수 없습니다");
            return false;
        }

        // 치료 도구는 감기를 마칠 때까지 아이템과 효과를 건드리지 않는다.
        if (definition.treatUpTo != InjurySeverity.None)
        {
            bool canTreat = injuries && injuries.HasTreatable(definition.treatUpTo);
            bool canStopBleeding = injuries && injuries.BleedingCount > 0;
            bool canHeal = definition.hpRestore > 0f && stats && stats.Hp < stats.MaxHp;

            if (!canTreat && !canStopBleeding && !canHeal)
            {
                SetMessage("치료할 부상도 없고 체력도 가득합니다");
                return false;
            }

            treatmentStack = stack;
            treatmentDefinition = definition;
            treatmentElapsed = 0f;
            treatmentBiteVersion = injuries ? injuries.BiteVersion : 0;
            SetMessage(definition.displayName + " 감는 중 0.0 / "
                       + definition.treatmentDuration.ToString("0.0") + "초");
            return definition.treatmentDuration <= 0f ? CompleteTreatment() : true;
        }

        if (stats)
        {
            stats.RestoreHunger(definition.hungerRestore);
            stats.RestoreThirst(definition.thirstRestore);
            stats.RestoreFatigue(definition.fatigueRestore);
            stats.Heal(definition.hpRestore);
        }

        inventory.TakeFrom(stack, 1);
        SetMessage(definition.displayName + " 사용");
        return true;
    }

    /// <summary>치료 완료 순간만 분리해 에디트 모드에서도 결과를 검증한다.</summary>
    public bool CompleteTreatment()
    {
        if (!inventory) EnsureComponents();
        if (!IsTreating) return false;

        var stack = treatmentStack;
        var definition = treatmentDefinition;
        ClearTreatment();

        if (!definition || inventory.TakeFrom(stack, 1) != 1)
        {
            SetMessage("치료 도구가 없어 치료가 취소됐습니다");
            return false;
        }

        if (injuries)
        {
            injuries.StopOneBleeding();
            injuries.Treat(definition.treatUpTo);
        }

        StartRecovery(definition.hpRestore, definition.recoveryDuration);
        SetMessage(definition.displayName + " 치료 완료");
        return true;
    }

    void CancelTreatment(string message)
    {
        ClearTreatment();
        SetMessage(message);
    }

    void ClearTreatment()
    {
        treatmentStack = null;
        treatmentDefinition = null;
        treatmentElapsed = 0f;
    }

    void StartRecovery(float amount, float duration)
    {
        RecoveryRemaining = Mathf.Max(0f, amount);
        RecoverySecondsRemaining = Mathf.Max(0f, duration);
        if (RecoveryRemaining <= 0f || RecoverySecondsRemaining > 0f) return;

        if (stats) stats.Heal(RecoveryRemaining);
        RecoveryRemaining = 0f;
    }

    void TickRecovery()
    {
        if (RecoveryRemaining <= 0f || RecoverySecondsRemaining <= 0f || !stats || stats.IsDead) return;

        float seconds = Mathf.Min(Time.deltaTime, RecoverySecondsRemaining);
        float amount = RecoveryRemaining * seconds / RecoverySecondsRemaining;
        stats.Heal(amount);
        RecoveryRemaining -= amount;
        RecoverySecondsRemaining -= seconds;
        if (RecoverySecondsRemaining <= 0f) RecoveryRemaining = 0f;
    }

    static bool HasMoveInput()
    {
        var keyboard = Keyboard.current;
        return keyboard != null && (keyboard.wKey.isPressed || keyboard.aKey.isPressed
                                    || keyboard.sKey.isPressed || keyboard.dKey.isPressed);
    }
}
