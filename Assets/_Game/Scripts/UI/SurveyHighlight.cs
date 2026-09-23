using UnityEngine;

/// <summary>
/// GDD 4항 F "주변 조사 / 오브젝트 강조". 누르면 주변의 아이템과 컨테이너가 잠깐 밝아진다.
///
/// 3D 아트가 없어 외곽선 셰이더를 못 쓰므로, HitFlash 와 같은 MaterialPropertyBlock 방식으로
/// 색을 물들여 강조한다 — 공유 머티리얼을 복제하지 않는다.
/// </summary>
public class SurveyHighlight : MonoBehaviour
{
    [Tooltip("이 거리 안의 대상을 강조한다")]
    [SerializeField] float surveyRange = 14f;

    [Tooltip("강조가 유지되는 시간")]
    [SerializeField] float duration = 2.5f;

    [SerializeField] Color itemColor = new Color(1f, 0.95f, 0.45f);
    [SerializeField] Color containerColor = new Color(0.5f, 0.85f, 1f);

    /// <summary>UI 용. 마지막 조사에서 강조한 대상 수.</summary>
    public int LastHighlightCount { get; private set; }
    public float LastSurveyTime { get; private set; } = -99f;

    /// <summary>F 입력 처리. 강조한 대상 수를 반환.</summary>
    public int Survey()
    {
        int count = 0;
        float rangeSqr = surveyRange * surveyRange;

        foreach (var item in FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
        {
            if ((item.transform.position - transform.position).sqrMagnitude > rangeSqr) continue;
            if (Highlight(item.gameObject, itemColor)) count++;
        }

        var containers = WorldContainer.All;
        for (int i = 0; i < containers.Count; i++)
        {
            var container = containers[i];
            if (!container) continue;
            if ((container.transform.position - transform.position).sqrMagnitude > rangeSqr) continue;
            if (Highlight(container.gameObject, containerColor)) count++;
        }

        LastHighlightCount = count;
        LastSurveyTime = Time.time;
        return count;
    }

    bool Highlight(GameObject target, Color color)
    {
        if (!target.GetComponent<Renderer>()) return false;

        var flash = target.GetComponent<HitFlash>();
        if (!flash) flash = target.AddComponent<HitFlash>();

        flash.Play(color, duration);
        return true;
    }
}
