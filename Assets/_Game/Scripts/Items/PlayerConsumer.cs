using UnityEngine;

/// <summary>
/// GDD 11항 인벤토리 "사용" + GDD 6항 치료. 아이템 1개를 소비한다.
/// 사용 시간(GDD 9항)은 V0.1 에서 즉시 처리한다 — 시전 중 취소/이동 제한은 전투(7단계)와 함께.
/// </summary>
[RequireComponent(typeof(PlayerInventory))]
public class PlayerConsumer : MonoBehaviour
{
    PlayerInventory inventory;
    PlayerStats stats;
    PlayerInjuries injuries;
    PlayerAssassinate assassinate;

    /// <summary>UI 에 그대로 띄우는 마지막 결과 메시지.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    /// <summary>메시지가 갱신된 시각. UI 가 일정 시간만 띄우는 데 쓴다.</summary>
    public float LastMessageTime { get; private set; } = -999f;

    void SetMessage(string message)
    {
        LastMessage = message;
        LastMessageTime = Time.time;
    }

    void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        stats = GetComponent<PlayerStats>();
        injuries = GetComponent<PlayerInjuries>();
        assassinate = GetComponent<PlayerAssassinate>();
    }

    public bool TryUse(ItemStack stack)
    {
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

        // 치료 도구는 할 일이 아무것도 없을 때만 거부한다.
        // 부상이 없어도 HP 가 비어 있으면 쓸 수 있어야 한다 — 그렇지 않으면 HP 회복 경로가 아예 없다.
        if (definition.treatUpTo != InjurySeverity.None)
        {
            bool treated = injuries && injuries.Treat(definition.treatUpTo);
            bool canHeal = definition.hpRestore > 0f && stats && stats.Hp < stats.MaxHp;

            if (!treated && !canHeal)
            {
                SetMessage(treated ? "" : "치료할 부상도 없고 체력도 가득합니다");
                return false;
            }
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
}
