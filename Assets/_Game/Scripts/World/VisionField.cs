using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 기획 8·9·10항 시야 판정. 눈높이에서 사방으로 광선을 쏴 "어느 방향으로 얼마나 멀리 보이는가"
/// 를 표로 들고 있는다. 원형 범위가 아니라 벽에 막힌 만큼 잘린 모양이 된다.
///
/// 바라보는 방향으로 열린 부채꼴이다. 사용자 요구로 등 뒤는 전혀 보이지 않는다 —
/// 발밑(alwaysVisibleRadius) 만 예외로 항상 보인다.
/// 후방을 조금 되살리고 싶으면 rearVisibility 를 0 보다 올리면 된다.
///
/// 사거리는 부채꼴 안에서 방향과 무관하게 같다. 그래야 먼 쪽 끝이 깔끔한 호가 된다.
///
/// 그리기와 분리돼 있다 — 여기는 판정만 하고 음영은 VisionFog 가 이 값을 읽어서 그린다.
/// 조명(Light) 은 일절 관여하지 않는다. 좀비에게 붙이면 그대로 좀비 시야가 된다.
/// </summary>
public class VisionField : MonoBehaviour
{
    [Tooltip("부채꼴 안에서의 시야 거리")]
    [SerializeField] float range = 18f;
    [Tooltip("사방으로 쏘는 광선 수. 적으면 벽 그림자 경계가 다각형으로 각진다")]
    [SerializeField, Min(16)] int rayCount = 360;
    [Tooltip("이 거리 안은 방향과 무관하게 항상 보인다 (자기 발밑)")]
    [SerializeField] float alwaysVisibleRadius = 0.9f;
    [Tooltip("벽 뒤로 넘어갈 때의 경계 두께(m). 클수록 경계가 부드럽다")]
    [SerializeField] float edgeSoftness = 1.2f;
    [Tooltip("마스크를 흐리는 반경(칸). 벽 모서리 그림자의 측면 경계는 이걸로만 부드러워진다")]
    [SerializeField, Range(0, 6)] int maskBlurCells = 2;

    [Header("실내 조명 (사용자 요구)")]
    [Tooltip("불이 켜져 있으면 지금 있는 방 전체가 보인다. 꺼져 있으면 부채꼴만 보인다")]
    [SerializeField] bool revealLitRoom = true;
    [Tooltip("방 상자를 이만큼 넓혀서 벽면까지 꽉 밝힌다. 외벽 두께(0.25m) 보다 커야 벽이 어둡게 남지 않는다")]
    [SerializeField] float litRoomMargin = 0.3f;
    [Tooltip("방 안쪽으로 이만큼 걸쳐 빛이 사그라든다. 마스크 칸(0.278m) 의 4배는 돼야 계단이 안 보인다")]
    [SerializeField] float litRoomFeather = 1.1f;

    [Header("시야 부채꼴")]
    [Tooltip("정면 기준 반각(도). 70 이면 전체 140도가 보인다")]
    [SerializeField, Range(10f, 180f)] float coneHalfAngle = 70f;
    [Tooltip("부채꼴 옆 가장자리를 부드럽게 하는 폭(도). 0 이면 칼로 자른 듯 끊긴다")]
    [SerializeField, Range(0f, 45f)] float coneEdgeAngle = 14f;
    [Tooltip("부채꼴 바깥 밝기. 0 이면 등 뒤가 전혀 안 보인다 (사용자 요구)")]
    [SerializeField, Range(0f, 1f)] float rearVisibility = 0f;

    // 방향별로 두 값을 들고 있는다. 전부 Sample 에서 한 번에 채워 두고,
    // Visibility 는 칸마다 각도 한 번만 구해 표를 읽는다 (칸이 수만 개라 여기서 삼각함수를 더 쓰면 비싸다).
    float[] wallDistance;   // 벽까지의 실제 거리
    float[] coneWeight;     // 부채꼴 안이면 1, 가장자리에서 0 으로
    PlayerAim aim;
    PlayerAssassinate assassinate;
    PlayerStats stats;
    InventoryUI inventoryUi;
    float flashlightRange;

    public float Range => range;
    public float FlashlightRange => flashlightRange;
    public bool FlashlightOn { get; private set; } = true;
    /// <summary>눈 위치. 음영 텍스처의 중심이기도 하다.</summary>
    public Vector3 Origin { get; private set; }
    /// <summary>검증용. 마지막으로 쓴 정면 방향(XZ).</summary>
    public Vector3 Facing { get; private set; } = Vector3.forward;

    void Awake()
    {
        flashlightRange = range;
        aim = GetComponent<PlayerAim>();
        assassinate = GetComponent<PlayerAssassinate>();
        stats = GetComponent<PlayerStats>();
        inventoryUi = FindFirstObjectByType<InventoryUI>();
        Allocate();
        Sample();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (CanToggleFlashlight(keyboard != null && keyboard.fKey.wasPressedThisFrame,
                               stats && stats.IsDead, inventoryUi && inventoryUi.IsOpen))
            SetFlashlight(!FlashlightOn);
    }

    public void SetFlashlight(bool enabled)
    {
        FlashlightOn = enabled;
        range = enabled ? flashlightRange : alwaysVisibleRadius;
    }

    public static bool CanToggleFlashlight(bool pressed, bool dead, bool inventoryOpen) =>
        pressed && !dead && !inventoryOpen;

    void LateUpdate() => Sample();

    /// <summary>에디터 검증처럼 Update 가 돌지 않는 곳에서 한 번만 훑고 싶을 때.</summary>
    public void Refresh() => Sample();

    void Allocate()
    {
        // 배열을 전부 확인한다. 하나만 보면, 재생 중 스크립트가 다시 컴파일될 때
        // 기존 배열만 복원되고 새로 추가한 배열은 null 로 돌아와 그대로 터진다 (실제로 겪었다).
        if (wallDistance != null && coneWeight != null && wallDistance.Length == rayCount) return;

        wallDistance = new float[rayCount];
        coneWeight = new float[rayCount];
    }

    void Sample()
    {
        Allocate();
        if (!aim) aim = GetComponent<PlayerAim>();

        Origin = transform.position + Vector3.up * (aim ? Vision.PlayerEyeOffset : Vision.EyeOffset);

        // 몸은 이동을 따라가도 시야는 마우스 조준을 따라가야 한다.
        Vector3 facing = assassinate && assassinate.IsAssassinating
            ? transform.forward
            : aim ? aim.AimDirection : transform.forward;
        facing.y = 0f;
        Facing = facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector3.forward;

        float step = Mathf.PI * 2f / rayCount;
        float solid = Mathf.Max(0f, coneHalfAngle - coneEdgeAngle);
        float feather = Mathf.Max(0.01f, coneEdgeAngle);

        for (int i = 0; i < rayCount; i++)
        {
            float angle = i * step;
            var direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

            float cosTheta = Mathf.Clamp(direction.x * Facing.x + direction.z * Facing.z, -1f, 1f);
            float offAxis = Mathf.Acos(cosTheta) * Mathf.Rad2Deg;

            // 부채꼴 안은 1, 옆 가장자리에서 부드럽게 0.
            coneWeight[i] = Mathf.Max(1f - SmoothStep01((offAxis - solid) / feather), rearVisibility);

            // 사거리는 모든 방향에서 같게 둔다 — 그래야 부채꼴 끝이 깔끔한 호가 되고,
            // 가장자리에서 사거리가 뚝 떨어져 생기는 검은 홈이 안 생긴다.
            // 부채꼴 밖 광선도 그냥 쏜다 (360발에 0.2ms 라 아낄 이유가 없고, 안 쏘면
            // 이웃 광선과 보간될 때 벽이 코앞에 있는 것처럼 계산돼 가장자리가 패인다).
            wallDistance[i] = Vision.SightDistance(Origin, direction, range);
        }
    }

    /// <summary>0 = 전혀 안 보임, 1 = 정상. 경계는 부드럽게 넘어간다 (기획 10항).</summary>
    public float Visibility(Vector3 worldPoint)
    {
        if (wallDistance == null || wallDistance.Length == 0) return 1f;

        Vector3 delta = worldPoint - Origin;
        delta.y = 0f;

        float distance = delta.magnitude;
        if (distance <= alwaysVisibleRadius) return 1f;
        if (distance >= range) return 0f;   // 어느 방향도 range 를 넘지 않는다

        // 인접한 두 광선 사이를 보간한다. 안 하면 부채꼴 경계가 톱니처럼 보인다.
        float angle = Mathf.Atan2(delta.x, delta.z);
        if (angle < 0f) angle += Mathf.PI * 2f;

        float slot = angle / (Mathf.PI * 2f) * wallDistance.Length;
        int low = Mathf.FloorToInt(slot) % wallDistance.Length;

        return Evaluate(distance, low, slot - Mathf.Floor(slot));
    }

    /// <summary>
    /// 거리 + 광선 표의 한 지점에서 밝기를 낸다. Visibility(점) 와 FillMask(격자) 가
    /// 같은 식을 쓰도록 여기 한 곳에만 둔다.
    /// </summary>
    float Evaluate(float distance, int low, float blend)
    {
        int high = low + 1 == wallDistance.Length ? 0 : low + 1;

        float wall = wallDistance[low] + (wallDistance[high] - wallDistance[low]) * blend;
        float cone = coneWeight[low] + (coneWeight[high] - coneWeight[low]) * blend;

        // 벽까지는 보이고, 벽을 넘어가면 어두워진다.
        float behindWall = 1f - SmoothStep01((distance - (wall - edgeSoftness * 0.5f)) / edgeSoftness);

        // 시야 끝도 급하게 끊지 않는다.
        float fadeStart = range * 0.6f;
        float falloff = 1f - SmoothStep01((distance - fadeStart) / Mathf.Max(0.01f, range - fadeStart));

        return (behindWall < falloff ? behindWall : falloff) * cone;
    }

    static float SmoothStep01(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return t * t * (3f - 2f * t);
    }

    /// <summary>좀비 AI 등에서 쓰는 이진 판정.</summary>
    public bool IsVisible(Vector3 worldPoint) => Visibility(worldPoint) > 0.5f;

    // ---- 음영 마스크 채우기 ----
    //
    // 마스크 격자는 항상 플레이어를 중심에 두므로, 각 칸의 "중심에서의 거리" 와 "각도" 는
    // 매 프레임 똑같다. 칸마다 sqrt + atan2 를 다시 하면 144x144 에서 3.5ms 가 나갔다
    // (광선 360발은 0.18ms 였다 — 비싼 쪽은 광선이 아니라 칸 순회였다).
    // 그래서 거리/각도는 한 번만 구해 표로 들고, 매 프레임은 표를 읽어 보간만 한다.
    int tableResolution = -1;
    int tableRays = -1;
    float tableArea = -1f;
    float[] cellDistance;
    int[] cellSlot;
    float[] cellBlend;
    byte[] rawMask;
    byte[] blurScratch;

    void BuildCellTables(int resolution, float areaSize)
    {
        // 광선 수가 바뀌면 칸이 가리키는 광선 번호도 다시 구해야 한다.
        if (cellDistance != null && tableResolution == resolution
            && tableRays == wallDistance.Length && Mathf.Approximately(tableArea, areaSize))
            return;

        tableResolution = resolution;
        tableRays = wallDistance.Length;
        tableArea = areaSize;

        int count = resolution * resolution;
        cellDistance = new float[count];
        cellSlot = new int[count];
        cellBlend = new float[count];

        float cell = areaSize / resolution;
        float half = areaSize * 0.5f;
        int rays = wallDistance.Length;

        for (int y = 0; y < resolution; y++)
        {
            float offsetZ = (y + 0.5f) * cell - half;
            int row = y * resolution;

            for (int x = 0; x < resolution; x++)
            {
                float offsetX = (x + 0.5f) * cell - half;
                int i = row + x;

                cellDistance[i] = Mathf.Sqrt(offsetX * offsetX + offsetZ * offsetZ);

                float angle = Mathf.Atan2(offsetX, offsetZ);
                if (angle < 0f) angle += Mathf.PI * 2f;

                float slot = angle / (Mathf.PI * 2f) * rays;
                int low = Mathf.FloorToInt(slot) % rays;
                cellSlot[i] = low;
                cellBlend[i] = slot - Mathf.Floor(slot);
            }
        }
    }

    /// <summary>
    /// VisionFog 가 넘긴 버퍼를 이번 프레임 시야로 채운다. 판정은 여기가 하고 그리기는 저쪽이 한다.
    /// 격자는 Origin 을 중심으로 areaSize x areaSize 를 덮는다고 약속한다.
    /// </summary>
    public void FillMask(Color32[] pixels, int resolution, float areaSize)
    {
        if (pixels == null || wallDistance == null || wallDistance.Length == 0) return;

        BuildCellTables(resolution, areaSize);

        int count = resolution * resolution;
        if (rawMask == null || rawMask.Length != count)
        {
            rawMask = new byte[count];
            blurScratch = new byte[count];
        }

        for (int i = 0; i < count; i++)
        {
            float distance = cellDistance[i];

            if (distance <= alwaysVisibleRadius) rawMask[i] = 255;
            else if (distance >= range) rawMask[i] = 0;
            else rawMask[i] = (byte)(Mathf.Clamp01(Evaluate(distance, cellSlot[i], cellBlend[i])) * 255f);
        }

        Blur(resolution);
        RevealLitRoom(resolution, areaSize);

        for (int i = 0; i < count; i++)
        {
            byte v = rawMask[i];
            pixels[i] = new Color32(v, v, v, 255);
        }
    }

    /// <summary>
    /// 불이 켜져 있으면 지금 있는 방 전체를 드러낸다.
    ///
    /// 부채꼴만 보이면 실내에서 방 배치를 전혀 읽을 수 없다 (사용자 요구: 불이 켜져 있으면
    /// 예전처럼 실내가 잘 보이게, 꺼져 있으면 부채꼴만). 실외에서는 아무 것도 하지 않는다.
    ///
    /// 흐리기(Blur) 뒤에 덮어쓴다 — 빛은 벽에서 딱 끊겨야 하고, 흐리면 방 경계가 번진다.
    /// 벽면까지 밝아야 방 모양이 읽히므로 방 상자를 벽 두께만큼 넓혀서 채운다.
    /// </summary>
    void RevealLitRoom(int resolution, float areaSize)
    {
        if (!revealLitRoom || !HouseLights.On) return;

        var room = Room.At(Origin);
        if (!room) return;

        // 벽면까지 꽉 밝히고(litRoomMargin), 그 바깥으로 litRoomFeather 만큼 걸쳐 사그라든다.
        //
        // 하드 컷으로 채우면 한 칸(0.278m) 에서 255 → 68 로 떨어진다 (실측). 월드 기준
        // 축정렬 사각형이 아이소메트릭 화면에서는 대각선이 되므로 그 절벽이 격자에 찍혀
        // 계단으로 보인다 — 예전 각도 방향 절벽과 같은 문제다 (사용자 지적: "드르륵").
        // 램프를 방 '안쪽' 으로 걸친다. 바깥으로 걸치면 벽을 넘어 옆방 바닥까지 밝아진다.
        // 안쪽으로 걸치면 벽 가까이가 살짝 어두워지는데, 천장등 빛이 벽에서 떨어지는 것과
        // 같은 모양이라 자연스럽고 조명이 벽으로 새는 것도 같이 가려준다.
        var bounds = room.InnerBounds;
        bounds.Expand(new Vector3(litRoomMargin * 2f, 0f, litRoomMargin * 2f));

        float cell = areaSize / resolution;
        float half = areaSize * 0.5f;

        for (int y = 0; y < resolution; y++)
        {
            float worldZ = Origin.z + (y + 0.5f) * cell - half;
            float insideZ = Mathf.Min(worldZ - bounds.min.z, bounds.max.z - worldZ);
            if (insideZ <= 0f) continue;

            int row = y * resolution;
            for (int x = 0; x < resolution; x++)
            {
                float worldX = Origin.x + (x + 0.5f) * cell - half;
                float insideX = Mathf.Min(worldX - bounds.min.x, bounds.max.x - worldX);
                if (insideX <= 0f) continue;

                float edge = Mathf.Min(insideX, insideZ);
                // 선형 램프를 쓴다. smoothstep 은 가운데가 1.5배 급해서 같은 폭이라도
                // 한 칸 점프가 더 크다 (실측: smoothstep 109 vs 선형 예상 64).
                float t = litRoomFeather > 0f ? Mathf.Clamp01(edge / litRoomFeather) : 1f;
                byte lit = (byte)(t * 255f);

                if (lit > rawMask[row + x]) rawMask[row + x] = lit;
            }
        }
    }

    /// <summary>
    /// 다 채운 뒤 한 번 흐린다.
    ///
    /// edgeSoftness 는 반지름 방향(벽을 향해 가까워지는 방향) 만 부드럽게 한다. 벽 모서리가
    /// 만드는 그림자의 '측면' 경계는 인접한 두 광선 사이에서 통째로 뒤집히기 때문에 전혀
    /// 완화되지 않는다 — 실측하면 반지름 방향은 1.2m 에 걸쳐 255→0 인데 각도 방향은
    /// 0.05m 에서 237→0 이었다. 그 절벽이 마스크 격자에 찍혀 계단으로 보인다.
    /// 광선을 늘리거나 마스크를 키우면 계단이 잘아질 뿐 절벽은 그대로다. 그래서 흐린다.
    /// </summary>
    void Blur(int resolution)
    {
        int r = maskBlurCells;
        if (r <= 0) return;

        int taps = r * 2 + 1;

        for (int y = 0; y < resolution; y++)
        {
            int row = y * resolution;
            for (int x = 0; x < resolution; x++)
            {
                int sum = 0;
                for (int k = -r; k <= r; k++)
                {
                    int sx = x + k;
                    if (sx < 0) sx = 0;
                    else if (sx >= resolution) sx = resolution - 1;
                    sum += rawMask[row + sx];
                }
                blurScratch[row + x] = (byte)(sum / taps);
            }
        }

        for (int y = 0; y < resolution; y++)
        {
            int row = y * resolution;
            for (int x = 0; x < resolution; x++)
            {
                int sum = 0;
                for (int k = -r; k <= r; k++)
                {
                    int sy = y + k;
                    if (sy < 0) sy = 0;
                    else if (sy >= resolution) sy = resolution - 1;
                    sum += blurScratch[sy * resolution + x];
                }
                rawMask[row + x] = (byte)(sum / taps);
            }
        }
    }
}
