using UnityEngine;
using UnityEngine.UI;

/// <summary>GDD 16항 좌측 상단 HP/스태미나 바. GDD 23항대로 로직은 건드리지 않고 값만 읽는다.</summary>
public class HudBars : MonoBehaviour
{
    [SerializeField] PlayerStats stats;
    [SerializeField] Image hpFill;
    [SerializeField] Image staminaFill;

    void Update()
    {
        if (!stats) return;
        if (hpFill) hpFill.fillAmount = stats.Hp / stats.MaxHp;
        if (staminaFill) staminaFill.fillAmount = stats.Stamina / stats.MaxStamina;
    }

    public void Bind(PlayerStats playerStats, Image hp, Image stamina)
    {
        stats = playerStats;
        hpFill = hp;
        staminaFill = stamina;
    }
}
