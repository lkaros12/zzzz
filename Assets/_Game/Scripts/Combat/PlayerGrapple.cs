using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 좀비가 붙었을 때의 저항. 게이지가 시간에 따라 줄고 좌클릭 연타로 오른다.
/// 1 에 도달하면 풀리고 좀비가 비틀거린다. 0 이 되면 물린다.
/// 저항 중에는 이동과 공격이 막히고 좌클릭은 연타 입력으로만 쓰인다.
/// </summary>
public class PlayerGrapple : MonoBehaviour
{
    [Tooltip("저항이 시작될 때의 게이지")]
    [SerializeField, Range(0.05f, 0.95f)] float startProgress = 0.55f;
    [Tooltip("초당 줄어드는 양")]
    [SerializeField] float drainPerSecond = 0.26f;
    [Tooltip("좌클릭 한 번당 오르는 양")]
    [SerializeField] float gainPerClick = 0.085f;
    [Tooltip("이 시간을 넘기면 물린다. 무한히 버티지 못하게 하는 상한")]
    [SerializeField] float maxDuration = 12f;

    PlayerInjuries injuries;
    [Tooltip("붙잡은 좀비가 이보다 멀어지면 자동으로 풀린다 (3D 거리)")]
    [SerializeField] float releaseDistance = 3.5f;

    Zombie holder;
    float deadline;

    public bool IsGrappled => holder != null;

    /// <summary>마지막으로 풀린 시각. 풀리는 그 클릭이 공격으로 이어지지 않게 하는 데 쓴다.</summary>
    public float LastReleaseTime { get; private set; } = -999f;

    /// <summary>0~1. UI 슬라이더가 이 값을 그린다.</summary>
    public float Progress { get; private set; }

    void Awake()
    {
        injuries = GetComponent<PlayerInjuries>();
    }

    /// <summary>좀비가 호출한다. 이미 붙잡혀 있으면 실패.</summary>
    public bool Begin(Zombie source)
    {
        if (IsGrappled || !source) return false;

        holder = source;
        Progress = startProgress;
        deadline = Time.time + maxDuration;
        return true;
    }

    /// <summary>
    /// 좀비가 죽거나 플레이어가 죽었을 때처럼 판정 없이 즉시 풀어야 하는 경우.
    ///
    /// 반드시 좀비에게 알려야 한다 — 알리지 않으면 좀비의 IsGrappling 이 true 로 남아
    /// 영원히 붙잡고 있는 줄 알고 멈춰 선다 (플레이어 사망/부활 후 좀비가 얼어붙던 버그).
    /// </summary>
    public void ForceRelease()
    {
        var source = holder;
        holder = null;
        Progress = 0f;

        if (!source) return;

        LastReleaseTime = Time.time;
        source.OnGrappleEnded(false); // 물지는 않는다. 상황이 끝난 것이지 저항에 실패한 게 아니다.
    }

    void Update()
    {
        if (!IsGrappled) return;

        if (holder.IsDead)
        {
            ForceRelease();
            return;
        }

        // 붙잡은 좀비가 실제로 옆에 없으면 놓아준다. 바닥을 뚫고 빠진 좀비가
        // 화면에 아무것도 없는 상태로 플레이어를 계속 붙잡는 일이 있었다 (실측 1100m 아래).
        // 수평이 아니라 진짜 3D 거리로 재야 위로/아래로 벌어진 경우가 걸린다.
        if (Vector3.Distance(holder.transform.position, transform.position) > releaseDistance)
        {
            ForceRelease();
            return;
        }

        Progress -= drainPerSecond * Time.deltaTime;

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame) Progress += ClickGain();

        if (Progress >= 1f)
        {
            End(true);
            return;
        }

        if (Progress <= 0f || Time.time >= deadline) End(false);
    }

    /// <summary>
    /// 연타는 스태미나를 쓰지 않는다 — 붙잡힌 상태에서 스태미나까지 요구하면 빠져나갈 방법이 없다.
    /// GDD 6항 머리 부상의 행동 효율만 반영한다.
    /// </summary>
    float ClickGain() => gainPerClick * (injuries ? injuries.ActionEfficiency : 1f);

    void End(bool escaped)
    {
        var source = holder;
        holder = null;
        Progress = 0f;
        LastReleaseTime = Time.time;

        if (!source) return;

        source.OnGrappleEnded(escaped);
        if (!escaped) source.Bite();
    }
}
