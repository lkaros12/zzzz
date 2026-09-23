using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어가 마우스 커서를 바라보게 한다. 시야와 공격 방향이 모두 이 방향을 쓴다.
///
/// 아이소메트릭이라 커서의 스크린 좌표를 플레이어 발밑 높이의 평면에 투영해서 월드 지점을 구한다.
/// 카메라 forward 를 그대로 쓰면 30° 내려보는 각도 때문에 지면 좌표가 어긋난다.
/// </summary>
public class PlayerAim : MonoBehaviour
{
    [SerializeField] Camera aimCamera;
    [Tooltip("0 이면 즉시 회전. 값을 주면 초당 각도로 따라간다")]
    [SerializeField] float turnSpeed = 0f;
    [Tooltip("커서 레이가 닿는 최대 거리. 총 사거리보다 넉넉하게 둔다")]
    [SerializeField] float maxAimDistance = 200f;

    PlayerGrapple grapple;
    PlayerStats stats;
    PlayerAssassinate assassinate;

    readonly RaycastHit[] hitBuffer = new RaycastHit[32];

    /// <summary>커서가 가리키는 월드 지점. 지면뿐 아니라 벽/장애물/좀비 표면도 된다.</summary>
    public Vector3 AimPoint { get; private set; }

    /// <summary>조준 지점의 표면 법선. 커서 링을 그 면에 눕히는 데 쓴다. 지면이면 위쪽.</summary>
    public Vector3 AimNormal { get; private set; } = Vector3.up;

    /// <summary>실제 콜라이더를 맞혔는지. 아니면 지면 평면으로 폴백한 것이다.</summary>
    public bool AimHitSurface { get; private set; }

    /// <summary>플레이어에서 커서로 향하는 수평 방향. 커서가 발밑이면 이전 값을 유지한다.</summary>
    public Vector3 AimDirection { get; private set; } = Vector3.forward;

    void Awake()
    {
        grapple = GetComponent<PlayerGrapple>();
        stats = GetComponent<PlayerStats>();
        assassinate = GetComponent<PlayerAssassinate>();
        AimDirection = transform.forward;
    }

    void Update()
    {
        if (!aimCamera) aimCamera = Camera.main;
        if (!aimCamera) return;

        // 붙잡혀 있거나 죽었으면 조준을 갱신하지 않는다.
        // GDD 19항 암살 중에는 위치와 방향이 고정되므로 마우스를 따라 돌지 않는다.
        if (stats && stats.IsDead) return;
        if (grapple && grapple.IsGrappled) return;
        if (assassinate && assassinate.IsAssassinating) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (!TryResolveAimPoint(mouse.position.ReadValue(), out var point)) return;

        AimPoint = point;

        Vector3 flat = point - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.01f) return;

        AimDirection = flat.normalized;
        FaceAim();
    }

    /// <summary>
    /// 커서 아래에 실제로 있는 것을 먼저 찾는다. 벽이나 블록 위도 조준할 수 있어야 한다.
    /// 아무것도 없으면 예전처럼 플레이어 발밑 높이의 지면 평면으로 떨어뜨린다.
    /// </summary>
    bool TryResolveAimPoint(Vector2 screenPosition, out Vector3 point)
    {
        var ray = aimCamera.ScreenPointToRay(screenPosition);

        // DefaultRaycastLayers 는 'Ignore Raycast' 를 뺀다 — 보이지 않는 천장을 조준하지 않도록.
        int count = Physics.RaycastNonAlloc(ray, hitBuffer, maxAimDistance,
                                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var collider = hitBuffer[i].collider;
            if (!collider) continue;
            if (collider.transform == transform) continue; // 내 몸은 조준 대상이 아니다

            if (hitBuffer[i].distance < bestDistance) { bestDistance = hitBuffer[i].distance; best = i; }
        }

        if (best >= 0)
        {
            point = hitBuffer[best].point;
            AimNormal = hitBuffer[best].normal;
            AimHitSurface = true;
            return true;
        }

        var ground = new Plane(Vector3.up, transform.position);
        if (ground.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            AimNormal = Vector3.up;
            AimHitSurface = false;
            return true;
        }

        point = transform.position;
        AimNormal = Vector3.up;
        AimHitSurface = false;
        return false;
    }

    void FaceAim()
    {
        var look = Quaternion.LookRotation(AimDirection);
        transform.rotation = turnSpeed <= 0f
            ? look
            : Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
    }
}
