using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>도시 다각형 건물의 현재 층만 찾고 위층을 숨긴다.</summary>
public sealed class BuildingFloors : MonoBehaviour
{
    const float CellSize = 32f;

    [Serializable]
    public sealed class Entry
    {
        public GameObject root;
        public Vector2[] polygon;
        public GameObject[] floors;
        public GameObject roof;
        public bool hasStairs;
        public Vector3 doorFront;
        public Vector3 topLanding;
    }

    [SerializeField] Transform target;
    [SerializeField] float floorHeight = 3.6f;
    [SerializeField] Entry[] buildings;
    public int BuiltDoorNavMeshSamples;
    public int BuiltCheckedDoors;
    public int BuiltStairPaths;

    readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();
    int currentBuilding = -1;
    int currentFloor = -1;
    WallSegment[] currentWalls;
    Camera viewCamera;

    public IReadOnlyList<Entry> Buildings => buildings;

    public void Setup(Entry[] entries, float height)
    {
        buildings = entries;
        floorHeight = height;
        BuildGrid();
    }

    public void SetTarget(Transform value) => target = value;

    void Awake() => BuildGrid();

    void OnDisable() => ShowAll(currentBuilding);

    void Update()
    {
        if (!target) return;

        int building;
        int floor;
        if (!TryLocate(target.position, out building, out floor))
        {
            if (currentBuilding >= 0) ShowAll(currentBuilding);
            currentBuilding = -1;
            currentFloor = -1;
            currentWalls = null;
            return;
        }

        if (building != currentBuilding || floor != currentFloor)
        {
            if (currentBuilding >= 0 && currentBuilding != building) ShowAll(currentBuilding);
            currentBuilding = building;
            currentFloor = floor;
            ApplyVisibility();
        }

        FadeCameraSideWalls();
    }

    public bool TryLocate(Vector3 point, out int building, out int floor)
    {
        building = -1;
        floor = -1;
        if (buildings == null) return false;
        if (grid.Count == 0) BuildGrid();

        var key = Cell(point);
        if (!grid.TryGetValue(key, out var candidates)) return false;
        for (int i = 0; i < candidates.Count; i++)
        {
            int index = candidates[i];
            if (!ContainsFloor(point, buildings[index].polygon, buildings[index].floors.Length,
                    floorHeight, out int foundFloor)) continue;
            building = index;
            floor = foundFloor;
            return true;
        }
        return false;
    }

    public static bool ContainsFloor(Vector3 point, Vector2[] polygon, int floors,
        float height, out int floor)
    {
        floor = Mathf.FloorToInt(point.y / height);
        if (polygon == null || polygon.Length < 3 || floor < 0 || floor >= floors) return false;

        var flat = new Vector2(point.x, point.z);
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            if ((a.y > flat.y) != (b.y > flat.y)
                && flat.x < (b.x - a.x) * (flat.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    void BuildGrid()
    {
        grid.Clear();
        if (buildings == null) return;

        for (int i = 0; i < buildings.Length; i++)
        {
            var polygon = buildings[i].polygon;
            if (polygon == null || polygon.Length == 0) continue;
            Vector2 min = polygon[0];
            Vector2 max = polygon[0];
            for (int p = 1; p < polygon.Length; p++)
            {
                min = Vector2.Min(min, polygon[p]);
                max = Vector2.Max(max, polygon[p]);
            }

            Vector2Int from = Cell(min);
            Vector2Int to = Cell(max);
            for (int x = from.x; x <= to.x; x++)
            for (int y = from.y; y <= to.y; y++)
            {
                var key = new Vector2Int(x, y);
                if (!grid.TryGetValue(key, out var values))
                {
                    values = new List<int>();
                    grid.Add(key, values);
                }
                values.Add(i);
            }
        }
    }

    void ApplyVisibility()
    {
        var building = buildings[currentBuilding];
        for (int i = 0; i < building.floors.Length; i++)
            SetRenderers(building.floors[i], i <= currentFloor);
        SetRenderers(building.roof, false);
        currentWalls = building.floors[currentFloor].GetComponentsInChildren<WallSegment>(true);
    }

    void ShowAll(int index)
    {
        if (buildings == null || index < 0 || index >= buildings.Length) return;
        var building = buildings[index];
        for (int i = 0; i < building.floors.Length; i++)
            SetRenderers(building.floors[i], true);
        SetRenderers(building.roof, true);
    }

    static void SetRenderers(GameObject root, bool visible)
    {
        if (!root) return;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = visible;
    }

    void FadeCameraSideWalls()
    {
        if (currentWalls == null) return;
        if (!viewCamera) viewCamera = Camera.main;
        if (!viewCamera) return;

        Vector2 center = Vector2.zero;
        Vector2[] polygon = buildings[currentBuilding].polygon;
        for (int i = 0; i < polygon.Length; i++) center += polygon[i];
        center /= polygon.Length;
        Vector2 camera = new Vector2(viewCamera.transform.position.x, viewCamera.transform.position.z);
        Vector2 cameraSide = camera - center;

        for (int i = 0; i < currentWalls.Length; i++)
        {
            var wall = currentWalls[i];
            if (!wall || !wall.IsExterior) continue;
            Vector2 offset = new Vector2(wall.Center.x, wall.Center.z) - center;
            if (Vector2.Dot(offset, cameraSide) > 0f) wall.RequestFade();
        }
    }

    static Vector2Int Cell(Vector3 point) => Cell(new Vector2(point.x, point.z));
    static Vector2Int Cell(Vector2 point) => new Vector2Int(
        Mathf.FloorToInt(point.x / CellSize), Mathf.FloorToInt(point.y / CellSize));
}
