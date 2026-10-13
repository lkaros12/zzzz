using UnityEngine;

/// <summary>
/// 조준 표시. 플레이어 발밑에서 커서가 가리키는 지면 지점까지 점선을 깔고, 끝에 링을 그린다.
///
/// PlayerAim 이 이미 커서를 지면 평면에 투영해 AimPoint 를 구해두므로 여기서는 그리기만 한다.
/// 아이소메트릭이라 화면 위 커서 위치만으로는 "월드에서 어디를 겨누는지" 가 안 읽혀서 필요하다.
///
/// ponytail: LineRenderer + 점선 텍스처 대신 작은 프리미티브를 늘어놓는다. 텍스처 에셋이
/// 없어도 되고, 실제 아트가 들어오면 이 파일을 지우고 데칼/스프라이트로 바꾸면 된다.
/// </summary>
public class AimCursorFx : MonoBehaviour
{
    [Tooltip("점선 조각 최대 개수")]
    [SerializeField, Min(2)] int dashCount = 22;
    [Tooltip("조각 하나의 크기 (가로, 두께, 길이)")]
    [SerializeField] Vector3 dashSize = new Vector3(0.09f, 0.02f, 0.30f);
    [Tooltip("조각 사이 간격(m). 이 간격으로 채우다가 개수가 모자라면 남은 구간은 비운다")]
    [SerializeField] float dashSpacing = 0.62f;

    [Tooltip("커서 링의 반지름")]
    [SerializeField] float ringRadius = 0.32f;
    [Tooltip("링을 이루는 조각 수")]
    [SerializeField, Min(4)] int ringSegments = 14;
    [SerializeField] Vector3 ringPieceSize = new Vector3(0.09f, 0.02f, 0.14f);

    [Tooltip("조준면과 겹쳐 깜빡이지 않게 법선 방향으로 살짝 띄운다")]
    [SerializeField] float surfaceOffset = 0.06f;
    [Tooltip("점선이 시작하는 어깨 높이")]
    [SerializeField] float lineStartHeight = 0.3f;

    [Tooltip("플레이어 바로 앞은 비워둔다 — 발밑에 점선이 뭉치면 지저분하다")]
    [SerializeField] float startGap = 0.9f;

    [SerializeField] Color lineColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] Color ringColor = new Color(1f, 1f, 1f, 1f);

    PlayerAim aim;
    PlayerStats stats;
    Transform root;
    Transform[] dashes;
    Transform[] ringPieces;

    void Awake()
    {
        aim = GetComponent<PlayerAim>();
        stats = GetComponent<PlayerStats>();
        Build();
    }

    void Build()
    {
        // 플레이어의 자식으로 두면 플레이어가 돌 때 같이 돌아버린다. 월드에 독립으로 둔다.
        root = new GameObject("AimCursor").transform;

        dashes = new Transform[dashCount];
        for (int i = 0; i < dashCount; i++)
            dashes[i] = CreatePiece("Dash_" + i, dashSize, lineColor);

        ringPieces = new Transform[ringSegments];
        for (int i = 0; i < ringSegments; i++)
            ringPieces[i] = CreatePiece("Ring_" + i, ringPieceSize, ringColor);
    }

    Transform CreatePiece(string name, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(root, false);
        go.transform.localScale = size;

        // 조준 표시가 총알이나 물리를 막으면 안 된다. Destroy 는 프레임 끝까지 미뤄지므로 먼저 끈다.
        var collider = go.GetComponent<Collider>();
        if (collider)
        {
            collider.enabled = false;
            Destroy(collider);
        }

        // 런타임에만 만들어지므로 블록이 날아갈 일은 없지만, 색 적용 경로를 하나로 둔다.
        if (go.GetComponent<Renderer>()) go.AddComponent<FaceTint>().SetTint(color);

        return go.transform;
    }

    void LateUpdate()
    {
        if (!aim || root == null) return;

        bool show = !(stats && stats.IsDead);
        if (!show) { SetVisible(dashes, 0); SetVisible(ringPieces, 0); return; }

        // 점선은 어깨 높이에서 조준 지점까지 3D 로 잇는다. 예전에는 둘 다 지면 높이로
        // 눌러서 벽이나 블록 위를 겨눌 때 선이 바닥에만 깔렸다.
        Vector3 from = transform.position + Vector3.up * lineStartHeight;
        Vector3 to = aim.AimPoint + aim.AimNormal * surfaceOffset;

        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < startGap + 0.2f) { SetVisible(dashes, 0); PlaceRing(to, aim.AimNormal); return; }

        Vector3 direction = delta / distance;
        var rotation = Quaternion.LookRotation(direction);

        // 조각 수가 고정이라 간격을 그대로 쓰면 먼 거리에서 선이 중간에 끊긴다.
        // 거리가 길면 간격을 늘려서 항상 조준 지점까지 닿게 한다.
        float spacing = Mathf.Max(dashSpacing, (distance - startGap) / dashes.Length);

        int used = 0;
        for (float travelled = startGap; travelled < distance && used < dashes.Length; travelled += spacing)
        {
            var piece = dashes[used++];
            piece.position = from + direction * travelled;
            piece.rotation = rotation;
            piece.gameObject.SetActive(true);
        }
        SetVisible(dashes, used);

        PlaceRing(to, aim.AimNormal);
    }

    /// <summary>링을 조준면에 눕힌다. 지면이면 수평, 벽이면 수직으로 붙는다.</summary>
    void PlaceRing(Vector3 center, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f) normal = Vector3.up;
        normal = normal.normalized;

        // 법선과 나란하지 않은 아무 축으로 접평면의 기준 두 축을 만든다.
        Vector3 reference = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
        Vector3 axisA = Vector3.Normalize(Vector3.Cross(normal, reference));
        Vector3 axisB = Vector3.Cross(normal, axisA);

        for (int i = 0; i < ringPieces.Length; i++)
        {
            float angle = i / (float)ringPieces.Length * Mathf.PI * 2f;
            Vector3 offset = (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * ringRadius;

            var piece = ringPieces[i];
            piece.position = center + offset;
            // 조각을 원 접선 방향으로 눕혀 테두리처럼 보이게 한다.
            Vector3 tangent = Vector3.Cross(normal, offset);
            piece.rotation = tangent.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(tangent, normal)
                : Quaternion.LookRotation(normal);
            piece.gameObject.SetActive(true);
        }
    }

    /// <summary>앞에서 count 개만 남기고 나머지는 숨긴다 (매 프레임 생성/파괴하지 않는다).</summary>
    static void SetVisible(Transform[] pieces, int count)
    {
        for (int i = count; i < pieces.Length; i++)
            if (pieces[i]) pieces[i].gameObject.SetActive(false);
    }

    void OnEnable() { if (root) root.gameObject.SetActive(true); }
    void OnDisable() { if (root) root.gameObject.SetActive(false); }

    void OnDestroy()
    {
        if (root) Destroy(root.gameObject);
    }
}
