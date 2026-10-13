using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>V로 잠깐 눈높이에서 본다. 등각 카메라와 렌더링 상태는 복귀 때 되돌린다.</summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-100)] // 애니메이션과 HairSpring(-200) 뒤에 머리 스케일을 덮어쓴다.
public sealed class FirstPersonView : MonoBehaviour
{
    [SerializeField] Transform target;
    public bool IsFirstPerson { get; private set; }
    public const float EyeHeight = 1.15f;

    Camera view;
    CharacterController body;
    Transform head;
    HairSpring hair;
    InventoryUI inventory;
    MapUI map;
    StatusPanelUI status;
    QuantityDialog quantity;
    Vector3 headScale, cameraOffset;
    Quaternion cameraRotation;
    bool orthographic;
    float size, fov, near, yaw, pitch;
    CursorLockMode cursorLock;
    bool cursorVisible;
    readonly List<(Behaviour component, bool enabled)> components = new List<(Behaviour, bool)>();
    readonly List<(Renderer renderer, bool enabled)> visuals = new List<(Renderer, bool)>();
    readonly List<(WallSegment wall, float alpha, bool enabled)> walls = new List<(WallSegment, float, bool)>();
    readonly List<(Renderer renderer, ShadowCastingMode mode)> hairRenderers = new List<(Renderer, ShadowCastingMode)>();

    public void SetTarget(Transform value) => target = value;

    // 자기검사에서도 씬이나 입력 없이 토글 대상을 확인한다.
    public static Type[] ComponentsToDisable(bool firstPerson) => firstPerson
        ? new[] { typeof(IsometricCamera), typeof(VisionFog), typeof(WallFader),
                  typeof(BuildingFloors), typeof(AimCursorFx), typeof(HairSpring) }
        : Array.Empty<Type>();

    void Start()
    {
        view = GetComponent<Camera>();
        if (!target) return;
        body = target.GetComponent<CharacterController>();
        var animator = target.GetComponentInChildren<Animator>();
        if (animator && animator.isHuman) head = animator.GetBoneTransform(HumanBodyBones.Head);
        hair = target.GetComponentInChildren<HairSpring>();
        inventory = FindFirstObjectByType<InventoryUI>();
        map = FindFirstObjectByType<MapUI>();
        status = FindFirstObjectByType<StatusPanelUI>();
        quantity = FindFirstObjectByType<QuantityDialog>();
    }

    bool PanelOpen => (inventory && inventory.IsOpen) || (map && map.IsOpen)
                   || (status && status.IsOpen) || (quantity && quantity.IsOpen);

    void Update()
    {
        if (!target || !view) return;
        if (Keyboard.current != null && Keyboard.current.vKey.wasPressedThisFrame)
            SetFirstPerson(!IsFirstPerson);
        if (!IsFirstPerson) return;
        UpdateCursor();
        if (!PanelOpen && Cursor.lockState == CursorLockMode.Locked && Mouse.current != null)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            yaw += delta.x * 0.1f;
            pitch = Mathf.Clamp(pitch - delta.y * 0.1f, -80f, 80f);
        }
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        PositionAtEyes();
    }

    void LateUpdate()
    {
        if (!IsFirstPerson || !target) return;
        PositionAtEyes();
        if (head) head.localScale = Vector3.one * 0.001f;
        // UI는 보통 Update에서 열리고 닫히므로 같은 프레임 끝에 커서를 맞춘다.
        UpdateCursor();
    }

    void PositionAtEyes()
    {
        Vector3 feet = body ? target.TransformPoint(body.center) - Vector3.up * body.height * 0.5f
                            : target.position;
        transform.position = feet + Vector3.up * EyeHeight;
    }

    void UpdateCursor()
    {
        bool release = PanelOpen || !Application.isFocused;
        Cursor.lockState = release ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = release;
    }

    public void SetFirstPerson(bool value)
    {
        if (value == IsFirstPerson || !target || !view) return;
        if (value)
        {
            cameraOffset = transform.position - target.position;
            cameraRotation = transform.rotation;
            orthographic = view.orthographic;
            size = view.orthographicSize;
            fov = view.fieldOfView;
            near = view.nearClipPlane;
            cursorLock = Cursor.lockState;
            cursorVisible = Cursor.visible;
            if (head) headScale = head.localScale;
            yaw = target.eulerAngles.y;
            pitch = 0f;
            var seen = new HashSet<Renderer>();
            var segments = WallSegment.All;
            for (int i = 0; i < segments.Count; i++)
            {
                var wall = segments[i];
                if (!wall) continue;
                walls.Add((wall, wall.Alpha, wall.enabled));
                RememberVisuals(wall.gameObject, seen);
                wall.RestoreAlpha(1f);
                wall.enabled = false;
            }
            var floors = FindObjectsByType<BuildingFloors>(FindObjectsSortMode.None);
            for (int i = 0; i < floors.Length; i++)
                foreach (var building in floors[i].Buildings)
                {
                    for (int f = 0; f < building.floors.Length; f++) RememberVisuals(building.floors[f], seen);
                    RememberVisuals(building.roof, seen);
                }
            foreach (Type type in ComponentsToDisable(true))
                foreach (var found in UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None))
                {
                    var component = (Behaviour)found;
                    components.Add((component, component.enabled));
                    component.enabled = false;
                }
            foreach (var visual in visuals) if (visual.renderer) visual.renderer.enabled = true;
            foreach (var renderer in target.GetComponentsInChildren<Renderer>(true))
                if (renderer.name == "Hair_work")
                {
                    hairRenderers.Add((renderer, renderer.shadowCastingMode));
                    renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            view.orthographic = false;
            view.fieldOfView = 70f;
            view.nearClipPlane = 0.05f;
        }
        else
        {
            if (head) head.localScale = headScale;
            if (hair) hair.ResetSimulation();
            foreach (var saved in walls)
                if (saved.wall) { saved.wall.RestoreAlpha(saved.alpha); saved.wall.enabled = saved.enabled; }
            foreach (var saved in components) if (saved.component) saved.component.enabled = saved.enabled;
            foreach (var saved in visuals) if (saved.renderer) saved.renderer.enabled = saved.enabled;
            foreach (var saved in hairRenderers) if (saved.renderer) saved.renderer.shadowCastingMode = saved.mode;
            walls.Clear(); components.Clear(); visuals.Clear(); hairRenderers.Clear();
            view.orthographic = orthographic;
            view.orthographicSize = size;
            view.fieldOfView = fov;
            view.nearClipPlane = near;
            transform.SetPositionAndRotation(target.position + cameraOffset, cameraRotation);
            Cursor.lockState = cursorLock;
            Cursor.visible = cursorVisible;
        }
        IsFirstPerson = value;
    }

    void RememberVisuals(GameObject root, HashSet<Renderer> seen)
    {
        if (!root) return;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            if (seen.Add(renderer)) visuals.Add((renderer, renderer.enabled));
    }

    void OnDisable() { if (IsFirstPerson) SetFirstPerson(false); }
}
