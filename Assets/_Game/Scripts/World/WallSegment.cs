using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 기획 2·3항 벽 한 조각. 가려질 때 반투명해지는 것만 담당한다.
///
/// 벽 전체가 아니라 조각 단위로 쪼개 두기 때문에 플레이어를 실제로 가리는 부분만 흐려진다.
/// 콜라이더는 손대지 않는다 — 투명해져도 못 지나간다 (기획 12항 2번).
///
/// 보이는 메쉬는 자기 자신일 수도 있고(플레이스홀더 박스), 자식으로 붙은 블렌더 작업본일 수도
/// 있다. 그래서 자기 렌더러가 아니라 "켜져 있는 모든 자식 렌더러" 를 대상으로 삼는다.
///
/// URP Lit 은 불투명/반투명이 아예 다른 머티리얼이라 알파만 낮출 수 없다.
/// 그래서 흐려질 때만 반투명 머티리얼로 갈아끼우고, 알파는 프로퍼티 블록으로 넣는다.
/// 반투명본은 빌더가 원본 머티리얼마다 하나씩 만들어 꽂아준다 (런타임 생성은 누출된다).
/// </summary>
public class WallSegment : MonoBehaviour
{
    static readonly List<WallSegment> all = new List<WallSegment>();
    public static IReadOnlyList<WallSegment> All => all;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Tooltip("가릴 때의 불투명도. 0 이면 완전히 사라져서 벽이 있다는 걸 알 수 없다")]
    [SerializeField, Range(0.05f, 0.9f)] float hiddenAlpha = 0.25f;
    [Tooltip("초당 알파 변화량. 1 이면 1초에 완전히 흐려진다")]
    [SerializeField] float fadeSpeed = 6f;

    [SerializeField] Renderer[] targets;
    [SerializeField] Material[] opaqueMaterials;
    [SerializeField] Material[] fadeMaterials;
    [SerializeField] bool exterior;
    [SerializeField] Room[] roofRooms;
    [SerializeField] AssetMaterialSet[] assetMaterials;

    [System.Serializable]
    class AssetMaterialSet
    {
        public Material[] opaque;
        public Material[] fade;
        [System.NonSerialized] public bool faded;
    }

    MaterialPropertyBlock block;
    Material[] roofOpaqueMaterials;
    float alpha = 1f;
    bool requested;

    /// <summary>검증용. 지금 반투명한 상태인지.</summary>
    public bool IsFaded => alpha < 0.99f;
    public float Alpha => alpha;
    public int VisualCount => targets != null ? targets.Length : 0;
    public bool IsExterior => exterior;
    public bool IsRoof => roofRooms != null && roofRooms.Length > 0;
    public bool DrawsOverVisionFog => IsRoof;
    public int TargetOpaqueRenderQueue
    {
        get
        {
            var fog = Shader.Find("Game/VisionFog");
            return fog ? fog.renderQueue + 1 : -1;
        }
    }
    public IReadOnlyList<Room> RoofRooms => roofRooms;

    public Bounds Bounds
    {
        get
        {
            if (targets == null || targets.Length == 0 || !targets[0]) return new Bounds(transform.position, Vector3.zero);

            var bounds = targets[0].bounds;
            for (int i = 1; i < targets.Length; i++)
                if (targets[i]) bounds.Encapsulate(targets[i].bounds);
            return bounds;
        }
    }

    public Vector3 Center
    {
        get
        {
            if (targets != null && targets.Length > 0 && targets[0]) return targets[0].bounds.center;

            var own = GetComponent<Renderer>();
            return own ? own.bounds.center : transform.position;
        }
    }

    void Awake()
    {
        if (!all.Contains(this)) all.Add(this);
        if (targets == null || targets.Length == 0) BindOwnRenderer();
        CreateRoofOpaqueMaterials();
        if (alpha >= 0.99f && !requested) enabled = false;
    }

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() { if (enabled) all.Remove(this); }

    void OnDestroy()
    {
        all.Remove(this);
        if (roofOpaqueMaterials == null) return;
        for (int i = 0; i < roofOpaqueMaterials.Length; i++)
            if (roofOpaqueMaterials[i]) Destroy(roofOpaqueMaterials[i]);
    }

    /// <summary>플레이스홀더 박스일 때. 자기 렌더러 하나만 다룬다.</summary>
    public void Setup(Material opaque, Material fade, bool isExterior = false)
    {
        var own = GetComponent<Renderer>();
        if (!own) return;

        targets = new[] { own };
        opaqueMaterials = new[] { opaque ? opaque : own.sharedMaterial };
        fadeMaterials = new[] { fade };
        exterior = isExterior;
    }

    /// <summary>지붕과 방을 빌더가 직접 묶는다. 이름이나 좌표로 소속 건물을 추측하지 않는다.</summary>
    public void BindRoof(Room[] rooms) => roofRooms = rooms;

    public bool Covers(Room room)
    {
        if (!room || roofRooms == null) return false;
        for (int i = 0; i < roofRooms.Length; i++)
            if (roofRooms[i] == room) return true;
        return false;
    }

    public void SetVisible(bool visible)
    {
        if (targets == null) return;
        for (int i = 0; i < targets.Length; i++)
            if (targets[i] && targets[i].enabled != visible) targets[i].enabled = visible;
    }

    void BindOwnRenderer()
    {
        var own = GetComponent<Renderer>();
        if (!own) return;

        targets = new[] { own };
        opaqueMaterials = new[] { own.sharedMaterial };
        fadeMaterials = new Material[1];
    }

    void CreateRoofOpaqueMaterials()
    {
        int renderQueue = TargetOpaqueRenderQueue;
        if (!DrawsOverVisionFog || renderQueue < 0 || opaqueMaterials == null) return;

        roofOpaqueMaterials = new Material[opaqueMaterials.Length];
        for (int i = 0; i < opaqueMaterials.Length; i++)
        {
            if (!opaqueMaterials[i]) continue;
            roofOpaqueMaterials[i] = new Material(opaqueMaterials[i]) { renderQueue = renderQueue };
        }
        opaqueMaterials = roofOpaqueMaterials;
        Apply();
    }

    /// <summary>
    /// 블렌더 작업본을 붙였을 때. 보이는 메쉬가 자식으로 바뀌었으므로 대상을 갈아끼운다.
    /// fade 는 각 원본 머티리얼의 반투명본이고, 빌더가 미리 에셋으로 만들어 넘긴다.
    /// </summary>
    public void BindVisuals(Renderer[] renderers, Material[] opaque, Material[] fade)
    {
        targets = renderers;
        opaqueMaterials = opaque;
        fadeMaterials = fade;
        alpha = 1f;
    }

    /// <summary>도시 에셋 한 채를 한 조각으로 묶는다. 기존 별장 바인딩은 그대로 둔다.</summary>
    public void BindAssetVisuals(Renderer[] renderers, Material[][] fades)
    {
        targets = renderers;
        exterior = true;
        assetMaterials = new AssetMaterialSet[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            assetMaterials[i] = new AssetMaterialSet { opaque = renderers[i].sharedMaterials, fade = fades[i] };
        alpha = 1f;
    }

    /// <summary>이번 프레임에 이 벽이 플레이어를 가리고 있다. 매 프레임 다시 불러야 한다.</summary>
    public void RequestFade()
    {
        requested = true;
        enabled = true;
    }

    // 1인칭 진입 때 즉시 불투명하게 만들고, 복귀 때 저장한 알파를 되돌린다.
    public void RestoreAlpha(float value)
    {
        alpha = value;
        requested = false;
        Apply();
    }

    void LateUpdate()
    {
        float target = requested ? hiddenAlpha : 1f;
        requested = false;

        if (Mathf.Approximately(alpha, target))
        {
            if (target >= 0.99f) enabled = false;
            return;
        }

        alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);
        Apply();
        if (Mathf.Approximately(alpha, 1f)) enabled = false;
    }

    void ApplyAssetMaterials()
    {
        bool transparent = IsFaded;
        if (block == null) block = new MaterialPropertyBlock();
        for (int i = 0; i < targets.Length; i++)
        {
            var renderer = targets[i];
            if (!renderer) continue;
            var set = assetMaterials[i];
            if (set.faded != transparent)
            {
                renderer.sharedMaterials = transparent ? set.fade : set.opaque;
                set.faded = transparent;
            }
            for (int slot = 0; slot < set.opaque.Length; slot++)
            {
                if (!transparent) { renderer.SetPropertyBlock(null, slot); continue; }
                block.Clear();
                renderer.GetPropertyBlock(block, slot);
                Color color = set.fade[slot].color;
                color.a = alpha;
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                renderer.SetPropertyBlock(block, slot);
            }
        }
    }

    void Apply()
    {
        if (targets == null) return;
        if (assetMaterials != null && assetMaterials.Length > 0)
        {
            ApplyAssetMaterials();
            return;
        }

        bool transparent = IsFaded;
        if (block == null) block = new MaterialPropertyBlock();

        for (int i = 0; i < targets.Length; i++)
        {
            var renderer = targets[i];
            if (!renderer) continue;

            var fade = fadeMaterials != null && i < fadeMaterials.Length ? fadeMaterials[i] : null;
            var opaque = opaqueMaterials != null && i < opaqueMaterials.Length ? opaqueMaterials[i] : null;

            var wanted = transparent && fade ? fade : opaque;
            if (wanted && renderer.sharedMaterial != wanted) renderer.sharedMaterial = wanted;

            if (!transparent || !fade) { renderer.SetPropertyBlock(null); continue; }

            renderer.GetPropertyBlock(block);
            Color color = fade.color;
            color.a = alpha;
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }
    }
}
