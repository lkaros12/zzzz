using UnityEngine;

/// <summary>
/// 기획 5항 창문 넘기. Space 가 점프 대신 이 동작이 되는 조건은 WindowFrame.CanVault 가 정한다.
///
/// 이동 중에는 CharacterController 를 꺼 두고 transform 을 직접 옮긴다.
/// 켜 둔 채로 Move 하면 창턱과 벽 사이에서 걸려 반쯤 넘다 끼인다 (콜라이더 안으로 밀어넣는 셈).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerVault : MonoBehaviour
{
    [SerializeField] float duration = 0.45f;
    [Tooltip("넘는 동안 몸이 뜨는 높이")]
    [SerializeField] float arcHeight = 0.55f;

    CharacterController cc;
    Vector3 from, to;
    float startTime = -1f;

    /// <summary>검증용. 마지막으로 넘은 창문.</summary>
    public WindowFrame LastWindow { get; private set; }

    public bool IsVaulting => startTime >= 0f && Time.time < startTime + duration;

    void Awake() => cc = GetComponent<CharacterController>();

    /// <summary>넘을 창문이 있으면 시작하고 true. 없으면 false 라서 호출한 쪽이 점프로 넘어간다.</summary>
    public bool TryVault()
    {
        if (IsVaulting) return true;

        var window = WindowFrame.VaultableFrom(transform.position);
        if (!window) return false;

        LastWindow = window;
        from = transform.position;
        to = window.VaultTarget(from, cc.height * 0.5f);
        startTime = Time.time;

        // 넘는 동안은 이 창이 닫히지 않게 잠근다. 안 잠그면 공중에서 E 로 닫아
        // 창짝 콜라이더가 몸 안에서 켜진다.
        window.BeginVault();

        cc.enabled = false;
        return true;
    }

    void Update()
    {
        if (startTime < 0f) return;

        float t = Mathf.Clamp01((Time.time - startTime) / duration);
        Vector3 position = Vector3.Lerp(from, to, t);
        position.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
        transform.position = position;

        if (t < 1f) return;

        startTime = -1f;
        cc.enabled = true;
        if (LastWindow) LastWindow.EndVault();
    }
}
