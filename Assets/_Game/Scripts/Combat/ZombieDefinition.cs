using UnityEngine;

/// <summary>
/// GDD 16항 좀비 종류. 개발 원칙 1번("시스템은 데이터 기반") 에 따라 종류별 수치를 에셋으로 뺀다.
/// GDD 16항 주석대로 수치는 프로토타입 초기값이며 플레이 테스트로 조정한다.
/// </summary>
[CreateAssetMenu(menuName = "V0.1/Zombie Definition", fileName = "zombie")]
public class ZombieDefinition : ScriptableObject
{
    [Tooltip("저장 파일에 쓰이는 식별자. 파일명과 같게 유지한다")]
    public string id;
    public string displayName;

    [Tooltip("캡슐 프리미티브 색. 종류를 눈으로 구분한다")]
    public Color placeholderColor = Color.gray;
    [Tooltip("공유 머티리얼 에셋. 에디트 모드에서 material 을 건드리면 머티리얼이 누출된다")]
    public Material placeholderMaterial;

    [Header("3D 모델 (비어 있으면 캡슐 플레이스홀더)")]
    [Tooltip("스킨드 메쉬 + Animator 가 달린 모델. 휴머노이드 아바타여야 클립이 리타기팅된다")]
    public GameObject model;
    [Tooltip("모델에 씌울 애니메이터. 파라미터: Speed(float) / Attack(trigger) / Dead(bool)")]
    public RuntimeAnimatorController animatorController;
    [Tooltip("콜라이더 높이. 플레이어 히트스캔이 이 높이를 훑으므로 너무 낮으면 총이 안 맞는다")]
    public float bodyHeight = 1.6f;
    [Tooltip("콜라이더 반지름. bodyHeight 의 절반을 넘을 수 없다")]
    public float bodyRadius = 0.4f;
    [Tooltip("모델을 bodyHeight 에 맞추는 배율. 빌더가 모델 실측 높이로 계산해 박아둔다")]
    public float modelScale = 1f;
    [Tooltip("사망 애니메이션이 끝날 때까지 시체를 남겨두는 시간. 0 이면 즉시 사라진다")]
    public float deathLinger = 1.1f;

    [Header("기본 수치 (GDD 16항)")]
    public float maxHealth = 100f;
    // 사용자 결정: 좀비 방어력은 없앴다. 기획서 16항 표에는 방어력 열이 있지만,
    // 뺄셈 방어라 식칼 12 - 방어 10 = 2 처럼 근접 피해가 거의 안 들어가서 걷어냈다.
    public float moveSpeed = 2.5f;

    [Header("공격")]
    [Tooltip("공격력 기준값 20 을 범위로 흔든다 (사용자 결정: 종류별 기준값 + 범위)")]
    public float damageMin = 15f;
    public float damageMax = 25f;
    [Tooltip("GDD 21항 방패 충격량. 이만큼 방패를 다시 들 수 없다")]
    public float blockImpact = 0.3f;

    [Tooltip("GDD 21항 표 가운데 줄 '강한 일반 공격' 이 나올 확률")]
    [Range(0f, 1f)] public float heavySwingChance = 0.25f;
    [Tooltip("강한 공격의 피해 배수")]
    public float heavySwingDamageMultiplier = 1.6f;
    [Tooltip("강한 공격의 방패 충격량. 표에서 '중간' = 0.7초")]
    public float heavySwingBlockImpact = 0.7f;

    [Header("피격 반응")]
    [Tooltip("사용자 결정: 헤비좀비는 무기 넉백에 밀리지 않는다. 경직은 그대로 받는다")]
    public bool immuneToKnockback;

    [Header("감지 (GDD 17항)")]
    [Tooltip("정면 시야 사거리")]
    public float visionRange = 16f;
    [Tooltip("시야 반각. 좁게 둬서 뒤에서 접근하면 못 알아채게 한다")]
    public float visionHalfAngle = 55f;

    [Header("암살 (GDD 19항)")]
    [Tooltip("GDD 19항: 헤비좀비는 암살할 수 없다")]
    public bool canBeAssassinated = true;

    [Header("돌진 (GDD 22항)")]
    public bool canCharge;
    [Tooltip("이 거리 안에 플레이어가 들어오면 돌진을 시작한다")]
    public float chargeTriggerRange = 7f;
    [Tooltip("준비 모션 시간. 플레이어가 보고 대응할 수 있어야 한다")]
    public float chargeWindup = 0.9f;
    public float chargeSpeed = 11f;
    [Tooltip("돌진으로 이동하는 거리")]
    public float chargeDistance = 6f;
    [Tooltip("GDD 22항: 돌진 종료 후 경직 시간")]
    public float chargeRecovery = 2f;
    [Tooltip("GDD 22항: 개별 좀비 기준 돌진 쿨타임")]
    public float chargeCooldown = 20f;
    [Tooltip("돌진에 받는 방패 충격량. 표에서 '매우 높음'")]
    public float chargeBlockImpact = 1.5f;
    [Tooltip("돌진 적중 피해. 일반 공격보다 세다 — 위험-보상 패턴(GDD 22항) 의 '위험' 쪽")]
    public float chargeDamageMin = 30f;
    public float chargeDamageMax = 45f;
}
