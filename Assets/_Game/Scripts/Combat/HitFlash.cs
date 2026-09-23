using UnityEngine;

/// <summary>
/// 피격 연출. 맞은 대상만 잠깐 밝아진다.
///
/// 좀비는 공유 머티리얼을 쓰므로 material.color 를 건드리면 모든 좀비가 같이 번쩍인다.
/// MaterialPropertyBlock 은 렌더러 단위로만 값을 덮어쓰므로 머티리얼을 복제하지 않고 한 마리만 칠할 수 있다.
/// </summary>
public class HitFlash : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int LegacyColorId = Shader.PropertyToID("_Color");

    [SerializeField] Color flashColor = new Color(1f, 0.92f, 0.92f);
    [SerializeField] float duration = 0.16f;

    Renderer[] targets;
    MaterialPropertyBlock block;
    Color baseColor;
    Color activeColor;
    float activeDuration;
    float endTime = -1f;

    void Awake() => Cache();

    /// <summary>에디트 모드(자기검사/빌더) 에서는 Awake 가 돌지 않아 Play 시점에 한 번 더 챈다.</summary>
    void Cache()
    {
        if (block != null) return;

        // 캡슐 플레이스홀더는 렌더러가 자기 자신에게 붙어 있지만, 3D 모델 좀비는
        // 스킨드 메쉬가 자식에 있다. 둘 다 물들여야 피격 피드백이 사라지지 않는다.
        targets = GetComponentsInChildren<Renderer>(true);
        block = new MaterialPropertyBlock();

        var material = targets.Length > 0 ? targets[0].sharedMaterial : null;
        baseColor = material ? material.color : Color.white;
    }

    public void Play() => Play(flashColor, duration);

    /// <summary>색과 길이를 지정해 물들인다. GDD 22항 돌진 준비 모션을 색으로 예고하는 데 쓴다.</summary>
    public void Play(Color color, float seconds)
    {
        if (seconds <= 0f) return;

        Cache();
        if (targets == null || targets.Length == 0) return;

        activeColor = color;
        activeDuration = seconds;
        endTime = Time.time + seconds;
        Apply(color);
    }

    void Update()
    {
        if (endTime < 0f) return;

        float remaining = endTime - Time.time;
        if (remaining <= 0f)
        {
            endTime = -1f;
            for (int i = 0; i < targets.Length; i++)
                if (targets[i]) targets[i].SetPropertyBlock(null); // 공유 머티리얼 원래 색으로 복귀
            return;
        }

        Apply(Color.Lerp(baseColor, activeColor, remaining / activeDuration));
    }

    void Apply(Color color)
    {
        if (targets == null || block == null) return;

        block.SetColor(BaseColorId, color);
        block.SetColor(LegacyColorId, color);
        for (int i = 0; i < targets.Length; i++)
            if (targets[i]) targets[i].SetPropertyBlock(block);
    }
}
