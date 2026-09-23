using UnityEngine;

/// <summary>
/// GDD 19항 암살. 좀비 후면 120° 안에서 E 를 누르면 즉시 시작한다.
///
/// 모션 3초 동안 플레이어는 무적이고 움직일 수 없으며, 다른 좀비의 공격으로 취소되지 않는다.
/// 준비시간은 없고, 종료 후 짧은 전환 딜레이만 둔다.
/// 애니메이션이 없으므로 "모션" 은 3초의 정지 + 무적 구간으로만 표현한다.
/// </summary>
public class PlayerAssassinate : MonoBehaviour
{
    [Tooltip("GDD 19항 암살 거리")]
    [SerializeField] float range = 1.8f;

    [Tooltip("GDD 19항: 좀비 후면 기준 이 각도 범위 안에 있어야 한다 (전체 각도)")]
    [SerializeField] float rearAngle = 120f;

    [Tooltip("GDD 19항: 암살 모션 3초. 이 구간 전체가 무적이다")]
    [SerializeField] float motionDuration = 3f;

    [Tooltip("GDD 19항: 종료 후 전환 딜레이. 별도 쿨타임은 없다")]
    [SerializeField] float transitionDelay = 0.2f;

    PlayerMotor motor;
    PlayerStats stats;
    PlayerGrapple grapple;

    Zombie target;
    float motionEndTime;
    float nextAllowedTime;

    /// <summary>GDD 19항: 모션 중에는 이동/공격 입력을 모두 막는다.</summary>
    public bool IsAssassinating => target != null;

    /// <summary>UI/디버그용 마지막 결과 메시지.</summary>
    public string LastMessage { get; private set; } = string.Empty;

    /// <summary>검증용. 성공한 암살 횟수.</summary>
    public int AssassinateCount { get; private set; }

    void Awake()
    {
        motor = GetComponent<PlayerMotor>();
        stats = GetComponent<PlayerStats>();
        grapple = GetComponent<PlayerGrapple>();
    }

    void Update()
    {
        if (target == null) return;

        // GDD 19항: 위치를 고정한다. 대상이 사라졌으면 그 자리에서 정리한다.
        if (Time.time < motionEndTime)
        {
            // 무적은 매 프레임 갱신한다 — 피격/회피 타이머가 이 구간을 깎지 못하게.
            if (motor) motor.SetInvulnerableFor(motionEndTime - Time.time);
            return;
        }

        target.FinishAssassinated();
        AssassinateCount++;
        LastMessage = "암살 성공";
        target = null;
        nextAllowedTime = Time.time + transitionDelay;
    }

    /// <summary>
    /// 지금 암살할 수 있는 좀비. 없으면 null.
    /// 조건: 거리 이내 + 좀비 후면 120° + 플레이어를 인식하지 못한 상태 + 암살 가능한 종류.
    /// </summary>
    public Zombie FindTarget()
    {
        if (IsAssassinating) return null;
        if (Time.time < nextAllowedTime) return null;
        if (stats && stats.IsDead) return null;
        if (grapple && grapple.IsGrappled) return null;

        var all = Zombie.All;
        Zombie best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < all.Count; i++)
        {
            var zombie = all[i];
            if (!zombie || zombie.IsDead || zombie.IsBeingAssassinated) continue;

            // GDD 19항: 헤비좀비는 암살할 수 없다.
            if (!zombie.CanBeAssassinated) continue;

            // GDD 19항: 대상이 플레이어를 인식하고 있으면 안 된다.
            if (zombie.IsAwareOfPlayer) continue;

            Vector3 toPlayer = transform.position - zombie.transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;
            if (distance > range || distance < 0.01f) continue;

            // GDD 19항: 좀비 후면 기준 범위. 정면 반대쪽을 중심으로 재는 것이다.
            if (Vector3.Angle(-zombie.transform.forward, toPlayer) > rearAngle * 0.5f) continue;

            // 벽을 사이에 두고는 찌를 수 없다. 거리와 각도만 보면 벽 너머 좀비가 잡힌다.
            if (!HasClearPath(zombie.transform.position)) continue;

            if (distance < bestDistance) { bestDistance = distance; best = zombie; }
        }

        return best;
    }

    /// <summary>
    /// 그 지점까지 벽에 막히지 않았는지. 좀비 시야와 같은 규칙(Vision)을 쓴다.
    /// 검사에서 부르려고 공개해 두었다 — 거리/각도만 보던 예전 판정은 벽 너머 좀비를 집었다.
    /// </summary>
    public bool HasClearPath(Vector3 targetPosition) =>
        Vision.CanSee(transform.position, targetPosition, range + 0.5f);

    /// <summary>E 입력이 들어왔을 때. 조건이 맞으면 즉시 시작한다 (GDD 19항: 준비시간 없음).</summary>
    public bool TryStart()
    {
        var found = FindTarget();
        if (found == null) return false;

        target = found;
        motionEndTime = Time.time + motionDuration;

        found.BeginAssassinated();

        // GDD 19항: 위치 고정. 좀비 등 뒤에 붙어 서서 서로를 바라본다.
        Vector3 behind = found.transform.position - found.transform.forward * 0.9f;
        behind.y = transform.position.y;
        MovePlayerTowards(behind);
        transform.rotation = Quaternion.LookRotation(found.transform.forward);

        // 모션 전체가 무적이다. 다른 좀비의 공격으로 취소되지 않는다.
        if (motor) motor.SetInvulnerableFor(motionDuration);

        LastMessage = "암살 중...";
        return true;
    }

    /// <summary>
    /// 좀비 등 뒤로 붙는다. 예전에는 CharacterController 를 끄고 transform 을 옮겼는데,
    /// 그러면 벽 너머 좀비를 암살할 때 플레이어가 벽을 뚫고 집 밖으로 나갔다.
    /// cc.Move 로 쓸어서 옮기면 막히는 만큼만 가고 벽을 통과하지 않는다.
    /// </summary>
    void MovePlayerTowards(Vector3 position)
    {
        var cc = GetComponent<CharacterController>();
        if (!cc) { transform.position = position; return; }

        Vector3 delta = position - transform.position;
        delta.y = 0f;
        cc.Move(delta);
    }

    /// <summary>대상이 먼저 죽었을 때 (다른 경로로 사망) 모션을 정리한다.</summary>
    public void OnTargetLost(Zombie zombie)
    {
        if (target != zombie) return;

        target = null;
        nextAllowedTime = Time.time + transitionDelay;
    }
}
