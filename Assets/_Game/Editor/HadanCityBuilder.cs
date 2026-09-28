using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

public static class HadanCityBuilder
{
    const string SceneFolder = "Assets/_Game/Scenes";
    const string ScenePath = SceneFolder + "/V01_City_Hadan.unity";
    const string MapPath = "docs/map/hadan_500m.json";
    const string DataFolder = "Assets/_Game/Data/Hadan";
    const string MeshAssetPath = DataFolder + "/HadanCityMeshes.asset";

    const double CenterLat = 35.10625;
    const double CenterLon = 128.96675;
    const float CityScale = 1.6f;
    const int MaxFloors = 6;
    const float FloorHeight = 3.6f;
    const float RoadY = 0.02f;
    const float Epsilon = 0.0001f;

    static bool meshAssetCreated;

    [MenuItem("Tools/V0.1/Build Hadan Pilot")]
    public static void BuildHadanPilot()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        MapData map;
        try
        {
            map = LoadMap();
        }
        catch (Exception exception)
        {
            Debug.LogError("[Hadan] 지도 데이터를 읽지 못했습니다: " + exception.Message);
            return;
        }

        Directory.CreateDirectory(SceneFolder);
        Directory.CreateDirectory(DataFolder);

        V01SceneBuilder.ApplyRenderSettings();
        V01SceneBuilder.CreateLight();

        var buildingMaterials = new[]
        {
            V01SceneBuilder.EnsureSharedMaterial("hadan_building_1", new Color(0.48f, 0.50f, 0.53f)),
            V01SceneBuilder.EnsureSharedMaterial("hadan_building_2", new Color(0.61f, 0.62f, 0.64f)),
            V01SceneBuilder.EnsureSharedMaterial("hadan_building_3", new Color(0.38f, 0.40f, 0.43f))
        };
        var roadMaterial = V01SceneBuilder.EnsureSharedMaterial("hadan_road", new Color(0.16f, 0.17f, 0.18f));
        var groundMaterial = V01SceneBuilder.EnsureSharedMaterial("hadan_ground", new Color(0.30f, 0.34f, 0.29f));

        CreateGround(map, groundMaterial);
        var meshes = new List<Mesh>();
        var buildings = CreateBuildings(map, buildingMaterials, meshes, out int buildingCount, out float maxHeight);
        int roadCount = CreateRoads(map, roadMaterial, meshes);

        Vector2 spawn = FindSpawn(map, buildings);
        var clock = V01SceneBuilder.CreateGameClock();
        var player = V01SceneBuilder.CreatePlayer();
        player.transform.position = new Vector3(spawn.x, player.transform.position.y, spawn.y);
        UnityEngine.Object.DestroyImmediate(player.GetComponent<DebugStartState>());
        var arrival = new GameObject("Arrive_FromEstate");
        arrival.transform.position = player.transform.position;
        V01SceneBuilder.CreateCamera(player.transform);
        V01SceneBuilder.CreateHud(player, clock);

        var watch = Stopwatch.StartNew();
        V01SceneBuilder.BuildNavMesh();
        watch.Stop();

        AssetDatabase.DeleteAsset(MeshAssetPath);
        meshAssetCreated = false;
        for (int i = 0; i < meshes.Count; i++) StoreMesh(meshes[i]);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        V01SceneBuilder.EnsureBuildScenes();
        int triangles = CountUniqueMeshTriangles();
        Debug.Log("[Hadan] 건물 " + buildingCount + ", 도로 " + roadCount
                  + ", 삼각형 " + triangles.ToString("N0")
                  + ", 최고 " + maxHeight.ToString("F1") + "m, NavMesh " + watch.ElapsedMilliseconds + "ms");
    }

    [MenuItem("Tools/V0.1/Check Hadan Pilot")]
    public static void CheckHadanPilot()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
        {
            Debug.LogError("[Hadan Check] V01_City_Hadan 씬을 연 뒤 실행하세요.");
            return;
        }

        MapData map;
        try
        {
            map = LoadMap();
        }
        catch (Exception exception)
        {
            Debug.LogError("[Hadan Check] 지도 데이터를 읽지 못했습니다: " + exception.Message);
            return;
        }

        int failures = 0;
        int expectedBuildings = CountBuildings(map);
        var root = GameObject.Find("Buildings");
        int actualBuildings = root ? root.transform.childCount : 0;
        int tolerance = Mathf.CeilToInt(expectedBuildings * 0.05f);
        Check(Mathf.Abs(actualBuildings - expectedBuildings) <= tolerance,
            "건물 수 " + actualBuildings + "/" + expectedBuildings + " (±5%)", ref failures);

        int missingColliders = root ? 0 : expectedBuildings;
        int badFaces = root ? 0 : 1;
        float maxHeight = root ? 0f : float.PositiveInfinity;
        var polygons = BuildingPolygons(map);
        if (root)
        {
            for (int i = 0; i < root.transform.childCount; i++)
            {
                var child = root.transform.GetChild(i).gameObject;
                var collider = child.GetComponent<MeshCollider>();
                var filter = child.GetComponent<MeshFilter>();
                if (!collider || !collider.sharedMesh || !filter || !filter.sharedMesh) missingColliders++;

                if (filter && filter.sharedMesh)
                {
                    maxHeight = Mathf.Max(maxHeight,
                        filter.sharedMesh.bounds.size.y * Mathf.Abs(child.transform.lossyScale.y));
                    if (TryBuildingId(child.name, out long id) && polygons.TryGetValue(id, out var polygon))
                        badFaces += CountBadFaces(filter.sharedMesh, polygon);
                    else
                        badFaces++;
                }
            }
        }

        Check(missingColliders == 0, "모든 건물에 MeshCollider가 있다", ref failures);
        Check(badFaces == 0, "건물 옆면 법선은 바깥, 지붕 법선은 위를 향한다", ref failures);

        var player = GameObject.Find("Player");
        bool spawnClear = false;
        if (player)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc)
            {
                spawnClear = true;
                float radius = cc.radius * Mathf.Max(player.transform.lossyScale.x, player.transform.lossyScale.z);
                var point = new Vector2(player.transform.position.x, player.transform.position.z);
                foreach (var polygon in polygons.Values)
                    if (DistanceToPolygon(point, polygon) < radius - Epsilon) { spawnClear = false; break; }
            }
        }
        Check(spawnClear, "플레이어 시작 위치가 건물 콜라이더와 겹치지 않는다", ref failures);

        var arrival = GameObject.Find("Arrive_FromEstate");
        bool arrivalClear = arrival;
        if (arrival)
        {
            var point = new Vector2(arrival.transform.position.x, arrival.transform.position.z);
            foreach (var polygon in polygons.Values)
                if (DistanceToPolygon(point, polygon) < 0.4f - Epsilon) { arrivalClear = false; break; }
        }
        Check(arrivalClear, "Arrive_FromEstate 가 있고 건물 콜라이더와 겹치지 않는다", ref failures);

        var exits = UnityEngine.Object.FindObjectsByType<SceneExit>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool returnsToEstate = false;
        for (int i = 0; i < exits.Length; i++)
            if (exits[i].TargetSceneName == "V01_Sandbox") { returnsToEstate = true; break; }
        Check(!returnsToEstate, "도시에 별장으로 가는 SceneExit 가 없다", ref failures);
        Check(!UnityEngine.Object.FindFirstObjectByType<SaveSystem>(),
              "도시는 SaveSystem 이 없어 별장 세이브를 덮어쓰지 않는다", ref failures);

        var navTriangles = NavMesh.CalculateTriangulation().indices.Length / 3;
        Check(navTriangles > 0, "NavMesh가 구워져 있다 (삼각형 " + navTriangles + ")", ref failures);
        int pathTargets = player ? CheckRoadPaths(map, player.transform.position) : 0;
        Check(pathTargets >= 3, "15~50m 도로점 경로 " + pathTargets + "개가 이어진다", ref failures);

        int triangles = CountUniqueMeshTriangles();
        Debug.Log("[Hadan Check] 씬 전체 고유 메쉬 삼각형 " + triangles.ToString("N0"));
        Check(maxHeight <= MaxFloors * FloorHeight + Epsilon,
            "최고 건물 " + maxHeight.ToString("F1") + "m <= " + (MaxFloors * FloorHeight).ToString("F1") + "m",
            ref failures);

        if (failures == 0) Debug.Log("[Hadan Check] 통과");
        else Debug.LogError("[Hadan Check] 실패 " + failures + "건");
    }

    static MapData LoadMap()
    {
        if (!File.Exists(MapPath)) throw new FileNotFoundException(MapPath);

        // JsonUtility가 콜론이 든 필드명을 읽지 못하므로 필요한 키 하나만 C# 필드명으로 바꾼다.
        string json = File.ReadAllText(MapPath).Replace("\"building:levels\"", "\"building_levels\"");
        var map = JsonUtility.FromJson<MapData>(json);
        if (map == null || map.elements == null) throw new InvalidDataException("elements[]가 없습니다.");
        return map;
    }

    static void CreateGround(MapData map, Material material)
    {
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < map.elements.Length; i++)
        {
            var element = map.elements[i];
            if (element.tags == null || (string.IsNullOrEmpty(element.tags.building)
                && string.IsNullOrEmpty(element.tags.highway)) || element.geometry == null) continue;
            for (int j = 0; j < element.geometry.Length; j++)
            {
                Vector2 point = GeoToWorld(element.geometry[j].lat, element.geometry[j].lon);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
        min -= Vector2.one * 10f;
        max += Vector2.one * 10f;

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3((min.x + max.x) * 0.5f, 0f, (min.y + max.y) * 0.5f);
        ground.transform.localScale = new Vector3((max.x - min.x) / 10f, 1f, (max.y - min.y) / 10f);
        ground.GetComponent<Renderer>().sharedMaterial = material;
        V01SceneBuilder.MakeGroundSolid(ground);
        V01SceneBuilder.MarkStatic(ground.transform);
    }

    static List<List<Vector2>> CreateBuildings(MapData map, Material[] materials, List<Mesh> meshes,
        out int count, out float maxHeight)
    {
        var root = new GameObject("Buildings").transform;
        var polygons = new List<List<Vector2>>();
        count = 0;
        maxHeight = 0f;

        for (int i = 0; i < map.elements.Length; i++)
        {
            var element = map.elements[i];
            if (element.tags == null || string.IsNullOrEmpty(element.tags.building)) continue;

            var polygon = MakePolygon(element.geometry);
            if (polygon == null)
            {
                Debug.LogWarning("[Hadan] 건물 " + element.id + "의 외곽선이 유효하지 않아 건너뜁니다.");
                continue;
            }

            int floors = BuildingFloors(element.tags, Mathf.Abs(SignedArea(polygon)) / (CityScale * CityScale));
            float height = floors * FloorHeight;
            var mesh = CreateBuildingMesh(element.id, polygon, height);
            if (!mesh)
            {
                Debug.LogWarning("[Hadan] 건물 " + element.id + "의 지붕 삼각분할에 실패해 건너뜁니다.");
                continue;
            }

            meshes.Add(mesh);
            var go = new GameObject("Building_" + element.id, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(root, false);
            go.layer = 0; // 시야 차단은 Default 레이어를 포함하므로 별도 레이어를 만들지 않는다.
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = materials[count % materials.Length];
            var collider = go.GetComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

            polygons.Add(polygon);
            count++;
            maxHeight = Mathf.Max(maxHeight, height);
        }

        return polygons;
    }

    static int CreateRoads(MapData map, Material material, List<Mesh> meshes)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int roadCount = 0;

        for (int i = 0; i < map.elements.Length; i++)
        {
            var element = map.elements[i];
            if (element.tags == null || string.IsNullOrEmpty(element.tags.highway)
                || element.geometry == null || element.geometry.Length < 2) continue;

            bool emitted = false;
            float halfWidth = RoadWidth(element.tags.highway) * CityScale * 0.5f;
            for (int j = 0; j < element.geometry.Length - 1; j++)
            {
                Vector2 from = GeoToWorld(element.geometry[j].lat, element.geometry[j].lon);
                Vector2 to = GeoToWorld(element.geometry[j + 1].lat, element.geometry[j + 1].lon);
                Vector2 direction = to - from;
                if (direction.sqrMagnitude < Epsilon) continue;

                Vector2 side = new Vector2(-direction.y, direction.x).normalized * halfWidth;
                int first = vertices.Count;
                vertices.Add(new Vector3(from.x + side.x, RoadY, from.y + side.y));
                vertices.Add(new Vector3(from.x - side.x, RoadY, from.y - side.y));
                vertices.Add(new Vector3(to.x - side.x, RoadY, to.y - side.y));
                vertices.Add(new Vector3(to.x + side.x, RoadY, to.y + side.y));
                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 1);
                triangles.Add(first);
                triangles.Add(first + 3);
                triangles.Add(first + 2);
                emitted = true;
            }
            if (emitted) roadCount++;
        }

        var mesh = new Mesh { name = "HadanRoads", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshes.Add(mesh);

        var roads = new GameObject("Roads", typeof(MeshFilter), typeof(MeshRenderer));
        roads.GetComponent<MeshFilter>().sharedMesh = mesh;
        roads.GetComponent<MeshRenderer>().sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(roads, StaticEditorFlags.BatchingStatic);
        return roadCount;
    }

    static Mesh CreateBuildingMesh(long id, List<Vector2> polygon, float height)
    {
        var roofTriangles = new List<int>();
        if (!Triangulate(polygon, roofTriangles)) return null;

        var vertices = new List<Vector3>(polygon.Count * 5);
        var triangles = new List<int>(roofTriangles.Count + polygon.Count * 6);
        for (int i = 0; i < polygon.Count; i++)
            vertices.Add(new Vector3(polygon[i].x, height, polygon[i].y));
        triangles.AddRange(roofTriangles);

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            int first = vertices.Count;
            vertices.Add(new Vector3(a.x, 0f, a.y));
            vertices.Add(new Vector3(b.x, 0f, b.y));
            vertices.Add(new Vector3(b.x, height, b.y));
            vertices.Add(new Vector3(a.x, height, a.y));
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 3);
        }

        var mesh = new Mesh { name = "HadanBuilding_" + id };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static List<Vector2> MakePolygon(GeoPoint[] geometry)
    {
        if (geometry == null || geometry.Length < 4) return null;

        var polygon = new List<Vector2>(geometry.Length - 1);
        for (int i = 0; i < geometry.Length; i++)
        {
            Vector2 point = GeoToWorld(geometry[i].lat, geometry[i].lon);
            if (polygon.Count == 0 || (polygon[polygon.Count - 1] - point).sqrMagnitude > Epsilon * Epsilon)
                polygon.Add(point);
        }
        if (polygon.Count > 1 && (polygon[0] - polygon[polygon.Count - 1]).sqrMagnitude <= Epsilon * Epsilon)
            polygon.RemoveAt(polygon.Count - 1);

        bool removed;
        do
        {
            removed = false;
            for (int i = polygon.Count - 1; i >= 0 && polygon.Count > 3; i--)
            {
                Vector2 a = polygon[(i - 1 + polygon.Count) % polygon.Count];
                Vector2 b = polygon[i];
                Vector2 c = polygon[(i + 1) % polygon.Count];
                if (Mathf.Abs(Cross(a, b, c)) > Epsilon) continue;
                polygon.RemoveAt(i);
                removed = true;
            }
        } while (removed);

        if (polygon.Count < 3 || Mathf.Abs(SignedArea(polygon)) <= Epsilon) return null;
        if (SignedArea(polygon) > 0f) polygon.Reverse(); // 위쪽 지붕 법선을 위해 x-z 감김을 시계 방향으로 맞춘다.
        return polygon;
    }

    static bool Triangulate(List<Vector2> polygon, List<int> triangles)
    {
        var remaining = new List<int>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++) remaining.Add(i);

        int guard = polygon.Count * polygon.Count;
        while (remaining.Count > 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int previous = remaining[(i - 1 + remaining.Count) % remaining.Count];
                int current = remaining[i];
                int next = remaining[(i + 1) % remaining.Count];
                if (Cross(polygon[previous], polygon[current], polygon[next]) >= -Epsilon) continue;

                bool containsPoint = false;
                for (int j = 0; j < remaining.Count; j++)
                {
                    int candidate = remaining[j];
                    if (candidate == previous || candidate == current || candidate == next) continue;
                    if (PointInTriangle(polygon[candidate], polygon[previous], polygon[current], polygon[next]))
                    {
                        containsPoint = true;
                        break;
                    }
                }
                if (containsPoint) continue;

                triangles.Add(previous);
                triangles.Add(current);
                triangles.Add(next);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) return false;
        }

        if (remaining.Count != 3) return false;
        triangles.Add(remaining[0]);
        triangles.Add(remaining[1]);
        triangles.Add(remaining[2]);
        return true;
    }

    static Vector2 FindSpawn(MapData map, List<List<Vector2>> buildings)
    {
        Vector2 best = default;
        float bestDistance = float.PositiveInfinity;
        bool found = false;

        for (int i = 0; i < map.elements.Length; i++)
        {
            var element = map.elements[i];
            if (element.tags == null || element.tags.highway != "primary"
                || element.geometry == null || element.geometry.Length < 2) continue;

            for (int j = 0; j < element.geometry.Length - 1; j++)
            {
                Vector2 from = GeoToWorld(element.geometry[j].lat, element.geometry[j].lon);
                Vector2 to = GeoToWorld(element.geometry[j + 1].lat, element.geometry[j + 1].lon);
                Vector2 segment = to - from;
                if (segment.sqrMagnitude < Epsilon) continue;

                float t = Mathf.Clamp01(Vector2.Dot(-from, segment) / segment.sqrMagnitude);
                Vector2 candidate = from + segment * t;
                TrySpawn(candidate, buildings, ref best, ref bestDistance, ref found);

                if (!IsSpawnClear(candidate, buildings))
                {
                    int steps = Mathf.CeilToInt(segment.magnitude);
                    for (int step = 0; step <= steps; step++)
                        TrySpawn(Vector2.Lerp(from, to, step / (float)steps), buildings,
                            ref best, ref bestDistance, ref found);
                }
            }
        }

        if (!found) throw new InvalidOperationException("건물과 겹치지 않는 primary 도로 시작점을 찾지 못했습니다.");
        return best;
    }

    static void TrySpawn(Vector2 candidate, List<List<Vector2>> buildings,
        ref Vector2 best, ref float bestDistance, ref bool found)
    {
        float distance = candidate.sqrMagnitude;
        if (distance >= bestDistance || !IsSpawnClear(candidate, buildings)) return;
        best = candidate;
        bestDistance = distance;
        found = true;
    }

    static bool IsSpawnClear(Vector2 point, List<List<Vector2>> buildings)
    {
        const float PlayerClearance = 0.4f;
        for (int i = 0; i < buildings.Count; i++)
            if (DistanceToPolygon(point, buildings[i]) < PlayerClearance) return false;
        return true;
    }

    static int BuildingFloors(MapTags tags, float area)
    {
        int floors;
        if (!string.IsNullOrEmpty(tags.building_levels)
            && int.TryParse(tags.building_levels, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tagged))
            floors = tagged;
        else if (tags.building == "apartments")
            floors = 15;
        else
            floors = area < 100f ? 3 : area < 250f ? 5 : 6;
        return Mathf.Clamp(floors, 1, MaxFloors);
    }

    static float RoadWidth(string highway)
    {
        switch (highway)
        {
            case "primary": return 12f;
            case "primary_link":
            case "secondary": return 9f;
            case "tertiary": return 7f;
            case "residential":
            case "service": return 5f;
            case "footway":
            case "pedestrian": return 2.5f;
            default: return 4f;
        }
    }

    static Vector2 GeoToWorld(double lat, double lon)
    {
        double x = (lon - CenterLon) * 111320.0 * Math.Cos(CenterLat * Math.PI / 180.0) * CityScale;
        double z = (lat - CenterLat) * 110540.0 * CityScale;
        return new Vector2((float)x, (float)z);
    }

    static void StoreMesh(Mesh mesh)
    {
        if (!meshAssetCreated)
        {
            AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            meshAssetCreated = true;
        }
        else
        {
            AssetDatabase.AddObjectToAsset(mesh, MeshAssetPath);
        }
    }

    static Dictionary<long, List<Vector2>> BuildingPolygons(MapData map)
    {
        var result = new Dictionary<long, List<Vector2>>();
        for (int i = 0; i < map.elements.Length; i++)
        {
            var element = map.elements[i];
            if (element.tags == null || string.IsNullOrEmpty(element.tags.building)) continue;
            var polygon = MakePolygon(element.geometry);
            if (polygon != null) result[element.id] = polygon;
        }
        return result;
    }

    static int CountBadFaces(Mesh mesh, List<Vector2> polygon)
    {
        int bad = 0;
        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        if (triangles.Length == 0) return 1;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude <= Epsilon * Epsilon) { bad++; continue; }
            normal.Normalize();

            if (Mathf.Abs(a.y - b.y) <= Epsilon && Mathf.Abs(b.y - c.y) <= Epsilon)
            {
                if (normal.y < 0.999f) bad++;
                continue;
            }

            Vector2 edgeA = new Vector2(a.x, a.z);
            Vector2 edgeB = new Vector2(b.x, b.z);
            bool matched = false;
            for (int j = 0; j < polygon.Count; j++)
            {
                Vector2 p = polygon[j];
                Vector2 q = polygon[(j + 1) % polygon.Count];
                if (!SameEdge(edgeA, edgeB, p, q)) continue;
                Vector2 edge = q - p;
                Vector2 outward = new Vector2(-edge.y, edge.x).normalized;
                if (Vector2.Dot(new Vector2(normal.x, normal.z), outward) < 0.999f) bad++;
                matched = true;
                break;
            }
            if (!matched) bad++;
        }
        return bad;
    }

    static int CheckRoadPaths(MapData map, Vector3 playerPosition)
    {
        if (!NavMesh.SamplePosition(playerPosition, out NavMeshHit start, 3f, NavMesh.AllAreas)) return 0;

        var startPoint = new Vector3(start.position.x, 0f, start.position.z);
        var points = new List<Vector3>();
        for (int i = 0; i < map.elements.Length; i++)
        {
            var element = map.elements[i];
            if (element.tags == null || string.IsNullOrEmpty(element.tags.highway) || element.geometry == null) continue;
            for (int j = 0; j < element.geometry.Length; j++)
            {
                Vector2 point = GeoToWorld(element.geometry[j].lat, element.geometry[j].lon);
                var world = new Vector3(point.x, 0f, point.y);
                float distance = (world - startPoint).sqrMagnitude;
                if (distance >= 15f * 15f && distance <= 50f * 50f)
                    points.Add(world);
            }
        }
        points.Sort((a, b) => (a - playerPosition).sqrMagnitude.CompareTo((b - playerPosition).sqrMagnitude));

        int reachable = 0;
        var selected = new List<Vector3>();
        for (int i = 0; i < points.Count && selected.Count < 5; i++)
        {
            bool tooClose = false;
            for (int j = 0; j < selected.Count; j++)
                if ((selected[j] - points[i]).sqrMagnitude < 4f) { tooClose = true; break; }
            if (tooClose || !NavMesh.SamplePosition(points[i], out NavMeshHit target, 2f, NavMesh.AllAreas)) continue;

            var path = new NavMeshPath();
            if (NavMesh.CalculatePath(start.position, target.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete)
            {
                reachable++;
                selected.Add(points[i]);
            }
        }
        return reachable;
    }

    static int CountUniqueMeshTriangles()
    {
        var meshes = new HashSet<Mesh>();
        var filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int triangles = 0;
        for (int i = 0; i < filters.Length; i++)
            if (filters[i].sharedMesh && meshes.Add(filters[i].sharedMesh))
                triangles += filters[i].sharedMesh.triangles.Length / 3;
        return triangles;
    }

    static int CountBuildings(MapData map)
    {
        int count = 0;
        for (int i = 0; i < map.elements.Length; i++)
        {
            var tags = map.elements[i].tags;
            if (tags != null && !string.IsNullOrEmpty(tags.building))
                count++;
        }
        return count;
    }

    static float SignedArea(List<Vector2> polygon)
    {
        float twiceArea = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            twiceArea += a.x * b.y - b.x * a.y;
        }
        return twiceArea * 0.5f;
    }

    static float Cross(Vector2 a, Vector2 b, Vector2 c)
        => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        => Cross(a, b, point) <= Epsilon && Cross(b, c, point) <= Epsilon && Cross(c, a, point) <= Epsilon;

    static float DistanceToPolygon(Vector2 point, List<Vector2> polygon)
    {
        if (PointInPolygon(point, polygon)) return 0f;
        float best = float.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            Vector2 segment = b - a;
            float t = segment.sqrMagnitude > 0f
                ? Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude)
                : 0f;
            best = Mathf.Min(best, Vector2.Distance(point, a + segment * t));
        }
        return best;
    }

    static bool PointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            if ((a.y > point.y) != (b.y > point.y)
                && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    static bool SameEdge(Vector2 a, Vector2 b, Vector2 p, Vector2 q)
        => ((a - p).sqrMagnitude <= Epsilon * Epsilon && (b - q).sqrMagnitude <= Epsilon * Epsilon)
           || ((a - q).sqrMagnitude <= Epsilon * Epsilon && (b - p).sqrMagnitude <= Epsilon * Epsilon);

    static bool TryBuildingId(string name, out long id)
    {
        id = 0;
        return name.StartsWith("Building_", StringComparison.Ordinal)
               && long.TryParse(name.Substring("Building_".Length), out id);
    }

    static void Check(bool condition, string message, ref int failures)
    {
        if (condition) Debug.Log("[Hadan Check] OK: " + message);
        else { failures++; Debug.LogError("[Hadan Check] FAIL: " + message); }
    }

    [Serializable]
    class MapData
    {
        public MapElement[] elements;
    }

    [Serializable]
    class MapElement
    {
        public long id;
        public MapTags tags;
        public GeoPoint[] geometry;
    }

    [Serializable]
    class MapTags
    {
        public string building;
        public string highway;
        public string building_levels;
    }

    [Serializable]
    class GeoPoint
    {
        public double lat;
        public double lon;
    }

}
