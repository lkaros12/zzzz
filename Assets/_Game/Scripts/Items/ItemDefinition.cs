using UnityEngine;

/// <summary>GDD 10항 무기 손 구분. 양손은 다중타격 + 넉백 + 경직, 한손은 단일 + 약한 경직.</summary>
public enum WeaponHandedness { None = 0, OneHanded = 1, TwoHanded = 2 }

/// <summary>
/// GDD 9항 아이템 데이터. 속성은 하드코딩하지 않고 이 에셋에서 읽는다.
/// V0.1 2단계에서 실제로 쓰는 필드만 둔다 — 무게/크기/내구도/희귀도/가격/사용효과는 해당 단계에서 추가.
/// </summary>
[CreateAssetMenu(fileName = "Item", menuName = "V0.1/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    public string id = "item";
    public string displayName = "아이템";

    [Tooltip("월드 모델 프리팹이 없는 동안 쓰는 플레이스홀더 색")]
    public Color placeholderColor = Color.white;

    [Tooltip("플레이스홀더 색으로 만든 머티리얼. 빌더가 에셋으로 생성해 넣는다.")]
    public Material placeholderMaterial;

    [Tooltip("GDD 11항 칸 기반 인벤토리에서 차지하는 칸 수 (가로, 세로)")]
    public Vector2Int gridSize = Vector2Int.one;

    [Min(1)] public int maxStack = 1;

    [Header("사용 효과 (GDD 9항 소비 아이템)")]
    public bool usable;
    [Tooltip("사용 시 줄어드는 허기")] public float hungerRestore;
    [Tooltip("사용 시 줄어드는 탈수")] public float thirstRestore;
    [Tooltip("사용 시 줄어드는 피곤함")] public float fatigueRestore;
    [Tooltip("사용 시 회복하는 HP")] public float hpRestore;

    [Tooltip("이 등급까지 부상을 치료할 수 있다. None 이면 치료 도구가 아니다. (GDD 6항)")]
    public InjurySeverity treatUpTo = InjurySeverity.None;

    [Header("장비 (GDD 10항)")]
    [Tooltip("None 이면 장비가 아니다")]
    public EquipmentSlot equipSlot = EquipmentSlot.None;
    [Tooltip("이 장비가 덮는 부위의 방어력")]
    public float defense;
    [Tooltip("이동속도 배율. 1 보다 크면 빨라진다 (천옷), 작으면 느려진다 (가죽옷)")]
    public float moveSpeedMultiplier = 1f;
    [Tooltip("스태미나 소비 배율. 1 보다 작으면 덜 쓴다")]
    public float staminaCostMultiplier = 1f;
    [Tooltip("부상 확률 배율. 1 보다 작으면 부상을 덜 입는다")]
    public float injuryChanceMultiplier = 1f;

    [Header("무기 (GDD 9·10항). attackDamage 가 0 이면 무기가 아니다")]
    public float attackDamage;
    public float attackRange = 1.6f;
    [Tooltip("공격 간격(초). 작을수록 빠르다")]
    public float attackInterval = 0.8f;
    public float attackStaminaCost = 10f;

    [Tooltip("GDD 10항 한손/양손")]
    public WeaponHandedness handedness = WeaponHandedness.None;
    [Tooltip("한 번의 공격으로 때릴 수 있는 최대 대상 수. 한손은 1 (GDD 10항 다중타격)")]
    [Min(1)] public int maxTargets = 1;
    [Tooltip("적중 시 밀어내는 거리(m). GDD 20항 — 새 넉백이 기존 넉백을 갱신한다")]
    public float knockback;
    [Tooltip("적중 시 경직 시간(초). GDD 20항 — 중첩되지 않고 더 긴 쪽으로 갱신된다")]
    public float staggerDuration;

    [Tooltip("GDD 18항 발사/타격 소음. 예시: 식칼 5 / 방망이 10 / 권총 40 / 산탄총 100")]
    public float noise;

    [Tooltip("GDD 21항 방패. 보조무기 슬롯에 넣고 우클릭으로 방어 자세를 취한다")]
    public bool isShield;

    [Tooltip("GDD 10항 원거리 무기. 총/활은 마우스 방향으로 즉시 판정한다")]
    public bool ranged;
    [Tooltip("0 보다 크면 이 무기 전용 공격 부채꼴 반각(도). 0 이면 PlayerCombat 기본값을 쓴다")]
    public float attackSpread;

    [Header("탄약 (GDD 4항 R 재장전)")]
    [Tooltip("이 무기가 먹는 탄약 아이템 id. 비어 있으면 탄약을 쓰지 않는다")]
    public string ammoId = string.Empty;
    [Tooltip("한 번 재장전에 채우는 발수. 0 이면 탄약 개념 없음")]
    [Min(0)] public int magazineSize;
    [Tooltip("재장전에 걸리는 시간(초)")]
    public float reloadTime = 1.6f;

    /// <summary>탄약을 쓰는 무기인지. 활도 화살을 먹으므로 여기 포함된다.</summary>
    public bool UsesAmmo => magazineSize > 0 && !string.IsNullOrEmpty(ammoId);

    [Header("내구도 (GDD 11항)")]
    [Tooltip("0 이면 내구도 개념이 없는 아이템. 현재 내구도는 개체마다 달라서 ItemStack 에 있다")]
    [Min(0)] public int maxDurability;
}
