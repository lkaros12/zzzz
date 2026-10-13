using UnityEngine;

/// <summary>
/// 기획 8·10항 시야 음영. VisionField 가 낸 판정을 화면에 그린다 — 판정은 하지 않는다.
///
/// 방법: 카메라 바로 앞에 사각형 한 장을 두고, 셰이더가 깊이 버퍼로 각 픽셀의 월드 좌표를
/// 복원해 그 지점의 밝기를 찾아 어둡게 한다. 바닥에 어두운 판을 까는 방식은 그 칸에 서 있는
/// 벽과 좀비가 밝게 남아서 못 쓴다 (아이소메트릭이라 키 큰 물체는 화면상 위로 비켜난다).
///
/// ponytail: 마스크는 144x144 Texture2D 를 매 프레임 CPU 로 채운다. 시야 범위가 훨씬 커지거나
/// 시야 주체가 여러 명이 되면 컴퓨트 셰이더로 옮긴다.
/// </summary>
[RequireComponent(typeof(Camera))]
public class VisionFog : MonoBehaviour
{
    [SerializeField] VisionField field;
    [SerializeField] Shader fogShader;
    [Tooltip("마스크 해상도. 높이면 경계가 또렷해지고 CPU 를 더 쓴다")]
    [SerializeField, Min(16)] int resolution = 144;
    [Tooltip("실외에서 시야 밖의 색과 짙기. 알파 1 이면 완전한 검정이 된다")]
    [SerializeField] Color fogColor = new Color(0.015f, 0.025f, 0.045f, 0.82f);
    // 알파를 0.975 로 뒀을 때는 선형 2.5% 만 남는데도 화면에서는 회색으로 읽혔다 —
    // sRGB 감마가 0.025 를 0.16 으로 끌어올린다. 그래서 여기는 0.99 를 넘겨야 검게 보인다.
    [Tooltip("밀실(방 안)에서 쓰는 짙은 음영. 실외 값으로는 낮 햇빛 위에서 회색으로 읽혀 안 보이는 곳이 다 보였다")]
    [SerializeField] Color indoorFogColor = new Color(0.002f, 0.003f, 0.006f, 0.995f);
    [Tooltip("문을 지날 때 짙기가 바뀌는 데 걸리는 시간(초). 0 이면 툭 끊긴다")]
    [SerializeField, Min(0f)] float indoorBlendTime = 0.35f;

    static readonly int VisionMaskId = Shader.PropertyToID("_VisionMask");
    static readonly int VisionAreaId = Shader.PropertyToID("_VisionArea");
    static readonly int FogColorId = Shader.PropertyToID("_FogColor");

    new Camera camera;
    Transform quad;
    Material material;
    Texture2D mask;
    Color32[] pixels;

    /// <summary>검증용. 마지막으로 올린 마스크 영역(가로세로 m).</summary>
    public float AreaSize { get; private set; }

    /// <summary>검증용. 0 이면 실외 짙기, 1 이면 밀실 짙기.</summary>
    public float IndoorBlend { get; private set; }

    void Awake()
    {
        camera = GetComponent<Camera>();
        if (!field) field = FindFirstObjectByType<VisionField>();
        if (!fogShader) fogShader = Shader.Find("Game/VisionFog");
        if (!fogShader) { enabled = false; return; }

        material = new Material(fogShader) { hideFlags = HideFlags.DontSave };
        material.SetColor(FogColorId, fogColor);

        mask = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.DontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        pixels = new Color32[resolution * resolution];
        material.SetTexture(VisionMaskId, mask);

        BuildQuad();
    }

    void BuildQuad()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "VisionFogQuad";
        go.hideFlags = HideFlags.DontSave;

        // 화면을 덮기만 하면 되는 판이라 물리에 끼어들면 안 된다.
        var collider = go.GetComponent<Collider>();
        if (collider) { collider.enabled = false; Destroy(collider); }

        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        quad = go.transform;
        quad.SetParent(transform, false);
    }

    void LateUpdate()
    {
        if (!field || quad == null) return;

        FitQuad();
        UpdateMask();
        UpdateFogColor();
    }

    /// <summary>
    /// 밀실에 있으면 시야 밖을 거의 검게 덮는다.
    ///
    /// 실외 짙기(알파 0.82) 로는 낮 햇빛을 받는 바닥이 18% 남아 회색으로 읽혀서,
    /// 안 보이는 영역의 가구와 방 배치가 그대로 다 보였다 (사용자 지적).
    /// 방 판정은 Room 이 한다 — 벽 투명화(WallFader) 와 같은 기준이다.
    /// </summary>
    void UpdateFogColor()
    {
        bool indoors = Room.At(field.Origin) != null;
        float step = indoorBlendTime > 0f ? Time.deltaTime / indoorBlendTime : 1f;

        IndoorBlend = Mathf.MoveTowards(IndoorBlend, indoors ? 1f : 0f, step);
        material.SetColor(FogColorId, Color.Lerp(fogColor, indoorFogColor, IndoorBlend));
    }

    /// <summary>줌이 바뀌어도 화면을 꽉 채우도록 근평면 바로 앞에 맞춘다.</summary>
    void FitQuad()
    {
        float depth = camera.nearClipPlane + 0.05f;
        float height = camera.orthographic ? camera.orthographicSize * 2f
                                           : 2f * depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

        quad.localPosition = new Vector3(0f, 0f, depth);
        quad.localRotation = Quaternion.identity;
        quad.localScale = new Vector3(height * camera.aspect * 1.05f, height * 1.05f, 1f);
    }

    void UpdateMask()
    {
        // 시야 범위보다 조금 넓게 잡는다. 딱 맞추면 가장자리 보간에서 밝기가 샌다.
        float size = (field.FlashlightRange + 2f) * 2f;
        AreaSize = size;

        Vector3 origin = field.Origin - new Vector3(size * 0.5f, 0f, size * 0.5f);
        // 칸별 판정은 VisionField 가 한다 (거리/각도 표를 그쪽이 캐시하고 있다).
        field.FillMask(pixels, resolution, size);

        mask.SetPixels32(pixels);
        mask.Apply(false);

        material.SetVector(VisionAreaId, new Vector4(origin.x, origin.z, 1f / size, 1f / size));
    }

    public void Bind(VisionField visionField) => field = visionField;

    void OnEnable() { if (quad) quad.gameObject.SetActive(true); }
    void OnDisable() { if (quad) quad.gameObject.SetActive(false); }

    void OnDestroy()
    {
        if (material) Destroy(material);
        if (mask) Destroy(mask);
    }
}
