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

    MaterialPropertyBlock block;
    float alpha = 1f;
    bool requested;

    /// <summary>검증용. 지금 반투명한 상태인지.</summary>
    public bool IsFaded => alpha < 0.99f;
    public float Alpha => alpha;
    public int VisualCount => targets != null ? targets.Length : 0;

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
        if (targets == null || targets.Length == 0) BindOwnRenderer();
    }

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    /// <summary>플레이스홀더 박스일 때. 자기 렌더러 하나만 다룬다.</summary>
    public void Setup(Material opaque, Material fade)
    {
        var own = GetComponent<Renderer>();
        if (!own) return;

        targets = new[] { own };
        opaqueMaterials = new[] { opaque ? opaque : own.sharedMaterial };
        fadeMaterials = new[] { fade };
    }

    void BindOwnRenderer()
    {
        var own = GetComponent<Renderer>();
        if (!own) return;

        targets = new[] { own };
        opaqueMaterials = new[] { own.sharedMaterial };
        fadeMaterials = new Material[1];
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

    /// <summary>이번 프레임에 이 벽이 플레이어를 가리고 있다. 매 프레임 다시 불러야 한다.</summary>
    public void RequestFade() => requested = true;

    void LateUpdate()
    {
        float target = requested ? hiddenAlpha : 1f;
        requested = false;

        if (Mathf.Approximately(alpha, target)) return;

        alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);
        Apply();
    }

    void Apply()
    {
        if (targets == null) return;

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
