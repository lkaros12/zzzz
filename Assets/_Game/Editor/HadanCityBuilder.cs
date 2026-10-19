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
    const string CommercialPlainTexturePath = DataFolder + "/hadan_facade_commercial_plain.asset";
    const string ResidentialBrickTexturePath = DataFolder + "/hadan_facade_residential_brick.asset";
    const string ResidentialPlainTexturePath = DataFolder + "/hadan_facade_residential_plain.asset";
    const string ApartmentTexturePath = DataFolder + "/hadan_facade_apartment.asset";
    const string SignTexturePath = DataFolder + "/hadan_sign_blocks.asset";
    const string DigitTexturePath = DataFolder + "/hadan_digits.asset";
    const string DataStableKey = "HadanCityBuilder.DataFilesStable";
    const bool UseAssetBuildings = true;
    const string BuildingPackFolder = "Assets/_Game/Art/Buildings/Pack";
    const string VehicleFolder = "Assets/_Game/Art/Vehicles";
    const string RoadTextureFolder = "Assets/_Game/Art/Roads";
    const string AsphaltTexturePath = DataFolder + "/hadan_asphalt.asset";
    static readonly string[] CarNames = { "sedan", "sports", "suv", "suv2", "lanos", "truck", "bus", "policebus" };
    const string AssetMaterialFolder = DataFolder + "/AssetMaterials";
    const string AssetMaterialFilesKey = "HadanCityBuilder.AssetMaterialFiles";
    const string AssetMaterialRebuildsKey = "HadanCityBuilder.AssetMaterialRebuilds";
    const float MaxAssetDistortion = 1.6f;
    // 임포트 캐시의 간판 면 법선 (0,-1,0)에 FBX 루트 X=-90°를 적용해 +Z를 확인했다.
    static readonly Vector3 AssetFront = Vector3.forward;

    const double CenterLat = 35.10625;
    const double CenterLon = 128.96675;
    const float CityScale = 1.6f;
    const int MaxFloors = 6;
    const float FloorHeight = 3.6f;
    const float FloorThickness = 0.2f;
    const float WallThickness = 0.25f;
    const float DoorWidth = 1.2f;
    const float DoorJambClearance = 0.1f;
    const float DoorHeight = 2.4f;
    const float StairWidth = 1.2f;
    const float StairGap = 0.1f;
    const float StairEntryDepth = 0.4f;
    const int StairSteps = 16;
    const float StairTread = 0.28f;
    const float RoadY = 0.02f;
    const float TreeRadius = 1.85f;
    const float PoleRadius = 1.12f;
    const float CarRadius = 2.4f;
    const float RailingHeight = 0.8f;
    const float RailingHalfThickness = 0.05f;
    const float Epsilon = 0.0001f;

    static readonly int[] DigitRows =
    {
        7, 5, 5, 5, 7, 2, 6, 2, 2, 7, 7, 1, 7, 4, 7, 7, 1, 7, 1, 7,
        5, 5, 7, 1, 1, 7, 4, 7, 1, 7, 7, 4, 7, 5, 7, 7, 1, 1, 1, 1,
        7, 5, 7, 5, 7, 7, 5, 7, 1, 7
    };

    static bool meshAssetCreated;

    [MenuItem("Tools/V0.1/Build Hadan Pilot")]
    public static void BuildHadanPilot()
    {
        MapData map;
        AssetModel[] assetModels;
        AssetModel[] carModels;
        try
        {
            map = LoadMap();
            assetModels = UseAssetBuildings ? LoadAssetModels() : null;
            carModels = LoadCarModels();
            if (UseAssetBuildings) AssetBuildingPlans(map, RoadInfos(map), assetModels, out _);
        }
        catch (Exception exception)
        {
            Debug.LogError("[Hadan] 지도·건물 에셋을 읽지 못했습니다: " + exception.Message);
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Directory.CreateDirectory(SceneFolder);
        Directory.CreateDirectory(DataFolder);
        int dataFilesBefore = CountDataFiles();

        V01SceneBuilder.ApplyRenderSettings();
        V01SceneBuilder.CreateLight();

        var roadMaterial = HadanMaterial("hadan_road", Color.white, EnsureAsphaltTexture());
        var groundMaterial = HadanMaterial("hadan_ground", new Color(0.30f, 0.34f, 0.29f));
        var detailMaterials = CreateDetailMaterials();
        var roads = RoadInfos(map);
        var counts = new PropCounts();

        CreateGround(map, groundMaterial);
        var meshes = new List<Mesh>();
        var buildings = CreateBuildings(map, roads, detailMaterials, meshes, counts, assetModels,
            out int buildingCount, out float maxHeight, out int stairBuildings,
            out int firstFloorOnly, out int totalFloors, out BuildingFloors floorVisibility);
        int roadCount = CreateRoads(roads, roadMaterial, meshes, counts);

        Vector2 spawn = FindSpawn(map, buildings);
        CreateStreetProps(roads, buildings, spawn, detailMaterials, meshes, counts, carModels);
        var clock = V01SceneBuilder.CreateGameClock();
        var player = V01SceneBuilder.CreatePlayer();
        player.transform.position = new Vector3(spawn.x, player.transform.position.y, spawn.y);
        if (floorVisibility) floorVisibility.SetTarget(player.transform);
        UnityEngine.Object.DestroyImmediate(player.GetComponent<DebugStartState>());
        var arrival = new GameObject("Arrive_FromEstate");
        arrival.transform.position = player.transform.position;
        V01SceneBuilder.BindPlayerDeath(player.GetComponent<PlayerDeath>(), new[] { arrival.transform });
        V01SceneBuilder.CreateCamera(player.transform);
        V01SceneBuilder.CreateHud(player, clock);

        var doorObstacles = floorVisibility ? floorVisibility.GetComponentsInChildren<NavMeshObstacle>(true)
            : Array.Empty<NavMeshObstacle>();
        for (int i = 0; i < doorObstacles.Length; i++) doorObstacles[i].enabled = false;
        var watch = Stopwatch.StartNew();
        V01SceneBuilder.BuildNavMesh();
        watch.Stop();
        if (floorVisibility)
        {
            floorVisibility.BuiltDoorNavMeshSamples = CheckDoorNavMesh(floorVisibility, out int checkedDoors);
            floorVisibility.BuiltCheckedDoors = checkedDoors;
            floorVisibility.BuiltStairPaths = CheckBuildingPaths(floorVisibility);
        }
        for (int i = 0; i < doorObstacles.Length; i++) doorObstacles[i].enabled = true;

        AssetDatabase.DeleteAsset(MeshAssetPath);
        meshAssetCreated = false;
        for (int i = 0; i < meshes.Count; i++) StoreMesh(meshes[i]);
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(DataStableKey, dataFilesBefore == CountDataFiles());
        if (UseAssetBuildings) RecordAssetMaterialFiles();
        EditorSceneManager.SaveScene(scene, ScenePath);
        V01SceneBuilder.EnsureBuildScenes();
        int triangles = CountUniqueMeshTriangles();
        int gameObjects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length;
        Debug.Log("[Hadan] 건물 " + buildingCount + ", 도로 " + roadCount
                  + ", 유형 상가 " + counts.types[(int)BuildingKind.Commercial]
                  + "/아파트 " + counts.types[(int)BuildingKind.Apartment]
                  + "/주택 " + counts.types[(int)BuildingKind.House]
                  + "/상가주택 " + counts.types[(int)BuildingKind.MixedUse]
                  + ", 간판 " + counts.signs + "/실외기 " + counts.wallAirConditioners
                  + "/옥탑방 " + counts.roofRooms + "/전선 " + counts.wires
                  + ", 소품 문 " + counts.doors + "/나무 " + counts.trees
                  + "/전봇대 " + counts.poles + "/차 " + counts.cars
                  + ", 계단 건물 " + stairBuildings + "/1층 전용 " + firstFloorOnly
                  + "/층 합계 " + totalFloors
                  + ", GameObject " + gameObjects.ToString("N0")
                  + ", 삼각형 " + triangles.ToString("N0")
                  + ", 최고 " + maxHeight.ToString("F1") + "m, NavMesh " + watch.ElapsedMilliseconds + "ms");
        if (UseAssetBuildings)
        {
            var usage = new List<string>();
            for (int i = 0; i < 13; i++) usage.Add("bld_" + i.ToString("D2") + "=" + counts.assets[i]);
            Debug.Log("[Hadan Asset] " + string.Join(", ", usage) + ", 가게 앞면 " + counts.shopfronts
                + ", 왜곡 잘림 " + counts.clippedAssets + ", 높이 잘림 " + counts.heightClippedAssets
                + ", 건너뜀 " + counts.skippedAssets
                + ", 인스턴스 삼각형 " + CountInstanceMeshTriangles().ToString("N0"));
        }
        var carUsage = new List<string>();
        for (int i = 0; i < CarNames.Length; i++) carUsage.Add(CarNames[i] + "=" + counts.carTypes[i]);
        Debug.Log("[Hadan] 차 " + string.Join(", ", carUsage) + ", 연석 메쉬 " + counts.curbMeshes
            + ", 연석 총 길이 " + counts.curbLength.ToString("F1") + "m");
        if (watch.ElapsedMilliseconds > 180000)
            Debug.LogWarning("[Hadan] NavMesh 굽기가 3분을 넘었습니다: " + watch.ElapsedMilliseconds + "ms");
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
        float maxHeight = float.PositiveInfinity;
        var polygons = UseAssetBuildings ? AssetBuildingPolygons(root ? root.transform : null) : BuildingPolygons(map);
        var floorVisibility = root ? root.GetComponent<BuildingFloors>() : null;
        if (UseAssetBuildings)
            CheckAssetBuildings(map, root ? root.transform : null, out maxHeight, ref failures);
        else
        {
            maxHeight = floorVisibility ? 0f : float.PositiveInfinity;
            int actualBuildings = floorVisibility != null && floorVisibility.Buildings != null
                ? floorVisibility.Buildings.Count : 0;
            int tolerance = Mathf.CeilToInt(expectedBuildings * 0.05f);
            Check(Mathf.Abs(actualBuildings - expectedBuildings) <= tolerance,
                "건물 수 " + actualBuildings + "/" + expectedBuildings + " (±5%)", ref failures);

            int missingColliders = floorVisibility ? 0 : expectedBuildings;
            int badFaces = floorVisibility ? 0 : 1;
            int loggedBadFaces = 0;
            if (floorVisibility)
            {
                for (int i = 0; i < floorVisibility.Buildings.Count; i++)
                {
                    var entry = floorVisibility.Buildings[i];
                    for (int floor = 0; floor < entry.floors.Length; floor++)
                        if (!entry.floors[floor] || !entry.floors[floor].GetComponent<MeshCollider>())
                            missingColliders++;
                    var roofCollider = entry.roof ? entry.roof.GetComponent<MeshCollider>() : null;
                    var roofFilter = entry.roof ? entry.roof.GetComponent<MeshFilter>() : null;
                    if (!roofCollider || !roofCollider.sharedMesh || !roofFilter || !roofFilter.sharedMesh)
                        missingColliders++;
                    else
                        maxHeight = Mathf.Max(maxHeight, roofFilter.sharedMesh.bounds.max.y - RailingHeight);

                    var structure = entry.root.GetComponentsInChildren<MeshFilter>(true);
                    var polygon = new List<Vector2>(entry.polygon);
                    for (int part = 0; part < structure.Length; part++)
                        if (structure[part].name.StartsWith("Floor_", StringComparison.Ordinal)
                            || structure[part].name.StartsWith("ExteriorWall_", StringComparison.Ordinal)
                            || structure[part].name.StartsWith("Stair_", StringComparison.Ordinal)
                            || structure[part].name == "Roof")
                            badFaces += CountBadFaces(structure[part].sharedMesh, structure[part].name,
                                polygon, ref loggedBadFaces);
                }
            }

            Check(missingColliders == 0, "모든 층·지붕에 MeshCollider가 있다", ref failures);
            Check(badFaces == 0, "층·외벽·계단·지붕 면의 법선이 올바르다", ref failures);
            CheckBuildingProps(floorVisibility, ref failures);
            CheckFloorLocator(floorVisibility, ref failures);
            CheckHadanDetails(map, root ? root.transform : null, polygons, ref failures);

        }

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
        CheckRoadDetails(ref failures);
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
        if (!UseAssetBuildings)
        {
            int doorNavMeshSamples = floorVisibility ? floorVisibility.BuiltDoorNavMeshSamples : 0;
            int checkedDoors = floorVisibility ? floorVisibility.BuiltCheckedDoors : 0;
            Check(checkedDoors == 20 && doorNavMeshSamples == checkedDoors,
                "문 중심 0.2m 안에 NavMesh가 있다 (" + doorNavMeshSamples + "/" + checkedDoors + ")", ref failures);
            int stairPaths = floorVisibility ? floorVisibility.BuiltStairPaths : 0;
            Check(stairPaths >= Mathf.Min(5, CountStairBuildings(floorVisibility)),
                "1층 문 앞→꼭대기 층 경로 " + stairPaths + "채가 이어진다", ref failures);
        }

        int triangles = CountUniqueMeshTriangles();
        Debug.Log("[Hadan Check] 씬 전체 고유 메쉬 삼각형 " + triangles.ToString("N0"));
        Check(triangles <= 500000, "씬 고유 메쉬 삼각형이 500,000 이하이다", ref failures);
        int gameObjects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length;
        Check(gameObjects <= 25000, "GameObject 수가 25,000 이하이다 (" + gameObjects + ")", ref failures);
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

    static Texture2D EnsureTexture(string path, int width, int height, Func<int, int, Color> pixel,
        TextureWrapMode wrap = TextureWrapMode.Repeat)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (!texture)
        {
            texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
                { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(texture, path);
        }
        else if (texture.width != width || texture.height != height || texture.mipmapCount == 1)
        {
            texture.Reinitialize(width, height, TextureFormat.RGBA32, true);
        }

        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            pixels[y * width + x] = pixel(x, y);

        texture.SetPixels(pixels);
        texture.wrapMode = wrap;
        texture.filterMode = FilterMode.Point;
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

    static Material HadanSignMaterial(string id, Color color, Texture texture)
    {
        var material = HadanMaterial(id, color, texture);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.22f);
            EditorUtility.SetDirty(material);
        }
        return material;
    }

    static Material HadanFadeMaterial(Material opaque, Dictionary<Material, Material> cache)
    {
        if (cache.TryGetValue(opaque, out var fade)) return fade;

        string path = DataFolder + "/" + opaque.name + "_fade.mat";
        fade = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!fade)
        {
            fade = new Material(opaque) { name = opaque.name + "_fade" };
            // 첫 빌드는 fade 자산이 추가돼 Data 파일 수 검사가 한 번 실패하고, 재빌드부터 안정된다.
            AssetDatabase.CreateAsset(fade, path);
        }
        fade.CopyPropertiesFromMaterial(opaque);
        Color color = opaque.color;
        color.a = 0.3f;
        fade.color = color;
        MakeTransparent(fade);
        EditorUtility.SetDirty(fade);
        cache.Add(opaque, fade);
        return fade;
    }

    static void MakeTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");
    }

    static DetailMaterials CreateDetailMaterials()
    {
        if (UseAssetBuildings)
            return new DetailMaterials
            {
                wire = HadanMaterial("hadan_wire", new Color(0.055f, 0.055f, 0.05f)),
                trunk = HadanMaterial("hadan_tree_trunk", new Color(0.28f, 0.18f, 0.10f)),
                leaves = HadanMaterial("hadan_tree_leaves", new Color(0.18f, 0.34f, 0.16f)),
                pole = HadanMaterial("hadan_utility_pole", new Color(0.25f, 0.25f, 0.24f)),
                car = HadanMaterial("hadan_abandoned_car", new Color(0.28f, 0.23f, 0.20f)),
                carRoof = HadanMaterial("hadan_abandoned_car_roof", new Color(0.18f, 0.18f, 0.17f))
            };
        // 왜: 유형별 창 형태와 줄눈을 텍스처에 함께 구우면 셰이더와 건물별 머티리얼을 늘리지 않는다.
        var commercialTile = EnsureTexture(BuildingTexturePath, 64, 64, (x, y) =>
            y >= 16 && y < 48 ? new Color(0.14f, 0.22f, 0.28f)
            : x % 16 == 0 || y % 8 == 0 ? new Color(0.58f, 0.58f, 0.56f) : Color.white);
        var commercialPlain = EnsureTexture(CommercialPlainTexturePath, 64, 64, (x, y) =>
            y >= 15 && y < 49 ? new Color(0.14f, 0.22f, 0.28f) : Color.white);
        var residentialBrick = EnsureTexture(ResidentialBrickTexturePath, 64, 64, (x, y) =>
        {
            bool window = x >= 20 && x < 44 && y >= 18 && y < 48;
            if (window)
                return x % 8 == 0 || y % 10 == 0 ? new Color(0.08f, 0.09f, 0.10f) : new Color(0.20f, 0.25f, 0.28f);
            int shiftedX = x + (y / 8 % 2) * 8;
            return shiftedX % 16 == 0 || y % 8 == 0 ? new Color(0.50f, 0.48f, 0.44f) : Color.white;
        });
        var residentialPlain = EnsureTexture(ResidentialPlainTexturePath, 64, 64, (x, y) =>
        {
            bool window = x >= 20 && x < 44 && y >= 18 && y < 48;
            return window
                ? x % 8 == 0 || y % 10 == 0 ? new Color(0.08f, 0.09f, 0.10f) : new Color(0.20f, 0.25f, 0.28f)
                : Color.white;
        });
        var apartment = EnsureTexture(ApartmentTexturePath, 64, 64, (x, y) =>
        {
            bool window = (x >= 8 && x < 24 || x >= 40 && x < 56) && y >= 18 && y < 48;
            if (window) return new Color(0.18f, 0.24f, 0.28f);
            return y == 14 || y == 50 ? new Color(0.48f, 0.50f, 0.50f) : Color.white;
        });
        var signTexture = EnsureTexture(SignTexturePath, 64, 16, (x, y) =>
        {
            bool block = y >= 5 && y <= 11 && (x % 13 >= 2 && x % 13 <= 8);
            return block ? Color.white : new Color(0.62f, 0.62f, 0.62f);
        });
        var digits = EnsureTexture(DigitTexturePath, 80, 8, (x, y) =>
        {
            int digit = x / 8;
            int cellX = x % 8;
            int glyphX = (cellX - 1) / 2;
            int glyphY = (y - 1) / 1;
            bool on = cellX >= 1 && glyphX < 3 && glyphY >= 0 && glyphY < 5
                && (DigitRows[digit * 5 + (4 - glyphY)] & (1 << (2 - glyphX))) != 0;
            return on ? new Color(0.10f, 0.16f, 0.22f) : new Color(0.92f, 0.90f, 0.82f);
        }, TextureWrapMode.Clamp);

        var result = new DetailMaterials
        {
            door = HadanMaterial("hadan_door", new Color(0.10f, 0.11f, 0.12f)),
            floor = HadanMaterial("hadan_floor", new Color(0.64f, 0.62f, 0.57f)),
            stair = HadanMaterial("hadan_stair", new Color(0.52f, 0.51f, 0.48f)),
            interiorWalls = new[]
            {
                new[]
                {
                    HadanMaterial("hadan_commercial_inner_warm", new Color(0.88f, 0.85f, 0.78f)),
                    HadanMaterial("hadan_commercial_inner_cool", new Color(0.82f, 0.86f, 0.86f))
                },
                new[]
                {
                    HadanMaterial("hadan_apartment_inner_warm", new Color(0.90f, 0.87f, 0.80f)),
                    HadanMaterial("hadan_apartment_inner_cool", new Color(0.84f, 0.87f, 0.88f))
                },
                new[]
                {
                    HadanMaterial("hadan_house_inner_warm", new Color(0.91f, 0.86f, 0.77f)),
                    HadanMaterial("hadan_house_inner_cool", new Color(0.85f, 0.89f, 0.86f))
                },
                new[]
                {
                    HadanMaterial("hadan_mixed_inner_warm", new Color(0.88f, 0.83f, 0.75f)),
                    HadanMaterial("hadan_mixed_inner_cool", new Color(0.82f, 0.85f, 0.86f))
                }
            },
            commercialWalls = new[]
            {
                HadanMaterial("hadan_commercial_beige_tile", new Color(0.76f, 0.69f, 0.56f), commercialTile),
                HadanMaterial("hadan_commercial_gray_stone", new Color(0.60f, 0.61f, 0.60f), commercialTile),
                HadanMaterial("hadan_commercial_curtainwall", new Color(0.45f, 0.58f, 0.65f), commercialPlain),
                HadanMaterial("hadan_commercial_white", new Color(0.85f, 0.84f, 0.80f), commercialPlain)
            },
            mixedUseWalls = new[]
            {
                HadanMaterial("hadan_mixed_red_brick", new Color(0.62f, 0.31f, 0.23f), residentialBrick),
                HadanMaterial("hadan_mixed_brown_brick", new Color(0.48f, 0.31f, 0.23f), residentialBrick),
                HadanMaterial("hadan_mixed_beige", new Color(0.78f, 0.70f, 0.58f), residentialPlain),
                HadanMaterial("hadan_mixed_gray_tile", new Color(0.59f, 0.61f, 0.61f), residentialBrick),
                HadanMaterial("hadan_mixed_dark_panel", new Color(0.29f, 0.31f, 0.32f), residentialPlain)
            },
            houseWalls = new[]
            {
                HadanMaterial("hadan_house_white", new Color(0.88f, 0.87f, 0.83f), residentialPlain),
                HadanMaterial("hadan_house_cream", new Color(0.86f, 0.78f, 0.64f), residentialPlain),
                HadanMaterial("hadan_house_red_brick", new Color(0.61f, 0.31f, 0.23f), residentialBrick)
            },
            apartmentWalls = new[]
            {
                HadanMaterial("hadan_apartment_white", new Color(0.88f, 0.89f, 0.87f), apartment),
                HadanMaterial("hadan_apartment_ivory", new Color(0.86f, 0.82f, 0.72f), apartment)
            },
            commercialRoof = HadanMaterial("hadan_roof", new Color(0.22f, 0.23f, 0.25f)),
            residentialRoofs = new[]
            {
                HadanMaterial("hadan_roof_green_1", new Color(0.16f, 0.38f, 0.24f)),
                HadanMaterial("hadan_roof_green_2", new Color(0.22f, 0.46f, 0.27f))
            },
            shopfront = HadanMaterial("hadan_shopfront", new Color(0.10f, 0.16f, 0.19f)),
            balcony = HadanMaterial("hadan_balcony", new Color(0.38f, 0.40f, 0.40f)),
            apartmentStripe = HadanMaterial("hadan_apartment_stripe", new Color(0.24f, 0.39f, 0.52f)),
            roofRoom = HadanMaterial("hadan_roof_room", new Color(0.67f, 0.65f, 0.57f)),
            roofRoomDoor = HadanMaterial("hadan_roof_room_door", new Color(0.16f, 0.18f, 0.17f)),
            tanks = new[]
            {
                HadanMaterial("hadan_water_tank_blue", new Color(0.18f, 0.38f, 0.58f)),
                HadanMaterial("hadan_water_tank_green", new Color(0.20f, 0.46f, 0.31f))
            },
            airConditioner = HadanMaterial("hadan_air_conditioner", new Color(0.67f, 0.68f, 0.66f)),
            signMaterials = new[]
            {
                HadanSignMaterial("hadan_sign_red", new Color(0.75f, 0.13f, 0.10f), signTexture),
                HadanSignMaterial("hadan_sign_blue", new Color(0.10f, 0.30f, 0.68f), signTexture),
                HadanSignMaterial("hadan_sign_yellow", new Color(0.90f, 0.68f, 0.10f), signTexture),
                HadanSignMaterial("hadan_sign_green", new Color(0.12f, 0.52f, 0.24f), signTexture),
                HadanSignMaterial("hadan_sign_white", new Color(0.88f, 0.87f, 0.82f), signTexture),
                HadanSignMaterial("hadan_sign_orange", new Color(0.90f, 0.38f, 0.08f), signTexture)
            },
            apartmentNumber = HadanMaterial("hadan_apartment_number", Color.white, digits),
            wire = HadanMaterial("hadan_wire", new Color(0.055f, 0.055f, 0.05f)),
            trunk = HadanMaterial("hadan_tree_trunk", new Color(0.28f, 0.18f, 0.10f)),
            leaves = HadanMaterial("hadan_tree_leaves", new Color(0.18f, 0.34f, 0.16f)),
            pole = HadanMaterial("hadan_utility_pole", new Color(0.25f, 0.25f, 0.24f)),
            car = HadanMaterial("hadan_abandoned_car", new Color(0.28f, 0.23f, 0.20f)),
            carRoof = HadanMaterial("hadan_abandoned_car_roof", new Color(0.18f, 0.18f, 0.17f))
        };
        result.CreateBatches();
        return result;
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

    static AssetModel[] LoadAssetModels()
    {
        string[] names = { "hadan_bld_00", "hadan_bld_01", "hadan_bld_02", "hadan_bld_03", "hadan_bld_04",
            "hadan_bld_05", "hadan_bld_06", "hadan_bld_07", "hadan_bld_08", "hadan_bld_09",
            "hadan_bld_10", "hadan_bld_11", "hadan_bld_12",
            "hadan_shop_1", "hadan_shop_2", "hadan_shop_4" };
        var assets = new AssetModel[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string path = BuildingPackFolder + "/" + names[i] + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!model) throw new InvalidOperationException("건물 에셋이 없습니다: " + path);
            var bounds = ModelBounds(model.transform, model.transform.parent);
            if (bounds.size.x <= Epsilon || bounds.size.y <= Epsilon || bounds.size.z <= Epsilon)
                throw new InvalidOperationException("건물 에셋 크기가 유효하지 않습니다: " + path);
            assets[i] = new AssetModel { model = model, bounds = bounds, path = path };
        }
        if (!Shader.Find("Universal Render Pipeline/Lit"))
            throw new InvalidOperationException("URP Lit 셰이더가 없습니다.");
        return assets;
    }

    // FBX 루트의 X=-90° 회전도 포함하되, 부모 좌표계의 X·Y·Z 크기를 구한다.
    static Bounds ModelBounds(Transform model, Transform relativeTo)
    {
        var bounds = new Bounds();
        bool first = true;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.sharedMesh) continue;
            var meshBounds = filter.sharedMesh.bounds;
            var matrix = relativeTo ? relativeTo.worldToLocalMatrix * filter.transform.localToWorldMatrix
                : filter.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1,
                        (corner & 4) == 0 ? -1 : 1));
                point = matrix.MultiplyPoint3x4(point);
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
        }
        return bounds;
    }

    static BuildingBox MinimumBuildingBox(List<Vector2> polygon)
    {
        var best = new BuildingBox();
        float bestArea = float.PositiveInfinity;
        for (int edge = 0; edge < polygon.Count; edge++)
        {
            Vector2 direction = polygon[(edge + 1) % polygon.Count] - polygon[edge];
            if (direction.sqrMagnitude <= Epsilon * Epsilon) continue;
            direction.Normalize();
            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < polygon.Count; i++)
            {
                var point = new Vector2(Vector2.Dot(polygon[i], direction), Vector2.Dot(polygon[i], perpendicular));
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            Vector2 size = max - min;
            float area = size.x * size.y;
            if (area >= bestArea) continue;
            bestArea = area;
            best = new BuildingBox
            {
                center = direction * ((min.x + max.x) * 0.5f) + perpendicular * ((min.y + max.y) * 0.5f),
                forward = perpendicular, width = size.x, depth = size.y
            };
        }
        if (best.width <= Epsilon || best.depth <= Epsilon)
            throw new InvalidOperationException("건물 OBB가 유효하지 않습니다.");
        return best;
    }

    static Vector2 NearestRoadDirection(Vector2 center, List<RoadInfo> roads)
    {
        Vector2 nearest = default;
        float bestDistance = float.PositiveInfinity;
        for (int road = 0; road < roads.Count; road++)
            for (int i = 0; i + 1 < roads[road].points.Count; i++)
            {
                Vector2 from = roads[road].points[i];
                Vector2 segment = roads[road].points[i + 1] - from;
                float t = segment.sqrMagnitude <= Epsilon * Epsilon ? 0f
                    : Mathf.Clamp01(Vector2.Dot(center - from, segment) / segment.sqrMagnitude);
                Vector2 point = from + segment * t;
                float distance = (point - center).sqrMagnitude;
                if (distance >= bestDistance) continue;
                nearest = point;
                bestDistance = distance;
            }
        if (float.IsPositiveInfinity(bestDistance) || bestDistance <= Epsilon * Epsilon)
            throw new InvalidOperationException("가장 가까운 도로의 방향을 정할 수 없습니다: " + center);
        return (nearest - center).normalized;
    }

    static BuildingBox FaceNearestRoad(BuildingBox box, List<RoadInfo> roads)
    {
        Vector2 toRoad = NearestRoadDirection(box.center, roads);
        Vector2 right = new Vector2(box.forward.y, -box.forward.x);
        if (Mathf.Abs(Vector2.Dot(toRoad, right)) > Mathf.Abs(Vector2.Dot(toRoad, box.forward)))
        {
            float width = box.width;
            box.width = box.depth;
            box.depth = width;
            box.forward = Vector2.Dot(toRoad, right) >= 0f ? right : -right;
        }
        else if (Vector2.Dot(toRoad, box.forward) < 0f) box.forward = -box.forward;
        return box;
    }

    static int SelectBuildingAsset(BuildingKind kind, BuildingBox box, AssetModel[] assets, long id, int floors)
    {
        int[] candidates = kind == BuildingKind.Commercial ? new[] { 0, 2, 6, 7, 11 }
            : kind == BuildingKind.MixedUse ? new[] { 1, 3, 0, 9, 5, 6 }
            : kind == BuildingKind.House ? new[] { 4, 3, 5, 10, 8 } : new[] { 0, 12 };
        var fitting = new List<int>();
        var closeFloors = new List<int>();
        foreach (int candidate in candidates)
        {
            Vector3 size = assets[candidate].bounds.size;
            float sx = box.width / size.x;
            float sz = box.depth / size.z;
            if (sx > sz * MaxAssetDistortion || sz > sx * MaxAssetDistortion) continue;
            fitting.Add(candidate);
            if (Mathf.Abs(Mathf.RoundToInt(size.y / FloorHeight) - floors) < 3) closeFloors.Add(candidate);
        }
        // 층수 차이로 후보가 사라지면 잘림 없는 후보 전체를 사용한다.
        var choices = closeFloors.Count > 0 ? closeFloors : fitting;
        if (choices.Count > 0) return choices[RoadRandom(id, 71).Next(choices.Count)];
        int start = RoadRandom(id, 71).Next(candidates.Length);
        int best = candidates[start];
        float bestDistortion = float.PositiveInfinity;
        for (int i = 0; i < candidates.Length; i++)
        {
            int candidate = candidates[(start + i) % candidates.Length];
            Vector3 size = assets[candidate].bounds.size;
            float sx = box.width / size.x;
            float sz = box.depth / size.z;
            float distortion = Mathf.Max(sx / sz, sz / sx)
                + Mathf.Abs(Mathf.RoundToInt(size.y / FloorHeight) - floors);
            if (distortion >= bestDistortion) continue;
            best = candidate;
            bestDistortion = distortion;
        }
        return best;
    }

    static bool HasOwnSign(int asset)
        => asset == 0 || asset == 6 || asset == 7 || asset == 8 || asset == 9 || asset == 11;

    static Vector3 FitBuildingAsset(BuildingBox box, Vector3 nativeSize, float height, out bool clipped)
    {
        float sx = box.width / nativeSize.x;
        float sz = box.depth / nativeSize.z;
        clipped = sx > sz * MaxAssetDistortion || sz > sx * MaxAssetDistortion;
        // 균일 확대는 허용하고, 더 늘어난 축만 줄여 비워 둔다.
        if (sx > sz * MaxAssetDistortion) sx = sz * MaxAssetDistortion;
        if (sz > sx * MaxAssetDistortion) sz = sx * MaxAssetDistortion;
        return new Vector3(sx, height / nativeSize.y, sz);
    }

    static List<AssetBuildingPlan> AssetBuildingPlans(MapData map, List<RoadInfo> roads, AssetModel[] assets, out int skipped)
    {
        var plans = new List<AssetBuildingPlan>();
        skipped = 0;
        foreach (var element in map.elements)
        {
            if (element.tags == null || string.IsNullOrEmpty(element.tags.building)) continue;
            var polygon = MakePolygon(element.geometry);
            if (polygon == null)
            {
                Debug.LogWarning("[Hadan Asset] 건물 " + element.id + "의 외곽선이 유효하지 않아 건너뜁니다.");
                skipped++;
                continue;
            }
            float area = Mathf.Abs(SignedArea(polygon)) / (CityScale * CityScale);
            int floors = FloorCount(element.tags, area);
            var kind = ClassifyBuilding(element.tags, polygon, floors, area, roads);
            var box = MinimumBuildingBox(polygon);
            try { box = FaceNearestRoad(box, roads); }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning("[Hadan Asset] 건물 " + element.id + "을 건너뜁니다: " + exception.Message);
                skipped++;
                continue;
            }
            int asset = SelectBuildingAsset(kind, box, assets, element.id, floors);
            float targetHeight = floors * FloorHeight;
            float nativeHeight = assets[asset].bounds.size.y;
            float height = Mathf.Clamp(targetHeight, nativeHeight * 0.75f, nativeHeight * 1.35f);
            Vector3 scale = FitBuildingAsset(box, assets[asset].bounds.size, height, out bool clipped);
            box.width = assets[asset].bounds.size.x * scale.x;
            box.depth = assets[asset].bounds.size.z * scale.z;
            plans.Add(new AssetBuildingPlan { id = element.id, kind = kind, box = box, asset = asset,
                height = height, scale = scale, clipped = clipped, heightClipped = height != targetHeight });
        }
        return plans;
    }

    static List<Vector2> BuildingBoxPolygon(BuildingBox box)
    {
        Vector2 front = box.forward * (box.depth * 0.5f);
        Vector2 side = new Vector2(box.forward.y, -box.forward.x) * (box.width * 0.5f);
        return new List<Vector2> { box.center - side - front, box.center - side + front,
            box.center + side + front, box.center + side - front };
    }

    static string AssetMaterialKey(Material source)
    {
        if (!source || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
            throw new InvalidOperationException("원본 에셋 재질 식별자가 없습니다.");
        return guid + "_" + localId.ToString(CultureInfo.InvariantCulture);
    }

    static Dictionary<Material, AssetMaterialPair> CreateAssetMaterials(AssetModel[] assets)
    {
        if (!AssetDatabase.IsValidFolder(AssetMaterialFolder)) AssetDatabase.CreateFolder(DataFolder, "AssetMaterials");
        var result = new Dictionary<Material, AssetMaterialPair>();
        foreach (var asset in assets)
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(asset.path))
            {
                if (!(source is Material material) || result.ContainsKey(material)) continue;
                string key = AssetMaterialKey(material);
                var opaque = AssetDatabase.LoadAssetAtPath<Material>(AssetMaterialFolder + "/" + key + "_opaque.mat");
                if (!opaque)
                {
                    opaque = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = key + "_opaque" };
                    AssetDatabase.CreateAsset(opaque, AssetMaterialFolder + "/" + key + "_opaque.mat");
                }
                opaque.CopyPropertiesFromMaterial(material);
                opaque.shader = Shader.Find("Universal Render Pipeline/Lit");
                opaque.name = key + "_opaque";
                opaque.mainTexture = material.mainTexture;
                opaque.mainTextureScale = material.mainTextureScale;
                opaque.mainTextureOffset = material.mainTextureOffset;
                opaque.color = material.color;
                opaque.SetFloat("_Surface", 0f);
                opaque.SetFloat("_AlphaClip", 0f);
                opaque.SetFloat("_SrcBlend", (float)BlendMode.One);
                opaque.SetFloat("_DstBlend", (float)BlendMode.Zero);
                opaque.SetFloat("_ZWrite", 1f);
                opaque.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                opaque.DisableKeyword("_ALPHATEST_ON");
                opaque.SetOverrideTag("RenderType", "Opaque");
                opaque.SetShaderPassEnabled("ShadowCaster", true);
                opaque.renderQueue = (int)RenderQueue.Geometry;
                opaque.enableInstancing = true;
                var fade = AssetDatabase.LoadAssetAtPath<Material>(AssetMaterialFolder + "/" + key + "_fade.mat");
                if (!fade)
                {
                    fade = new Material(opaque) { name = key + "_fade" };
                    AssetDatabase.CreateAsset(fade, AssetMaterialFolder + "/" + key + "_fade.mat");
                }
                fade.CopyPropertiesFromMaterial(opaque);
                fade.shader = opaque.shader;
                fade.name = key + "_fade";
                MakeTransparent(fade);
                EditorUtility.SetDirty(opaque);
                EditorUtility.SetDirty(fade);
                result.Add(material, new AssetMaterialPair { opaque = opaque, fade = fade });
            }
        return result;
    }

    static Transform CreateAssetVisual(Transform parent, string name, AssetModel asset, Vector3 scale,
        Vector3 bottomCenter, Dictionary<Material, AssetMaterialPair> materials)
    {
        var visual = new GameObject(name).transform;
        visual.SetParent(parent, false);
        visual.localScale = scale;
        Vector3 sourceBottomCenter = new Vector3(asset.bounds.center.x, asset.bounds.min.y, asset.bounds.center.z);
        visual.localPosition = bottomCenter - Vector3.Scale(sourceBottomCenter, scale);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset.model, visual);
        model.transform.localPosition = asset.model.transform.localPosition;
        model.transform.localRotation = asset.model.transform.localRotation;
        model.transform.localScale = asset.model.transform.localScale;
        foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] shared = renderer.sharedMaterials;
            for (int slot = 0; slot < shared.Length; slot++) shared[slot] = materials[shared[slot]].opaque;
            renderer.sharedMaterials = shared;
        }
        foreach (var transform in model.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 0;
        return visual;
    }

    static List<List<Vector2>> CreateAssetBuildings(MapData map, List<RoadInfo> roads, Transform root,
        AssetModel[] assets, PropCounts counts, out int count, out float maxHeight)
    {
        var plans = AssetBuildingPlans(map, roads, assets, out counts.skippedAssets);
        var materials = CreateAssetMaterials(assets);
        var fades = new Dictionary<Material, Material>();
        foreach (var pair in materials.Values) fades.Add(pair.opaque, pair.fade);
        var polygons = new List<List<Vector2>>(plans.Count);
        maxHeight = 0f;
        foreach (var plan in plans)
        {
            var building = new GameObject("Building_" + plan.id).transform;
            building.SetParent(root, false);
            building.position = new Vector3(plan.box.center.x, 0f, plan.box.center.y);
            building.rotation = Quaternion.LookRotation(new Vector3(plan.box.forward.x, 0f, plan.box.forward.y), Vector3.up)
                * Quaternion.Inverse(Quaternion.LookRotation(AssetFront, Vector3.up));
            var collider = building.gameObject.AddComponent<BoxCollider>();
            collider.center = Vector3.up * (plan.height * 0.5f);
            collider.size = new Vector3(plan.box.width, plan.height, plan.box.depth);
            // 콜라이더를 수집하는 기존 굽기에서 지붕 보행면만 제외한다.
            var modifier = building.gameObject.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            modifier.overrideArea = true;
            modifier.area = NavMesh.GetAreaFromName("Not Walkable");
            CreateAssetVisual(building, "AssetVisual", assets[plan.asset], plan.scale, Vector3.zero, materials);
            if (!HasOwnSign(plan.asset) && (plan.kind == BuildingKind.MixedUse || plan.kind == BuildingKind.House)
                && plan.box.width >= 4f)
            {
                int shopIndex = 13 + RoadRandom(plan.id, 73).Next(3);
                var shop = assets[shopIndex];
                int shopCount = plan.box.width >= shop.bounds.size.x * 2f ? 2 : 1;
                float width = Mathf.Min(shop.bounds.size.x, plan.box.width / shopCount);
                Vector3 shopScale = new Vector3(width / shop.bounds.size.x, FloorHeight / shop.bounds.size.y, 1f);
                for (int i = 0; i < shopCount; i++)
                {
                    // 두께는 건물 안에 묻고, 앞쪽 경계만 외벽보다 5cm 돌출시킨다.
                    Vector3 position = new Vector3((i - (shopCount - 1) * 0.5f) * width, 0f,
                        plan.box.depth * 0.5f + 0.05f - shop.bounds.size.z * 0.5f);
                    CreateAssetVisual(building, "Shopfront_" + i, shop, shopScale, position, materials);
                    counts.shopfronts++;
                    counts.assets[shopIndex]++;
                }
            }
            var renderers = building.GetComponentsInChildren<Renderer>(true);
            var fadeSets = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] shared = renderers[i].sharedMaterials;
                fadeSets[i] = new Material[shared.Length];
                for (int slot = 0; slot < shared.Length; slot++) fadeSets[i][slot] = fades[shared[slot]];
            }
            building.gameObject.AddComponent<WallSegment>().BindAssetVisuals(renderers, fadeSets);
            polygons.Add(BuildingBoxPolygon(plan.box));
            counts.types[(int)plan.kind]++;
            counts.assets[plan.asset]++;
            if (plan.clipped) counts.clippedAssets++;
            if (plan.heightClipped) counts.heightClippedAssets++;
            maxHeight = Mathf.Max(maxHeight, plan.height);
        }
        V01SceneBuilder.MarkStatic(root);
        count = plans.Count;
        return polygons;
    }

    static Dictionary<long, List<Vector2>> AssetBuildingPolygons(Transform root)
    {
        var result = new Dictionary<long, List<Vector2>>();
        if (!root) return result;
        for (int i = 0; i < root.childCount; i++)
        {
            var building = root.GetChild(i);
            var collider = building.GetComponent<BoxCollider>();
            if (!collider || !TryBuildingId(building.name, out long id)) continue;
            var points = new List<Vector2>();
            foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(-1, 1),
                         new Vector2(1, 1), new Vector2(1, -1) })
                points.Add(Xz(building.TransformPoint(collider.center + new Vector3(
                    corner.x * collider.size.x * 0.5f, 0f, corner.y * collider.size.z * 0.5f))));
            result[id] = points;
        }
        return result;
    }

    static string AssetMaterialFiles()
    {
        if (!Directory.Exists(AssetMaterialFolder)) return "";
        string[] files = Directory.GetFiles(AssetMaterialFolder, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        return string.Join("\n", files);
    }

    static void RecordAssetMaterialFiles()
    {
        string files = AssetMaterialFiles();
        int rebuilds = files == EditorPrefs.GetString(AssetMaterialFilesKey, "")
            ? EditorPrefs.GetInt(AssetMaterialRebuildsKey, 0) + 1 : 0;
        EditorPrefs.SetString(AssetMaterialFilesKey, files);
        EditorPrefs.SetInt(AssetMaterialRebuildsKey, rebuilds);
    }

    static int CountInstanceMeshTriangles()
    {
        int triangles = 0;
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
            if (filter.sharedMesh)
                for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                    triangles += (int)filter.sharedMesh.GetIndexCount(i) / 3;
        return triangles;
    }

    static List<List<Vector2>> CreateBuildings(MapData map, List<RoadInfo> roads,
        DetailMaterials details, List<Mesh> meshes, PropCounts props, AssetModel[] assets,
        out int count, out float maxHeight, out int stairBuildings, out int firstFloorOnly,
        out int totalFloors, out BuildingFloors floorVisibility)
    {
        var root = new GameObject("Buildings").transform;
        floorVisibility = null;
        if (UseAssetBuildings)
        {
            stairBuildings = firstFloorOnly = totalFloors = 0;
            return CreateAssetBuildings(map, roads, root, assets, props, out count, out maxHeight);
        }
        floorVisibility = root.gameObject.AddComponent<BuildingFloors>();
        var polygons = new List<List<Vector2>>();
        var entries = new List<BuildingFloors.Entry>();
        var fadeMaterials = new Dictionary<Material, Material>();
        count = 0;
        maxHeight = 0f;
        stairBuildings = 0;
        firstFloorOnly = 0;
        totalFloors = 0;
        int apartmentIndex = 0;

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

            float area = Mathf.Abs(SignedArea(polygon)) / (CityScale * CityScale);
            int floors = FloorCount(element.tags, area);
            BuildingKind kind = ClassifyBuilding(element.tags, polygon, floors, area, roads);
            float height = floors * FloorHeight;
            int streetEdge = FindStreetEdge(polygon, roads, kind == BuildingKind.Commercial);
            Vector2 doorA = polygon[streetEdge];
            Vector2 doorB = polygon[(streetEdge + 1) % polygon.Count];
            Vector2 doorEdge = doorB - doorA;
            Vector2 doorOutward = new Vector2(-doorEdge.y, doorEdge.x).normalized;
            Vector2 doorCenter = (doorA + doorB) * 0.5f - doorOutward * (WallThickness * 0.5f);
            StairPlan stair = default;
            bool hasStairs = floors > 1 && TryStairPlan(polygon, doorCenter, out stair);

            var go = new GameObject("Building_" + element.id);
            go.transform.SetParent(root, false);
            var random = RoadRandom(element.id, 7);
            Material[] wallPalette = kind == BuildingKind.Commercial ? details.commercialWalls
                : kind == BuildingKind.Apartment ? details.apartmentWalls
                : kind == BuildingKind.House ? details.houseWalls : details.mixedUseWalls;
            Material wall = wallPalette[random.Next(wallPalette.Length)];
            Material roof = kind == BuildingKind.Commercial ? details.commercialRoof
                : details.residentialRoofs[random.Next(details.residentialRoofs.Length)];
            Material interior = details.interiorWalls[(int)kind][random.Next(2)];
            Material wallFade = HadanFadeMaterial(wall, fadeMaterials);
            Material roofFade = HadanFadeMaterial(roof, fadeMaterials);

            var entry = CreateBuildingStructure(go.transform, element.id, polygon, floors, streetEdge,
                hasStairs, stair, wall, wallFade, interior, roof, roofFade, details, meshes, props);
            if (entry == null)
            {
                UnityEngine.Object.DestroyImmediate(go);
                Debug.LogWarning("[Hadan] 건물 " + element.id + "의 층 삼각분할에 실패해 건너뜁니다.");
                continue;
            }

            CreateBuildingDetails(go.transform, entry.floors, entry.roof.transform, element.id, polygon, floors, height, kind,
                kind == BuildingKind.Apartment ? 101 + apartmentIndex++ : 0,
                streetEdge, details, meshes, props);

            polygons.Add(polygon);
            entries.Add(entry);
            props.types[(int)kind]++;
            count++;
            totalFloors += floors;
            if (hasStairs) stairBuildings++;
            else if (floors > 1) firstFloorOnly++;
            maxHeight = Mathf.Max(maxHeight, height);
        }

        details.EmitBatches(meshes);
        floorVisibility.Setup(entries.ToArray(), FloorHeight);
        return polygons;
    }

    static BuildingFloors.Entry CreateBuildingStructure(Transform building, long id,
        List<Vector2> polygon, int floors, int streetEdge, bool hasStairs, StairPlan stair,
        Material wall, Material wallFade, Material interior, Material roof, Material roofFade,
        DetailMaterials details, List<Mesh> meshes, PropCounts counts)
    {
        var floorObjects = new GameObject[floors];
        float perimeter = 0f;
        var edgeU = new float[polygon.Count];
        for (int edge = 0; edge < polygon.Count; edge++)
        {
            edgeU[edge] = perimeter;
            perimeter += Vector2.Distance(polygon[edge], polygon[(edge + 1) % polygon.Count]);
        }

        for (int floor = 0; floor < floors; floor++)
        {
            var mesh = CreateFloorMesh(id, floor, polygon, streetEdge, hasStairs, stair);
            if (!mesh) return null;
            meshes.Add(mesh);

            var floorObject = new GameObject("Floor_" + floor,
                typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            floorObject.transform.SetParent(building, false);
            floorObject.GetComponent<MeshFilter>().sharedMesh = mesh;
            floorObject.GetComponent<MeshRenderer>().sharedMaterials = new[] { details.floor, interior };
            floorObject.GetComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(floorObject, StaticEditorFlags.BatchingStatic);
            floorObjects[floor] = floorObject;

            for (int edge = 0; edge < polygon.Count; edge++)
            {
                Vector2 a = polygon[edge];
                Vector2 b = polygon[(edge + 1) % polygon.Count];
                float length = Vector2.Distance(a, b);
                bool doorEdge = floor == 0 && edge == streetEdge;
                var spans = WallSpans(length, doorEdge);
                for (int span = 0; span < spans.Count; span++)
                    CreateExteriorWall(floorObject.transform, id, floor, edge, span, a, b,
                        spans[span], edgeU[edge], wall, wallFade, meshes);
            }

            if (hasStairs && floor < floors - 1)
                CreateStair(floorObject.transform, id, floor, stair, details.stair, interior, meshes);
        }

        Vector2 doorA = polygon[streetEdge];
        Vector2 doorB = polygon[(streetEdge + 1) % polygon.Count];
        Vector2 doorEdgeVector = doorB - doorA;
        Vector2 outward = new Vector2(-doorEdgeVector.y, doorEdgeVector.x).normalized;
        Vector2 doorCenter = (doorA + doorB) * 0.5f - outward * (WallThickness * 0.5f);
        CreateDoor(floorObjects[0].transform, doorCenter, outward, details.door);
        counts.doors++;

        var roofMesh = CreateRoofMesh(id, polygon, floors * FloorHeight);
        if (!roofMesh) return null;
        meshes.Add(roofMesh);
        var roofObject = new GameObject("Roof", typeof(MeshFilter), typeof(MeshRenderer),
            typeof(MeshCollider), typeof(WallSegment));
        roofObject.transform.SetParent(building, false);
        roofObject.GetComponent<MeshFilter>().sharedMesh = roofMesh;
        roofObject.GetComponent<MeshRenderer>().sharedMaterial = roof;
        roofObject.GetComponent<MeshCollider>().sharedMesh = roofMesh;
        roofObject.GetComponent<WallSegment>().Setup(roof, roofFade, true);
        GameObjectUtility.SetStaticEditorFlags(roofObject, StaticEditorFlags.BatchingStatic);

        StairPlan topStair = hasStairs ? stair.Flight(floors - 2) : default;
        Vector2 top = hasStairs
            ? topStair.start + topStair.direction * (StairSteps * StairTread + 0.45f)
            : doorCenter - outward;
        return new BuildingFloors.Entry
        {
            root = building.gameObject,
            polygon = polygon.ToArray(),
            floors = floorObjects,
            roof = roofObject,
            hasStairs = hasStairs,
            doorFront = new Vector3(doorCenter.x + outward.x, 0f, doorCenter.y + outward.y),
            topLanding = new Vector3(top.x, (floors - 1) * FloorHeight, top.y)
        };
    }

    static List<WallSpan> WallSpans(float length, bool door)
    {
        var spans = new List<WallSpan>();
        if (!door)
        {
            spans.Add(new WallSpan(0f, length, 0f, FloorHeight));
            return spans;
        }

        float openingWidth = DoorWidth + DoorJambClearance * 2f;
        float left = Mathf.Max(0f, (length - openingWidth) * 0.5f);
        float right = Mathf.Min(length, (length + openingWidth) * 0.5f);
        if (left > Epsilon) spans.Add(new WallSpan(0f, left, 0f, FloorHeight));
        if (right < length - Epsilon) spans.Add(new WallSpan(right, length, 0f, FloorHeight));
        spans.Add(new WallSpan(left, right, DoorHeight, FloorHeight));
        return spans;
    }

    static void CreateExteriorWall(Transform parent, long id, int floor, int edge, int spanIndex,
        Vector2 a, Vector2 b, WallSpan span, float edgeU, Material wall, Material fade, List<Mesh> meshes)
    {
        Vector2 tangent = (b - a).normalized;
        Vector2 outward = new Vector2(-tangent.y, tangent.x);
        Vector2 p = a + tangent * span.start;
        Vector2 q = a + tangent * span.end;
        Vector2 innerP = p - outward * WallThickness;
        Vector2 innerQ = q - outward * WallThickness;
        float bottom = floor * FloorHeight + span.bottom;
        float top = floor * FloorHeight + span.top;

        var vertices = new List<Vector3>
        {
            new Vector3(p.x, bottom, p.y), new Vector3(q.x, bottom, q.y),
            new Vector3(q.x, top, q.y), new Vector3(p.x, top, p.y),
            new Vector3(innerP.x, bottom, innerP.y), new Vector3(innerQ.x, bottom, innerQ.y),
            new Vector3(innerQ.x, top, innerQ.y), new Vector3(innerP.x, top, innerP.y)
        };
        float u0 = (edgeU + span.start) / 3f;
        float u1 = (edgeU + span.end) / 3f;
        var uvs = new List<Vector2>
        {
            new Vector2(u0, bottom / FloorHeight), new Vector2(u1, bottom / FloorHeight),
            new Vector2(u1, top / FloorHeight), new Vector2(u0, top / FloorHeight),
            Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero
        };
        int[] triangles =
        {
            0, 1, 2, 0, 2, 3, 5, 4, 7, 5, 7, 6,
            4, 0, 3, 4, 3, 7, 1, 5, 6, 1, 6, 2,
            3, 2, 6, 3, 6, 7, 4, 5, 1, 4, 1, 0
        };
        var mesh = new Mesh { name = "HadanWall_" + id + "_" + floor + "_" + edge + "_" + spanIndex };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshes.Add(mesh);

        var go = new GameObject("ExteriorWall_" + edge + "_" + spanIndex,
            typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider), typeof(WallSegment));
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = wall;
        go.GetComponent<MeshCollider>().sharedMesh = mesh;
        go.GetComponent<WallSegment>().Setup(wall, fade, true);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    static void CreateDoor(Transform parent, Vector2 center, Vector2 outward, Material material)
    {
        var frame = new GameObject("Door").transform;
        frame.SetParent(parent, false);
        frame.position = new Vector3(center.x, DoorHeight * 0.5f, center.y);
        frame.rotation = Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.y));

        var hinge = new GameObject("Hinge").transform;
        hinge.SetParent(frame, false);
        hinge.localPosition = new Vector3(-DoorWidth * 0.5f, 0f, 0f);

        var pane = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pane.name = "Pane";
        pane.transform.SetParent(hinge, false);
        pane.transform.localPosition = new Vector3(DoorWidth * 0.5f, 0f, 0f);
        pane.transform.localScale = new Vector3(DoorWidth, DoorHeight, 0.08f);
        pane.GetComponent<Renderer>().sharedMaterial = material;
        var obstacle = pane.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;

        var opening = frame.gameObject.AddComponent<WindowFrame>();
        opening.Setup(hinge, pane.GetComponent<Collider>(), DoorWidth, "문", false);
        opening.SetupBreakRule(3, 6f);
    }

    static void CreateStair(Transform parent, long id, int floor, StairPlan plan,
        Material stairMaterial, Material wallMaterial, List<Mesh> meshes)
    {
        plan = plan.Flight(floor);
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var steps = new List<int>();
        var walls = new List<int>();
        float stepHeight = FloorHeight / StairSteps;
        float halfWidth = StairWidth * 0.5f;
        // 왜: 바닥 윗면에서 첫 단까지 열린 진입면을 만들어 NavMesh가 옆벽에 끊기지 않게 한다.
        AddQuad(vertices, uvs, steps,
            new Vector3(-halfWidth, 0f, -StairEntryDepth), new Vector3(-halfWidth, 0f, 0f),
            new Vector3(halfWidth, 0f, 0f), new Vector3(halfWidth, 0f, -StairEntryDepth));
        for (int step = 0; step < StairSteps; step++)
        {
            float z0 = step * StairTread;
            float z1 = (step + 1) * StairTread;
            float y = (step + 1) * stepHeight;
            AddQuad(vertices, uvs, steps,
                new Vector3(-halfWidth, y, z0), new Vector3(-halfWidth, y, z1),
                new Vector3(halfWidth, y, z1), new Vector3(halfWidth, y, z0));
            AddQuad(vertices, uvs, steps,
                new Vector3(-halfWidth, step * stepHeight, z0), new Vector3(-halfWidth, y, z0),
                new Vector3(halfWidth, y, z0), new Vector3(halfWidth, step * stepHeight, z0));
        }

        float run = StairSteps * StairTread;
        AddQuad(vertices, uvs, walls,
            new Vector3(-halfWidth, 0f, 0f), new Vector3(-halfWidth, FloorHeight + 1f, 0f),
            new Vector3(-halfWidth, FloorHeight + 1f, run), new Vector3(-halfWidth, 0f, run));
        AddQuad(vertices, uvs, walls,
            new Vector3(halfWidth, 0f, run), new Vector3(halfWidth, FloorHeight + 1f, run),
            new Vector3(halfWidth, FloorHeight + 1f, 0f), new Vector3(halfWidth, 0f, 0f));

        var mesh = new Mesh { name = "HadanStair_" + id + "_" + floor };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(steps, 0);
        mesh.SetTriangles(walls, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshes.Add(mesh);

        var go = new GameObject("Stair_" + floor, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(plan.start.x, floor * FloorHeight, plan.start.y);
        go.transform.rotation = Quaternion.LookRotation(new Vector3(plan.direction.x, 0f, plan.direction.y));
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterials = new[] { stairMaterial, wallMaterial };
        go.GetComponent<MeshCollider>().sharedMesh = mesh;
        for (int side = -1; side <= 1; side += 2)
        {
            var wall = go.AddComponent<BoxCollider>();
            wall.center = new Vector3(side * (halfWidth + 0.05f), (FloorHeight + 1f) * 0.5f, run * 0.5f);
            wall.size = new Vector3(0.1f, FloorHeight + 1f, run);
        }
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    static void AddQuad(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int first = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        uvs.Add(Vector2.zero);
        uvs.Add(Vector2.up);
        uvs.Add(Vector2.one);
        uvs.Add(Vector2.right);
        triangles.Add(first);
        triangles.Add(first + 1);
        triangles.Add(first + 2);
        triangles.Add(first);
        triangles.Add(first + 2);
        triangles.Add(first + 3);
    }

    static Texture2D EnsureAsphaltTexture()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AsphaltTexturePath);
        if (texture) return texture;
        // 부분 아틀라스의 반복과 상단 횡단보도 유입을 막고 원본 임포터는 건드리지 않는다.
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(RoadTextureFolder + "/road_atlas_baseColor.png")))
                throw new InvalidDataException("도로 아틀라스를 읽지 못했습니다.");
            int x = Mathf.CeilToInt(source.width * 0.38f);
            int width = Mathf.FloorToInt(source.width * 0.70f) - x;
            int height = Mathf.FloorToInt(source.height * 0.92f);
            texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
                { name = "hadan_asphalt", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            texture.SetPixels(source.GetPixels(x, 0, width, height));
            texture.Apply(true, false);
            AssetDatabase.CreateAsset(texture, AsphaltTexturePath);
            return texture;
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    static int CreateRoads(List<RoadInfo> roads, Material material, List<Mesh> meshes, PropCounts counts)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();
        var curbMaterial = HadanMaterial("hadan_curb", Color.white,
            AssetDatabase.LoadAssetAtPath<Texture2D>(RoadTextureFolder + "/curb_baseColor.png"));
        curbMaterial.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RoadTextureFolder + "/curb_normal.png"));
        curbMaterial.EnableKeyword("_NORMALMAP");
        var curbRoot = new GameObject("Curbs").transform;
        int roadCount = 0;

        for (int i = 0; i < roads.Count; i++)
        {
            var road = roads[i];

            bool emitted = false;
            float distance = 0f;
            var curb = new MeshBatch("Curb_" + road.id, curbMaterial);
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
                float length = direction.magnitude;
                uvs.Add(new Vector2(0f, distance / 8f));
                uvs.Add(new Vector2(1f, distance / 8f));
                uvs.Add(new Vector2(1f, (distance + length) / 8f));
                uvs.Add(new Vector2(0f, (distance + length) / 8f));
                distance += length;
                var rotation = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y));
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector2 center = (from + to) * 0.5f + side.normalized * sign * (road.halfWidth + 0.125f);
                    curb.AddBox(new Vector3(center.x, RoadY + 0.075f, center.y),
                        new Vector3(0.25f, 0.15f, length), rotation);
                    counts.curbLength += length;
                }
                emitted = true;
            }
            if (emitted)
            {
                roadCount++;
                curb.Emit(curbRoot, meshes);
                meshes[meshes.Count - 1].RecalculateTangents();
                counts.curbMeshes++;
            }
        }

        var mesh = new Mesh { name = "HadanRoads", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
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

    static void CreateBuildingDetails(Transform building, GameObject[] floorObjects, Transform roofParent, long id,
        List<Vector2> polygon, int floors, float height, BuildingKind kind, int apartmentNumber,
        int streetEdge, DetailMaterials materials, List<Mesh> meshes, PropCounts counts)
    {
        Vector2 from = polygon[streetEdge];
        Vector2 to = polygon[(streetEdge + 1) % polygon.Count];
        Vector2 edge = to - from;
        Vector2 tangent = edge.normalized;
        Vector2 outward = new Vector2(-edge.y, edge.x).normalized;
        Vector2 center = (from + to) * 0.5f;
        var facing = Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.y));
        var random = RoadRandom(id, 17);
        var airConditionerBatches = new MeshBatch[floors];
        var signBatches = new MeshBatch[floors, materials.signMaterials.Length];

        bool hasShop = kind == BuildingKind.Commercial || kind == BuildingKind.MixedUse;
        if (hasShop)
        {
            float shopWidth = edge.magnitude * 0.94f;
            float sideWidth = Mathf.Max(0f, (shopWidth - DoorWidth - 0.1f) * 0.5f);
            for (int side = -1; side <= 1 && sideWidth > 0.01f; side += 2)
            {
                Vector2 point = center + tangent * side * (DoorWidth * 0.5f + 0.05f + sideWidth * 0.5f);
                materials.shopfrontBatch.AddBox(
                    new Vector3(point.x + outward.x * 0.05f, 1.5f, point.y + outward.y * 0.05f),
                    new Vector3(sideWidth, 3f, 0.08f), facing);
            }
            CreatePrimitive(PrimitiveType.Cube, "Awning", building,
                new Vector3(center.x + outward.x * 0.6f, 2.8f, center.y + outward.y * 0.6f),
                new Vector3(edge.magnitude * 0.6f, 0.12f, 1.2f),
                facing * Quaternion.Euler(6f, 0f, 0f), materials.apartmentStripe, false);
            counts.awnings++;
        }

        int buildingSigns = 0;
        if (kind == BuildingKind.Commercial)
        {
            float chance = Mathf.Lerp(0.50f, 0.80f, (float)random.NextDouble());
            for (int floor = 1; floor < floors; floor++)
            {
                if (random.NextDouble() > chance) continue;
                AddHorizontalSign(materials, signBatches, floor, random, center, outward, facing, edge.magnitude,
                    floor * FloorHeight + 2.65f);
                buildingSigns++;
            }
            if (buildingSigns == 0)
            {
                int signFloor = Mathf.Min(1, floors - 1);
                AddHorizontalSign(materials, signBatches, signFloor, random, center, outward, facing, edge.magnitude,
                    FloorHeight + 2.65f);
                buildingSigns++;
            }

            int verticalSigns = 1 + random.Next(2);
            for (int i = 0; i < verticalSigns; i++)
            {
                Vector2 corner = i == 0 ? from : to;
                float signHeight = Mathf.Min(height - 2f, Mathf.Lerp(3f, 8f, (float)random.NextDouble()));
                Vector3 signCenter = new Vector3(corner.x + outward.x * 0.4f,
                    Mathf.Min(height - signHeight * 0.5f, 4f + signHeight * 0.5f),
                    corner.y + outward.y * 0.4f);
                int signFloor = Mathf.Clamp(Mathf.FloorToInt(signCenter.y / FloorHeight), 0, floors - 1);
                int material = random.Next(materials.signMaterials.Length);
                var batch = signBatches[signFloor, material]
                    ?? (signBatches[signFloor, material] = new MeshBatch("Signs_" + material,
                        materials.signMaterials[material]));
                batch.AddBox(signCenter, new Vector3(0.08f, signHeight, 0.8f), facing);
                var verticalMarker = new GameObject("VerticalSignAnchor").transform;
                verticalMarker.SetParent(building, false);
                verticalMarker.position = new Vector3(corner.x, signCenter.y, corner.y);
                buildingSigns++;
            }

            var marker = new GameObject("CommercialSignAnchor").transform;
            marker.SetParent(building, false);
            marker.position = new Vector3(center.x + outward.x * 0.05f, FloorHeight + 2.65f,
                center.y + outward.y * 0.05f);
        }
        else if (kind == BuildingKind.MixedUse)
        {
            AddHorizontalSign(materials, signBatches, 0, random, center, outward, facing, edge.magnitude, 3.15f);
            buildingSigns++;
        }
        counts.signs += buildingSigns;

        bool residential = kind != BuildingKind.Commercial;
        if (residential)
        {
            int firstResidentialFloor = kind == BuildingKind.MixedUse ? 1 : 0;
            for (int floor = firstResidentialFloor; floor < floors; floor++)
            {
                int units = random.Next(3);
                for (int unit = 0; unit < units; unit++)
                {
                    float along = Mathf.Lerp(0.18f, 0.82f, (float)random.NextDouble());
                    Vector2 point = Vector2.Lerp(from, to, along);
                    var batch = airConditionerBatches[floor]
                        ?? (airConditionerBatches[floor] = new MeshBatch("WallAirConditioners",
                            materials.airConditioner));
                    batch.AddBox(
                        new Vector3(point.x + outward.x * 0.24f, floor * FloorHeight + 1.0f,
                            point.y + outward.y * 0.24f),
                        new Vector3(0.8f, 0.55f, 0.45f), facing);
                    counts.wallAirConditioners++;
                }

                float balconyChance = kind == BuildingKind.Apartment ? 0.70f
                    : kind == BuildingKind.MixedUse ? 0.28f : 0f;
                if (random.NextDouble() >= balconyChance) continue;
                float width = Mathf.Min(3.2f, edge.magnitude * 0.45f);
                Vector3 railCenter = new Vector3(center.x + outward.x * 0.38f,
                    floor * FloorHeight + 1.35f, center.y + outward.y * 0.38f);
                materials.balconyBatch.AddBox(railCenter, new Vector3(width, 0.10f, 0.08f), facing);
                for (int bar = -1; bar <= 1; bar++)
                    materials.balconyBatch.AddBox(railCenter + new Vector3(tangent.x * width * bar * 0.4f,
                            -0.42f, tangent.y * width * bar * 0.4f),
                        new Vector3(0.06f, 0.85f, 0.06f), facing);
            }
        }

        if (kind == BuildingKind.Apartment)
            AddApartmentMarkings(materials, polygon, streetEdge, height, apartmentNumber);

        for (int floor = 0; floor < floors; floor++)
        {
            airConditionerBatches[floor]?.Emit(floorObjects[floor].transform, meshes);
            for (int material = 0; material < materials.signMaterials.Length; material++)
                signBatches[floor, material]?.Emit(floorObjects[floor].transform, meshes);
        }

        var rooftop = new GameObject("Rooftop").transform;
        rooftop.SetParent(roofParent, false);
        counts.rooftop += polygon.Count;
        var occupied = new List<Placement>();
        var roofRandom = RoadRandom(id, 31);

        if ((kind == BuildingKind.MixedUse || kind == BuildingKind.House) && roofRandom.NextDouble() < 0.30
            && TryRoofPoint(polygon, roofRandom, 1.2f, occupied, out Vector2 roomPoint))
        {
            CreatePrimitive(PrimitiveType.Cube, "RoofRoom", rooftop,
                new Vector3(roomPoint.x, height + 1.2f, roomPoint.y), new Vector3(2.4f, 2.4f, 2.4f),
                Quaternion.identity, materials.roofRoom, false);
            materials.roofRoomDoorBatch.AddBox(new Vector3(roomPoint.x, height + 1.1f, roomPoint.y + 1.22f),
                new Vector3(0.8f, 1.8f, 0.04f), Quaternion.identity);
            occupied.Add(new Placement(roomPoint, 1.2f));
            counts.roofRooms++;
        }

        if (residential && roofRandom.Next(2) == 0
            && TryRoofPoint(polygon, roofRandom, 0.7f, occupied, out Vector2 tankPoint))
        {
            CreatePrimitive(PrimitiveType.Cylinder, "WaterTank", rooftop,
                new Vector3(tankPoint.x, height + 0.75f, tankPoint.y), new Vector3(0.7f, 0.75f, 0.7f),
                Quaternion.identity, materials.tanks[roofRandom.Next(materials.tanks.Length)], false);
            counts.rooftop++;
        }
    }

    static void AddHorizontalSign(DetailMaterials materials, MeshBatch[,] batches, int floor,
        System.Random random, Vector2 center, Vector2 outward, Quaternion facing, float wallWidth, float y)
    {
        float width = wallWidth * Mathf.Lerp(0.60f, 0.95f, (float)random.NextDouble());
        float height = Mathf.Lerp(0.8f, 1.2f, (float)random.NextDouble());
        int material = random.Next(materials.signMaterials.Length);
        var batch = batches[floor, material]
            ?? (batches[floor, material] = new MeshBatch("Signs_" + material, materials.signMaterials[material]));
        batch.AddBox(
            new Vector3(center.x + outward.x * 0.05f, y, center.y + outward.y * 0.05f),
            new Vector3(width, height, 0.08f), facing);
    }

    static void AddApartmentMarkings(DetailMaterials materials, List<Vector2> polygon, int streetEdge,
        float height, int number)
    {
        int sideEdge = (streetEdge + 1) % polygon.Count;
        Vector2 from = polygon[sideEdge];
        Vector2 to = polygon[(sideEdge + 1) % polygon.Count];
        Vector2 edge = to - from;
        Vector2 tangent = edge.normalized;
        Vector2 outward = new Vector2(-edge.y, edge.x).normalized;
        Vector2 center = (from + to) * 0.5f;
        var facing = Quaternion.LookRotation(new Vector3(outward.x, 0f, outward.y));

        materials.apartmentStripeBatch.AddBox(
            new Vector3(center.x + outward.x * 0.04f, height * 0.5f, center.y + outward.y * 0.04f),
            new Vector3(Mathf.Min(1.2f, edge.magnitude * 0.25f), height * 0.88f, 0.06f), facing);

        string text = number.ToString(CultureInfo.InvariantCulture);
        float digitWidth = Mathf.Min(0.8f, edge.magnitude / (text.Length + 1f));
        for (int i = 0; i < text.Length; i++)
        {
            int digit = text[i] - '0';
            float offset = (i - (text.Length - 1) * 0.5f) * digitWidth;
            Vector2 point = center + tangent * offset;
            materials.apartmentNumberBatch.AddQuad(
                new Vector3(point.x + outward.x * 0.09f, height - 2.0f, point.y + outward.y * 0.09f),
                new Vector3(tangent.x, 0f, tangent.y), Vector3.up, digitWidth * 0.82f, 1.25f,
                new Vector2(digit * 0.1f, 0f), new Vector2((digit + 1) * 0.1f, 1f));
        }
    }

    static void CreateStreetProps(List<RoadInfo> roads, List<List<Vector2>> buildings, Vector2 spawn,
        DetailMaterials materials, List<Mesh> meshes, PropCounts counts, AssetModel[] carModels)
    {
        var root = new GameObject("StreetProps").transform;
        var treeRoot = new GameObject("Trees").transform;
        var poleRoot = new GameObject("UtilityPoles").transform;
        var carRoot = new GameObject("AbandonedCars").transform;
        treeRoot.SetParent(root, false);
        poleRoot.SetParent(root, false);
        carRoot.SetParent(root, false);
        var occupied = new List<Placement>();
        var carSlots = new List<CarSlot>();
        var wireBatch = new MeshBatch("Wires", materials.wire);

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
                bool hasPreviousPole = false;
                Vector2 previousPole = default;
                for (float distance = (float)random.NextDouble() * 30f; distance < length; distance += 30f)
                {
                    if (!PointAlongRoad(road, distance, out Vector2 point, out Vector2 direction)) continue;
                    Vector2 side = new Vector2(-direction.y, direction.x);
                    Vector2 position = point + side * sign * (road.halfWidth + PoleRadius);
                    if (!CanPlace(position, PoleRadius, buildings, spawn, occupied)
                        || !IsClearOfRoads(position, roads)) continue;
                    CreatePole(poleRoot, position, direction, materials, counts);
                    occupied.Add(new Placement(position, PoleRadius));

                    if (hasPreviousPole && Vector2.Distance(previousPole, position) <= 30f + Epsilon)
                        AddWireSpan(wireBatch, previousPole, position, direction, counts);
                    previousPole = position;
                    hasPreviousPole = true;

                    if (random.NextDouble() >= 0.20) continue;
                    Vector2 opposite = point - side * sign * (road.halfWidth + PoleRadius);
                    if (!CanPlace(opposite, PoleRadius, buildings, spawn, occupied)
                        || !IsClearOfRoads(opposite, roads)) continue;
                    CreatePole(poleRoot, opposite, direction, materials, counts);
                    occupied.Add(new Placement(opposite, PoleRadius));
                    AddWireSpan(wireBatch, position, opposite, direction, counts);
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
                float angle = (float)random.NextDouble() * 16f - 8f;
                carSlots.Add(new CarSlot { position = position, direction = direction, angle = angle,
                    seed = unchecked(road.id * 397 + car) });
                occupied.Add(new Placement(position, CarRadius));
            }
        }

        var carFootprints = new List<List<Vector2>>();
        foreach (var slot in carSlots)
            CreateCar(carRoot, slot, carModels, buildings, occupied, carFootprints, counts);
        wireBatch.Emit(root, meshes);
        V01SceneBuilder.MarkStatic(root);
    }

    static void AddWireSpan(MeshBatch batch, Vector2 from, Vector2 to, Vector2 roadDirection,
        PropCounts counts)
    {
        Vector2 side = new Vector2(-roadDirection.y, roadDirection.x).normalized;
        for (int wire = -1; wire <= 1; wire++)
        {
            Vector2 offset = side * wire * 0.65f;
            Vector3 start = new Vector3(from.x + offset.x, 6.25f, from.y + offset.y);
            Vector3 end = new Vector3(to.x + offset.x, 6.25f, to.y + offset.y);
            Vector3 middle = (start + end) * 0.5f - Vector3.up * 0.5f;
            batch.AddBeam(start, middle, 0.035f);
            batch.AddBeam(middle, end, 0.035f);
            counts.wires++;
        }
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

    static AssetModel[] LoadCarModels()
    {
        var models = new AssetModel[CarNames.Length];
        for (int i = 0; i < models.Length; i++)
        {
            string path = VehicleFolder + "/hadan_car_" + CarNames[i] + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!model) throw new InvalidOperationException("차 에셋이 없습니다: " + path);
            var bounds = ModelBounds(model.transform, model.transform.parent);
            if (bounds.size.x <= Epsilon || bounds.size.y <= Epsilon || bounds.size.z <= Epsilon)
                throw new InvalidOperationException("차 에셋 크기가 유효하지 않습니다: " + path);
            models[i] = new AssetModel { model = model, path = path, bounds = bounds };
        }
        return models;
    }

    static List<Vector2> CarFootprint(Vector2 position, Quaternion rotation, Bounds bounds)
        => BuildingBoxPolygon(new BuildingBox { center = position, forward = Xz(rotation * Vector3.forward),
            width = bounds.size.x, depth = bounds.size.z });

    static bool CarFits(List<Vector2> footprint, Vector2 position, List<List<Vector2>> buildings,
        List<Placement> occupied, List<List<Vector2>> cars)
    {
        foreach (var building in buildings)
            if (PolygonDistance(footprint, building) < 1f - Epsilon) return false;
        foreach (var prop in occupied)
            if ((prop.point - position).sqrMagnitude > Epsilon
                && DistanceToPolygon(prop.point, footprint) < (prop.radius == CarRadius ? 6f : prop.radius) - Epsilon) return false;
        foreach (var car in cars)
            if (PolygonDistance(footprint, car) <= Epsilon) return false;
        return true;
    }

    static float PolygonDistance(List<Vector2> a, List<Vector2> b)
    {
        if (PointInPolygon(a[0], b) || PointInPolygon(b[0], a)) return 0f;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < a.Count; i++)
            for (int j = 0; j < b.Count; j++)
                distance = Mathf.Min(distance, SegmentDistance(a[i], a[(i + 1) % a.Count],
                    b[j], b[(j + 1) % b.Count]));
        return distance;
    }

    static void CreateCar(Transform parent, CarSlot slot, AssetModel[] models,
        List<List<Vector2>> buildings, List<Placement> occupied, List<List<Vector2>> footprints, PropCounts counts)
    {
        var random = RoadRandom(slot.seed, 79);
        int roll = random.Next(100);
        int index = roll < 75 ? random.Next(5) : roll < 90 ? 5 : 6 + random.Next(2);
        var rotation = Quaternion.LookRotation(new Vector3(slot.direction.x, 0f, slot.direction.y))
            * Quaternion.Euler(0f, slot.angle, 0f);
        var footprint = CarFootprint(slot.position, rotation, models[index].bounds);
        if (index >= 6 && !CarFits(footprint, slot.position, buildings, occupied, footprints))
        {
            int start = random.Next(5);
            bool fits = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                index = (start + attempt) % 5;
                footprint = CarFootprint(slot.position, rotation, models[index].bounds);
                if (!CarFits(footprint, slot.position, buildings, occupied, footprints)) continue;
                fits = true;
                break;
            }
            if (!fits) throw new InvalidOperationException("기존 차 자리에서 승용차를 배치할 수 없습니다: " + slot.position);
        }
        var asset = models[index];
        var car = new GameObject("AbandonedCar").transform;
        car.SetParent(parent, false);
        car.position = new Vector3(slot.position.x, 0f, slot.position.y);
        car.rotation = rotation;
        var visual = new GameObject("CarVisual").transform;
        visual.SetParent(car, false);
        visual.localPosition = -new Vector3(asset.bounds.center.x, asset.bounds.min.y, asset.bounds.center.z);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset.model, visual);
        model.transform.localPosition = asset.model.transform.localPosition;
        model.transform.localRotation = asset.model.transform.localRotation;
        model.transform.localScale = asset.model.transform.localScale;
        foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
        var box = car.gameObject.AddComponent<BoxCollider>();
        box.size = asset.bounds.size;
        box.center = Vector3.up * (asset.bounds.size.y * 0.5f);
        footprints.Add(footprint);
        counts.carTypes[index]++;
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

    static Mesh CreateFloorMesh(long id, int floor, List<Vector2> polygon,
        int streetEdge, bool hasStairs, StairPlan stair)
    {
        var sourceTriangles = new List<int>();
        if (!Triangulate(polygon, sourceTriangles)) return null;

        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var floorTriangles = new List<int>();
        var innerTriangles = new List<int>();
        float top = floor * FloorHeight;
        float bottom = top - FloorThickness;
        bool cutHole = hasStairs && floor > 0;
        StairPlan hole = cutHole ? stair.Flight(floor - 1) : default;

        for (int i = 0; i < sourceTriangles.Count; i += 3)
        {
            var triangle = new List<Vector2>
            {
                polygon[sourceTriangles[i]], polygon[sourceTriangles[i + 1]], polygon[sourceTriangles[i + 2]]
            };
            var pieces = cutHole ? SubtractStairHole(triangle, hole)
                : new List<List<Vector2>> { triangle };
            for (int piece = 0; piece < pieces.Count; piece++)
                AddFloorPiece(vertices, uvs, floorTriangles, pieces[piece], top, bottom);
        }

        for (int edge = 0; edge < polygon.Count; edge++)
        {
            Vector2 a = polygon[edge];
            Vector2 b = polygon[(edge + 1) % polygon.Count];
            AddQuad(vertices, uvs, floorTriangles,
                new Vector3(a.x, bottom, a.y), new Vector3(b.x, bottom, b.y),
                new Vector3(b.x, top, b.y), new Vector3(a.x, top, a.y));

            float length = Vector2.Distance(a, b);
            Vector2 tangent = (b - a).normalized;
            Vector2 outward = new Vector2(-tangent.y, tangent.x);
            float inset = WallThickness + 0.002f;
            Vector2 offsetA = a - outward * inset;
            Vector2 innerA = InsetCorner(polygon, edge, inset);
            Vector2 innerB = InsetCorner(polygon, (edge + 1) % polygon.Count, inset);
            float innerStart = Vector2.Dot(innerA - offsetA, tangent);
            float innerEnd = Vector2.Dot(innerB - offsetA, tangent);
            var spans = WallSpans(length, floor == 0 && edge == streetEdge);
            for (int span = 0; span < spans.Count; span++)
            {
                WallSpan value = spans[span];
                float start = value.start <= Epsilon ? innerStart : Mathf.Max(value.start, innerStart);
                float end = value.end >= length - Epsilon ? innerEnd : Mathf.Min(value.end, innerEnd);
                if (end <= start + Epsilon) continue;
                Vector2 p = offsetA + tangent * start;
                Vector2 q = offsetA + tangent * end;
                float y0 = top + value.bottom;
                float y1 = Mathf.Min(top + value.top, top + FloorHeight - FloorThickness);
                AddQuad(vertices, uvs, innerTriangles,
                    new Vector3(p.x, y0, p.y), new Vector3(p.x, y1, p.y),
                    new Vector3(q.x, y1, q.y), new Vector3(q.x, y0, q.y));
            }
        }

        if (cutHole)
        {
            Vector2[] corners = hole.HoleCorners();
            for (int edge = 0; edge < corners.Length; edge++)
            {
                Vector2 a = corners[edge];
                Vector2 b = corners[(edge + 1) % corners.Length];
                AddQuad(vertices, uvs, floorTriangles,
                    new Vector3(b.x, bottom, b.y), new Vector3(a.x, bottom, a.y),
                    new Vector3(a.x, top, a.y), new Vector3(b.x, top, b.y));
            }
        }

        var mesh = new Mesh { name = "HadanFloor_" + id + "_" + floor };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(floorTriangles, 0);
        mesh.SetTriangles(innerTriangles, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector2 InsetCorner(List<Vector2> polygon, int vertex, float inset)
    {
        Vector2 a = polygon[(vertex - 1 + polygon.Count) % polygon.Count];
        Vector2 b = polygon[vertex];
        Vector2 c = polygon[(vertex + 1) % polygon.Count];
        Vector2 previous = (b - a).normalized;
        Vector2 next = (c - b).normalized;
        Vector2 p = b - new Vector2(-previous.y, previous.x) * inset;
        Vector2 q = b - new Vector2(-next.y, next.x) * inset;
        float cross = previous.x * next.y - previous.y * next.x;
        if (Mathf.Abs(cross) <= Epsilon) return q;
        float distance = ((q.x - p.x) * next.y - (q.y - p.y) * next.x) / cross;
        return p + previous * distance;
    }

    static void AddFloorPiece(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
        List<Vector2> polygon, float top, float bottom)
    {
        if (polygon.Count < 3) return;
        int first = vertices.Count;
        for (int i = 0; i < polygon.Count; i++)
        {
            vertices.Add(new Vector3(polygon[i].x, top, polygon[i].y));
            uvs.Add(polygon[i]);
        }
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            triangles.Add(first);
            triangles.Add(first + i);
            triangles.Add(first + i + 1);
        }

        first = vertices.Count;
        for (int i = 0; i < polygon.Count; i++)
        {
            vertices.Add(new Vector3(polygon[i].x, bottom, polygon[i].y));
            uvs.Add(polygon[i]);
        }
        for (int i = 1; i < polygon.Count - 1; i++)
        {
            triangles.Add(first);
            triangles.Add(first + i + 1);
            triangles.Add(first + i);
        }
    }

    static List<List<Vector2>> SubtractStairHole(List<Vector2> polygon, StairPlan stair)
    {
        var outside = new List<List<Vector2>>();
        var candidates = new List<List<Vector2>> { polygon };
        for (int boundary = 0; boundary < 4; boundary++)
        {
            var next = new List<List<Vector2>>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var removed = ClipHoleHalfPlane(candidates[i], stair, boundary, false);
                if (removed.Count >= 3) outside.Add(removed);
                var kept = ClipHoleHalfPlane(candidates[i], stair, boundary, true);
                if (kept.Count >= 3) next.Add(kept);
            }
            candidates = next;
            if (candidates.Count == 0) break;
        }
        return outside;
    }

    static List<Vector2> ClipHoleHalfPlane(List<Vector2> polygon, StairPlan stair,
        int boundary, bool keepInside)
    {
        var result = new List<Vector2>();
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            float aValue = HoleHalfPlane(a, stair, boundary);
            float bValue = HoleHalfPlane(b, stair, boundary);
            bool aKept = keepInside ? aValue >= -Epsilon : aValue <= Epsilon;
            bool bKept = keepInside ? bValue >= -Epsilon : bValue <= Epsilon;
            if (aKept) result.Add(a);
            if (aKept == bKept) continue;
            float t = aValue / (aValue - bValue);
            result.Add(Vector2.Lerp(a, b, t));
        }
        return result;
    }

    static float HoleHalfPlane(Vector2 point, StairPlan stair, int boundary)
    {
        Vector2 offset = point - stair.start;
        float x = Vector2.Dot(offset, stair.right);
        float z = Vector2.Dot(offset, stair.direction);
        float halfWidth = StairWidth * 0.5f;
        float run = StairSteps * StairTread;
        switch (boundary)
        {
            case 0: return x + halfWidth;
            case 1: return halfWidth - x;
            case 2: return z;
            default: return run - z;
        }
    }

    static Mesh CreateRoofMesh(long id, List<Vector2> polygon, float height)
    {
        var surface = new List<int>();
        if (!Triangulate(polygon, surface)) return null;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float bottom = height - FloorThickness;

        for (int i = 0; i < surface.Count; i += 3)
        {
            var piece = new List<Vector2>
            {
                polygon[surface[i]], polygon[surface[i + 1]], polygon[surface[i + 2]]
            };
            AddFloorPiece(vertices, uvs, triangles, piece, height, bottom);
        }
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            AddQuad(vertices, uvs, triangles,
                new Vector3(a.x, bottom, a.y), new Vector3(b.x, bottom, b.y),
                new Vector3(b.x, height, b.y), new Vector3(a.x, height, a.y));

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
            int[] railing =
            {
                0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11
            };
            for (int j = 0; j < railing.Length; j++) triangles.Add(first + railing[j]);
        }

        var mesh = new Mesh { name = "HadanRoof_" + id };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static bool TryStairPlan(List<Vector2> polygon, Vector2 doorCenter, out StairPlan plan)
    {
        var edges = new List<int>();
        for (int i = 0; i < polygon.Count; i++) edges.Add(i);
        edges.Sort((left, right) => Vector2.Distance(polygon[right], polygon[(right + 1) % polygon.Count])
            .CompareTo(Vector2.Distance(polygon[left], polygon[(left + 1) % polygon.Count])));

        float clearance = WallThickness + V01SceneBuilder.AgentRadius();
        float inwardOffset = clearance + StairWidth * 0.5f;
        float topOffset = clearance + StairSteps * StairTread + 1f;
        for (int i = 0; i < edges.Count; i++)
        {
            Vector2 a = polygon[edges[i]];
            Vector2 b = polygon[(edges[i] + 1) % polygon.Count];
            Vector2 direction = (b - a).normalized;
            Vector2 inward = new Vector2(direction.y, -direction.x);
            var forward = new StairPlan(a + direction * topOffset + inward * inwardOffset,
                -direction, inward * (StairWidth + StairGap));
            if (StairFits(polygon, forward, doorCenter)) { plan = forward; return true; }
            var backward = new StairPlan(b - direction * topOffset + inward * inwardOffset,
                direction, inward * (StairWidth + StairGap));
            if (StairFits(polygon, backward, doorCenter)) { plan = backward; return true; }
        }
        plan = default;
        return false;
    }

    static bool StairFits(List<Vector2> polygon, StairPlan plan, Vector2 doorCenter)
    {
        float halfWidth = StairWidth * 0.5f;
        float run = StairSteps * StairTread;
        Vector2 across = plan.secondOffset.normalized;
        Vector2[] corners =
        {
            plan.start - across * halfWidth - plan.direction,
            plan.start - across * halfWidth + plan.direction * (run + 1f),
            plan.start + across * (StairWidth + StairGap + halfWidth) + plan.direction * (run + 1f),
            plan.start + across * (StairWidth + StairGap + halfWidth) - plan.direction
        };
        for (int i = 0; i < corners.Length; i++)
            if (!InsideStairArea(corners[i], polygon)) return false;

        for (int side = 0; side < corners.Length; side++)
            for (int edge = 0; edge < polygon.Count; edge++)
                if (SegmentDistance(corners[side], corners[(side + 1) % corners.Length],
                        polygon[edge], polygon[(edge + 1) % polygon.Count])
                    < WallThickness + V01SceneBuilder.AgentRadius() - Epsilon)
                    return false;

        Vector2 doorOffset = doorCenter - plan.start;
        float x = Mathf.Clamp(Vector2.Dot(doorOffset, across),
            -halfWidth - 0.2f, StairWidth + StairGap + halfWidth + 0.2f);
        float z = Mathf.Clamp(Vector2.Dot(doorOffset, plan.direction), 0f, StairSteps * StairTread);
        // 왜: 문 안팎의 1.5m 접근 공간을 계단 구멍과 옆벽이 막지 않게 한다.
        if ((doorCenter - plan.start - across * x - plan.direction * z).sqrMagnitude < 1.5f * 1.5f)
            return false;
        return true;
    }

    static bool InsideStairArea(Vector2 point, List<Vector2> polygon)
        => PointInPolygon(point, polygon)
           && DistanceToPolygonEdge(point, polygon)
           >= WallThickness + V01SceneBuilder.AgentRadius() - Epsilon;

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

    static int FloorCount(MapTags tags, float area)
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

    static BuildingKind ClassifyBuilding(MapTags tags, List<Vector2> polygon, int floors, float area,
        List<RoadInfo> roads)
    {
        if (tags.building == "apartments") return BuildingKind.Apartment;
        if (floors >= 4)
            for (int edge = 0; edge < polygon.Count; edge++)
                for (int road = 0; road < roads.Count; road++)
                    if ((roads[road].highway == "primary" || roads[road].highway == "secondary")
                        && DistanceToRoad(polygon[edge], polygon[(edge + 1) % polygon.Count], roads[road]) <= 15f)
                        return BuildingKind.Commercial;
        return area < 100f ? BuildingKind.House : BuildingKind.MixedUse;
    }

    static int FindStreetEdge(List<Vector2> polygon, List<RoadInfo> roads, bool mainRoadOnly)
    {
        int bestEdge = 0;
        float bestDistance = float.PositiveInfinity;
        float longest = 0f;
        for (int edge = 0; edge < polygon.Count; edge++)
        {
            Vector2 from = polygon[edge];
            Vector2 to = polygon[(edge + 1) % polygon.Count];
            float length = Vector2.Distance(from, to);
            if (length > longest)
            {
                longest = length;
                if (float.IsPositiveInfinity(bestDistance)) bestEdge = edge;
            }
            if (length < 1.6f) continue;
            for (int road = 0; road < roads.Count; road++)
            {
                if (mainRoadOnly && roads[road].highway != "primary" && roads[road].highway != "secondary")
                    continue;
                float distance = DistanceToRoad(from, to, roads[road]);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                bestEdge = edge;
            }
        }
        return bestEdge;
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

    static void CheckAssetBuildings(MapData map, Transform root, out float maxHeight, ref int failures)
    {
        maxHeight = 0f;
        AssetModel[] assets;
        List<AssetBuildingPlan> plans;
        var roads = RoadInfos(map);
        try
        {
            assets = LoadAssetModels();
            plans = AssetBuildingPlans(map, roads, assets, out _);
        }
        catch (Exception exception)
        {
            maxHeight = float.PositiveInfinity;
            Check(false, "건물 에셋 검사 준비 실패: " + exception.Message, ref failures);
            return;
        }
        Check(plans.Count == 459 && root && root.childCount == plans.Count,
            "459채 모두 에셋 건물이다 (" + (root ? root.childCount : 0) + ")", ref failures);
        bool validBuildings = root;
        bool validScale = root;
        bool validShops = root;
        bool sharedMeshes = root;
        bool staticBatching = root;
        bool validFadeBindings = root;
        bool facesRoad = root;
        var pairs = new Dictionary<Material, Material>();
        int sourceMaterials = 0;
        bool materialAssets = true;
        foreach (var asset in assets)
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(asset.path))
            {
                if (!(source is Material material)) continue;
                sourceMaterials++;
                string key = AssetMaterialKey(material);
                var opaque = AssetDatabase.LoadAssetAtPath<Material>(AssetMaterialFolder + "/" + key + "_opaque.mat");
                var fade = AssetDatabase.LoadAssetAtPath<Material>(AssetMaterialFolder + "/" + key + "_fade.mat");
                materialAssets &= opaque && fade && opaque.shader.name == "Universal Render Pipeline/Lit"
                    && fade.shader.name == "Universal Render Pipeline/Lit" && fade.GetFloat("_Surface") == 1f
                    && opaque.mainTexture == material.mainTexture && fade.mainTexture == material.mainTexture
                    && opaque.name == key + "_opaque" && fade.name == key + "_fade";
                if (opaque && fade) pairs[opaque] = fade;
            }
        var types = new int[4];
        int roofSamples = 0;
        int roofNavHits = 0;
        var roofSampleIndices = new HashSet<int>();
        for (int i = 0; i < Mathf.Min(20, plans.Count); i++) roofSampleIndices.Add(i * plans.Count / 20);
        int shopfronts = 0;
        int signedShopfronts = 0;
        var usedAssets = new HashSet<int>();
        var assetUsage = new int[13];
        int mostUsedAssetCount = 0;
        int roadSamples = 0;
        var sampleIndices = new HashSet<int>();
        for (int i = 0; i < Mathf.Min(50, plans.Count); i++) sampleIndices.Add(i * plans.Count / 50);
        for (int i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            var building = root ? root.Find("Building_" + plan.id) : null;
            var collider = building ? building.GetComponent<BoxCollider>() : null;
            var visual = building ? building.Find("AssetVisual") : null;
            var model = visual && visual.childCount == 1 ? visual.GetChild(0) : null;
            bool valid = building && collider && collider.enabled && !collider.isTrigger && model
                && PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject) == assets[plan.asset].model;
            validBuildings &= valid;
            if (!valid) { validScale = validShops = sharedMeshes = validFadeBindings = facesRoad = false; continue; }
            validBuildings &= building.localScale == Vector3.one && building.gameObject.layer == 0
                && building.GetComponentsInChildren<Collider>(true).Length == 1
                && Vector3.Distance(collider.size, new Vector3(plan.box.width, plan.height, plan.box.depth)) < 0.01f
                && Vector3.Distance(collider.center, Vector3.up * (plan.height * 0.5f)) < 0.01f
                && Vector2.Distance(Xz(building.position), plan.box.center) < 0.01f;
            usedAssets.Add(plan.asset);
            mostUsedAssetCount = Mathf.Max(mostUsedAssetCount, ++assetUsage[plan.asset]);
            types[(int)plan.kind]++;
            if (roofSampleIndices.Contains(i))
            {
                Vector3 roofCenter = building.TransformPoint(collider.center + Vector3.up * (collider.size.y * 0.5f));
                roofSamples++;
                if (NavMesh.SamplePosition(roofCenter, out _, 1f, NavMesh.AllAreas)) roofNavHits++;
            }
            maxHeight = Mathf.Max(maxHeight, collider.size.y);
            Vector3 scale = visual.localScale;
            validScale &= scale.x > 0f && scale.y >= 0.75f - Epsilon && scale.y <= 1.35f + Epsilon && scale.z > 0f
                && Mathf.Max(scale.x / scale.z, scale.z / scale.x) <= MaxAssetDistortion + Epsilon
                && Vector3.Distance(scale, plan.scale) < 0.001f;
            var bodyBounds = ModelBounds(model, building);
            validScale &= Vector3.Distance(bodyBounds.size, collider.size) < 0.01f
                && Vector3.Distance(bodyBounds.center, collider.center) < 0.01f;
            maxHeight = Mathf.Max(maxHeight, bodyBounds.max.y);
            if (sampleIndices.Contains(i))
            {
                Vector2 forward = Xz(building.TransformDirection(AssetFront)).normalized;
                facesRoad &= Vector2.Angle(forward, NearestRoadDirection(Xz(building.position), roads)) < 60f;
                roadSamples++;
            }
            int expectedShops = 0;
            var shop = assets[13 + RoadRandom(plan.id, 73).Next(3)];
            if (!HasOwnSign(plan.asset) && (plan.kind == BuildingKind.MixedUse || plan.kind == BuildingKind.House)
                && plan.box.width >= 4f)
                expectedShops = plan.box.width >= shop.bounds.size.x * 2f ? 2 : 1;
            int actualShops = 0;
            for (int child = 0; child < building.childCount; child++)
            {
                var storefront = building.GetChild(child);
                if (!storefront.name.StartsWith("Shopfront_", StringComparison.Ordinal)) continue;
                actualShops++;
                shopfronts++;
                var shopModel = storefront.childCount == 1 ? storefront.GetChild(0) : null;
                validShops &= shopModel && PrefabUtility.GetCorrespondingObjectFromSource(shopModel.gameObject) == shop.model
                    && storefront.GetComponentsInChildren<Collider>(true).Length == 0;
                if (!shopModel) continue;
                var bounds = ModelBounds(shopModel, building);
                validShops &= Mathf.Abs(bounds.min.y) < 0.01f && Mathf.Abs(bounds.size.y - FloorHeight) < 0.01f
                    && Mathf.Abs(bounds.max.z - (collider.size.z * 0.5f + 0.05f)) < 0.01f
                    && bounds.min.x >= -collider.size.x * 0.5f - 0.01f
                    && bounds.max.x <= collider.size.x * 0.5f + 0.01f;
            }
            validShops &= actualShops == expectedShops;
            if (HasOwnSign(plan.asset)) signedShopfronts += actualShops;
            foreach (var filter in building.GetComponentsInChildren<MeshFilter>(true))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(filter);
                sharedMeshes &= source && source.sharedMesh == filter.sharedMesh;
                staticBatching &= (GameObjectUtility.GetStaticEditorFlags(filter.gameObject) & StaticEditorFlags.BatchingStatic) != 0;
            }
            var wall = building.GetComponent<WallSegment>();
            validFadeBindings &= wall && wall.IsExterior
                && building.GetComponentsInChildren<WallSegment>(true).Length == 1;
            if (!wall) continue;
            var serialized = new SerializedObject(wall);
            var targets = serialized.FindProperty("targets");
            var sets = serialized.FindProperty("assetMaterials");
            var renderers = building.GetComponentsInChildren<Renderer>(true);
            bool setsValid = targets.arraySize == renderers.Length && sets.arraySize == renderers.Length;
            validFadeBindings &= setsValid;
            if (!setsValid) continue;
            for (int renderer = 0; renderer < renderers.Length; renderer++)
            {
                validFadeBindings &= targets.GetArrayElementAtIndex(renderer).objectReferenceValue == renderers[renderer];
                var set = sets.GetArrayElementAtIndex(renderer);
                var opaque = set.FindPropertyRelative("opaque");
                var fade = set.FindPropertyRelative("fade");
                var shared = renderers[renderer].sharedMaterials;
                if (opaque.arraySize != shared.Length || fade.arraySize != shared.Length)
                { validFadeBindings = false; continue; }
                for (int slot = 0; slot < shared.Length; slot++)
                    validFadeBindings &= opaque.GetArrayElementAtIndex(slot).objectReferenceValue == shared[slot]
                        && shared[slot] && pairs.TryGetValue(shared[slot], out var expectedFade)
                        && fade.GetArrayElementAtIndex(slot).objectReferenceValue == expectedFade;
            }
        }
        Check(roofSamples == 20 && roofNavHits == 0,
            "OBB 중심 지붕 반경 1m에 NavMesh가 없다 (표본 " + roofSamples + ", 탐색 성공 " + roofNavHits + ")", ref failures);
        Check(types[(int)BuildingKind.Commercial] > 0 && types[(int)BuildingKind.Apartment] > 0,
            "상가·아파트 유형이 각각 1채 이상 있다 (상가 " + types[(int)BuildingKind.Commercial]
            + ", 아파트 " + types[(int)BuildingKind.Apartment] + ")", ref failures);
        var footprints = new List<List<Vector2>>(AssetBuildingPolygons(root).Values);
        int overlapPairs = 0;
        for (int i = 0; i < footprints.Count; i++)
            for (int j = i + 1; j < footprints.Count; j++)
                if (BuildingOverlapArea(footprints[i], footprints[j]) > 0.5f) overlapPairs++;
        Debug.Log("[Hadan Asset] 건물 OBB XZ 겹침 면적 0.5㎡ 초과 쌍 " + overlapPairs);
        Check(validBuildings, "모든 건물에 원본 FBX 인스턴스와 Default 레이어 OBB BoxCollider가 있다", ref failures);
        Check(validScale, "모든 건물의 X/Z 비율 왜곡이 1.6 이하이고 외관 크기가 콜라이더와 같다", ref failures);
        Check(facesRoad && roadSamples == 50, "앞면과 가장 가까운 도로 방향 각 <60° (" + roadSamples + "채)", ref failures);
        Check(usedAssets.Count >= 10, "건물 13종 중 10종 이상 사용 (" + usedAssets.Count + ")", ref failures);
        Check(plans.Count > 0 && mostUsedAssetCount <= plans.Count * 0.35d,
            "최다 사용 건물 에셋이 전체의 35% 이하 (" + mostUsedAssetCount + "/" + plans.Count + ")", ref failures);
        Check(signedShopfronts == 0, "자체 간판 건물의 추가 가게 앞면이 0개다", ref failures);
        Check(validShops, "한글 가게 앞면이 1층·외벽 밖 0.05m에 있고 콜라이더가 없다 (" + shopfronts + ")", ref failures);
        Check(sharedMeshes && staticBatching, "원본 메쉬를 공유하고 정적 배칭이 설정돼 있다", ref failures);
        Check(validFadeBindings, "건물마다 외벽 WallSegment 한 조각이 모든 재질 슬롯에 반투명본을 연결한다", ref failures);
        Check(materialAssets && pairs.Count == sourceMaterials
            && AssetDatabase.FindAssets("t:Material", new[] { AssetMaterialFolder }).Length == sourceMaterials * 2,
            "원본 재질 " + sourceMaterials + "개마다 공유 URP Lit 불투명·반투명 에셋이 하나씩 있다", ref failures);
        Check(EditorPrefs.GetInt(AssetMaterialRebuildsKey, 0) >= 1
            && AssetMaterialFiles() == EditorPrefs.GetString(AssetMaterialFilesKey, ""),
            "같은 빌드 2번에 에셋 재질 파일 목록·수가 같다", ref failures);
        Check(root && root.GetComponentsInChildren<BuildingFloors>(true).Length == 0
            && root.GetComponentsInChildren<Room>(true).Length == 0
            && root.GetComponentsInChildren<WindowFrame>(true).Length == 0 && !GameObject.Find("BuildingDecorations"),
            "에셋 경로에서는 15·16번 직접 생성 장식·실내를 만들지 않는다", ref failures);
        var wires = GameObject.Find("StreetProps")?.transform.Find("Wires");
        Check(wires && wires.GetComponentsInChildren<Collider>(true).Length == 0,
            "전선이 유지되고 장식 콜라이더가 없다", ref failures);
    }

    static float BuildingOverlapArea(List<Vector2> a, List<Vector2> b)
    {
        var polygon = new List<Vector2>(a);
        float winding = Mathf.Sign(SignedArea(b));
        for (int edge = 0; edge < b.Count && polygon.Count > 0; edge++)
        {
            var clipped = new List<Vector2>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 from = polygon[i];
                Vector2 to = polygon[(i + 1) % polygon.Count];
                float fromSide = winding * Cross(b[edge], b[(edge + 1) % b.Count], from);
                float toSide = winding * Cross(b[edge], b[(edge + 1) % b.Count], to);
                bool fromInside = fromSide >= 0f;
                bool toInside = toSide >= 0f;
                if (fromInside) clipped.Add(from);
                if (fromInside != toInside)
                    clipped.Add(Vector2.Lerp(from, to, fromSide / (fromSide - toSide)));
            }
            polygon = clipped;
        }
        return Mathf.Abs(SignedArea(polygon));
    }

    static int CountBadFaces(Mesh mesh, string partName, List<Vector2> polygon, ref int logged)
    {
        if (!mesh) return 1;
        var vertices = mesh.vertices;
        var triangles = mesh.triangles;
        if (triangles.Length == 0) return 1;
        int bad = 0;
        bool floor = partName.StartsWith("Floor_", StringComparison.Ordinal);
        bool roof = partName == "Roof";
        bool wall = partName.StartsWith("ExteriorWall_", StringComparison.Ordinal);
        bool stair = partName.StartsWith("Stair_", StringComparison.Ordinal);
        float surface = floor ? mesh.bounds.min.y + FloorThickness : roof ? vertices[0].y : 0f;
        int subMesh = 0;
        int subMeshEnd = (int)mesh.GetIndexCount(0);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            while (i >= subMeshEnd && subMesh + 1 < mesh.subMeshCount)
                subMeshEnd += (int)mesh.GetIndexCount(++subMesh);
            Vector3 a = vertices[triangles[i]];
            Vector3 b = vertices[triangles[i + 1]];
            Vector3 c = vertices[triangles[i + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            Vector3 center3 = (a + b + c) / 3f;
            if (normal.sqrMagnitude <= Epsilon * Epsilon)
            {
                LogBadFace(mesh, subMesh, center3, normal, "0이 아닌 유효 법선", ref logged);
                bad++;
                continue;
            }
            normal.Normalize();

            if (Mathf.Abs(a.y - b.y) <= Epsilon && Mathf.Abs(b.y - c.y) <= Epsilon)
            {
                float expected = floor || roof
                    ? (a.y < surface - Epsilon ? -1f : 1f)
                    : wall && Mathf.Abs(a.y - mesh.bounds.min.y) <= Epsilon ? -1f : 1f;
                if (normal.y * expected < 0.999f)
                {
                    LogBadFace(mesh, subMesh, center3, Vector3.up * expected, normal, ref logged);
                    bad++;
                }
                continue;
            }

            if (Mathf.Abs(normal.y) > Epsilon)
            {
                LogBadFace(mesh, subMesh, center3, normal, "수평 법선(y=0)", ref logged);
                bad++;
                continue;
            }
            if (stair) continue;
            Vector2 center = (new Vector2(a.x, a.z) + new Vector2(b.x, b.z)
                + new Vector2(c.x, c.z)) / 3f;
            if (wall)
            {
                Vector3 expected = (center3 - mesh.bounds.center).normalized;
                if (Vector3.Dot(normal, center3 - mesh.bounds.center) <= Epsilon)
                {
                    LogBadFace(mesh, subMesh, center3, expected, normal, ref logged);
                    bad++;
                }
                continue;
            }
            float distance = float.PositiveInfinity;
            Vector2 outward = Vector2.zero;
            float signed = 0f;
            for (int edge = 0; edge < polygon.Count; edge++)
            {
                Vector2 p = polygon[edge];
                Vector2 q = polygon[(edge + 1) % polygon.Count];
                float candidate = DistanceToSegment(center, p, q);
                if (candidate < distance)
                {
                    distance = candidate;
                    outward = new Vector2(-(q.y - p.y), q.x - p.x).normalized;
                    signed = Vector2.Dot(center - p, outward);
                }
            }

            float expectedDirection = 0f;
            if (wall || floor)
            {
                float inner = wall ? WallThickness : WallThickness + 0.002f;
                if (Mathf.Abs(signed) <= 0.01f) expectedDirection = 1f;
                else if (Mathf.Abs(signed + inner) <= 0.01f) expectedDirection = -1f;
            }
            else if (roof)
            {
                if (Mathf.Abs(signed) <= 0.01f) expectedDirection = 1f;
                else if (Mathf.Abs(Mathf.Abs(signed) - RailingHalfThickness) <= 0.01f)
                    expectedDirection = Mathf.Sign(signed);
            }

            if (expectedDirection != 0f
                && Vector2.Dot(new Vector2(normal.x, normal.z), outward) * expectedDirection < 0.999f)
            {
                LogBadFace(mesh, subMesh, center3,
                    new Vector3(outward.x, 0f, outward.y) * expectedDirection, normal, ref logged);
                bad++;
            }
        }
        return bad;
    }

    static void LogBadFace(Mesh mesh, int subMesh, Vector3 center, Vector3 expected,
        Vector3 normal, ref int logged)
        => LogBadFace(mesh, subMesh, center, normal, expected.ToString("F3"), ref logged);

    static void LogBadFace(Mesh mesh, int subMesh, Vector3 center, Vector3 normal,
        string expected, ref int logged)
    {
        if (logged >= 3) return;
        Debug.LogWarning("[Hadan Check] 법선 실패: 메쉬 " + mesh.name + ", 서브메쉬 " + subMesh
            + ", 면 중심 " + center.ToString("F3") + ", 법선 " + normal.ToString("F3")
            + ", 기대 방향 " + expected);
        logged++;
    }

    static void CheckBuildingProps(BuildingFloors manager, ref int failures)
    {
        int doors = 0;
        int stairs = 0;
        bool doorsValid = manager;
        bool openingsValid = manager;
        bool floorsValid = manager;
        bool stepHeightsValid = manager;
        bool stairPlacementValid = manager;
        bool stairHeadroomValid = manager;
        bool rooftopValid = manager;
        bool facadeUvValid = manager;
        int facadeUvFailures = 0;
        bool noAwningColliders = manager;
        bool railingsMerged = manager;

        if (manager)
        {
            for (int i = 0; i < manager.Buildings.Count; i++)
            {
                var entry = manager.Buildings[i];
                var openings = entry.root.GetComponentsInChildren<WindowFrame>(true);
                doors += openings.Length;
                doorsValid &= openings.Length == 1 && !openings[0].CanVault(openings[0].Center);
                if (openings.Length == 1)
                {
                    var walls = entry.floors[0].GetComponentsInChildren<WallSegment>(true);
                    for (int wall = 0; wall < walls.Length; wall++)
                    {
                        var collider = walls[wall].GetComponent<Collider>();
                        Vector3 normal = openings[0].Normal.normalized;
                        var ray = new Ray(openings[0].Center - normal, normal);
                        if (collider && collider.Raycast(ray, out _, 2f))
                            openingsValid = false;
                    }
                }

                for (int floor = 0; floor < entry.floors.Length; floor++)
                {
                    var filter = entry.floors[floor].GetComponent<MeshFilter>();
                    var renderer = entry.floors[floor].GetComponent<MeshRenderer>();
                    floorsValid &= filter && filter.sharedMesh && filter.sharedMesh.subMeshCount == 2
                        && Mathf.Abs(filter.sharedMesh.GetSubMesh(1).bounds.min.y - floor * FloorHeight) <= Epsilon
                        && Mathf.Abs(filter.sharedMesh.GetSubMesh(1).bounds.max.y
                            - ((floor + 1) * FloorHeight - FloorThickness)) <= Epsilon
                        && renderer && renderer.sharedMaterials.Length == 2;
                }

                var stairParts = entry.root.GetComponentsInChildren<MeshFilter>(true);
                var stairPolygon = new List<Vector2>(entry.polygon);
                int buildingStairs = 0;
                for (int part = 0; part < stairParts.Length; part++)
                {
                    if (!stairParts[part].name.StartsWith("Stair_", StringComparison.Ordinal)) continue;
                    buildingStairs++;
                    Vector3 first = stairParts[part].transform.TransformPoint(
                        new Vector3(0f, 0f, StairTread * 0.5f));
                    Vector3 entryPoint = stairParts[part].transform.TransformPoint(new Vector3(0f, 0f, -0.8f));
                    Vector3 last = stairParts[part].transform.TransformPoint(
                        new Vector3(0f, 0f, (StairSteps - 0.5f) * StairTread));
                    stairPlacementValid &= InsideStairArea(new Vector2(first.x, first.z), stairPolygon)
                        && InsideStairArea(new Vector2(entryPoint.x, entryPoint.z), stairPolygon)
                        && InsideStairArea(new Vector2(last.x, last.z), stairPolygon);
                    var heights = new List<float>();
                    var stairVertices = stairParts[part].sharedMesh.vertices;
                    for (int vertex = 0; vertex < stairVertices.Length; vertex++)
                    {
                        float y = stairVertices[vertex].y;
                        if (y < -Epsilon || y > FloorHeight + Epsilon) continue;
                        bool known = false;
                        for (int knownIndex = 0; knownIndex < heights.Count; knownIndex++)
                            if (Mathf.Abs(heights[knownIndex] - y) <= Epsilon) { known = true; break; }
                        if (!known) heights.Add(y);
                    }
                    heights.Sort();
                    for (int height = 1; height < heights.Count; height++)
                        if (heights[height] - heights[height - 1] > 0.25f + Epsilon)
                            stepHeightsValid = false;
                }
                stairs += buildingStairs;
                floorsValid &= entry.hasStairs
                    ? buildingStairs == entry.floors.Length - 1 : buildingStairs == 0;
                stairHeadroomValid &= !entry.hasStairs || HasStairHeadroom(entry);

                var exteriorWalls = entry.root.GetComponentsInChildren<WallSegment>(true);
                for (int wall = 0; wall < exteriorWalls.Length; wall++)
                {
                    if (!exteriorWalls[wall].name.StartsWith("ExteriorWall_", StringComparison.Ordinal)) continue;
                    var filter = exteriorWalls[wall].GetComponent<MeshFilter>();
                    var renderer = exteriorWalls[wall].GetComponent<MeshRenderer>();
                    var uv = filter && filter.sharedMesh ? filter.sharedMesh.uv : null;
                    bool valid = filter && filter.sharedMesh
                        && uv.Length == filter.sharedMesh.vertexCount && uv.Length >= 4
                        && (uv[1] - uv[0]).sqrMagnitude > Epsilon * Epsilon
                        && (uv[3] - uv[0]).sqrMagnitude > Epsilon * Epsilon
                        && renderer && renderer.sharedMaterial && renderer.sharedMaterial.mainTexture;
                    facadeUvValid &= valid;
                    if (!valid && facadeUvFailures++ < 3)
                        Debug.LogWarning("[Hadan Check] 외관 UV 실패: " + entry.root.name + "/"
                            + exteriorWalls[wall].name + " 바깥면(정점 0~3), UV "
                            + (uv != null ? uv.Length.ToString() : "없음") + "/"
                            + (filter && filter.sharedMesh ? filter.sharedMesh.vertexCount.ToString() : "메쉬 없음")
                            + ", 텍스처 " + (renderer && renderer.sharedMaterial
                                && renderer.sharedMaterial.mainTexture ? "있음" : "없음"));
                }

                float roofHeight = entry.floors.Length * FloorHeight;
                var roofFilter = entry.roof.GetComponent<MeshFilter>();
                railingsMerged &= CountNamed(entry.root.transform, "Railing") == 0
                    && roofFilter && roofFilter.sharedMesh
                    && roofFilter.sharedMesh.bounds.max.y >= roofHeight + RailingHeight - Epsilon;
                var all = entry.roof.GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length; j++)
                {
                    var part = all[j];
                    if (part.name != "WaterTank" && part.name != "AirConditioner") continue;
                    var propRenderer = part.GetComponent<Renderer>();
                    Vector2 point = new Vector2(part.position.x, part.position.z);
                    if (!propRenderer || propRenderer.bounds.min.y < roofHeight - Epsilon
                        || !PointInPolygon(point, new List<Vector2>(entry.polygon))
                        || DistanceToPolygonEdge(point, new List<Vector2>(entry.polygon)) < 1f)
                        rooftopValid = false;
                }

                var buildingParts = entry.root.GetComponentsInChildren<Transform>(true);
                for (int part = 0; part < buildingParts.Length; part++)
                    if (buildingParts[part].name == "Awning"
                        && buildingParts[part].GetComponentsInChildren<Collider>(true).Length > 0)
                        noAwningColliders = false;
            }
        }

        Check(doorsValid, "모든 건물에 실제 문 컴포넌트가 1개 있다 (" + doors + ")", ref failures);
        Check(openingsValid, "1층 외벽 메쉬가 문 중심을 막지 않는다", ref failures);
        Check(floorsValid, "건물은 층 메쉬와 필요한 계단을 가진다 (계단 " + stairs + ")", ref failures);
        Check(FloorThickness <= 0.25f + Epsilon && stepHeightsValid,
            "바닥 두께와 계단 한 칸 높이가 0.25m 이하이다", ref failures);
        Check(stairPlacementValid, "모든 계단의 첫 단·입구 앞 0.8m·마지막 단이 축소 실내 다각형 안이다", ref failures);
        Check(stairHeadroomValid, "모든 계단 발판 위 2.0m에 다른 계단·바닥이 없다", ref failures);
        Check(noAwningColliders, "차양에 콜라이더가 없다", ref failures);
        Check(railingsMerged, "난간이 지붕 메쉬에 병합됐다", ref failures);
        Check(rooftopValid, "난간과 옥상 소품이 자기 지붕 위와 외곽 안쪽에 있다", ref failures);
        Check(facadeUvValid, "외벽 바깥면에 기존 외관 텍스처와 UV가 있다", ref failures);

        int signs = CountBatchBoxes("Signs_", true);
        int airConditioners = CountBatchBoxes("WallAirConditioners", false);
        int roofRooms = manager ? CountNamed(manager.transform, "RoofRoom") : 0;
        int wires = CountBatchBoxes("Wires", false) / 2;
        Check(manager && Mathf.Abs(signs - 356) <= Mathf.CeilToInt(356 * 0.05f)
              && Mathf.Abs(airConditioners - 1623) <= Mathf.CeilToInt(1623 * 0.05f)
              && Mathf.Abs(roofRooms - 91) <= Mathf.CeilToInt(91 * 0.05f)
              && Mathf.Abs(wires - 1278) <= Mathf.CeilToInt(1278 * 0.05f),
            "15번 외관 수가 ±5%다 (간판 " + signs + ", 실외기 " + airConditioners
            + ", 옥탑방 " + roofRooms + ", 전선 " + wires + ")", ref failures);
    }

    static int CountBatchBoxes(string name, bool prefix)
    {
        int count = 0;
        var filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < filters.Length; i++)
        {
            bool matches = prefix ? filters[i].name.StartsWith(name, StringComparison.Ordinal)
                : filters[i].name == name;
            if (matches && filters[i].sharedMesh) count += filters[i].sharedMesh.triangles.Length / 36;
        }
        return count;
    }

    static void CheckFloorLocator(BuildingFloors manager, ref int failures)
    {
        bool inside = false;
        bool outside = false;
        if (manager && manager.Buildings.Count > 0)
        {
            var entry = manager.Buildings[0];
            var polygon = new List<Vector2>(entry.polygon);
            var triangles = new List<int>();
            if (Triangulate(polygon, triangles) && triangles.Count >= 3)
            {
                Vector2 point = (polygon[triangles[0]] + polygon[triangles[1]] + polygon[triangles[2]]) / 3f;
                int wanted = Mathf.Min(1, entry.floors.Length - 1);
                inside = manager.TryLocate(new Vector3(point.x, wanted * FloorHeight + 1f, point.y),
                    out int building, out int floor) && building == 0 && floor == wanted;

                var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                for (int i = 0; i < manager.Buildings.Count; i++)
                    for (int vertex = 0; vertex < manager.Buildings[i].polygon.Length; vertex++)
                        min = Vector2.Min(min, manager.Buildings[i].polygon[vertex]);
                outside = !manager.TryLocate(new Vector3(min.x - 32f, 1f, min.y - 32f), out _, out _);
            }
        }

        Vector2[] concave =
        {
            new Vector2(0f, 0f), new Vector2(0f, 3f), new Vector2(1f, 3f),
            new Vector2(1f, 1f), new Vector2(3f, 1f), new Vector2(3f, 0f)
        };
        bool concaveOutside = !BuildingFloors.ContainsFloor(
            new Vector3(2f, 1f, 2f), concave, 2, FloorHeight, out _);
        Check(inside && outside && concaveOutside,
            "층 판정이 건물 번호·층과 평면 외부·오목한 빈 곳을 구분한다", ref failures);
    }

    static void CheckHadanDetails(MapData map, Transform buildings,
        Dictionary<long, List<Vector2>> polygons, ref int failures)
    {
        var roads = RoadInfos(map);
        var types = new int[4];
        bool commercialSigns = buildings;
        bool signsTouchWalls = buildings;
        bool greenResidentialRoofs = buildings;

        if (buildings)
        {
            for (int i = 0; i < buildings.childCount; i++)
            {
                Transform building = buildings.GetChild(i);
                if (!TryBuildingId(building.name, out long id) || !polygons.TryGetValue(id, out var polygon))
                    continue;
                MapElement element = null;
                for (int j = 0; j < map.elements.Length; j++)
                    if (map.elements[j].id == id) { element = map.elements[j]; break; }
                if (element == null) continue;

                float area = Mathf.Abs(SignedArea(polygon)) / (CityScale * CityScale);
                int floors = FloorCount(element.tags, area);
                BuildingKind kind = ClassifyBuilding(element.tags, polygon, floors, area, roads);
                types[(int)kind]++;

                if (kind == BuildingKind.Commercial)
                {
                    var marker = building.Find("CommercialSignAnchor");
                    commercialSigns &= marker;
                    var all = building.GetComponentsInChildren<Transform>(true);
                    for (int j = 0; j < all.Length; j++)
                        if (all[j].name.EndsWith("SignAnchor", StringComparison.Ordinal))
                            signsTouchWalls &= DistanceToPolygonEdge(
                                new Vector2(all[j].position.x, all[j].position.z), polygon) <= 0.3f + Epsilon;
                }

                if (kind == BuildingKind.Commercial) continue;
                var roof = building.Find("Roof");
                var renderer = roof ? roof.GetComponent<MeshRenderer>() : null;
                var shared = renderer ? renderer.sharedMaterials : null;
                if (shared == null || shared.Length == 0 || !shared[0]) greenResidentialRoofs = false;
                else
                {
                    Color roofColor = shared[0].color;
                    greenResidentialRoofs &= roofColor.g > roofColor.r && roofColor.g > roofColor.b;
                }
            }
        }

        Check(types[(int)BuildingKind.Commercial] > 0 && types[(int)BuildingKind.Apartment] > 0
              && types[(int)BuildingKind.House] > 0 && types[(int)BuildingKind.MixedUse] > 0,
            "건물 유형 4가지가 모두 있다 (상가 " + types[(int)BuildingKind.Commercial]
            + ", 아파트 " + types[(int)BuildingKind.Apartment]
            + ", 주택 " + types[(int)BuildingKind.House]
            + ", 상가주택 " + types[(int)BuildingKind.MixedUse] + ")", ref failures);
        Check(commercialSigns, "모든 상가건물에 도로 쪽 간판 기준점이 있다", ref failures);
        Check(signsTouchWalls, "간판 기준점이 벽에서 0.3m 이내다", ref failures);
        Check(greenResidentialRoofs, "주거 유형 지붕이 초록 계열이다", ref failures);

        bool noDecorationColliders = true;
        bool hasSigns = false;
        bool hasAirConditioners = false;
        bool hasWires = false;
        bool hasRoofRoomDoors = false;
        string[] roots = { "Buildings", "BuildingDecorations", "StreetProps" };
        for (int i = 0; i < roots.Length; i++)
        {
            var root = GameObject.Find(roots[i]);
            if (!root) { noDecorationColliders = false; continue; }
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < all.Length; j++)
            {
                string name = all[j].name;
                hasSigns |= name.StartsWith("Signs_", StringComparison.Ordinal);
                hasAirConditioners |= name == "WallAirConditioners";
                hasWires |= name == "Wires";
                hasRoofRoomDoors |= name == "RoofRoomDoors";
                if ((name.StartsWith("Signs_", StringComparison.Ordinal)
                     || name == "WallAirConditioners" || name == "Wires" || name == "RoofRoomDoors")
                    && all[j].GetComponentsInChildren<Collider>(true).Length > 0)
                    noDecorationColliders = false;
            }
        }
        Check(noDecorationColliders, "간판·실외기·전선·옥탑방 문 무늬에 콜라이더가 없다", ref failures);
        Check(hasSigns && hasAirConditioners && hasWires && hasRoofRoomDoors,
            "간판·실외기·전선·옥탑방 문 무늬의 결합 메쉬가 있다", ref failures);
    }

    static void CheckRoadDetails(ref int failures)
    {
        var roads = GameObject.Find("Roads");
        var renderer = roads ? roads.GetComponent<MeshRenderer>() : null;
        var texture = renderer && renderer.sharedMaterial ? renderer.sharedMaterial.mainTexture : null;
        string path = texture ? AssetDatabase.GetAssetPath(texture) : "";
        Check(renderer && renderer.sharedMaterial.name == "hadan_road"
            && (path == AsphaltTexturePath || path == RoadTextureFolder + "/road_atlas_baseColor.png"),
            "도로 재질에 아틀라스 또는 아스팔트 텍스처가 있다", ref failures);
        var curbs = GameObject.Find("Curbs");
        var filters = curbs ? curbs.GetComponentsInChildren<MeshFilter>(true) : Array.Empty<MeshFilter>();
        bool valid = filters.Length > 0 && curbs.GetComponentsInChildren<Collider>(true).Length == 0;
        foreach (var filter in filters)
            valid &= filter.sharedMesh && filter.sharedMesh.vertexCount > 0
                && (GameObjectUtility.GetStaticEditorFlags(filter.gameObject) & StaticEditorFlags.NavigationStatic) == 0;
        Check(valid, "연석 메쉬가 있고 콜라이더와 NavigationStatic이 없다 (" + filters.Length + ")", ref failures);
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
        bool assetCars = root;
        var carTypes = new HashSet<string>();
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
                        var box = prop.GetComponent<BoxCollider>();
                        collidersValid &= box && box.enabled && !box.isTrigger
                            && prop.GetComponentsInChildren<Collider>(true).Length == 1;
                        var visual = prop.Find("CarVisual");
                        var model = visual && visual.childCount == 1 ? visual.GetChild(0) : null;
                        var source = model ? PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject) : null;
                        string path = source ? AssetDatabase.GetAssetPath(source) : "";
                        bool recognized = false;
                        foreach (string name in CarNames)
                            recognized |= path == VehicleFolder + "/hadan_car_" + name + ".fbx";
                        assetCars &= recognized && !prop.Find("Body") && !prop.Find("Roof");
                        if (recognized) carTypes.Add(path);
                        if (model && box)
                        {
                            var bounds = ModelBounds(model, prop);
                            collidersValid &= Vector3.Distance(bounds.size, box.size) < 0.01f
                                && Vector3.Distance(bounds.center, box.center) < 0.01f;
                            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
                            {
                                var original = PrefabUtility.GetCorrespondingObjectFromSource(filter);
                                assetCars &= original && original.sharedMesh == filter.sharedMesh
                                    && (GameObjectUtility.GetStaticEditorFlags(filter.gameObject) & StaticEditorFlags.BatchingStatic) != 0;
                            }
                            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                            {
                                var original = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                                var shared = renderer.sharedMaterials;
                                assetCars &= original && original.sharedMaterials.Length == shared.Length;
                                if (!original || original.sharedMaterials.Length != shared.Length) continue;
                                for (int slot = 0; slot < shared.Length; slot++)
                                    assetCars &= shared[slot] == original.sharedMaterials[slot];
                            }
                        }
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

        Check(cars == 56 && assetCars && carTypes.Count >= 3,
            "차 56대 모두 원본 FBX·공유 메쉬·원본 재질이며 3종 이상이다 (" + cars + "/" + carTypes.Count + ")", ref failures);
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

    static int CountStairBuildings(BuildingFloors manager)
    {
        if (!manager || manager.Buildings == null) return 0;
        int count = 0;
        for (int i = 0; i < manager.Buildings.Count; i++)
            if (manager.Buildings[i].hasStairs) count++;
        return count;
    }

    static bool HasStairHeadroom(BuildingFloors.Entry entry)
    {
        var parts = entry.root.GetComponentsInChildren<MeshFilter>(true);
        for (int stairIndex = 0; stairIndex < parts.Length; stairIndex++)
        {
            var stair = parts[stairIndex];
            if (!stair.name.StartsWith("Stair_", StringComparison.Ordinal)) continue;
            for (int step = -1; step < StairSteps; step++)
            {
                float y = step < 0 ? 0f : (step + 1f) * FloorHeight / StairSteps;
                float z0 = step < 0 ? -StairEntryDepth : step * StairTread;
                float z1 = step < 0 ? 0f : (step + 1f) * StairTread;
                float halfWidth = StairWidth * 0.5f - 0.02f;
                Vector2[] tread =
                {
                    Xz(stair.transform.TransformPoint(new Vector3(-halfWidth, y, z0 + 0.02f))),
                    Xz(stair.transform.TransformPoint(new Vector3(-halfWidth, y, z1 - 0.02f))),
                    Xz(stair.transform.TransformPoint(new Vector3(halfWidth, y, z1 - 0.02f))),
                    Xz(stair.transform.TransformPoint(new Vector3(halfWidth, y, z0 + 0.02f)))
                };
                float worldY = stair.transform.TransformPoint(new Vector3(0f, y, 0f)).y;
                for (int part = 0; part < parts.Length; part++)
                    if (parts[part] != stair
                        && (parts[part].name.StartsWith("Floor_", StringComparison.Ordinal)
                            || parts[part].name.StartsWith("Stair_", StringComparison.Ordinal))
                        && HasSurfaceAbove(parts[part], tread, worldY, 2f))
                        return false;
            }
        }
        return true;
    }

    static bool HasSurfaceAbove(MeshFilter filter, Vector2[] tread, float treadY, float clearance)
    {
        var vertices = filter.sharedMesh.vertices;
        var triangles = filter.sharedMesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 a = filter.transform.TransformPoint(vertices[triangles[i]]);
            Vector3 b = filter.transform.TransformPoint(vertices[triangles[i + 1]]);
            Vector3 c = filter.transform.TransformPoint(vertices[triangles[i + 2]]);
            if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - Mathf.Min(a.y, Mathf.Min(b.y, c.y)) > Epsilon)
                continue;
            float height = a.y - treadY;
            if (height <= Epsilon || height >= clearance - Epsilon) continue;
            Vector2[] triangle = { Xz(a), Xz(b), Xz(c) };
            for (int point = 0; point < tread.Length; point++)
                if (InsideConvex(tread[point], triangle)) return true;
            for (int point = 0; point < triangle.Length; point++)
                if (InsideConvex(triangle[point], tread)) return true;
            for (int treadEdge = 0; treadEdge < tread.Length; treadEdge++)
                for (int triangleEdge = 0; triangleEdge < triangle.Length; triangleEdge++)
                    if (SegmentDistance(tread[treadEdge], tread[(treadEdge + 1) % tread.Length],
                            triangle[triangleEdge], triangle[(triangleEdge + 1) % triangle.Length]) <= Epsilon)
                        return true;
        }
        return false;
    }

    static Vector2 Xz(Vector3 point) => new Vector2(point.x, point.z);

    static bool InsideConvex(Vector2 point, Vector2[] polygon)
    {
        bool positive = false;
        bool negative = false;
        for (int i = 0; i < polygon.Length; i++)
        {
            float side = Cross(polygon[i], polygon[(i + 1) % polygon.Length], point);
            positive |= side > Epsilon;
            negative |= side < -Epsilon;
        }
        return !positive || !negative;
    }

    static int CheckDoorNavMesh(BuildingFloors manager, out int samples)
    {
        samples = 0;
        if (!manager || manager.Buildings == null) return 0;

        int valid = 0;
        for (int i = 0; i < manager.Buildings.Count && samples < 20; i++)
        {
            var opening = manager.Buildings[i].root.GetComponentInChildren<WindowFrame>(true);
            if (!opening) continue;

            Vector3 center = opening.Center;
            center.y = 0f;
            samples++;
            if (NavMesh.SamplePosition(center, out _, 0.2f, NavMesh.AllAreas)) valid++;
        }
        return valid;
    }

    static int CheckBuildingPaths(BuildingFloors manager)
    {
        if (!manager || manager.Buildings == null) return 0;
        var candidates = new List<int>();
        for (int i = 0; i < manager.Buildings.Count; i++)
            if (manager.Buildings[i].hasStairs) candidates.Add(i);

        var random = new System.Random(16);
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int swap = random.Next(i + 1);
            int value = candidates[i];
            candidates[i] = candidates[swap];
            candidates[swap] = value;
        }

        int reachable = 0;
        int samples = Mathf.Min(5, candidates.Count);
        bool loggedFailure = false;
        for (int i = 0; i < samples; i++)
        {
            var entry = manager.Buildings[candidates[i]];
            var path = new NavMeshPath();
            bool complete = NavMesh.SamplePosition(entry.doorFront, out NavMeshHit start, 0.75f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(entry.topLanding, out NavMeshHit target, 2f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(start.position, target.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete;
            if (complete) reachable++;
            else if (!loggedFailure)
            {
                loggedFailure = true;
                var opening = entry.root.GetComponentInChildren<WindowFrame>(true);
                Transform firstStair = entry.floors[0].transform.Find("Stair_0");
                Vector3 doorInside = opening ? opening.Center - opening.Normal * 0.8f : default;
                doorInside.y = 0f;
                Vector3 stairBottom = firstStair
                    ? firstStair.TransformPoint(new Vector3(0f, 0f, -StairEntryDepth * 0.5f)) : default;
                Vector3 stairTop = firstStair
                    ? firstStair.TransformPoint(new Vector3(0f, FloorHeight, StairSteps * StairTread + 0.2f)) : default;
                Debug.LogWarning("[Hadan Check] 경로 구간 실패 " + entry.root.name
                    + ": 문 밖→문 안 " + PathStatus(entry.doorFront, doorInside)
                    + ", 문 안→계단 아래 " + PathStatus(doorInside, stairBottom)
                    + ", 계단 위→꼭대기 " + PathStatus(stairTop, entry.topLanding));
            }
        }
        return reachable;
    }

    static string PathStatus(Vector3 from, Vector3 to)
    {
        if (!NavMesh.SamplePosition(from, out NavMeshHit start, 0.5f, NavMesh.AllAreas)) return "시작 NavMesh 없음";
        if (!NavMesh.SamplePosition(to, out NavMeshHit target, 0.5f, NavMesh.AllAreas)) return "끝 NavMesh 없음";

        var path = new NavMeshPath();
        return NavMesh.CalculatePath(start.position, target.position, NavMesh.AllAreas, path)
            ? path.status.ToString() : "계산 실패";
    }

    static int CountUniqueMeshTriangles()
    {
        var meshes = new HashSet<Mesh>();
        var filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int triangles = 0;
        for (int i = 0; i < filters.Length; i++)
            if (filters[i].sharedMesh && meshes.Add(filters[i].sharedMesh))
                for (int subMesh = 0; subMesh < filters[i].sharedMesh.subMeshCount; subMesh++)
                    triangles += (int)filters[i].sharedMesh.GetIndexCount(subMesh) / 3;
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

    class AssetModel
    {
        public GameObject model;
        public string path;
        public Bounds bounds;
    }

    struct BuildingBox
    {
        public Vector2 center;
        public Vector2 forward;
        public float width;
        public float depth;
    }

    class AssetBuildingPlan
    {
        public long id;
        public BuildingKind kind;
        public BuildingBox box;
        public int asset;
        public float height;
        public Vector3 scale;
        public bool clipped;
        public bool heightClipped;
    }

    struct AssetMaterialPair
    {
        public Material opaque;
        public Material fade;
    }

    enum BuildingKind
    {
        Commercial,
        Apartment,
        House,
        MixedUse
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
        public Material floor;
        public Material stair;
        public Material[][] interiorWalls;
        public Material[] commercialWalls;
        public Material[] mixedUseWalls;
        public Material[] houseWalls;
        public Material[] apartmentWalls;
        public Material commercialRoof;
        public Material[] residentialRoofs;
        public Material shopfront;
        public Material balcony;
        public Material apartmentStripe;
        public Material roofRoom;
        public Material roofRoomDoor;
        public Material[] tanks;
        public Material airConditioner;
        public Material[] signMaterials;
        public Material apartmentNumber;
        public Material wire;
        public Material trunk;
        public Material leaves;
        public Material pole;
        public Material car;
        public Material carRoof;

        public MeshBatch shopfrontBatch;
        public MeshBatch balconyBatch;
        public MeshBatch apartmentStripeBatch;
        public MeshBatch apartmentNumberBatch;
        public MeshBatch roofRoomDoorBatch;

        public void CreateBatches()
        {
            shopfrontBatch = new MeshBatch("Shopfronts", shopfront);
            balconyBatch = new MeshBatch("BalconyRailings", balcony);
            apartmentStripeBatch = new MeshBatch("ApartmentStripes", apartmentStripe);
            apartmentNumberBatch = new MeshBatch("ApartmentNumbers", apartmentNumber);
            roofRoomDoorBatch = new MeshBatch("RoofRoomDoors", roofRoomDoor);
        }

        public void EmitBatches(List<Mesh> meshes)
        {
            var root = new GameObject("BuildingDecorations").transform;
            shopfrontBatch.Emit(root, meshes);
            balconyBatch.Emit(root, meshes);
            apartmentStripeBatch.Emit(root, meshes);
            apartmentNumberBatch.Emit(root, meshes);
            roofRoomDoorBatch.Emit(root, meshes);
            V01SceneBuilder.MarkStatic(root);
        }
    }

    class PropCounts
    {
        public readonly int[] types = new int[4];
        public readonly int[] assets = new int[16];
        public readonly int[] carTypes = new int[8];
        public int curbMeshes;
        public float curbLength;
        public int shopfronts;
        public int clippedAssets;
        public int heightClippedAssets;
        public int skippedAssets;
        public int doors;
        public int awnings;
        public int rooftop;
        public int signs;
        public int wallAirConditioners;
        public int roofRooms;
        public int wires;
        public int trees;
        public int poles;
        public int cars;
    }

    class MeshBatch
    {
        readonly string name;
        readonly Material material;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> triangles = new List<int>();

        public MeshBatch(string name, Material material)
        {
            this.name = name;
            this.material = material;
        }

        public void AddBox(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 half = size * 0.5f;
            var corners = new[]
            {
                new Vector3(-half.x, -half.y, -half.z), new Vector3(half.x, -half.y, -half.z),
                new Vector3(half.x, half.y, -half.z), new Vector3(-half.x, half.y, -half.z),
                new Vector3(-half.x, -half.y, half.z), new Vector3(half.x, -half.y, half.z),
                new Vector3(half.x, half.y, half.z), new Vector3(-half.x, half.y, half.z)
            };
            int[,] faces =
            {
                { 4, 5, 6, 7 }, { 1, 0, 3, 2 }, { 5, 1, 2, 6 },
                { 0, 4, 7, 3 }, { 7, 6, 2, 3 }, { 0, 1, 5, 4 }
            };
            for (int face = 0; face < 6; face++)
            {
                int first = vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                    vertices.Add(center + rotation * corners[faces[face, corner]]);
                uvs.Add(Vector2.zero);
                uvs.Add(Vector2.right);
                uvs.Add(Vector2.one);
                uvs.Add(Vector2.up);
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 3);
            }
        }

        public void AddQuad(Vector3 center, Vector3 right, Vector3 up, float width, float height,
            Vector2 uvMin, Vector2 uvMax)
        {
            right = right.normalized * width * 0.5f;
            up = up.normalized * height * 0.5f;
            int first = vertices.Count;
            vertices.Add(center - right - up);
            vertices.Add(center + right - up);
            vertices.Add(center + right + up);
            vertices.Add(center - right + up);
            uvs.Add(uvMin);
            uvs.Add(new Vector2(uvMax.x, uvMin.y));
            uvs.Add(uvMax);
            uvs.Add(new Vector2(uvMin.x, uvMax.y));
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 3);
        }

        public void AddBeam(Vector3 from, Vector3 to, float thickness)
        {
            Vector3 direction = to - from;
            if (direction.sqrMagnitude <= Epsilon) return;
            AddBox((from + to) * 0.5f, new Vector3(thickness, thickness, direction.magnitude),
                Quaternion.LookRotation(direction));
        }

        public void Emit(Transform parent, List<Mesh> meshes)
        {
            if (vertices.Count == 0) return;
            var mesh = new Mesh { name = "Hadan" + name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
    }

    readonly struct WallSpan
    {
        public readonly float start;
        public readonly float end;
        public readonly float bottom;
        public readonly float top;

        public WallSpan(float start, float end, float bottom, float top)
        {
            this.start = start;
            this.end = end;
            this.bottom = bottom;
            this.top = top;
        }
    }

    readonly struct StairPlan
    {
        public readonly Vector2 start;
        public readonly Vector2 direction;
        public readonly Vector2 right;
        public readonly Vector2 secondOffset;

        public StairPlan(Vector2 start, Vector2 direction, Vector2 secondOffset)
        {
            this.start = start;
            this.direction = direction.normalized;
            right = new Vector2(this.direction.y, -this.direction.x);
            this.secondOffset = secondOffset;
        }

        public StairPlan Flight(int floor)
            => (floor & 1) == 0 ? this
                : new StairPlan(start + direction * (StairSteps * StairTread) + secondOffset,
                    -direction, Vector2.zero);

        public Vector2[] HoleCorners()
        {
            float halfWidth = StairWidth * 0.5f;
            float run = StairSteps * StairTread;
            return new[]
            {
                start - right * halfWidth,
                start - right * halfWidth + direction * run,
                start + right * halfWidth + direction * run,
                start + right * halfWidth
            };
        }
    }

    class CarSlot
    {
        public Vector2 position;
        public Vector2 direction;
        public float angle;
        public long seed;
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
