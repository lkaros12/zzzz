using UnityEngine;
using UnityEngine.UI;

/// <summary>물림과 출혈 틱을 화면 가장자리의 붉은 섬광으로 알린다.</summary>
public class DamageVignetteUI : MonoBehaviour
{
    [SerializeField] PlayerInjuries injuries;
    [SerializeField] Image image;
    [SerializeField] float fadeDuration = 0.6f;
    [SerializeField, Range(0f, 1f)] float biteAlpha = 0.75f;
    [SerializeField, Range(0f, 1f)] float bleedingAlpha = 0.45f;

    int lastBiteVersion;
    int lastBleedingTickVersion;
    float alpha;
    float fadeSpeed;
    Texture2D texture;
    Sprite sprite;

    public void Bind(PlayerInjuries playerInjuries, Image vignette)
    {
        injuries = playerInjuries;
        image = vignette;
        if (injuries)
        {
            lastBiteVersion = injuries.BiteVersion;
            lastBleedingTickVersion = injuries.BleedingTickVersion;
        }
    }

    void Start() => CreateTexture();

    void Update()
    {
        if (!injuries || !image) return;

        if (lastBiteVersion != injuries.BiteVersion)
        {
            lastBiteVersion = injuries.BiteVersion;
            Flash(biteAlpha);
        }
        if (lastBleedingTickVersion != injuries.BleedingTickVersion)
        {
            lastBleedingTickVersion = injuries.BleedingTickVersion;
            Flash(bleedingAlpha);
        }

        alpha = Mathf.Max(0f, alpha - fadeSpeed * Time.unscaledDeltaTime);
        image.color = new Color(1f, 1f, 1f, alpha);
    }

    void Flash(float targetAlpha)
    {
        alpha = Mathf.Max(alpha, targetAlpha);
        fadeSpeed = fadeDuration > 0f ? alpha / fadeDuration : alpha;
    }

    void CreateTexture()
    {
        const int size = 64;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "DamageVignetteTexture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float edgeDistance = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
                float edge = 1f - Mathf.Clamp01(edgeDistance / (size * 0.42f));
                pixels[y * size + x] = new Color(0.65f, 0f, 0f, edge * edge);
            }

        texture.SetPixels(pixels);
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        image.sprite = sprite;
        image.color = new Color(1f, 1f, 1f, 0f);
    }

    void OnDestroy()
    {
        if (sprite) Destroy(sprite);
        if (texture) Destroy(texture);
    }
}
