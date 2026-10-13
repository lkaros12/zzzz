using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>GDD 3항 카메라. 고정 아이소메트릭, 회전 불가, 플레이어 추적, 휠 줌.</summary>
[RequireComponent(typeof(Camera))]
public class IsometricCamera : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Vector3 fixedEulerAngles = new Vector3(30f, 45f, 0f);
    [SerializeField] float distance = 30f;

    [Header("줌 (Orthographic Size)")]
    // 9 면 지면 49x36m 가 보여서 방 하나가 화면의 1/10 이 된다 (실측). 좀보이드 정도로 당긴다.
    [SerializeField] float zoom = 5.5f;
    [SerializeField] float minZoom = 1.5f;
    [Tooltip("맵이 60x60 이라 18 까지 빼면 바닥 끝과 그 밖 빈 공간이 보인다. 맵이 커지면 올린다")]
    [SerializeField] float maxZoom = 14f;
    [SerializeField] float zoomStep = 1.5f;
    [SerializeField] float zoomLerpSpeed = 12f;

    Camera cam;
    float targetZoom;

    InventoryUI inventoryUi;
    MapUI mapUi;
    StatusPanelUI statusUi;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;
        transform.rotation = Quaternion.Euler(fixedEulerAngles);
        targetZoom = Mathf.Clamp(zoom, minZoom, maxZoom);
        cam.orthographicSize = targetZoom;

        inventoryUi = FindFirstObjectByType<InventoryUI>();
        mapUi = FindFirstObjectByType<MapUI>();
        statusUi = FindFirstObjectByType<StatusPanelUI>();

        // 기획 2·8항 벽 투명화 + 시야 음영. 둘 다 카메라에 붙는 렌더링 기능이라
        // 씬을 다시 굽지 않아도 붙도록 여기서 보강한다.
        var fader = GetComponent<WallFader>();
        if (!fader) fader = gameObject.AddComponent<WallFader>();
        fader.SetTarget(target);

        if (!GetComponent<VisionFog>()) gameObject.AddComponent<VisionFog>();
        var firstPerson = GetComponent<FirstPersonView>();
        if (!firstPerson) firstPerson = gameObject.AddComponent<FirstPersonView>();
        firstPerson.SetTarget(target);
    }

    void LateUpdate()
    {
    // 패널이 열려 있으면 휠은 그 패널 것이다. 커서가 UI 그래픽 위에 있는지로만 판단하면
        // 인벤토리 빈 격자(레이캐스트 대상이 없는 곳) 위에서 맵이 같이 줌아웃된다.
        bool panelOpen = (inventoryUi && inventoryUi.IsOpen)
                         || (mapUi && mapUi.IsOpen)
                         || (statusUi && statusUi.IsOpen);

        bool overUi = panelOpen
                      || (EventSystem.current && EventSystem.current.IsPointerOverGameObject());

        // 휠 델타는 플랫폼마다 크기가 달라서(윈도우는 노치당 ±120) 부호만 쓴다.
        float scroll = !overUi && Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
        if (Mathf.Abs(scroll) > 0.01f)
            targetZoom = Mathf.Clamp(targetZoom - Mathf.Sign(scroll) * zoomStep, minZoom, maxZoom);

        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetZoom, zoomLerpSpeed * Time.deltaTime);

        if (target) transform.position = target.position - transform.forward * distance;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        var fader = GetComponent<WallFader>();
        if (fader) fader.SetTarget(newTarget);
        var firstPerson = GetComponent<FirstPersonView>();
        if (firstPerson) firstPerson.SetTarget(newTarget);
    }
}
