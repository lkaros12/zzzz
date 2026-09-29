using UnityEngine;

/// <summary>
/// 총의 즉발 판정선을 잠깐 보여주는 임시 궤적.
/// ponytail: 프리미티브 + 런타임 머티리얼. 실제 총알 궤적 이펙트가 들어오면 이 파일은 지운다.
/// </summary>
public class AttackSwingFx : MonoBehaviour
{
    [Header("총 궤적 (즉발 직선)")]
    [SerializeField] Color tracerColor = new Color(1f, 0.95f, 0.6f, 0.9f);
    [SerializeField] float tracerDuration = 0.07f;
    [SerializeField] float tracerThickness = 0.045f;

    Transform tracerRoot;
    Material tracerMaterial;
    float tracerEndTime;

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

    void Update() => UpdateTracer();

    void OnDestroy()
    {
        // 월드에 독립으로 둔 궤적은 직접 치운다.
        if (tracerRoot) Destroy(tracerRoot.gameObject);
    }
}
