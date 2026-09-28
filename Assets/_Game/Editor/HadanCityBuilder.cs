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
    const string BuildingTexturePath = DataFolder + "/hadan_building_windows.asset";
    const string DataStableKey = "HadanCityBuilder.DataFilesStable";

    const double CenterLat = 35.10625;
    const double CenterLon = 128.96675;
    const float CityScale = 1.6f;
    const int MaxFloors = 6;
    const float FloorHeight = 3.6f;
    const float RoadY = 0.02f;
    const float TreeRadius = 1.85f;
    const float PoleRadius = 1.12f;
    const float CarRadius = 2.4f;
    const float RailingHeight = 0.8f;
    const float RailingHalfThickness = 0.05f;
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
        int dataFilesBefore = CountDataFiles();

        V01SceneBuilder.ApplyRenderSettings();
        V01SceneBuilder.CreateLight();

        var buildingTexture = EnsureBuildingTexture();
        var buildingMaterials = new[]
        {
            HadanMaterial("hadan_building_1", new Color(0.48f, 0.50f, 0.53f), buildingTexture),
            HadanMaterial("hadan_building_2", new Color(0.61f, 0.62f, 0.64f), buildingTexture),
            HadanMaterial("hadan_building_3", new Color(0.38f, 0.40f, 0.43f), buildingTexture)
        };
        var roofMaterial = HadanMaterial("hadan_roof", new Color(0.22f, 0.23f, 0.25f));
        var roadMaterial = HadanMaterial("hadan_road", new Color(0.16f, 0.17f, 0.18f));
        var groundMaterial = HadanMaterial("hadan_ground", new Color(0.30f, 0.34f, 0.29f));
        var detailMaterials = CreateDetailMaterials();
        var roads = RoadInfos(map);
        var counts = new PropCounts();

        CreateGround(map, groundMaterial);
        var meshes = new List<Mesh>();
        var buildings = CreateBuildings(map, roads, buildingMaterials, roofMaterial, detailMaterials,
            meshes, counts, out int buildingCount, out float maxHeight);
        int roadCount = CreateRoads(roads, roadMaterial, meshes);

        Vector2 spawn = FindSpawn(map, buildings);
        CreateStreetProps(roads, buildings, spawn, detailMaterials, counts);
        var clock = V01SceneBuilder.CreateGameClock();
        var player = V01SceneBuilder.CreatePlayer();
        player.transform.position = new Vector3(spawn.x, player.transform.position.y, spawn.y);
        UnityEngine.Object.DestroyImmediate(player.GetComponent<DebugStartState>());
        var arrival = new GameObject("Arrive_FromEstate");
        arrival.transform.position = player.transform.position;
        player.GetComponent<PlayerDeath>().Bind(new[] { arrival.transform },
            HadanMaterial("hadan_player_zombie", new Color(0.58f, 0.26f, 0.58f)));
        V01SceneBuilder.CreateCamera(player.transform);
        V01SceneBuilder.CreateHud(player, clock);

        var watch = Stopwatch.StartNew();
        V01SceneBuilder.BuildNavMesh();
        watch.Stop();

        AssetDatabase.DeleteAsset(MeshAssetPath);
        meshAssetCreated = false;
        for (int i = 0; i < meshes.Count; i++) StoreMesh(meshes[i]);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(DataStableKey, dataFilesBefore == CountDataFiles());
        EditorSceneManager.SaveScene(scene, ScenePath);
        V01SceneBuilder.EnsureBuildScenes();
        int triangles = CountUniqueMeshTriangles();
        Debug.Log("[Hadan] 건물 " + buildingCount + ", 도로 " + roadCount
                  + ", 소품 문 " + counts.doors + "/차양 " + counts.awnings
                  + "/옥상 " + counts.rooftop + "/나무 " + counts.trees
                  + "/전봇대 " + counts.poles + "/차 " + counts.cars
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
                        (filter.sharedMesh.bounds.size.y - RailingHeight) * Mathf.Abs(child.transform.lossyScale.y));
                    if (TryBuildingId(child.name, out long id) && polygons.TryGetValue(id, out var polygon))
                        badFaces += CountBadFaces(filter.sharedMesh, polygon);
                    else
                        badFaces++;
                }
            }
        }

        Check(missingColliders == 0, "모든 건물에 MeshCollider가 있다", ref failures);
        Check(badFaces == 0, "건물 옆면 법선은 바깥, 지붕 법선은 위를 향한다", ref failures);
        CheckBuildingProps(root ? root.transform : null, actualBuildings, polygons, ref failures);

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
        CheckStreetProps(polygons, arrival ? new Vector2(arrival.transform.position.x, arrival.transform.position.z) : default,
            ref failures);

        bool respawnAtArrival = false;
        if (player && arrival)
        {
            var death = player.GetComponent<PlayerDeath>();
            if (death)
            {
                var points = new SerializedObject(death).FindProperty("spawnPoints");
                respawnAtArrival = points != null && points.arraySize == 1
                    && points.GetArrayElementAtIndex(0).objectReferenceValue == arrival.transform;
            }
        }
        Check(respawnAtArrival, "도시 PlayerDeath의 부활 지점이 Arrive_FromEstate다", ref failures);

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
        Check(triangles <= 150000, "씬 고유 메쉬 삼각형이 150,000 이하이다", ref failures);
        Check(EditorPrefs.GetBool(DataStableKey, false),
            "재빌드 전후 " + DataFolder + " 파일 수가 같다", ref failures);
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

    static Texture2D EnsureBuildingTexture()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BuildingTexturePath);
        if (!texture)
        {
            texture = new Texture2D(64, 64, TextureFormat.RGBA32, true) { name = "hadan_building_windows" };
            AssetDatabase.CreateAsset(texture, BuildingTexturePath);
        }
        else if (texture.mipmapCount == 1)
        {
            texture.Reinitialize(64, 64, TextureFormat.RGBA32, true);
        }

        var pixels = new Color[64 * 64];
        // 한 장을 반복해야 건물 수가 늘어도 텍스처와 머티리얼 수가 늘지 않는다.
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            pixels[y * 64 + x] = y < 4
                ? new Color(0.42f, 0.42f, 0.42f)
                : x >= 18 && x < 46 && y >= 18 && y < 52
                    ? new Color(0.16f, 0.22f, 0.28f)
                    : Color.white;

        texture.SetPixels(pixels);
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply(true, false);
        EditorUtility.SetDirty(texture);
        return texture;
    }

    static Material HadanMaterial(string id, Color color, Texture texture = null)
    {
        var material = V01SceneBuilder.EnsureSharedMaterial(id, color);
        material.color = color;
        material.mainTexture = texture;
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    static DetailMaterials CreateDetailMaterials()
    {
        return new DetailMaterials
        {
            door = HadanMaterial("hadan_door", new Color(0.10f, 0.11f, 0.12f)),
            awnings = new[]
            {
                HadanMaterial("hadan_awning_1", new Color(0.55f, 0.16f, 0.13f)),
                HadanMaterial("hadan_awning_2", new Color(0.13f, 0.35f, 0.48f)),
                HadanMaterial("hadan_awning_3", new Color(0.69f, 0.52f, 0.14f)),
                HadanMaterial("hadan_awning_4", new Color(0.20f, 0.45f, 0.25f))
            },
            tank = HadanMaterial("hadan_water_tank", new Color(0.42f, 0.46f, 0.49f)),
            airConditioner = HadanMaterial("hadan_air_conditioner", new Color(0.67f, 0.68f, 0.66f)),
            trunk = HadanMaterial("hadan_tree_trunk", new Color(0.28f, 0.18f, 0.10f)),
            leaves = HadanMaterial("hadan_tree_leaves", new Color(0.18f, 0.34f, 0.16f)),
            pole = HadanMaterial("hadan_utility_pole", new Color(0.25f, 0.25f, 0.24f)),
            car = HadanMaterial("hadan_abandoned_car", new Color(0.28f, 0.23f, 0.20f)),
            carRoof = HadanMaterial("hadan_abandoned_car_roof", new Color(0.18f, 0.18f, 0.17f))
        };
    }

    static List<RoadInfo> RoadInfos(MapData map)
    {
        var roads = new List<RoadInfo>();
        for (int i = 0; i < map.elements.Length; i++)
        {
            var element = map.elements[i];
            if (element.tags == null || string.IsNullOrEmpty(element.tags.highway)
                || element.geometry == null || element.geometry.Length < 2) continue;

            var road = new RoadInfo
            {
                id = element.id,
                highway = element.tags.highway,
                halfWidth = RoadWidth(element.tags.highway) * CityScale * 0.5f
            };
            for (int j = 0; j < element.geometry.Length; j++)
                road.points.Add(GeoToWorld(element.geometry[j].lat, element.geometry[j].lon));
            roads.Add(road);
        }
        return roads;
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

    static List<List<Vector2>> CreateBuildings(MapData map, List<RoadInfo> roads, Material[] materials,
        Material roofMaterial, DetailMaterials details, List<Mesh> meshes, PropCounts props,
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
            go.GetComponent<MeshRenderer>().sharedMaterials = new[] { materials[count % materials.Length], roofMaterial };
            var collider = go.GetComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

            CreateBuildingDetails(go.transform, element.id, polygon, height, roads, details, props);

            polygons.Add(polygon);
            count++;
            maxHeight = Mathf.Max(maxHeight, height);
        }

        return polygons;
    }

    static int CreateRoads(List<RoadInfo> roads, Material material, List<Mesh> meshes)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int roadCount = 0;

        for (int i = 0; i < roads.Count; i++)
        {
            var road = roads[i];

            bool emitted = false;
            for (int j = 0; j < road.points.Count - 1; j++)
            {
                Vector2 from = road.points[j];
                Vector2 to = road.points[j + 1];
                Vector2 direction = to - from;
                if (direction.sqrMagnitude < Epsilon) continue;

                Vector2 side = new Vector2(-direction.y, direction.x).normalized * road.halfWidth;
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

        var roadObject = new GameObject("Roads", typeof(MeshFilter), typeof(MeshRenderer));
        roadObject.GetComponent<MeshFilter>().sharedMesh = mesh;
        roadObject.GetComponent<MeshRenderer>().sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(roadObject, StaticEditorFlags.BatchingStatic);
        return roadCount;
    }

    static void CreateBuildingDetails(Transform building, long id, List<Vector2> polygon, float height,
        List<RoadInfo> roads, DetailMaterials materials, PropCounts counts)
    {
        int streetEdge = -1;
        int longestEdge = 0;
        float longestLength = 0f;
        float nearestRoad = float.PositiveInfinity;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            float length = Vector2.Distance(a, b);
            if (length > longestLength)
            {
                longestLength = length;
                longestEdge = i;
            }
            if (length < 1.6f) continue;
            for (int j = 0; j < roads.Count; j++)
            {
                float distance = DistanceToRoad(a, b, roads[j]);
                if (distance >= nearestRoad) continue;
                nearestRoad = distance;
                streetEdge = i;
            }
        }
        if (streetEdge < 0) streetEdge = longestEdge;

        Vector2 from = polygon[streetEdge];
        Vector2 to = polygon[(streetEdge + 1) % polygon.Count];
        Vector2 edge = to - from;
        Vector2 outward = new Vector2(-edge.y, edge.x).normalized;
        Vector2 center = (from + to) * 0.5f;
        Quaternion facing = Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.y));

        CreatePrimitive(PrimitiveType.Cube, "Door", building,
            new Vector3(center.x + outward.x * 0.02f, 1.1f, center.y + outward.y * 0.02f),
            new Vector3(1.2f, 2.2f, 0.04f), facing, materials.door, false);
        counts.doors++;

        bool shop = false;
        for (int i = 0; i < roads.Count; i++)
            if (IsCommercialRoad(roads[i].highway) && DistanceToRoad(from, to, roads[i]) <= 15f)
            {
                shop = true;
                break;
            }
        if (shop)
        {
            var random = RoadRandom(id, 17);
            CreatePrimitive(PrimitiveType.Cube, "Awning", building,
                new Vector3(center.x + outward.x * 0.6f, 2.8f, center.y + outward.y * 0.6f),
                new Vector3(edge.magnitude * 0.6f, 0.12f, 1.2f),
                facing * Quaternion.Euler(6f, 0f, 0f), materials.awnings[random.Next(materials.awnings.Length)], false);
            counts.awnings++;
        }

        var rooftop = new GameObject("Rooftop").transform;
        rooftop.SetParent(building, false);
        counts.rooftop += polygon.Count;

        var occupied = new List<Placement>();
        var roofRandom = RoadRandom(id, 31);
        if (Mathf.Abs(SignedArea(polygon)) / (CityScale * CityScale) >= 100f)
        {
            if (roofRandom.Next(2) == 0
                && TryRoofPoint(polygon, roofRandom, 0.7f, occupied, out Vector2 tankPoint))
            {
                CreatePrimitive(PrimitiveType.Cylinder, "WaterTank", rooftop,
                    new Vector3(tankPoint.x, height + 0.75f, tankPoint.y), new Vector3(0.7f, 0.75f, 0.7f),
                    Quaternion.identity, materials.tank, false);
                occupied.Add(new Placement(tankPoint, 0.7f));
                counts.rooftop++;
            }

            int units = roofRandom.Next(4);
            for (int i = 0; i < units; i++)
            {
                if (!TryRoofPoint(polygon, roofRandom, 0.5f, occupied, out Vector2 unitPoint)) continue;
                CreatePrimitive(PrimitiveType.Cube, "AirConditioner", rooftop,
                    new Vector3(unitPoint.x, height + 0.3f, unitPoint.y), new Vector3(0.8f, 0.6f, 0.6f),
                    Quaternion.Euler(0f, (float)roofRandom.NextDouble() * 360f, 0f), materials.airConditioner, false);
                occupied.Add(new Placement(unitPoint, 0.5f));
                counts.rooftop++;
            }
        }
    }

    static void CreateStreetProps(List<RoadInfo> roads, List<List<Vector2>> buildings, Vector2 spawn,
        DetailMaterials materials, PropCounts counts)
    {
        var root = new GameObject("StreetProps").transform;
        var treeRoot = new GameObject("Trees").transform;
        var poleRoot = new GameObject("UtilityPoles").transform;
        var carRoot = new GameObject("AbandonedCars").transform;
        treeRoot.SetParent(root, false);
        poleRoot.SetParent(root, false);
        carRoot.SetParent(root, false);
        var occupied = new List<Placement>();

        for (int i = 0; i < roads.Count; i++)
        {
            var road = roads[i];
            float length = RoadLength(road);
            var random = RoadRandom(road.id, 53);

            if (road.highway == "primary" || road.highway == "secondary")
            {
                for (float distance = (float)random.NextDouble() * 15f; distance < length; distance += 15f)
                {
                    if (!PointAlongRoad(road, distance, out Vector2 point, out Vector2 direction)) continue;
                    Vector2 side = new Vector2(-direction.y, direction.x);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector2 position = point + side * sign * (road.halfWidth + TreeRadius);
                        if (!CanPlace(position, TreeRadius, buildings, spawn, occupied)
                            || !IsClearOfRoads(position, roads)) continue;
                        CreateTree(treeRoot, position, materials, counts);
                        occupied.Add(new Placement(position, TreeRadius));
                    }
                }
            }

            if (road.highway == "tertiary" || road.highway == "residential" || road.highway == "service")
            {
                int sign = random.Next(2) == 0 ? -1 : 1;
                for (float distance = (float)random.NextDouble() * 30f; distance < length; distance += 30f)
                {
                    if (!PointAlongRoad(road, distance, out Vector2 point, out Vector2 direction)) continue;
                    Vector2 side = new Vector2(-direction.y, direction.x);
                    Vector2 position = point + side * sign * (road.halfWidth + PoleRadius);
                    if (!CanPlace(position, PoleRadius, buildings, spawn, occupied)
                        || !IsClearOfRoads(position, roads)) continue;
                    CreatePole(poleRoot, position, direction, materials, counts);
                    occupied.Add(new Placement(position, PoleRadius));
                }
            }

            if (!IsCommercialRoad(road.highway)) continue;
            float wanted = length / 125f;
            int cars = Mathf.FloorToInt(wanted);
            if (random.NextDouble() < wanted - cars) cars++;
            for (int car = 0; car < cars; car++)
            {
                float distance = (car + 0.5f + ((float)random.NextDouble() - 0.5f) * 0.5f) * length / cars;
                if (!PointAlongRoad(road, distance, out Vector2 point, out Vector2 direction)) continue;
                Vector2 side = new Vector2(-direction.y, direction.x);
                Vector2 position = point + side * (random.Next(2) == 0 ? -1f : 1f) * road.halfWidth * 0.5f;
                if (!CanPlace(position, CarRadius, buildings, spawn, occupied)) continue;
                CreateCar(carRoot, position, direction, (float)random.NextDouble() * 16f - 8f, materials, counts);
                occupied.Add(new Placement(position, CarRadius));
            }
        }

        V01SceneBuilder.MarkStatic(root);
    }

    static void CreateTree(Transform parent, Vector2 position, DetailMaterials materials, PropCounts counts)
    {
        var tree = new GameObject("Tree").transform;
        tree.SetParent(parent, false);
        tree.position = new Vector3(position.x, 0f, position.y);
        CreatePrimitive(PrimitiveType.Cylinder, "Trunk", tree, new Vector3(0f, 1.5f, 0f),
            new Vector3(0.35f, 1.5f, 0.35f), Quaternion.identity, materials.trunk, true);
        CreatePrimitive(PrimitiveType.Cube, "Leaves", tree, new Vector3(0f, 3.8f, 0f),
            new Vector3(2.6f, 2.8f, 2.6f), Quaternion.identity, materials.leaves, false);
        counts.trees++;
    }

    static void CreatePole(Transform parent, Vector2 position, Vector2 direction,
        DetailMaterials materials, PropCounts counts)
    {
        var pole = new GameObject("UtilityPole").transform;
        pole.SetParent(parent, false);
        pole.position = new Vector3(position.x, 0f, position.y);
        CreatePrimitive(PrimitiveType.Cylinder, "Post", pole, new Vector3(0f, 3.5f, 0f),
            new Vector3(0.25f, 3.5f, 0.25f), Quaternion.identity, materials.pole, true);
        CreatePrimitive(PrimitiveType.Cube, "Crossbar", pole, new Vector3(0f, 6.2f, 0f),
            new Vector3(2.2f, 0.18f, 0.18f),
            Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y)), materials.pole, false);
        counts.poles++;
    }

    static void CreateCar(Transform parent, Vector2 position, Vector2 direction, float angle,
        DetailMaterials materials, PropCounts counts)
    {
        var car = new GameObject("AbandonedCar").transform;
        car.SetParent(parent, false);
        car.position = new Vector3(position.x, 0f, position.y);
        car.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y)) * Quaternion.Euler(0f, angle, 0f);
        CreatePrimitive(PrimitiveType.Cube, "Body", car, new Vector3(0f, 0.55f, 0f),
            new Vector3(1.9f, 0.8f, 4.2f), Quaternion.identity, materials.car, true);
        CreatePrimitive(PrimitiveType.Cube, "Roof", car, new Vector3(0f, 1.15f, -0.2f),
            new Vector3(1.55f, 0.55f, 2.1f), Quaternion.identity, materials.carRoof, false);
        counts.cars++;
    }

    static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Vector3 position,
        Vector3 scale, Quaternion rotation, Material material, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!keepCollider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    static Mesh CreateBuildingMesh(long id, List<Vector2> polygon, float height)
    {
        var roofTriangles = new List<int>();
        if (!Triangulate(polygon, roofTriangles)) return null;

        var vertices = new List<Vector3>(polygon.Count * 17);
        var uvs = new List<Vector2>(polygon.Count * 17);
        var wallTriangles = new List<int>(polygon.Count * 6);
        // 지붕을 별도 서브메쉬로 두면 창문 타일을 피하려고 UV 규칙을 비틀 필요가 없다.
        for (int i = 0; i < polygon.Count; i++)
        {
            vertices.Add(new Vector3(polygon[i].x, height, polygon[i].y));
            uvs.Add(Vector2.zero);
        }

        float perimeter = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            float length = Vector2.Distance(a, b);
            int first = vertices.Count;
            vertices.Add(new Vector3(a.x, 0f, a.y));
            vertices.Add(new Vector3(b.x, 0f, b.y));
            vertices.Add(new Vector3(b.x, height, b.y));
            vertices.Add(new Vector3(a.x, height, a.y));
            uvs.Add(new Vector2(perimeter / 3f, 0f));
            uvs.Add(new Vector2((perimeter + length) / 3f, 0f));
            uvs.Add(new Vector2((perimeter + length) / 3f, height / FloorHeight));
            uvs.Add(new Vector2(perimeter / 3f, height / FloorHeight));
            wallTriangles.Add(first);
            wallTriangles.Add(first + 1);
            wallTriangles.Add(first + 2);
            wallTriangles.Add(first);
            wallTriangles.Add(first + 2);
            wallTriangles.Add(first + 3);
            perimeter += length;
        }

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            Vector2 offset = new Vector2(-(b.y - a.y), b.x - a.x).normalized * RailingHalfThickness;
            int first = vertices.Count;
            vertices.Add(new Vector3(a.x + offset.x, height, a.y + offset.y));
            vertices.Add(new Vector3(b.x + offset.x, height, b.y + offset.y));
            vertices.Add(new Vector3(b.x + offset.x, height + RailingHeight, b.y + offset.y));
            vertices.Add(new Vector3(a.x + offset.x, height + RailingHeight, a.y + offset.y));
            vertices.Add(new Vector3(a.x - offset.x, height, a.y - offset.y));
            vertices.Add(new Vector3(a.x - offset.x, height + RailingHeight, a.y - offset.y));
            vertices.Add(new Vector3(b.x - offset.x, height + RailingHeight, b.y - offset.y));
            vertices.Add(new Vector3(b.x - offset.x, height, b.y - offset.y));
            vertices.Add(new Vector3(a.x + offset.x, height + RailingHeight, a.y + offset.y));
            vertices.Add(new Vector3(b.x + offset.x, height + RailingHeight, b.y + offset.y));
            vertices.Add(new Vector3(b.x - offset.x, height + RailingHeight, b.y - offset.y));
            vertices.Add(new Vector3(a.x - offset.x, height + RailingHeight, a.y - offset.y));
            for (int j = 0; j < 12; j++) uvs.Add(Vector2.zero);

            roofTriangles.Add(first);
            roofTriangles.Add(first + 1);
            roofTriangles.Add(first + 2);
            roofTriangles.Add(first);
            roofTriangles.Add(first + 2);
            roofTriangles.Add(first + 3);
            roofTriangles.Add(first + 4);
            roofTriangles.Add(first + 5);
            roofTriangles.Add(first + 6);
            roofTriangles.Add(first + 4);
            roofTriangles.Add(first + 6);
            roofTriangles.Add(first + 7);
            roofTriangles.Add(first + 8);
            roofTriangles.Add(first + 9);
            roofTriangles.Add(first + 10);
            roofTriangles.Add(first + 8);
            roofTriangles.Add(first + 10);
            roofTriangles.Add(first + 11);
        }

        var mesh = new Mesh { name = "HadanBuilding_" + id };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(wallTriangles, 0);
        mesh.SetTriangles(roofTriangles, 1);
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

    static bool IsCommercialRoad(string highway)
        => highway == "primary" || highway == "secondary" || highway == "tertiary";

    static System.Random RoadRandom(long id, int salt)
        => new System.Random(unchecked((int)(id ^ (id >> 32)) * 397 ^ salt));

    static bool TryRoofPoint(List<Vector2> polygon, System.Random random, float radius,
        List<Placement> occupied, out Vector2 point)
    {
        Vector2 min = polygon[0];
        Vector2 max = polygon[0];
        for (int i = 1; i < polygon.Count; i++)
        {
            min = Vector2.Min(min, polygon[i]);
            max = Vector2.Max(max, polygon[i]);
        }

        for (int attempt = 0; attempt < 40; attempt++)
        {
            point = new Vector2(Mathf.Lerp(min.x, max.x, (float)random.NextDouble()),
                Mathf.Lerp(min.y, max.y, (float)random.NextDouble()));
            if (!PointInPolygon(point, polygon) || DistanceToPolygonEdge(point, polygon) < 1f + radius) continue;
            bool overlaps = false;
            for (int i = 0; i < occupied.Count; i++)
                if (Vector2.Distance(point, occupied[i].point) < radius + occupied[i].radius)
                {
                    overlaps = true;
                    break;
                }
            if (!overlaps) return true;
        }

        point = default;
        return false;
    }

    static bool CanPlace(Vector2 point, float radius, List<List<Vector2>> buildings, Vector2 spawn,
        List<Placement> occupied)
    {
        if (Vector2.Distance(point, spawn) < 5f + radius) return false;
        for (int i = 0; i < buildings.Count; i++)
            if (DistanceToPolygon(point, buildings[i]) < 1f + radius) return false;
        for (int i = 0; i < occupied.Count; i++)
            if (Vector2.Distance(point, occupied[i].point) < radius + occupied[i].radius) return false;
        return true;
    }

    static bool IsClearOfRoads(Vector2 point, List<RoadInfo> roads)
    {
        for (int i = 0; i < roads.Count; i++)
        {
            float clearance = roads[i].halfWidth + 0.5f;
            for (int j = 0; j < roads[i].points.Count - 1; j++)
            {
                Vector2 a = roads[i].points[j];
                Vector2 b = roads[i].points[j + 1];
                if (point.x < Mathf.Min(a.x, b.x) - clearance || point.x > Mathf.Max(a.x, b.x) + clearance
                    || point.y < Mathf.Min(a.y, b.y) - clearance || point.y > Mathf.Max(a.y, b.y) + clearance)
                    continue;
                if (DistanceToSegment(point, a, b) < clearance) return false;
            }
        }
        return true;
    }

    static float RoadLength(RoadInfo road)
    {
        float length = 0f;
        for (int i = 0; i < road.points.Count - 1; i++)
            length += Vector2.Distance(road.points[i], road.points[i + 1]);
        return length;
    }

    static bool PointAlongRoad(RoadInfo road, float distance, out Vector2 point, out Vector2 direction)
    {
        for (int i = 0; i < road.points.Count - 1; i++)
        {
            Vector2 segment = road.points[i + 1] - road.points[i];
            float length = segment.magnitude;
            if (length <= Epsilon) continue;
            if (distance <= length)
            {
                direction = segment / length;
                point = road.points[i] + direction * distance;
                return true;
            }
            distance -= length;
        }

        point = default;
        direction = default;
        return false;
    }

    static float DistanceToRoad(Vector2 a, Vector2 b, RoadInfo road)
    {
        float best = float.PositiveInfinity;
        for (int i = 0; i < road.points.Count - 1; i++)
            best = Mathf.Min(best, SegmentDistance(a, b, road.points[i], road.points[i + 1]));
        return Mathf.Max(0f, best - road.halfWidth);
    }

    static float SegmentDistance(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float abC = Cross(a, b, c);
        float abD = Cross(a, b, d);
        float cdA = Cross(c, d, a);
        float cdB = Cross(c, d, b);
        bool crosses = abC * abD < 0f && cdA * cdB < 0f;
        bool touches = Mathf.Abs(abC) <= Epsilon && PointOnSegment(c, a, b)
            || Mathf.Abs(abD) <= Epsilon && PointOnSegment(d, a, b)
            || Mathf.Abs(cdA) <= Epsilon && PointOnSegment(a, c, d)
            || Mathf.Abs(cdB) <= Epsilon && PointOnSegment(b, c, d);
        if (crosses || touches) return 0f;
        return Mathf.Min(Mathf.Min(DistanceToSegment(a, c, d), DistanceToSegment(b, c, d)),
            Mathf.Min(DistanceToSegment(c, a, b), DistanceToSegment(d, a, b)));
    }

    static bool PointOnSegment(Vector2 point, Vector2 a, Vector2 b)
        => point.x >= Mathf.Min(a.x, b.x) - Epsilon && point.x <= Mathf.Max(a.x, b.x) + Epsilon
           && point.y >= Mathf.Min(a.y, b.y) - Epsilon && point.y <= Mathf.Max(a.y, b.y) + Epsilon;

    static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 segment = b - a;
        float t = segment.sqrMagnitude > Epsilon
            ? Mathf.Clamp01(Vector2.Dot(point - a, segment) / segment.sqrMagnitude)
            : 0f;
        return Vector2.Distance(point, a + segment * t);
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

    static int CountDataFiles()
        => Directory.Exists(DataFolder) ? Directory.GetFiles(DataFolder).Length : 0;

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
        float roofHeight = vertices[0].y;
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
            if (Mathf.Min(a.y, Mathf.Min(b.y, c.y)) >= roofHeight - Epsilon)
            {
                Vector2 center = (edgeA + edgeB + new Vector2(c.x, c.z)) / 3f;
                for (int j = 0; j < polygon.Count; j++)
                {
                    Vector2 p = polygon[j];
                    Vector2 q = polygon[(j + 1) % polygon.Count];
                    Vector2 outward = new Vector2(-(q.y - p.y), q.x - p.x).normalized;
                    float alignment = Vector2.Dot(new Vector2(normal.x, normal.z), outward);
                    if (Mathf.Abs(alignment) < 0.999f
                        || DistanceToSegment(center, p, q) > RailingHalfThickness + Epsilon) continue;
                    if (Vector2.Dot(center - p, outward) * alignment <= 0f) bad++;
                    matched = true;
                    break;
                }
                if (!matched) bad++;
                continue;
            }
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

    static void CheckBuildingProps(Transform root, int buildingCount,
        Dictionary<long, List<Vector2>> polygons, ref int failures)
    {
        int doors = 0;
        int awnings = 0;
        bool doorsValid = root;
        bool noForbiddenColliders = root;
        bool railingsMerged = root;
        bool rooftopValid = root;
        bool facadeUvValid = root;
        Texture facadeTexture = null;

        if (root)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var building = root.GetChild(i);
                int buildingDoors = CountNamed(building, "Door");
                doors += buildingDoors;
                awnings += CountNamed(building, "Awning");
                if (buildingDoors != 1) doorsValid = false;

                var filter = building.GetComponent<MeshFilter>();
                var renderer = building.GetComponent<MeshRenderer>();
                if (!filter || !filter.sharedMesh || filter.sharedMesh.subMeshCount != 2
                    || filter.sharedMesh.uv.Length != filter.sharedMesh.vertexCount)
                    facadeUvValid = false;
                var shared = renderer ? renderer.sharedMaterials : null;
                if (shared == null || shared.Length != 2 || !shared[0] || !shared[1]
                    || !shared[0].mainTexture || shared[1].mainTexture)
                    facadeUvValid = false;
                else if (!facadeTexture)
                    facadeTexture = shared[0].mainTexture;
                else if (facadeTexture != shared[0].mainTexture)
                    facadeUvValid = false;

                if (!TryBuildingId(building.name, out long id) || !polygons.TryGetValue(id, out var polygon)
                    || !filter || !filter.sharedMesh)
                {
                    rooftopValid = false;
                    continue;
                }

                float roofHeight = filter.sharedMesh.bounds.max.y - RailingHeight;
                bool meshRailing = filter.sharedMesh.vertexCount == polygon.Count * 17;
                railingsMerged &= CountNamed(building, "Railing") == 0 && meshRailing;
                if (!meshRailing) rooftopValid = false;
                var all = building.GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length; j++)
                {
                    var part = all[j];
                    if ((part.name == "Door" || part.name == "Awning")
                        && part.GetComponentsInChildren<Collider>(true).Length > 0)
                        noForbiddenColliders = false;

                    if (part.name != "WaterTank" && part.name != "AirConditioner") continue;
                    var propRenderer = part.GetComponent<Renderer>();
                    Vector2 point = new Vector2(part.position.x, part.position.z);
                    if (!propRenderer || propRenderer.bounds.min.y < roofHeight - Epsilon
                        || !PointInPolygon(point, polygon) || DistanceToPolygonEdge(point, polygon) < 1f)
                        rooftopValid = false;
                }
            }
        }

        Check(doorsValid, "모든 건물에 문이 1개 있다 (" + doors + ")", ref failures);
        Check(awnings > 0, "상가 차양이 있다 (" + awnings + ")", ref failures);
        Check(noForbiddenColliders && railingsMerged, "문·차양에 콜라이더가 없고 난간은 건물 메쉬에 병합됐다", ref failures);
        Check(rooftopValid, "난간과 옥상 소품이 자기 지붕 위와 외곽 안쪽에 있다", ref failures);
        Check(facadeUvValid, "건물 옆면 UV와 단색 지붕 서브메쉬가 있다", ref failures);
    }

    static void CheckStreetProps(Dictionary<long, List<Vector2>> polygons, Vector2 spawn, ref int failures)
    {
        var root = GameObject.Find("StreetProps");
        bool collidersValid = root;
        bool clear = root;
        bool noOverlap = root;
        int trees = 0;
        int poles = 0;
        int cars = 0;
        var occupied = new List<Placement>();

        if (root)
        {
            for (int groupIndex = 0; groupIndex < root.transform.childCount; groupIndex++)
            {
                var group = root.transform.GetChild(groupIndex);
                for (int i = 0; i < group.childCount; i++)
                {
                    var prop = group.GetChild(i);
                    float radius;
                    if (prop.name == "Tree")
                    {
                        radius = TreeRadius;
                        trees++;
                        var trunk = prop.Find("Trunk");
                        var leaves = prop.Find("Leaves");
                        collidersValid &= trunk && trunk.GetComponent<CapsuleCollider>()
                            && leaves && leaves.GetComponentsInChildren<Collider>(true).Length == 0;
                    }
                    else if (prop.name == "UtilityPole")
                    {
                        radius = PoleRadius;
                        poles++;
                        collidersValid &= prop.GetComponentInChildren<Collider>(true);
                    }
                    else if (prop.name == "AbandonedCar")
                    {
                        radius = CarRadius;
                        cars++;
                        collidersValid &= prop.GetComponentInChildren<BoxCollider>(true);
                    }
                    else continue;

                    Vector2 point = new Vector2(prop.position.x, prop.position.z);
                    if (Vector2.Distance(point, spawn) < 5f + radius) clear = false;
                    foreach (var polygon in polygons.Values)
                        if (DistanceToPolygon(point, polygon) < 1f + radius - Epsilon)
                        {
                            clear = false;
                            break;
                        }
                    for (int j = 0; j < occupied.Count; j++)
                        if (Vector2.Distance(point, occupied[j].point) < radius + occupied[j].radius - Epsilon)
                        {
                            noOverlap = false;
                            break;
                        }
                    occupied.Add(new Placement(point, radius));
                }
            }
        }

        Check(trees > 0 && poles > 0 && cars > 0,
            "거리 소품이 있다 (나무 " + trees + ", 전봇대 " + poles + ", 차 " + cars + ")", ref failures);
        Check(collidersValid, "나무 줄기·전봇대·차에 지정 콜라이더가 있다", ref failures);
        Check(clear, "거리 소품이 건물 외곽 1m와 시작점 반경 5m를 비운다", ref failures);
        Check(noOverlap, "거리 소품끼리 겹치지 않는다", ref failures);
    }

    static int CountNamed(Transform root, string name)
    {
        int count = 0;
        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == name) count++;
        return count;
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
        return DistanceToPolygonEdge(point, polygon);
    }

    static float DistanceToPolygonEdge(Vector2 point, List<Vector2> polygon)
    {
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

    class RoadInfo
    {
        public long id;
        public string highway;
        public float halfWidth;
        public readonly List<Vector2> points = new List<Vector2>();
    }

    class DetailMaterials
    {
        public Material door;
        public Material[] awnings;
        public Material tank;
        public Material airConditioner;
        public Material trunk;
        public Material leaves;
        public Material pole;
        public Material car;
        public Material carRoof;
    }

    class PropCounts
    {
        public int doors;
        public int awnings;
        public int rooftop;
        public int trees;
        public int poles;
        public int cars;
    }

    readonly struct Placement
    {
        public readonly Vector2 point;
        public readonly float radius;

        public Placement(Vector2 point, float radius)
        {
            this.point = point;
            this.radius = radius;
        }
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
