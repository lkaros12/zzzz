using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 기획 4·5·11항 창문. 벽에 뚫린 구멍 + 여닫는 창틀 한 장.
///
/// 시야와 통행을 따로 만들지 않는다. 창짝(pane) 콜라이더 하나가 둘 다 한다 —
/// 닫히면 켜져서 못 지나가고 시야도 막고, 열리면 꺼져서 넘어갈 수 있고 너머가 보인다.
/// 그래서 Vision 은 창문을 몰라도 되고, 창문은 Vision 을 몰라도 된다.
///
/// 아래 턱(sill)은 항상 남는다. 그래야 열려 있어도 걸어서는 못 지나가고 Space 로 넘어야 한다.
/// </summary>
public class WindowFrame : MonoBehaviour
{
    static readonly List<WindowFrame> all = new List<WindowFrame>();
    public static IReadOnlyList<WindowFrame> All => all;

    [Tooltip("E 로 여닫을 수 있는 거리")]
    [SerializeField] float interactRange = 2.0f;
    [Tooltip("창을 넘을 수 있는 거리 (창면 기준 앞뒤)")]
    [SerializeField] float vaultRange = 1.5f;
    [Tooltip("넘은 뒤 반대편에 내려서는 거리. 벽 두께보다 커야 벽에 낀다")]
    [SerializeField] float vaultExit = 1.1f;
    [Tooltip("구멍의 가로 폭. 이 폭 밖에 서 있으면 못 넘는다")]
    [SerializeField] float openingWidth = 1.8f;

    [SerializeField] Transform hinge;
    [SerializeField] Collider paneCollider;
    [SerializeField] bool isOpen;
    [Tooltip("안내 문구에 쓰는 이름. 창문인지 문인지")]
    [SerializeField] string label = "창문";
    [Tooltip("Space 로 넘을 수 있는지. 창턱이 없는 문은 열면 그냥 걸어 나가므로 false")]
    [SerializeField] bool vaultable = true;

    [Header("커튼")]
    [Tooltip("커튼이 달려 있는지. 문에는 없다")]
    [SerializeField] bool hasCurtain;
    [SerializeField] bool curtainClosed;
    [Tooltip("보이는 천. 닫을 때만 켠다")]
    [SerializeField] GameObject curtainVisual;
    [Tooltip("닫힌 커튼이 시야를 막는 콜라이더. 창짝과 같은 방식이다 — Vision 은 커튼을 몰라도 된다")]
    [SerializeField] Collider curtainBlocker;

    [Header("좀비 공성")]
    [Tooltip("동시에 이만큼 붙어 두드리면 부서진다. 창문 2, 문 3 (사용자 요구)")]
    [SerializeField, Min(1)] int poundersToBreak = 2;
    [Tooltip("필요한 수가 붙은 뒤 부서지기까지 걸리는 시간(초)")]
    [SerializeField] float secondsToBreak = 4f;
    [SerializeField] bool isBroken;

    // 두드리는 좀비는 매 프레임 Pound 를 부른다. 여기서 세고 매 프레임 비운다 —
    // 떨어져 나간 좀비를 따로 지울 필요가 없다.
    readonly HashSet<Zombie> pounding = new HashSet<Zombie>();

    public bool IsOpen => isOpen;
    public bool IsBroken => isBroken;

    /// <summary>몸이 지나갈 수 있는 상태. 열려 있거나 부서졌으면 통과할 수 있다.</summary>
    public bool Passable => isOpen || isBroken;

    /// <summary>넘어서 통과하는 개구부인지 (창문). 문은 열면 걸어 나간다.</summary>
    public bool Vaultable => vaultable;

    /// <summary>구멍의 가로 폭. 여러 마리가 나란히 붙을 자리를 나눌 때 쓴다.</summary>
    public float OpeningWidth => openingWidth;

    /// <summary>
    /// 두드릴 수 있는 자리인지. 구멍 폭 안에서 창면에 가까우면 된다.
    /// 중심점까지의 거리로 재면 옆으로 늘어선 좀비가 사거리 밖으로 밀려나서
    /// 두 마리가 같이 두드릴 수 없다 (실측: 2번째가 1.52m 로 빠졌다).
    /// </summary>
    public bool IsAtOpening(Vector3 worldPoint, float range)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);

        return Mathf.Abs(local.x) <= openingWidth * 0.5f + 0.35f
            && Mathf.Abs(local.z) <= range;
    }

    public bool HasCurtain => hasCurtain;
    public bool IsCurtainClosed => hasCurtain && curtainClosed;

    /// <summary>지난 프레임에 붙어서 두드리던 좀비 수.</summary>
    public int PounderCount { get; private set; }
    public int PoundersToBreak => poundersToBreak;
    public float BreakProgress01 => secondsToBreak > 0f ? Mathf.Clamp01(breakProgress / secondsToBreak) : 0f;

    float breakProgress;

    public float InteractRange => interactRange;
    /// <summary>창면의 법선. 벽을 가로지르는 방향이다.</summary>
    public Vector3 Normal => transform.forward;
    public Vector3 Center => transform.position;
    public string PromptText
    {
        get
        {
            if (isBroken) return label + " 부서짐";

            string text = isOpen
                ? "[E] " + label + " 닫기" + (vaultable ? "  ·  [Space] 넘기" : "")
                : "[E] " + label + " 열기";

            if (hasCurtain) text += "  ·  [C] 커튼 " + (curtainClosed ? "열기" : "치기");
            return text;
        }
    }

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    void Start() => ApplyState();

    public void Setup(Transform paneHinge, Collider pane, float width) =>
        Setup(paneHinge, pane, width, label, vaultable);

    public void Setup(Transform paneHinge, Collider pane, float width, string displayLabel, bool canVault)
    {
        hinge = paneHinge;
        paneCollider = pane;
        openingWidth = width;
        label = displayLabel;
        vaultable = canVault;
        ApplyState();
    }

    /// <summary>커튼을 달아준다. 빌더가 창문에만 부른다.</summary>
    public void SetupCurtain(GameObject visual, Collider blocker, bool startClosed)
    {
        hasCurtain = true;
        curtainVisual = visual;
        curtainBlocker = blocker;
        curtainClosed = startClosed;
        ApplyCurtain();
    }

    /// <summary>부서지는 조건. 창문은 2마리, 문은 3마리 (사용자 요구).</summary>
    public void SetupBreakRule(int pounders, float seconds)
    {
        poundersToBreak = Mathf.Max(1, pounders);
        secondsToBreak = seconds;
    }

    /// <summary>
    /// 지금 이 창을 넘고 있는 몸의 수. 넘는 동안은 닫히지 않는다 —
    /// 파쿠르 중에 E 로 닫으면 창짝 콜라이더가 몸 안에서 켜져 벽에 끼거나 통과했다
    /// (사용자 지적). 좀비가 넘을 때도 같은 규칙이 필요하니 창문 쪽에 둔다.
    /// </summary>
    int vaulters;

    public bool IsBeingVaulted => vaulters > 0;

    public void BeginVault() => vaulters++;
    public void EndVault() => vaulters = Mathf.Max(0, vaulters - 1);

    /// <summary>실제로 상태가 바뀌었으면 true.</summary>
    public bool Toggle() => SetOpen(!isOpen);

    public bool SetOpen(bool open)
    {
        if (isBroken) return false;
        if (!open && vaulters > 0) return false;

        isOpen = open;
        ApplyState();
        return true;
    }

    /// <summary>창짝을 실제로 젖힌다. 열림/닫힘이 눈에 보여야 한다 (기획 4항).</summary>
    void ApplyState()
    {
        // 창짝 콜라이더 하나가 통행과 시야를 같이 막는다. 부서지면 영구히 열린 셈이다.
        if (paneCollider) paneCollider.enabled = !Passable;

        // NavMeshObstacle 은 창짝에 붙어 있다. 끄지 않고 경첩을 젖혀 길 밖으로 치운다 —
        // 그래서 여기서는 회전만 건드리면 길찾기까지 같이 열린다.
        if (hinge) hinge.localRotation = Quaternion.Euler(0f, Passable ? -100f : 0f, 0f);

        ApplyCurtain();
    }

    // ---- 커튼 (좀비의 실내 발견 조건) ----

    /// <summary>커튼을 치거나 젖힌다. 달려 있지 않으면 false.</summary>
    public bool ToggleCurtain()
    {
        if (!hasCurtain || isBroken) return false;

        curtainClosed = !curtainClosed;
        ApplyCurtain();
        return true;
    }

    /// <summary>
    /// 닫힌 커튼은 창짝과 똑같이 콜라이더로 시야를 막는다. 그래서 Vision 도 좀비도
    /// 커튼을 알 필요가 없다 — "커튼 치면 안 보인다" 가 공짜로 성립한다.
    /// 부서진 창의 커튼은 찢어진 것으로 본다.
    /// </summary>
    void ApplyCurtain()
    {
        bool drawn = hasCurtain && curtainClosed && !isBroken;

        if (curtainBlocker) curtainBlocker.enabled = drawn;
        if (curtainVisual) curtainVisual.SetActive(drawn);
    }

    // ---- 좀비 공성 (두드리기 → 부서짐) ----

    /// <summary>두드리는 좀비가 매 프레임 부른다. 세는 건 이쪽에서 한다.</summary>
    public void Pound(Zombie zombie)
    {
        if (isBroken || !zombie) return;

        pounding.Add(zombie);
    }

    void Update()
    {
        PounderCount = pounding.Count;
        pounding.Clear();

        if (isBroken) return;

        // 사용자 요구: 창문은 2마리, 문은 3마리가 붙어야 부서진다.
        // 수가 모자라면 진행도가 식어서 한 마리가 오래 두드려도 못 뚫는다.
        if (PounderCount >= poundersToBreak) breakProgress += Time.deltaTime;
        else breakProgress = Mathf.Max(0f, breakProgress - Time.deltaTime * 0.5f);

        if (breakProgress >= secondsToBreak) Break();
    }

    public void Break()
    {
        if (isBroken) return;

        isBroken = true;
        breakProgress = 0f;
        ApplyState();

        // 부서지는 소리는 벽을 넘어 퍼진다 — 이 순간이 무리를 부른다.
        Noise.Emit(Center, 45f, true);
    }

    public bool IsInRange(Vector3 worldPoint) =>
        (worldPoint - transform.position).sqrMagnitude <= interactRange * interactRange;

    /// <summary>사거리 안에서 가장 가까운 창문. 없으면 null.</summary>
    public static WindowFrame Nearest(Vector3 worldPoint)
    {
        WindowFrame best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < all.Count; i++)
        {
            var window = all[i];
            if (!window || !window.IsInRange(worldPoint)) continue;

            float distance = (window.transform.position - worldPoint).sqrMagnitude;
            if (distance >= bestDistance) continue;

            best = window;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// 기획 5항 파쿠르 조건. 열려 있고, 구멍 정면에 서 있고, 창면에 충분히 가까울 때만.
    /// 창문 옆 벽 앞에서 Space 를 눌러 벽을 통과하는 일이 없도록 가로 폭도 본다.
    /// </summary>
    public bool CanVault(Vector3 worldPoint)
    {
        if (!Passable || !vaultable) return false;

        Vector3 local = transform.InverseTransformPoint(worldPoint);
        if (Mathf.Abs(local.x) > openingWidth * 0.5f + 0.35f) return false;
        if (Mathf.Abs(local.z) > vaultRange) return false;
        return Mathf.Abs(local.y) <= 1.6f;
    }

    /// <summary>넘은 뒤 서 있을 자리. 반대편 바닥에 발을 붙인다.</summary>
    public Vector3 VaultTarget(Vector3 worldPoint) => VaultTarget(worldPoint, 1.05f);

    /// <summary>
    /// 캡슐 절반 높이를 받는 판. 좀비는 플레이어보다 작아서 1.05 를 박아두면
    /// 바닥에 묻히거나 떠서 내려선다.
    /// </summary>
    public Vector3 VaultTarget(Vector3 worldPoint, float capsuleHalfHeight)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        float side = local.z >= 0f ? 1f : -1f;

        // 지금 있는 쪽의 반대편으로 나간다.
        Vector3 target = transform.position - Normal * side * vaultExit;
        target.y = worldPoint.y;

        // 바깥과 실내의 바닥 높이가 다를 수 있으니 실제 바닥을 찾는다.
        RaycastHit hit;
        if (Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out hit, 6f,
                            Vision.BlockerMask, QueryTriggerInteraction.Ignore))
            target.y = hit.point.y + capsuleHalfHeight;

        return target;
    }

    /// <summary>
    /// 이 개구부 앞에 설 자리. 묻는 쪽이 있는 편의 바깥이다.
    ///
    /// 문간 안쪽을 목표로 잡으면 닫힌 문의 NavMeshObstacle 카빙에 걸려 경로가 끊긴다.
    /// lateral 은 벽을 따라 옆으로 비키는 거리 — 여러 마리가 나란히 붙게 한다.
    /// </summary>
    public Vector3 StandPoint(Vector3 seeker, float outward, float lateral)
    {
        Vector3 local = transform.InverseTransformPoint(seeker);
        float side = local.z >= 0f ? 1f : -1f;

        return Center + Normal * side * outward + transform.right * lateral;
    }

    /// <summary>넘을 수 있는 가장 가까운 창문. 없으면 null.</summary>
    public static WindowFrame VaultableFrom(Vector3 worldPoint)
    {
        WindowFrame best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < all.Count; i++)
        {
            var window = all[i];
            if (!window || !window.CanVault(worldPoint)) continue;

            float distance = (window.transform.position - worldPoint).sqrMagnitude;
            if (distance >= bestDistance) continue;

            best = window;
            bestDistance = distance;
        }

        return best;
    }
}
