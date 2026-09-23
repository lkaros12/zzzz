using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GDD 16항 우측 하단 2D 캐릭터 일러스트. 화면에 고정되며 월드 위치와 무관하다 (GDD 23항).
/// GDD 6항대로 V0.1 에서는 부상 상태에 따라 이미지를 교체한다.
///
/// 일러스트 PNG 가 없으면 색 사각형 + 상태 텍스트로 대체해 시스템만 돌려둔다.
/// </summary>
public class CharacterPortraitUI : MonoBehaviour
{
    [SerializeField] PlayerInjuries injuries;
    [SerializeField] Image portrait;
    [SerializeField] Text stateLabel;

    [Tooltip("부상 등급별 일러스트 (없음/경상/중상/심각). 비어 있으면 색으로 대체한다")]
    [SerializeField] Sprite[] severitySprites = new Sprite[4];

    [Tooltip("일러스트가 없을 때 등급별 대체 색")]
    [SerializeField] Color[] fallbackColors =
    {
        new Color(0.80f, 0.82f, 0.86f, 0.95f),
        new Color(0.85f, 0.75f, 0.55f, 0.95f),
        new Color(0.85f, 0.55f, 0.42f, 0.95f),
        new Color(0.72f, 0.30f, 0.28f, 0.95f),
    };

    int lastVersion = -1;

    public void Bind(PlayerInjuries playerInjuries, Image image, Text label, Sprite[] sprites)
    {
        injuries = playerInjuries;
        portrait = image;
        stateLabel = label;
        if (sprites != null && sprites.Length == severitySprites.Length) severitySprites = sprites;
    }

    void Start() => Apply();

    void Update()
    {
        if (!injuries || injuries.Version == lastVersion) return;
        Apply();
    }

    void Apply()
    {
        if (!injuries) return;
        lastVersion = injuries.Version;

        int index = Mathf.Clamp((int)injuries.WorstSeverity, 0, severitySprites.Length - 1);

        // 해당 등급 이미지가 없으면 더 낮은 등급으로 내려가며 찾는다.
        // 등급별 일러스트를 다 준비하지 않아도 동작하게 하려는 것 (GDD 6항: 부상 일러스트 우선 구현).
        var sprite = severitySprites[index];
        for (int i = index - 1; i >= 0 && !sprite; i--) sprite = severitySprites[i];

        if (portrait)
        {
            portrait.sprite = sprite;
            // 일러스트가 있으면 원래 색으로, 없으면 등급 색으로 상태를 표현한다.
            portrait.color = sprite ? Color.white : fallbackColors[Mathf.Clamp(index, 0, fallbackColors.Length - 1)];
            portrait.preserveAspect = true;
        }

        if (!stateLabel) return;

        stateLabel.text = injuries.HasAnyInjury
            ? PlayerInjuries.SeverityName(injuries.WorstSeverity) + " (" + injuries.InjuryCount + "곳)"
            : "양호";
    }
}
