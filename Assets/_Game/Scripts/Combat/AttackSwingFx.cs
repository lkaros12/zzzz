using UnityEngine;

/// <summary>
/// 공격 모션 대체물. 애니메이션 에셋이 없으므로 공격 판정 범위를 그대로 훑는 부채꼴 스윙을 그린다.
/// GDD 26항의 최우선 검증 항목이 "공격 판정의 넉넉함" 이라, 모션이 판정 범위와 같은 모양인 편이
/// 눈으로 확인하기에 낫다.
///
/// ponytail: 프리미티브 + 런타임 머티리얼. 실제 캐릭터 애니메이션이 들어오면 이 파일은 지운다.
/// </summary>
public class AttackSwingFx : MonoBehaviour
{
    [SerializeField] Color oneHandedColor = new Color(0.85f, 0.90f, 1f, 0.55f);
    [SerializeField] Color twoHandedColor = new Color(1f, 0.80f, 0.45f, 0.65f);
    [SerializeField] float duration = 0.14f;
    [SerializeField] float bladeThickness = 0.14f;

    [Header("총 궤적 (즉발 직선)")]
    [SerializeField] Color tracerColor = new Color(1f, 0.95f, 0.6f, 0.9f);
    [SerializeField] float tracerDuration = 0.07f;
    [SerializeField] float tracerThickness = 0.045f;

    Transform pivot;
    Transform blade;
    Material material;

    Transform tracerRoot;
    Material tracerMaterial;
    float tracerEndTime;

    float endTime;
    float halfAngle;

    void Awake() => Build();

    void Build()
    {
        // 회전 중심 → 그 아래에 앞으로 뻗은 납작한 판(무기 궤적)
        pivot = new GameObject("SwingPivot").transform;
        pivot.SetParent(transform, false);

        var bladeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bladeGo.name = "SwingBlade";
        Destroy(bladeGo.GetComponent<Collider>());

        blade = bladeGo.transform;
        blade.SetParent(pivot, false);

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) shader = Shader.Find("Unlit/Color");

        // 런타임에서만 만들어지므로 material 인스턴스를 써도 씬에 누출되지 않는다.
        material = new Material(shader);
        bladeGo.GetComponent<Renderer>().material = material;

        pivot.gameObject.SetActive(false);
    }

    /// <summary>공격 판정과 같은 사거리·각도로 스윙을 재생한다.</summary>
    public void Play(float range, float attackHalfAngle, bool twoHanded)
    {
        if (!pivot) Build();

        halfAngle = attackHalfAngle;
        endTime = Time.time + duration;

        pivot.localPosition = new Vector3(0f, 0.2f, 0f);
        blade.localScale = new Vector3(bladeThickness, bladeThickness, range);
        blade.localPosition = new Vector3(0f, 0f, range * 0.5f);

        material.color = twoHanded ? twoHandedColor : oneHandedColor;
        pivot.gameObject.SetActive(true);
        Sweep(0f);
    }

    /// <summary>
    /// 총 궤적. 총은 휘두르는 게 아니라 즉발이므로 부채꼴 대신 발사선을 한 줄 그린다.
    /// 시작점과 끝점은 실제 히트스캔이 쓴 값을 그대로 받는다 — 보이는 선과 판정이 어긋나면
    /// "분명 맞췄는데" 가 생긴다.
    /// </summary>
    public void PlayTracer(Vector3 from, Vector3 to)
    {
        if (!tracerRoot) BuildTracer();

        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < 0.05f) return;

        tracerRoot.position = from + delta * 0.5f;
        tracerRoot.rotation = Quaternion.LookRotation(delta / length);
        tracerRoot.localScale = new Vector3(tracerThickness, tracerThickness, length);

        tracerMaterial.color = tracerColor;
        tracerEndTime = Time.time + tracerDuration;
        tracerRoot.gameObject.SetActive(true);
    }

    void BuildTracer()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "ShotTracer";

        // 총알선이 물리를 막으면 안 된다. Destroy 는 프레임 끝까지 미뤄지므로 먼저 끈다.
        var collider = go.GetComponent<Collider>();
        if (collider)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        // 플레이어의 자식으로 두면 플레이어가 돌 때 궤적도 같이 돌아간다. 월드에 독립으로 둔다.
        tracerRoot = go.transform;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) shader = Shader.Find("Unlit/Color");

        tracerMaterial = new Material(shader);
        go.GetComponent<Renderer>().material = tracerMaterial;

        tracerRoot.gameObject.SetActive(false);
    }

    void UpdateTracer()
    {
        if (!tracerRoot || !tracerRoot.gameObject.activeSelf) return;

        float remaining = tracerEndTime - Time.time;
        if (remaining <= 0f)
        {
            tracerRoot.gameObject.SetActive(false);
            return;
        }

        // 짧게 반짝이고 사라진다.
        var color = tracerMaterial.color;
        color.a = tracerColor.a * (remaining / tracerDuration);
        tracerMaterial.color = color;
    }

    void Update()
    {
        UpdateTracer();

        if (!pivot || !pivot.gameObject.activeSelf) return;

        float remaining = endTime - Time.time;
        if (remaining <= 0f)
        {
            pivot.gameObject.SetActive(false);
            return;
        }

        Sweep(1f - remaining / duration);
    }

    /// <summary>진행도 0~1 을 -halfAngle ~ +halfAngle 회전으로 바꾼다.</summary>
    void Sweep(float progress)
    {
        float angle = Mathf.Lerp(-halfAngle, halfAngle, Mathf.Clamp01(progress));
        pivot.localRotation = Quaternion.Euler(0f, angle, 0f);

        var color = material.color;
        color.a = Mathf.Lerp(color.a, 0f, progress * 0.35f);
        material.color = color;
    }

    void OnDestroy()
    {
        // 월드에 독립으로 둔 궤적은 직접 치운다.
        if (tracerRoot) Destroy(tracerRoot.gameObject);
    }
}
