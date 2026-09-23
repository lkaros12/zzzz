using UnityEngine;

/// <summary>
/// 좀비 상태를 Animator 파라미터로 옮긴다. AI 는 애니메이션을 전혀 모르고,
/// 애니메이션은 AI 를 건드리지 않는다 — 한쪽 방향으로만 읽는다.
///
/// 이동 속도는 Zombie 에게 묻지 않고 실제 위치 변화로 측정한다. 넉백·돌진·경직 같은
/// 것들이 moveSpeed 와 무관하게 몸을 움직이기 때문에, 실측이 항상 화면과 맞는다.
///
/// Speed 는 m/s 가 아니라 "보통 걸음 = 1" 로 정규화한 값이다. 그래서 같은 파라미터가
/// 걷기 전환 조건과 걷기 재생 속도(발이 미끄러지지 않게) 를 동시에 담당한다.
/// </summary>
[RequireComponent(typeof(Zombie))]
public class ZombieAnimator : MonoBehaviour
{
    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int AttackId = Animator.StringToHash("Attack");
    static readonly int DeadId = Animator.StringToHash("Dead");

    [Tooltip("이 속도(m/s) 를 Speed=1 로 본다. 종류 수치의 moveSpeed 를 넣는다")]
    [SerializeField] float walkSpeedReference = 2.5f;
    [Tooltip("속도 변화를 얼마나 빨리 따라갈지. 낮으면 걷기/멈춤이 흐물흐물해진다")]
    [SerializeField] float speedSmoothing = 10f;

    Zombie zombie;
    Animator animator;
    Vector3 lastPosition;
    float speed;
    float seenAttackTime;
    bool reportedDead;

    void Awake()
    {
        zombie = GetComponent<Zombie>();
        animator = GetComponentInChildren<Animator>();
        lastPosition = transform.position;

        // 생성 직후의 공격 시각을 이미 본 것으로 처리한다. 안 하면 태어나자마자 한 번 휘두른다.
        seenAttackTime = zombie.LastAttackTime;
    }

    /// <summary>빌더/생성 코드가 종류별 이동속도를 넘겨준다.</summary>
    public void SetWalkSpeedReference(float metresPerSecond)
    {
        if (metresPerSecond > 0.01f) walkSpeedReference = metresPerSecond;
    }

    void Update()
    {
        if (!animator) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        lastPosition = transform.position;

        float measured = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        // 프레임 독립 감쇠. Lerp(.., speedSmoothing * dt) 는 프레임률에 따라 결과가 달라진다.
        speed = Mathf.Lerp(speed, measured / walkSpeedReference,
                           1f - Mathf.Exp(-speedSmoothing * Time.deltaTime));
        animator.SetFloat(SpeedId, speed);

        if (zombie.LastAttackTime > seenAttackTime)
        {
            seenAttackTime = zombie.LastAttackTime;
            animator.SetTrigger(AttackId);
        }

        // 사망은 한 번만 알린다. 매 프레임 SetBool 을 해도 되지만 죽은 뒤엔 여기까지 올 일이 없다.
        if (zombie.IsDead && !reportedDead)
        {
            reportedDead = true;
            animator.SetBool(DeadId, true);
        }
    }
}
