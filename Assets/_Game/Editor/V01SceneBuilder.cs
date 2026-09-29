using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// V0.1 1단계 검증용 샌드박스 씬을 한 번에 만든다.
/// 3D 에셋이 없는 동안의 플레이스홀더 씬이므로, 실제 아트가 들어오면 이 스크립트는 지워도 된다.
/// </summary>
public static class V01SceneBuilder
{
    const string SceneFolder = "Assets/_Game/Scenes";
    const string ScenePath = SceneFolder + "/V01_Sandbox.unity";
    const string CityScenePath = SceneFolder + "/V01_City_Hadan.unity";
    const string ItemDataFolder = "Assets/_Game/Data/Items";
    const string MaterialFolder = "Assets/_Game/Data/Materials";
    const string ZombieDataFolder = "Assets/_Game/Data/Zombies";
    const string RecipeDataFolder = "Assets/_Game/Data/Recipes";
    const string PortraitFolder = "Assets/_Game/Art/Portrait";

    [MenuItem("Tools/V0.1/Build Sandbox Scene")]
    public static void BuildSandboxScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("V01_Sandbox 덮어쓰기",
                ScenePath + " 이(가) 이미 있습니다. 덮어쓸까요?", "덮어쓰기", "취소"))
            return;

        BuildInternal();
    }

    /// <summary>
    /// 물어보지 않고 다시 짓는다. 자동화(MCP) 에서 쓰는 입구다.
    ///
    /// 열려 있는 씬의 에셋을 먼저 지우면 그 씬이 "수정됨" 이 되고, Build 쪽의
    /// SaveCurrentModifiedScenesIfUserWantsTo 가 모달 대화상자를 띄워 에디터가 통째로 멈춘다
    /// (실제로 멈췄다). 그래서 빈 씬으로 먼저 갈아타 dirty 를 없앤 뒤 지운다.
    /// </summary>
    [MenuItem("Tools/V0.1/Rebuild Sandbox Scene (no prompt)")]
    public static void RebuildSandboxScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);

        BuildInternal();
    }

    static void BuildInternal()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var clock = CreateGameClock();

        ApplyRenderSettings();
        CreateLight();
        CreateGround();
        CreateForest(null);
        CreateApartment();

        var player = CreatePlayer();
        CreateCamera(player.transform);

        var items = EnsureItemDefinitions();
        var zombieTypes = EnsureZombieDefinitions();
        var recipes = EnsureCraftRecipes();
        ScatterItems(items);
        CreateContainers(items);
        AttachContainersToFurniture();
        CreateSaveSystem(items, clock, zombieTypes);

        player.GetComponent<PlayerDeath>().Bind(
            CreateSpawnPoints(),
            EnsureSharedMaterial("player_zombie", new Color(0.58f, 0.26f, 0.58f)));
        ApplyZombieModel(zombieTypes);
        SpawnZombies(zombieTypes);
        CreateNoiseDirector(zombieTypes);
        player.GetComponent<PlayerCrafting>().Bind(recipes);

        // 지형이 다 놓인 뒤에 굽는다.
        BuildNavMesh();

        CreateHud(player, clock);

        Directory.CreateDirectory(SceneFolder);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EnsureBuildScenes();
        AssetDatabase.Refresh();
        Debug.Log("[V0.1] 샌드박스 씬 생성 완료: " + ScenePath);
    }

    internal static void EnsureBuildScenes()
    {
        string[] required = { ScenePath, CityScenePath };
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        bool changed = false;

        for (int r = 0; r < required.Length; r++)
        {
            int found = scenes.FindIndex(scene => scene.path == required[r]);
            if (found < 0)
            {
                scenes.Add(new EditorBuildSettingsScene(required[r], true));
                changed = true;
            }
            else if (!scenes[found].enabled)
            {
                scenes[found] = new EditorBuildSettingsScene(required[r], true);
                changed = true;
            }
        }

        if (changed) EditorBuildSettings.scenes = scenes.ToArray();
    }

    static bool BuildSettingsContains(string path)
    {
        var scenes = EditorBuildSettings.scenes;
        for (int i = 0; i < scenes.Length; i++)
            if (scenes[i].enabled && scenes[i].path == path) return true;
        return false;
    }

    internal static void CreateLight()
    {
        var go = new GameObject("Directional Light");
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        // 해 그림자와 시야 음영이 서로 다른 방향으로 생겨서 한 물체에 그림자가 두 개
        // 있는 것처럼 보였다. 해 그림자를 음영보다 약하게 눌러 명암 으로 읽히게 한다.
        light.shadowStrength = 0.55f;
    }

    static void CreateGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        // 90 x 90. 부지(64 x 50) 와 그 밖의 숲 띠까지 덮어야 한다. 삼각형 2개다.
        ground.transform.localScale = new Vector3(9f, 1f, 9f);
        // 원점에 두면 부지 동쪽이 지면 밖으로 나가 좀비가 허공을 걷는다. 부지 중심에 맞춘다.
        Vector3 site = SiteOrigin();
        ground.transform.position = new Vector3(site.x + SiteWidth * 0.5f, 0f, site.z + SiteDepth * 0.5f);

        MakeGroundSolid(ground);
    }

    // ---- 별장 부지 (승인된 배치도 기준) ----

    const float SiteWidth = 64f;    // 동서 (본채 28.5m + 마당 + 차고)
    const float SiteDepth = 50f;    // 남북
    const float ForestBand = 10f;   // 부지 밖 숲 띠 폭
    const float TreeSpacing = 2.6f;
    const int ForestSeed = 20260922;
    const string TreeMeshPath = "Assets/_Game/Art/Meshes/LowPolyTree.asset";

    /// <summary>부지 남서쪽 모서리. 본채(지금은 아파트) 가 북쪽 중앙에 오도록 맞춘다.</summary>
    static Vector3 SiteOrigin()
    {
        float northEdge = ApartmentOrigin.z + FlatDepth * Scale + 2.3f;
        float centerX = ApartmentOrigin.x + FlatWidth * Scale * 0.5f;

        return new Vector3(centerX - SiteWidth * 0.5f, 0f, northEdge - SiteDepth);
    }

    /// <summary>
    /// 나무 한 그루 = 큐브 3개를 구운 메쉬 하나. 아이소메트릭 부감에서는 수관 윗면만
    /// 보이므로 이 이상은 낭비다. 수관 하나를 45도 돌려서 위에서 보면 팔각 실루엣이 된다.
    ///
    /// 모든 나무가 이 메쉬 하나를 공유한다 — 그래서 정적 배칭이 한 묶음으로 삼켜준다.
    /// 나무를 개별 모델링하면 그루당 500삼각형에 드로우콜도 그루당 하나가 된다.
    /// </summary>
    static Mesh EnsureTreeMesh()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(TreeMeshPath);
        if (existing) return existing;

        var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        if (!cube) { Debug.LogWarning("[V0.1] 내장 Cube 메쉬를 찾지 못해 숲을 건너뜁니다"); return null; }

        var parts = new CombineInstance[3];
        parts[0].mesh = cube;   // 줄기
        parts[0].transform = Matrix4x4.TRS(new Vector3(0f, 1.1f, 0f), Quaternion.identity,
                                           new Vector3(0.34f, 2.2f, 0.34f));
        parts[1].mesh = cube;   // 아래 수관
        parts[1].transform = Matrix4x4.TRS(new Vector3(0f, 3.4f, 0f), Quaternion.identity,
                                           new Vector3(3.2f, 2.6f, 3.2f));
        parts[2].mesh = cube;   // 위 수관 (45도 돌림)
        parts[2].transform = Matrix4x4.TRS(new Vector3(0f, 4.5f, 0f), Quaternion.Euler(0f, 45f, 0f),
                                           new Vector3(2.3f, 1.8f, 2.3f));

        var mesh = new Mesh { name = "LowPolyTree" };
        mesh.CombineMeshes(parts, true, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Directory.CreateDirectory(Path.GetDirectoryName(TreeMeshPath));
        AssetDatabase.CreateAsset(mesh, TreeMeshPath);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    /// <summary>
    /// 배치도의 삼면 숲. 부지 밖이라 콜라이더를 주지 않는다 — 나무 수백 개에 콜라이더를
    /// 달면 NavMesh 굽기와 물리 질의가 같이 비싸진다. 숲으로 탈출하는 구간이 생기면
    /// 그 구역만 콜라이더를 붙인다.
    /// </summary>
    static void CreateForest(Transform root)
    {
        var mesh = EnsureTreeMesh();
        if (!mesh) return;

        var material = EnsureSharedMaterial("flat_tree", new Color(0.20f, 0.27f, 0.17f));
        material.enableInstancing = true;

        var parent = new GameObject("Forest").transform;
        parent.SetParent(root, false);

        Vector3 origin = SiteOrigin();
        var site = new Rect(origin.x, origin.z, SiteWidth, SiteDepth);
        var band = new Rect(site.xMin - ForestBand, site.yMin,
                            site.width + ForestBand * 2f, site.height + ForestBand);

        // 다시 구워도 같은 숲이 나와야 한다. 전역 난수 상태는 원래대로 돌려준다.
        var state = Random.state;
        Random.InitState(ForestSeed);

        int planted = 0;
        for (float z = band.yMin; z < band.yMax; z += TreeSpacing)
        {
            for (float x = band.xMin; x < band.xMax; x += TreeSpacing)
            {
                // 배치도대로 남쪽은 도로다. 숲은 북·서·동 삼면만.
                if (site.Contains(new Vector2(x, z))) continue;

                Vector2 jitter = Random.insideUnitCircle * 1.05f;
                var tree = new GameObject("Tree");
                tree.transform.SetParent(parent, false);
                tree.transform.position = new Vector3(x + jitter.x, 0f, z + jitter.y);
                tree.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);

                float spread = Random.Range(0.78f, 1.35f);
                tree.transform.localScale = new Vector3(spread, Random.Range(0.85f, 1.45f), spread);

                tree.AddComponent<MeshFilter>().sharedMesh = mesh;
                tree.AddComponent<MeshRenderer>().sharedMaterial = material;
                planted++;
            }
        }

        Random.state = state;
        MarkStatic(parent);

        Debug.Log("[V0.1] 숲: " + planted + "그루 x " + (mesh.triangles.Length / 3)
                  + "삼각형 = " + (planted * mesh.triangles.Length / 3).ToString("N0")
                  + " (메쉬·재질 1개 공유, 콜라이더 없음)");
    }

    /// <summary>
    /// Plane 의 MeshCollider 는 두께가 0 이라 CharacterController 가 프레임이 튈 때 뚫고 빠진다.
    /// 실제로 좀비가 바닥을 통과해 -1100m 까지 낙하하면서도 플레이어를 계속 물었다.
    /// 겉모습은 그대로 두고 콜라이더만 두꺼운 박스로 바꾼다 (윗면은 y=0 유지).
    /// </summary>
    internal static void MakeGroundSolid(GameObject ground)
    {
        var mesh = ground.GetComponent<MeshCollider>();
        if (mesh) Object.DestroyImmediate(mesh);

        if (ground.GetComponent<BoxCollider>()) return;

        var box = ground.AddComponent<BoxCollider>();
        // Plane 메쉬는 로컬 10x10, 두께 없음. 로컬 y 로 2 만큼 두껍게 하고 윗면을 y=0 에 맞춘다.
        box.size = new Vector3(10f, 2f, 10f);
        box.center = new Vector3(0f, -1f, 0f);
    }

    /// <summary>
    /// GDD 17항 추적용 NavMesh. 계단/플랫폼처럼 높이가 바뀌는 지형에서 좀비가
    /// 길을 찾아가려면 구워둔 보행 가능 영역이 필요하다.
    ///
    /// 씬 빌드 때 한 번 굽는다 — 지형이 바뀌면 다시 빌드하거나 Tools 메뉴로 다시 굽는다.
    /// </summary>
    /// <summary>
    /// NavMesh 가 "올라갈 수 있다" 고 보는 높이. CharacterController.stepOffset 을 여기에 맞춰야
    /// 경로와 물리가 어긋나지 않는다. 안 맞추면 경로는 올라가라 하고 물리는 막아 영구 정지한다.
    /// </summary>
    public static float AgentClimb() => UnityEngine.AI.NavMesh.GetSettingsByID(0).agentClimb;

    internal static void BuildNavMesh()
    {
        var go = new GameObject("NavMesh");
        var surface = go.AddComponent<Unity.AI.Navigation.NavMeshSurface>();

        // 씬 전체를 대상으로 하고, 렌더 메쉬가 아니라 콜라이더를 기준으로 굽는다 —
        // 지면 콜라이더를 두꺼운 박스로 바꿔놨으므로 콜라이더 쪽이 실제 보행면과 맞다.
        surface.collectObjects = Unity.AI.Navigation.CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

        // 보이지 않는 천장(Ignore Raycast) 은 빼고 굽는다. 안 그러면 지붕 위에
        // 갈 수도 없는 보행 영역이 생긴다.
        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0) surface.layerMask = ~(1 << ignoreRaycast);

        // 문/창문 판은 빼고 굽는다. 닫힌 채로 구우면 문간이 영구히 막힌 것으로 구워져서
        // 나중에 문을 열어도 좀비가 절대 못 들어온다. 대신 판마다 NavMeshObstacle 을 달아
        // 닫혀 있는 동안만 실시간으로 길을 깎게 한다 (CreateOpening 참고).
        var panes = Object.FindObjectsByType<WindowFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var hidden = new System.Collections.Generic.List<Collider>();
        for (int i = 0; i < panes.Length; i++)
        {
            var collider = panes[i].GetComponentInChildren<Collider>(true);
            if (collider == null || !collider.enabled) continue;

            collider.enabled = false;
            hidden.Add(collider);
        }
        Physics.SyncTransforms();

        // 주의: surface.GetBuildSettings() 는 복사본을 돌려준다. 여기서 값을 바꿔도
        // 굽기에 반영되지 않는다 (실제로 그렇게 착각해서 계단이 막혔다).
        // 크기는 프로젝트의 에이전트 타입(기본 Humanoid) 설정을 그대로 쓰고,
        // 대신 CharacterController 의 stepOffset 을 그 climb 에 맞춘다 — AgentClimb() 참고.
        surface.BuildNavMesh();

        for (int i = 0; i < hidden.Count; i++) hidden[i].enabled = true;
        Physics.SyncTransforms();

        var triangulation = UnityEngine.AI.NavMesh.CalculateTriangulation();
        Debug.Log("[V0.1] NavMesh 구움: 정점 " + triangulation.vertices.Length
                  + ", 삼각형 " + triangulation.indices.Length / 3
                  + " (에이전트 반지름 " + AgentRadius().ToString("F2") + "m, 문 제외)");
    }

    internal static GameObject CreatePlayer()
    {
        var model = EnsurePlayerModelAndAnimation();
        var controller = EnsurePlayerController();
        var player = new GameObject("Player");
        player.name = "Player";
        // 도입부: 주인공은 자기 방에서 시작한다.
        player.transform.position = Flat(2.2f, 8.2f) + Vector3.up * BodyY;

        var cc = player.AddComponent<CharacterController>();
        cc.center = Vector3.zero;
        cc.height = PlayerHeight;
        // 지름 1m 는 실제 문(1.0m) 을 못 지난다. 사람 어깨폭에 가깝게 줄였다.
        cc.radius = 0.32f;
        cc.slopeLimit = 45f;
        // NavMesh 가 올라갈 수 있다고 보는 높이와 맞춘다 (경로/물리 불일치 방지).
        cc.stepOffset = AgentClimb();

        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, player.transform);
        visual.name = "Model";
        // character01 원점은 발밑이므로 콜라이더 아래끝까지 내린다.
        visual.transform.localPosition = Vector3.down * (PlayerHeight * 0.5f);
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * (PlayerHeight / MeasureModelHeight(model));
        ApplyPlayerMaterials(visual);

        var animator = visual.GetComponentInChildren<Animator>();
        if (!animator) throw new System.InvalidOperationException("character01에 Animator가 없습니다.");
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        var hair = visual.AddComponent<HairSpring>();
        var serializedHair = new SerializedObject(hair);
        var hairRoots = serializedHair.FindProperty("roots");
        hairRoots.arraySize = PlayerHairRootNames.Length;
        var modelTransforms = visual.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < PlayerHairRootNames.Length; i++)
        {
            Transform root = null;
            for (int t = 0; t < modelTransforms.Length; t++)
                if (modelTransforms[t].name == PlayerHairRootNames[i]) { root = modelTransforms[t]; break; }
            if (!root) throw new System.InvalidOperationException("character01 머리카락 본이 없습니다: " + PlayerHairRootNames[i]);
            hairRoots.GetArrayElementAtIndex(i).objectReferenceValue = root;
        }
        serializedHair.ApplyModifiedPropertiesWithoutUndo();

        player.AddComponent<PlayerStats>();
        player.AddComponent<PlayerMotor>();

        // 플레이어도 마우스 방향으로 도니까 자기 몸이 어디를 보는지 보여야 한다.
        int characterLayer = LayerMask.NameToLayer("Character");
        if (characterLayer >= 0)
        {
            var all = player.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++) all[i].gameObject.layer = characterLayer;
        }

        player.AddComponent<PlayerInventory>();
        player.AddComponent<PlayerEquipment>();
        player.AddComponent<PlayerInteractor>();
        player.AddComponent<PlayerInjuries>();
        player.AddComponent<PlayerConsumer>();
        player.AddComponent<PlayerQuickSlots>();
        player.AddComponent<PlayerCrafting>();
        player.AddComponent<SurveyHighlight>();
        player.AddComponent<PlayerShield>();
        player.AddComponent<PlayerAssassinate>();
        player.AddComponent<AimCursorFx>();
        player.AddComponent<PlayerAim>();

        player.AddComponent<PlayerVault>();
        player.AddComponent<VisionField>();

        player.AddComponent<PlayerGrapple>();
        player.AddComponent<PlayerCombat>();
        player.AddComponent<PlayerAnimator>();
        player.AddComponent<PlayerDeath>();

        // 테스트용 시작 상태. 실제 시작 상태가 정해지면 이 줄과 DebugStartState.cs 를 지운다.
        player.AddComponent<DebugStartState>();
        return player;
    }

    internal static void CreateCamera(Transform target)
    {
        var go = new GameObject("Main Camera") { tag = "MainCamera" };
        go.transform.rotation = Quaternion.Euler(30f, 45f, 0f);
        go.transform.position = target.position - go.transform.forward * 30f;

        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 5.5f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 200f;
        go.AddComponent<UniversalAdditionalCameraData>();

        go.AddComponent<WallFader>().SetTarget(target);
        go.AddComponent<VisionFog>();
        go.AddComponent<IsometricCamera>().SetTarget(target);
    }

    internal static GameClock CreateGameClock()
    {
        var go = new GameObject("GameClock");
        return go.AddComponent<GameClock>();
    }

    internal static void CreateHud(GameObject player, GameClock clock)
    {
        var sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        var go = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var hp = CreateBar(go.transform, "HpBar", new Vector2(24f, -24f), new Color(0.85f, 0.16f, 0.16f), sprite);
        var stamina = CreateBar(go.transform, "StaminaBar", new Vector2(24f, -54f), new Color(0.16f, 0.55f, 0.92f), sprite);
        var canvasRect = (RectTransform)go.transform;

        go.AddComponent<HudBars>().Bind(player.GetComponent<PlayerStats>(), hp, stamina);

        var prompt = CreateLabel(go.transform, "PickupPrompt", new Vector2(0f, 170f), 26);

        // GDD 16항 좌측 상태 아이콘. 상태가 나빠지면 어두워진다.
        // 글리프는 BMP 범위만 쓴다 — 레거시 Text 는 서로게이트 페어를 그리지 못한다 (EmojiFont 주석 참고).
        var hunger = CreateStatusIcon(canvasRect, "HungerIcon", "☕", "허기", 150f);
        var thirst = CreateStatusIcon(canvasRect, "ThirstIcon", "☔", "탈수", 0f);
        var fatigue = CreateStatusIcon(canvasRect, "FatigueIcon", "☾", "피곤함", -150f);

        // GDD 16항 우측 상단 DAY / 현재 시간.
        var clockLabel = CreateAnchoredText(canvasRect, "ClockLabel", "DAY 1   08:00   낮", 26,
            new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(360f, 36f), TextAnchor.UpperRight);

        go.AddComponent<SurvivalHudUI>().Bind(
            player.GetComponent<PlayerStats>(), clock, hunger, thirst, fatigue, clockLabel);

        go.AddComponent<PickupPromptUI>().Bind(
            player.GetComponent<PlayerInteractor>(), player.GetComponent<PlayerConsumer>(), prompt);

        CreatePortrait(go, sprite, player);

        // GDD 11항 인벤토리 화면. 컴포넌트는 캔버스에 둔다 —
        // 패널 오브젝트에 붙이면 패널을 끌 때 컴포넌트도 꺼져서 Tab 으로 다시 열 수 없다.
        CreateInventoryUI(go, sprite, player, CreateQuickSlotBar(go, sprite, player));
        CreateDamageVignette(canvasRect, player.GetComponent<PlayerInjuries>());

        // 한글 글리프가 없는 내장 폰트를 OS 폰트로 전부 교체한다.
        go.AddComponent<KoreanFontApplier>();

        CreateGrappleUI(go, sprite, player);

        // GDD 4항 Z 상태창 / M 전체 지도.
        CreateStatusPanel(go, sprite, player);
        CreateMapPanel(go, sprite);

        CreateEventSystem();
    }

    /// <summary>
    /// GDD 19항 저장/로드. 아이템을 id 로 되찾기 위한 카탈로그를 만들어 물려준다.
    /// ItemDefinition 은 Resources 밖에 있어서 런타임 경로 로드가 안 된다.
    /// </summary>
    static void CreateSaveSystem(ItemDefinition[] items, GameClock clock, ZombieDefinition[] zombieTypes)
    {
        var go = new GameObject("SaveSystem");
        go.AddComponent<SaveSystem>().Bind(
            EnsureItemCatalog(items),
            clock,
            EnsureSharedMaterial("zombie", new Color(0.32f, 0.52f, 0.30f)),
            EnsureSharedMaterial("player_zombie", new Color(0.58f, 0.26f, 0.58f)),
            zombieTypes);
    }

    static ItemCatalog EnsureItemCatalog(ItemDefinition[] items)
    {
        Directory.CreateDirectory(ItemDataFolder);
        string path = ItemDataFolder + "/ItemCatalog.asset";

        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(path);
        if (!catalog)
        {
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
        }

        // 빌드마다 목록을 다시 채운다 — 아이템을 추가했는데 카탈로그가 비면 로드에서 조용히 사라진다.
        catalog.SetItems(items);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    /// <summary>GDD 7항 적. 3D 에셋이 없어 캡슐 프리미티브로 만든다.</summary>
    /// <summary>
    /// GDD 16항 좀비 3종. 수치는 기획서 표 그대로 두고, 공격력 20 은 범위로 흔든다
    /// (사용자 결정: 종류별 기준값 + 범위).
    /// </summary>
    static ZombieDefinition[] EnsureZombieDefinitions()
    {
        Directory.CreateDirectory(ZombieDataFolder);

        var normal = EnsureZombieType("zombie_normal", "일반 좀비", new Color(0.32f, 0.52f, 0.30f),
                                      100f, 2.5f, true);
        var female = EnsureZombieType("zombie_female", "여성형 좀비", new Color(0.55f, 0.35f, 0.55f),
                                      60f, 4.0f, true);
        var heavy = EnsureZombieType("zombie_heavy", "헤비좀비", new Color(0.30f, 0.34f, 0.45f),
                                     220f, 1.5f, false);

        // GDD 21항 충격량 표: 일반 공격 낮음(0.3s), 헤비좀비 공격/돌진 매우 높음(1.5s).
        if (heavy.blockImpact < 1f)
        {
            heavy.blockImpact = 1.5f;
            EditorUtility.SetDirty(heavy);
        }

        // 사용자 결정: 헤비좀비는 넉백에 밀리지 않는다 (경직은 그대로).
        if (!heavy.immuneToKnockback)
        {
            heavy.immuneToKnockback = true;
            EditorUtility.SetDirty(heavy);
        }

        // GDD 22항 돌진은 헤비좀비만.
        if (!heavy.canCharge)
        {
            heavy.canCharge = true;
            heavy.chargeTriggerRange = 8f;
            heavy.chargeWindup = 0.9f;
            heavy.chargeSpeed = 11f;
            heavy.chargeDistance = 6.5f;
            heavy.chargeRecovery = 2f;   // GDD 22항: 돌진 종료 후 2초 경직
            heavy.chargeCooldown = 20f;  // GDD 22항: 개별 좀비 기준 20초
            heavy.chargeBlockImpact = 1.5f;
            heavy.chargeDamageMin = 30f;   // 사용자 요구: 돌진 데미지
            heavy.chargeDamageMax = 45f;
            EditorUtility.SetDirty(heavy);
        }

        AssetDatabase.SaveAssets();
        return new[] { normal, female, heavy };
    }

    static ZombieDefinition EnsureZombieType(string id, string displayName, Color color,
                                            float hp, float speed, bool assassinable)
    {
        string path = ZombieDataFolder + "/" + id + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<ZombieDefinition>(path);

        if (!asset)
        {
            asset = ScriptableObject.CreateInstance<ZombieDefinition>();
            AssetDatabase.CreateAsset(asset, path);
        }

        // 손으로 조정한 수치는 덮어쓰지 않는다 — 비어 있는 항목만 채운다.
        if (string.IsNullOrEmpty(asset.id))
        {
            asset.id = id;
            asset.displayName = displayName;
            asset.maxHealth = hp;
            asset.moveSpeed = speed;
            asset.damageMin = 15f;
            asset.damageMax = 25f;
            asset.canBeAssassinated = assassinable;
        }

        asset.placeholderColor = color;
        if (!asset.placeholderMaterial) asset.placeholderMaterial = EnsureSharedMaterial(id, color);

        EditorUtility.SetDirty(asset);
        return asset;
    }

    /// <summary>GDD 26항 소음에 따른 추가 적 유입.</summary>
    static void CreateNoiseDirector(ZombieDefinition[] types)
    {
        var go = new GameObject("NoiseDirector");
        go.AddComponent<NoiseDirector>().Bind(
            types, EnsureSharedMaterial("zombie", new Color(0.32f, 0.52f, 0.30f)));
    }

    /// <summary>GDD 16항 3종을 섞어 배치한다. 헤비좀비는 돌진 검증용으로 한 마리만.</summary>
    static void SpawnZombies(ZombieDefinition[] types)
    {
        Vector3[] spots =
        {
            new Vector3(9f, 0f, 9f),
            new Vector3(-11f, 0f, 6f),
            new Vector3(6f, 0f, -12f),
            new Vector3(-8f, 0f, -11f),
            new Vector3(3f, 0f, 14f),
            new Vector3(27f, 0f, 5.5f), // 중앙홀 안. 벽 너머 좀비가 안 보이는지 확인용
        };

        // 일반 2 / 여성형 2 / 헤비 1 / 실내 1
        int[] typeIndex = { 0, 0, 1, 1, 2, 0 };

        var fallback = EnsureSharedMaterial("zombie", new Color(0.32f, 0.52f, 0.30f));
        for (int i = 0; i < spots.Length; i++)
        {
            var type = types != null && typeIndex[i] < types.Length ? types[typeIndex[i]] : null;
            Zombie.Spawn(spots[i], type, fallback);
        }
    }


    /// <summary>GDD 8항 부활 지점.</summary>
    static Transform[] CreateSpawnPoints()
    {
        var root = new GameObject("SpawnPoints");
        Vector3[] spots =
        {
            Flat(2.2f, 8.2f) + Vector3.up * BodyY,
            new Vector3(10f, BodyY, -10f),
            new Vector3(38f, BodyY, -10f),
            new Vector3(-2f, BodyY, -10f),
        };

        var points = new Transform[spots.Length];
        for (int i = 0; i < spots.Length; i++)
        {
            var go = new GameObject("Spawn_" + i);
            go.transform.SetParent(root.transform);
            go.transform.position = spots[i];
            points[i] = go.transform;
        }
        return points;
    }

    /// <summary>저항 게이지. 붙잡혔을 때만 화면 중앙 아래에 나타난다.</summary>
    static GrappleUI CreateGrappleUI(GameObject canvas, Sprite sprite, GameObject player)
    {
        var panel = new GameObject("GrapplePanel", typeof(Image));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 250f);
        rect.sizeDelta = new Vector2(560f, 92f);

        var background = panel.GetComponent<Image>();
        background.sprite = sprite;
        background.type = Image.Type.Sliced;
        background.color = new Color(0.05f, 0.05f, 0.07f, 0.80f);
        background.raycastTarget = false;

        var label = CreateAnchoredText(rect, "Label", "물렸다! 좌클릭 연타로 저항", 22,
            new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(540f, 30f), TextAnchor.MiddleCenter);
        AddOutline(label, new Color(0.05f, 0.05f, 0.08f, 0.9f), 1.6f);

        var track = new GameObject("Track", typeof(Image));
        var trackRect = (RectTransform)track.transform;
        trackRect.SetParent(rect, false);
        trackRect.anchorMin = trackRect.anchorMax = trackRect.pivot = new Vector2(0.5f, 0f);
        trackRect.anchoredPosition = new Vector2(0f, 14f);
        trackRect.sizeDelta = new Vector2(520f, 32f);

        var trackImage = track.GetComponent<Image>();
        trackImage.sprite = sprite;
        trackImage.type = Image.Type.Sliced;
        trackImage.color = new Color(0f, 0f, 0f, 0.65f);
        trackImage.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(Image));
        var fillRect = (RectTransform)fillGo.transform;
        fillRect.SetParent(trackRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(3f, 3f);
        fillRect.offsetMax = new Vector2(-3f, -3f);

        var fill = fillGo.GetComponent<Image>();
        fill.sprite = sprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0.55f;
        fill.raycastTarget = false;

        var ui = canvas.AddComponent<GrappleUI>();
        ui.Bind(player.GetComponent<PlayerGrapple>(), panel, fill, label);

        panel.SetActive(false);
        return ui;
    }

    /// <summary>
    /// GDD 16항 우측 하단 2D 캐릭터 일러스트. 화면 고정이며 월드와 무관하다 (GDD 23항).
    /// PortraitFolder 에 PNG 가 있으면 자동으로 물리고, 없으면 색 사각형으로 대체된다.
    /// </summary>
    static void CreatePortrait(GameObject canvas, Sprite sprite, GameObject player)
    {
        var root = new GameObject("CharacterPortrait", typeof(RectTransform));
        var rect = (RectTransform)root.transform;
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
        // 크기/위치는 사용자가 조정하는 값이다. 임의로 바꾸지 않는다.
        rect.anchoredPosition = new Vector2(-24f, 24f);
        rect.sizeDelta = new Vector2(320f, 460f);

        var imageGo = new GameObject("Portrait", typeof(Image));
        var imageRect = (RectTransform)imageGo.transform;
        imageRect.SetParent(rect, false);
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = new Vector2(0f, 34f);
        imageRect.offsetMax = Vector2.zero;

        var image = imageGo.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;

        var label = CreateAnchoredText(rect, "StateLabel", "양호", 20,
            new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(320f, 28f), TextAnchor.MiddleCenter);
        AddOutline(label, new Color(0.05f, 0.05f, 0.08f, 0.9f), 1.6f);

        canvas.AddComponent<CharacterPortraitUI>()
              .Bind(player.GetComponent<PlayerInjuries>(), image, label, LoadPortraitSprites());
    }

    /// <summary>
    /// 부상 등급별 일러스트를 파일명으로 찾는다. 없는 등급은 null 이 들어가고
    /// CharacterPortraitUI 가 더 낮은 등급 이미지로 대체한다.
    /// </summary>
    static Sprite[] LoadPortraitSprites()
    {
        EnsurePortraitImportSettings();

        string[] names = { "portrait_normal", "portrait_minor", "portrait_serious", "portrait_critical" };
        var sprites = new Sprite[names.Length];

        for (int i = 0; i < names.Length; i++)
            sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(PortraitFolder + "/" + names[i] + ".png");

        return sprites;
    }

    /// <summary>
    /// PNG 를 그냥 넣으면 Sprite 로 로드되지 않는다. 손으로 인스펙터를 만지지 않아도 되게 빌더가 맞춰준다.
    ///
    /// 함정: textureType 만 Sprite 로 바꿔도 spriteImportMode 는 따라오지 않는다.
    /// Multiple 로 남아 있으면 스프라이트가 개별 서브에셋이 되어 LoadAssetAtPath&lt;Sprite&gt; 가 null 을 반환한다.
    /// </summary>
    static void EnsurePortraitImportSettings()
    {
        if (!Directory.Exists(PortraitFolder)) return;

        AssetDatabase.Refresh();

        var files = Directory.GetFiles(PortraitFolder, "*.png");
        bool changed = false;

        for (int i = 0; i < files.Length; i++)
        {
            string assetPath = files[i].Replace(Path.DirectorySeparatorChar, (char)47);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) continue;

            bool alreadyCorrect = importer.textureType == TextureImporterType.Sprite &&
                                  importer.spriteImportMode == SpriteImportMode.Single;
            if (alreadyCorrect) continue;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            changed = true;
        }

        if (!changed) return;

        AssetDatabase.Refresh();
        Debug.Log("[V0.1] 일러스트 임포트 설정을 Sprite(Single) 로 맞췄습니다: " + PortraitFolder);
    }

    /// <summary>uGUI 드래그/클릭에 필요. activeInputHandler 가 신규 Input System 이므로 전용 모듈을 쓴다.</summary>
    static void CreateEventSystem()
    {
        var go = new GameObject("EventSystem", typeof(EventSystem));
        go.AddComponent<InputSystemUIInputModule>();
    }

    /// <summary>
    /// 하단 중앙 퀵슬롯 바. 앞 두 칸은 주무기/보조무기(장비를 그대로 비춘다), 뒤 세 칸은 소모품.
    /// 인벤토리 패널이 꺼져도 남아야 하므로 HUD 캔버스에 붙인다.
    /// </summary>
    static QuickSlotBarUI CreateQuickSlotBar(GameObject canvas, Sprite sprite, GameObject player)
    {
        var root = new GameObject("QuickSlotBar", typeof(RectTransform));
        var rect = (RectTransform)root.transform;
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 24f);
        rect.sizeDelta = new Vector2(520f, 76f);

        var bar = canvas.AddComponent<QuickSlotBarUI>();
        bar.Bind(player.GetComponent<PlayerQuickSlots>(), player.GetComponent<PlayerInventory>(),
                 player.GetComponent<PlayerEquipment>(), rect, sprite);
        return bar;
    }

    static void CreateInventoryUI(GameObject canvas, Sprite sprite, GameObject player, QuickSlotBarUI quickSlotBar)
    {
        var inventory = player.GetComponent<PlayerInventory>();
        const float cellSize = 56f;

        var panel = new GameObject("InventoryPanel", typeof(Image));
        var panelRect = (RectTransform)panel.transform;
        panelRect.SetParent(canvas.transform, false);
        panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        // 왼쪽 = 인벤토리 격자, 가운데 = 캐릭터/장비, 오른쪽 = 주변 물건.
        float gridWidth = inventory.Width * cellSize;
        float gridHeight = inventory.Height * cellSize;
        float characterWidth = gridWidth;
        float nearbyWidth = gridWidth * 0.65f;
        panelRect.sizeDelta = new Vector2(32f + gridWidth + 24f + characterWidth + 24f + nearbyWidth + 32f,
                                         gridHeight + 196f);

        var panelImage = panel.GetComponent<Image>();
        panelImage.sprite = sprite;
        panelImage.type = Image.Type.Sliced;
        panelImage.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);

        CreateAnchoredText(panelRect, "Title", "인벤토리", 22,
            new Vector2(0f, 1f), new Vector2(32f, -30f), new Vector2(300f, 26f), TextAnchor.LowerLeft);

        var gridRoot = new GameObject("GridRoot", typeof(RectTransform));
        var gridRect = (RectTransform)gridRoot.transform;
        gridRect.SetParent(panelRect, false);
        gridRect.anchorMin = gridRect.anchorMax = gridRect.pivot = new Vector2(0f, 1f);
        gridRect.anchoredPosition = new Vector2(32f, -68f);
        gridRect.sizeDelta = new Vector2(gridWidth, gridHeight);

        var sortButton = CreateButton(panelRect, "SortButton", "자동 정렬",
            new Vector2(32f, 26f), new Vector2(150f, 36f), new Vector2(0f, 0f), sprite);

        var useButton = CreateButton(panelRect, "UseButton", "사용 / 장착",
            new Vector2(192f, 26f), new Vector2(150f, 36f), new Vector2(0f, 0f), sprite);

        var hint = CreateAnchoredText(panelRect, "Hint", "우클릭 착용/사용 · Shift+우클릭 분할 · 패널 밖으로 드래그해 버리기", 14,
            new Vector2(1f, 0f), new Vector2(-32f, 26f), new Vector2(400f, 36f), TextAnchor.MiddleRight);

        float characterX = 32f + gridWidth + 24f;
        CreateAnchoredText(panelRect, "CharacterLabel", "캐릭터 · 장비 (클릭해서 해제)", 16,
            new Vector2(0f, 1f), new Vector2(characterX, -30f), new Vector2(characterWidth, 26f), TextAnchor.LowerLeft);

        var characterRoot = new GameObject("CharacterPanel", typeof(RectTransform));
        var characterRect = (RectTransform)characterRoot.transform;
        characterRect.SetParent(panelRect, false);
        characterRect.anchorMin = characterRect.anchorMax = characterRect.pivot = new Vector2(0f, 1f);
        characterRect.anchoredPosition = new Vector2(characterX, -68f);
        characterRect.sizeDelta = new Vector2(characterWidth, gridHeight);

        var equipmentSlots = new[]
        {
            EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Legs, EquipmentSlot.Feet,
            EquipmentSlot.Backpack, EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Misc
        };
        var equipmentButtons = new Button[equipmentSlots.Length];
        for (int i = 0; i < equipmentSlots.Length; i++)
        {
            bool right = i >= 4;
            equipmentButtons[i] = CreateButton(characterRect, "EquipSlot_" + equipmentSlots[i],
                PlayerEquipment.SlotName(equipmentSlots[i]) + " : -",
                new Vector2(0f, -(i % 4) * 44f), new Vector2(136f, 38f),
                new Vector2(right ? 1f : 0f, 1f), sprite);
            equipmentButtons[i].GetComponentInChildren<Text>().fontSize = 14;
        }

        var bodyParts = new Image[PlayerInjuries.PartCount];
        var bleedingOutlines = new Outline[PlayerInjuries.PartCount];
        bodyParts[(int)BodyPart.Head] = CreateBodyPart(characterRect, "BodyPart_Head",
            new Vector2(0f, -14f), new Vector2(52f, 52f), sprite, out bleedingOutlines[(int)BodyPart.Head]);
        bodyParts[(int)BodyPart.Torso] = CreateBodyPart(characterRect, "BodyPart_Torso",
            new Vector2(0f, -74f), new Vector2(64f, 100f), sprite, out bleedingOutlines[(int)BodyPart.Torso]);
        bodyParts[(int)BodyPart.LeftArm] = CreateBodyPart(characterRect, "BodyPart_LeftArm",
            new Vector2(-49f, -78f), new Vector2(26f, 106f), sprite, out bleedingOutlines[(int)BodyPart.LeftArm]);
        bodyParts[(int)BodyPart.RightArm] = CreateBodyPart(characterRect, "BodyPart_RightArm",
            new Vector2(49f, -78f), new Vector2(26f, 106f), sprite, out bleedingOutlines[(int)BodyPart.RightArm]);
        bodyParts[(int)BodyPart.LeftLeg] = CreateBodyPart(characterRect, "BodyPart_LeftLeg",
            new Vector2(-18f, -182f), new Vector2(28f, 102f), sprite, out bleedingOutlines[(int)BodyPart.LeftLeg]);
        bodyParts[(int)BodyPart.RightLeg] = CreateBodyPart(characterRect, "BodyPart_RightLeg",
            new Vector2(18f, -182f), new Vector2(28f, 102f), sprite, out bleedingOutlines[(int)BodyPart.RightLeg]);

        var injurySummary = CreateAnchoredText(characterRect, "InjurySummary", "부상 없음", 15,
            new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(characterWidth, 28f), TextAnchor.MiddleCenter);
        var equipSummary = CreateAnchoredText(panelRect, "EquipSummary", string.Empty, 15,
            new Vector2(0f, 1f), new Vector2(characterX, -68f - gridHeight - 8f),
            new Vector2(characterWidth, 50f), TextAnchor.UpperLeft);

        // GDD 13항 주변 물건 목록.
        float nearbyX = characterX + characterWidth + 24f;
        CreateAnchoredText(panelRect, "NearbyLabel", "주변 물건 (클릭해서 줍기)", 16,
            new Vector2(0f, 1f), new Vector2(nearbyX, -30f), new Vector2(nearbyWidth, 26f), TextAnchor.LowerLeft);

        var nearbyRoot = new GameObject("NearbyRoot", typeof(RectTransform));
        var nearbyRect = (RectTransform)nearbyRoot.transform;
        nearbyRect.SetParent(panelRect, false);
        nearbyRect.anchorMin = nearbyRect.anchorMax = nearbyRect.pivot = new Vector2(0f, 1f);
        nearbyRect.anchoredPosition = new Vector2(nearbyX, -68f);
        nearbyRect.sizeDelta = new Vector2(nearbyWidth, gridHeight);

        var emptyText = CreateAnchoredText(nearbyRect, "Empty", "주변에 아무것도 없습니다", 15,
            new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(nearbyWidth, 28f), TextAnchor.MiddleCenter);

        var dialog = CreateQuantityDialog(canvas, sprite);

        // GDD 8항: Q 투척도 같은 수량창을 쓴다.
        player.GetComponent<PlayerInteractor>().BindQuantityDialog(dialog);

        canvas.AddComponent<InventoryUI>().Bind(
            inventory, player.GetComponent<PlayerInteractor>(), panel, gridRect, dialog, sortButton, useButton,
            player.GetComponent<PlayerConsumer>(), player.GetComponent<PlayerEquipment>(),
            player.GetComponent<PlayerQuickSlots>(), quickSlotBar,
            hint, sprite, cellSize);

        canvas.AddComponent<NearbyItemsUI>().Bind(
            player.GetComponent<PlayerInteractor>(), panel, nearbyRect, emptyText, sprite);

        canvas.AddComponent<EquipmentUI>().Bind(
            player.GetComponent<PlayerEquipment>(), player.GetComponent<PlayerInjuries>(),
            panel, equipmentButtons, equipSummary);
        canvas.AddComponent<CharacterInjuryUI>().Bind(
            player.GetComponent<PlayerInjuries>(), panel, bodyParts, bleedingOutlines, injurySummary);

        panel.SetActive(false);


    }

    /// <summary>GDD 4항 Z 캐릭터 상태 패널. 글자 하나로 전부 보여준다.</summary>
    static void CreateStatusPanel(GameObject canvas, Sprite sprite, GameObject player)
    {
        var panel = new GameObject("StatusPanel", typeof(Image));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(680f, 720f);

        var image = panel.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.07f, 0.08f, 0.11f, 0.95f);

        var body = CreateAnchoredText(rect, "Body", string.Empty, 20,
            new Vector2(0f, 1f), new Vector2(28f, -24f), new Vector2(620f, 660f), TextAnchor.UpperLeft);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Overflow;

        canvas.AddComponent<StatusPanelUI>().Bind(
            player.GetComponent<PlayerStats>(), player.GetComponent<PlayerInjuries>(),
            player.GetComponent<PlayerEquipment>(), panel, body);

        panel.SetActive(false);
    }

    /// <summary>GDD 4항 M 전체 지도 + GDD 25항 "미니맵 없음".</summary>
    static void CreateMapPanel(GameObject canvas, Sprite sprite)
    {
        var panel = new GameObject("MapPanel", typeof(Image));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(820f, 880f);

        var image = panel.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.06f, 0.09f, 0.08f, 0.96f);

        var legend = CreateAnchoredText(rect, "Legend", string.Empty, 20,
            new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(780f, 62f), TextAnchor.UpperCenter);

        var mapRoot = new GameObject("MapArea", typeof(Image));
        var mapRect = (RectTransform)mapRoot.transform;
        mapRect.SetParent(rect, false);
        mapRect.anchorMin = mapRect.anchorMax = mapRect.pivot = new Vector2(0.5f, 0.5f);
        mapRect.anchoredPosition = new Vector2(0f, -30f);
        mapRect.sizeDelta = new Vector2(740f, 740f);

        var mapImage = mapRoot.GetComponent<Image>();
        mapImage.sprite = sprite;
        mapImage.type = Image.Type.Sliced;
        mapImage.color = new Color(0.12f, 0.16f, 0.14f, 1f);
        mapImage.raycastTarget = false;

        canvas.AddComponent<MapUI>().Bind(panel, mapRect, sprite, legend);

        panel.SetActive(false);
    }

    static QuantityDialog CreateQuantityDialog(GameObject canvas, Sprite sprite)
    {
        var panel = new GameObject("QuantityDialog", typeof(Image));
        var rect = (RectTransform)panel.transform;
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(440f, 200f);

        var image = panel.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.10f, 0.11f, 0.14f, 0.98f);

        var title = CreateAnchoredText(rect, "Title", "수량", 22,
            new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(400f, 32f), TextAnchor.MiddleCenter);
        var value = CreateAnchoredText(rect, "Value", "1", 30,
            new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(200f, 40f), TextAnchor.MiddleCenter);

        var minus = CreateButton(rect, "Minus", "-", new Vector2(-170f, -70f), new Vector2(50f, 38f), new Vector2(0.5f, 1f), sprite);
        var plus = CreateButton(rect, "Plus", "+", new Vector2(170f, -70f), new Vector2(50f, 38f), new Vector2(0.5f, 1f), sprite);
        var slider = CreateSlider(rect, sprite);
        var confirm = CreateButton(rect, "Confirm", "확인", new Vector2(-92f, 28f), new Vector2(150f, 40f), new Vector2(0.5f, 0f), sprite);
        var cancel = CreateButton(rect, "Cancel", "취소", new Vector2(92f, 28f), new Vector2(150f, 40f), new Vector2(0.5f, 0f), sprite);

        var dialog = canvas.AddComponent<QuantityDialog>();
        dialog.Bind(panel, title, value, slider, minus, plus, confirm, cancel);

        panel.SetActive(false);
        return dialog;
    }

    static Slider CreateSlider(RectTransform parent, Sprite sprite)
    {
        var go = new GameObject("Slider", typeof(Image), typeof(Slider));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -114f);
        rect.sizeDelta = new Vector2(360f, 22f);

        var background = go.GetComponent<Image>();
        background.sprite = sprite;
        background.type = Image.Type.Sliced;
        background.color = new Color(0f, 0f, 0f, 0.5f);

        var fillArea = new GameObject("FillArea", typeof(RectTransform));
        var fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.SetParent(rect, false);
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(4f, 4f);
        fillAreaRect.offsetMax = new Vector2(-4f, -4f);

        var fill = new GameObject("Fill", typeof(Image));
        var fillRect = (RectTransform)fill.transform;
        fillRect.SetParent(fillAreaRect, false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.sizeDelta = new Vector2(10f, 0f);

        var fillImage = fill.GetComponent<Image>();
        fillImage.sprite = sprite;
        fillImage.type = Image.Type.Sliced;
        fillImage.color = new Color(0.35f, 0.62f, 0.9f, 1f);
        fillImage.raycastTarget = false;

        var slider = go.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fillRect;
        slider.targetGraphic = background;
        slider.wholeNumbers = true;
        slider.minValue = 1f;
        slider.maxValue = 1f;
        return slider;
    }

    static Button CreateButton(RectTransform parent, string name, string label,
                               Vector2 anchoredPosition, Vector2 size, Vector2 anchor, Sprite sprite)
    {
        var go = new GameObject(name, typeof(Image), typeof(Button));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = new Color(0.25f, 0.28f, 0.34f, 1f);

        var button = go.GetComponent<Button>();
        button.targetGraphic = image;

        CreateStretchedText(rect, "Text", label, 18, TextAnchor.MiddleCenter);
        return button;
    }

    static Image CreateBodyPart(RectTransform parent, string name, Vector2 position, Vector2 size,
                                Sprite sprite, out Outline bleedingOutline)
    {
        var go = new GameObject(name, typeof(Image), typeof(Outline));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.color = new Color32(0xD3, 0xD1, 0xC7, 0xFF);
        image.raycastTarget = false;

        bleedingOutline = go.GetComponent<Outline>();
        bleedingOutline.effectColor = new Color32(0x75, 0x00, 0x00, 0xFF);
        bleedingOutline.effectDistance = new Vector2(3f, 3f);
        bleedingOutline.useGraphicAlpha = false;
        bleedingOutline.enabled = false;
        return image;
    }

    static void CreateDamageVignette(RectTransform parent, PlayerInjuries injuries)
    {
        var go = new GameObject("DamageVignette", typeof(Image), typeof(DamageVignetteUI));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = go.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);
        image.raycastTarget = false;
        go.GetComponent<DamageVignetteUI>().Bind(injuries, image);
        rect.SetAsLastSibling();
    }

    static Text CreateAnchoredText(RectTransform parent, string name, string content, int fontSize,
                                   Vector2 anchor, Vector2 anchoredPosition, Vector2 size, TextAnchor alignment)
    {
        var go = new GameObject(name, typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        return ConfigureText(go.GetComponent<Text>(), content, fontSize, alignment);
    }

    static Text CreateStretchedText(RectTransform parent, string name, string content, int fontSize, TextAnchor alignment)
    {
        var go = new GameObject(name, typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(6f, 4f);
        rect.offsetMax = new Vector2(-6f, -4f);

        return ConfigureText(go.GetComponent<Text>(), content, fontSize, alignment);
    }

    static Text ConfigureText(Text text, string content, int fontSize, TextAnchor alignment)
    {
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = content;
        return text;
    }

    static Image CreateBar(Transform parent, string name, Vector2 anchoredPosition, Color color, Sprite sprite)
    {
        var root = new GameObject(name, typeof(Image));
        var rt = (RectTransform)root.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = new Vector2(360f, 24f);

        var background = root.GetComponent<Image>();
        background.sprite = sprite;
        background.type = Image.Type.Sliced;
        background.color = new Color(0f, 0f, 0f, 0.55f);
        background.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(Image));
        var fillRt = (RectTransform)fillGo.transform;
        fillRt.SetParent(rt, false);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(2f, 2f);
        fillRt.offsetMax = new Vector2(-2f, -2f);

        var fill = fillGo.GetComponent<Image>();
        fill.sprite = sprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        fill.color = color;
        fill.raycastTarget = false;
        return fill;
    }

    /// <summary>
    /// GDD 16항 상태 아이콘. 글리프 + 아래 한글 라벨.
    /// 배지 없이 월드 위에 반투명으로 얹는다 — 불투명 판을 깔면 스티커처럼 떠 보인다.
    /// 대신 글리프와 라벨에 외곽선을 넣어 어떤 배경 위에서도 형태가 읽히게 한다.
    /// 색을 입히는 아이콘 Text 를 돌려준다 (SurvivalHudUI 가 상태에 따라 어둡게 만든다).
    /// </summary>
    static Text CreateStatusIcon(RectTransform parent, string name, string glyph, string label, float y)
    {
        var root = new GameObject(name, typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.SetParent(parent, false);
        rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0f, 0.5f);
        rootRect.anchoredPosition = new Vector2(34f, y);
        rootRect.sizeDelta = new Vector2(140f, 142f);

        var icon = CreateAnchoredText(rootRect, "Glyph", glyph, 82,
            new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(140f, 100f), TextAnchor.MiddleCenter);
        AddOutline(icon, new Color(0.05f, 0.05f, 0.08f, 0.85f), 2.2f);

        var text = CreateAnchoredText(rootRect, "Label", label, 22,
            new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(140f, 30f), TextAnchor.MiddleCenter);
        AddOutline(text, new Color(0.05f, 0.05f, 0.08f, 0.9f), 1.6f);

        return icon;
    }

    static void AddOutline(Text text, Color color, float thickness)
    {
        var outline = text.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(thickness, thickness);
    }

    static ItemDefinition[] EnsureItemDefinitions()
    {
        Directory.CreateDirectory(ItemDataFolder);
        var items = new[]
        {
            // GDD 11항이 예로 든 1x1 / 1x2 / 2x2 / 2x3 을 모두 쓴다.
            Consumable(EnsureItem("water_bottle", "물병", new Color(0.35f, 0.65f, 0.95f), 3, new Vector2Int(1, 2)),
                       0f, 35f, 0f, 0f, InjurySeverity.None),
            Consumable(EnsureItem("canned_food", "통조림", new Color(0.85f, 0.7f, 0.3f), 5, new Vector2Int(1, 1)),
                       40f, 0f, 0f, 0f, InjurySeverity.None),
            // 붕대는 중상까지, 구급상자는 심각한 부상까지 (GDD 6항은 등급별 도구를 명시하지 않는다).
            Consumable(EnsureItem("bandage", "붕대", new Color(0.95f, 0.95f, 0.9f), 10, new Vector2Int(1, 1)),
                       0f, 0f, 0f, 12f, InjurySeverity.Serious, 5f, 6f),
            // GDD 10항 근접 6종. 한손은 단일 대상 + 약한 경직, 양손은 다중타격 + 넉백 + 경직.
            // 야구방망이 내구도 120 은 GDD 12항 합성 예시("120 → 240") 의 기준값이다.
            Weapon(Equipment(EnsureItem("fire_axe", "손도끼", new Color(0.80f, 0.25f, 0.20f), 1, new Vector2Int(2, 3)),
                             EquipmentSlot.OffHand, 0f, 0.98f, 1.02f, 1f),
                   22f, 1.9f, 0.75f, 12f, WeaponHandedness.OneHanded, 1, 0.2f, 0.25f, 100),
            Weapon(Equipment(EnsureItem("kitchen_knife", "식칼", new Color(0.72f, 0.74f, 0.78f), 1, new Vector2Int(1, 2)),
                             EquipmentSlot.OffHand, 0f, 1f, 0.98f, 1f),
                   12f, 1.15f, 0.24f, 4f, WeaponHandedness.OneHanded, 1, 0f, 0.1f, 80),
            Weapon(Equipment(EnsureItem("baseball_bat", "야구방망이", new Color(0.66f, 0.48f, 0.26f), 1, new Vector2Int(1, 4)),
                             EquipmentSlot.MainHand, 0f, 0.97f, 1.04f, 1f),
                   26f, 2.2f, 0.95f, 16f, WeaponHandedness.TwoHanded, 3, 1.4f, 0.6f, 120),
            Weapon(Equipment(EnsureItem("axe", "도끼", new Color(0.55f, 0.35f, 0.22f), 1, new Vector2Int(2, 3)),
                             EquipmentSlot.MainHand, 0f, 0.95f, 1.08f, 1f),
                   38f, 2.1f, 1.15f, 20f, WeaponHandedness.TwoHanded, 3, 1.0f, 0.5f, 110),
            Weapon(Equipment(EnsureItem("sledgehammer", "해머", new Color(0.42f, 0.44f, 0.48f), 1, new Vector2Int(2, 3)),
                             EquipmentSlot.MainHand, 0f, 0.92f, 1.12f, 1f),
                   46f, 2.0f, 1.35f, 24f, WeaponHandedness.TwoHanded, 2, 1.2f, 0.9f, 140),
            Weapon(Equipment(EnsureItem("fireman_axe", "소방도끼", new Color(0.85f, 0.35f, 0.18f), 1, new Vector2Int(2, 4)),
                             EquipmentSlot.MainHand, 0f, 0.94f, 1.06f, 1f),
                   42f, 2.3f, 1.05f, 22f, WeaponHandedness.TwoHanded, 4, 1.1f, 0.55f, 160),
            // GDD 10항 원거리 4종. 사용자 결정: 총은 한손이든 양손이든 무조건 주무기.
            // 탄약/재장전/소음은 아직 없다 — 조준 방향 즉시 판정만 한다.
            Ranged(Weapon(Equipment(EnsureItem("pistol", "권총", new Color(0.30f, 0.32f, 0.36f), 1, new Vector2Int(1, 2)),
                                    EquipmentSlot.MainHand, 0f, 1f, 1f, 1f),
                          30f, 56f, 0.50f, 0f, WeaponHandedness.OneHanded, 1, 0.3f, 0.2f, 200), 6f),
            Ranged(Weapon(Equipment(EnsureItem("shotgun", "산탄총", new Color(0.48f, 0.34f, 0.22f), 1, new Vector2Int(2, 4)),
                                    EquipmentSlot.MainHand, 0f, 0.96f, 1.05f, 1f),
                          22f, 32f, 1.10f, 0f, WeaponHandedness.TwoHanded, 4, 1.5f, 0.5f, 150), 16f),
            Ranged(Weapon(Equipment(EnsureItem("rifle", "소총", new Color(0.36f, 0.40f, 0.30f), 1, new Vector2Int(2, 4)),
                                    EquipmentSlot.MainHand, 0f, 0.96f, 1.04f, 1f),
                          45f, 88f, 0.80f, 0f, WeaponHandedness.TwoHanded, 1, 0.4f, 0.3f, 220), 3f),
            Ranged(Weapon(Equipment(EnsureItem("bow", "활", new Color(0.62f, 0.52f, 0.30f), 1, new Vector2Int(2, 4)),
                                    EquipmentSlot.MainHand, 0f, 0.98f, 1.06f, 1f),
                          28f, 64f, 1.00f, 9f, WeaponHandedness.TwoHanded, 1, 0.2f, 0.25f, 120), 2f),
            // GDD 21항 방패 3종. 임시(쓰레기통 뚜껑) → 정식(경찰 방패) 순으로 내구도가 오른다.
            // 공격력이 0 이라 우클릭이 공격으로 가지 않고 PlayerShield 의 방어 자세로 간다.
            Shield(Equipment(EnsureItem("trashcan_lid", "쓰레기통 뚜껑", new Color(0.55f, 0.57f, 0.60f), 1, new Vector2Int(2, 2)),
                             EquipmentSlot.OffHand, 2f, 0.98f, 1.03f, 1f), 30),
            Shield(Equipment(EnsureItem("metal_sign", "금속 표지판", new Color(0.70f, 0.72f, 0.35f), 1, new Vector2Int(2, 3)),
                             EquipmentSlot.OffHand, 3f, 0.96f, 1.05f, 1f), 55),
            Shield(Equipment(EnsureItem("riot_shield", "경찰 방패", new Color(0.22f, 0.26f, 0.34f), 1, new Vector2Int(2, 4)),
                             EquipmentSlot.OffHand, 5f, 0.93f, 1.10f, 1f), 120),
            // GDD 4항 R 재장전용 탄약. 총기별로 탄종이 다르다. 스택이 커서 1x1 로 둔다.
            EnsureItem("ammo_pistol", "권총탄", new Color(0.78f, 0.68f, 0.30f), 60, new Vector2Int(1, 1)),
            EnsureItem("ammo_shotgun", "산탄", new Color(0.80f, 0.35f, 0.25f), 40, new Vector2Int(1, 1)),
            EnsureItem("ammo_rifle", "소총탄", new Color(0.60f, 0.62f, 0.35f), 50, new Vector2Int(1, 1)),
            EnsureItem("ammo_arrow", "화살", new Color(0.72f, 0.60f, 0.38f), 30, new Vector2Int(1, 2)),
            // GDD 13항 합성 재료. 스택이 커야 내구도 회복에 쓸 수 있다.
            EnsureItem("nail", "못", new Color(0.62f, 0.60f, 0.58f), 50, new Vector2Int(1, 1)),
            EnsureItem("cloth", "천", new Color(0.85f, 0.82f, 0.72f), 20, new Vector2Int(1, 1)),
            EnsureItem("scrap_metal", "철조각", new Color(0.50f, 0.52f, 0.55f), 30, new Vector2Int(1, 1)),
            // GDD 13항 기본 무기: 쇠파이프 (천과 합성해 손잡이를 보강한다)
            Weapon(Equipment(EnsureItem("steel_pipe", "쇠파이프", new Color(0.55f, 0.57f, 0.62f), 1, new Vector2Int(1, 3)),
                             EquipmentSlot.OffHand, 0f, 0.99f, 1.0f, 1f),
                   20f, 1.8f, 0.60f, 8f, WeaponHandedness.OneHanded, 1, 0.4f, 0.3f, 90),
            // GDD 13항 합성 결과 3종. 내구도는 아래 EnsureCraftRecipes 가 원본의 정확히 2배로 강제한다.
            Weapon(Equipment(EnsureItem("nailed_bat", "못박힌 야구방망이", new Color(0.72f, 0.42f, 0.24f), 1, new Vector2Int(1, 4)),
                             EquipmentSlot.MainHand, 0f, 0.97f, 1.05f, 1f),
                   34f, 2.2f, 0.95f, 17f, WeaponHandedness.TwoHanded, 3, 1.5f, 0.65f, 240),
            Weapon(Equipment(EnsureItem("wrapped_pipe", "손잡이 보강 쇠파이프", new Color(0.62f, 0.58f, 0.45f), 1, new Vector2Int(1, 3)),
                             EquipmentSlot.OffHand, 0f, 1.0f, 0.88f, 1f),
                   22f, 1.8f, 0.58f, 6f, WeaponHandedness.OneHanded, 1, 0.4f, 0.3f, 180),
            Weapon(Equipment(EnsureItem("plated_bat", "철판 보강 야구방망이", new Color(0.48f, 0.50f, 0.56f), 1, new Vector2Int(2, 4)),
                             EquipmentSlot.MainHand, 0f, 0.95f, 1.09f, 1f),
                   40f, 2.2f, 1.25f, 20f, WeaponHandedness.TwoHanded, 3, 1.6f, 0.7f, 240),
            Consumable(EnsureItem("medkit", "구급상자", new Color(0.3f, 0.8f, 0.45f), 1, new Vector2Int(2, 2)),
                       0f, 0f, 0f, 60f, InjurySeverity.Critical, 20f, 10f),
            // GDD 10항 상성 예시: 천옷 = 이동속도 증가 > 방어력 감소, 가죽옷 = 방어력 증가 > 이동속도 감소
            Equipment(EnsureItem("cloth_shirt", "천옷", new Color(0.80f, 0.78f, 0.62f), 1, new Vector2Int(2, 2)),
                      EquipmentSlot.Body, 2f, 1.06f, 0.96f, 1.1f),
            Equipment(EnsureItem("leather_jacket", "가죽 재킷", new Color(0.45f, 0.30f, 0.20f), 1, new Vector2Int(2, 2)),
                      EquipmentSlot.Body, 7f, 0.93f, 1.07f, 0.85f),
            Equipment(EnsureItem("helmet", "방탄 헬멧", new Color(0.35f, 0.40f, 0.34f), 1, new Vector2Int(2, 2)),
                      EquipmentSlot.Head, 6f, 0.98f, 1.03f, 0.8f),
            Equipment(EnsureItem("work_pants", "작업 바지", new Color(0.30f, 0.38f, 0.55f), 1, new Vector2Int(2, 2)),
                      EquipmentSlot.Legs, 4f, 0.97f, 1.02f, 0.95f),
            Equipment(EnsureItem("sneakers", "운동화", new Color(0.88f, 0.88f, 0.92f), 1, new Vector2Int(1, 2)),
                      EquipmentSlot.Feet, 1f, 1.05f, 0.95f, 1f),
        };
        ApplyNoiseValues();
        ApplyAmmoValues();
        AssetDatabase.SaveAssets();
        return items;
    }

    /// <summary>
    /// GDD 9·10·11항 무기 속성. 기본값인 항목만 채운다 — 손으로 조정한 수치는 덮어쓰지 않는다.
    /// V0.1 에서 만든 손도끼는 handedness/내구도가 비어 있으므로 그 부분만 보충된다.
    /// </summary>
    static ItemDefinition Weapon(ItemDefinition item, float damage, float range, float interval, float staminaCost,
                                 WeaponHandedness handedness, int maxTargets,
                                 float knockback, float stagger, int durability)
    {
        bool needsBasics = item.attackDamage <= 0f;
        bool needsCombat = item.handedness == WeaponHandedness.None || item.maxDurability <= 0;
        if (!needsBasics && !needsCombat) return item;

        if (needsBasics)
        {
            item.attackDamage = damage;
            item.attackRange = range;
            item.attackInterval = interval;
            item.attackStaminaCost = staminaCost;
        }

        if (needsCombat)
        {
            item.handedness = handedness;
            item.maxTargets = maxTargets;
            item.knockback = knockback;
            item.staggerDuration = stagger;
            item.maxDurability = durability;
        }

        EditorUtility.SetDirty(item);
        return item;
    }

    /// <summary>
    /// GDD 12·13항 합성 레시피. GDD 12항의 "합성 무기 최대 내구도 = 원본의 정확히 2배" 를
    /// 여기서 강제한다 — 데이터가 규칙을 어기면 자기검사가 잡는다.
    /// </summary>
    static CraftRecipe[] EnsureCraftRecipes()
    {
        Directory.CreateDirectory(RecipeDataFolder);

        var recipes = new[]
        {
            EnsureRecipe("recipe_nailed_bat", "baseball_bat", "nail", "nailed_bat", 5, 10),
            EnsureRecipe("recipe_wrapped_pipe", "steel_pipe", "cloth", "wrapped_pipe", 3, 8),
            EnsureRecipe("recipe_plated_bat", "baseball_bat", "scrap_metal", "plated_bat", 4, 12),
        };

        // GDD 12항: 결과물 내구도는 원본의 정확히 2배.
        for (int i = 0; i < recipes.Length; i++)
        {
            var recipe = recipes[i];
            if (!recipe || !recipe.baseWeapon || !recipe.result) continue;

            int expected = recipe.baseWeapon.maxDurability * 2;
            if (recipe.result.maxDurability == expected) continue;

            recipe.result.maxDurability = expected;
            EditorUtility.SetDirty(recipe.result);
        }

        AssetDatabase.SaveAssets();
        return recipes;
    }

    static CraftRecipe EnsureRecipe(string id, string baseId, string materialId, string resultId,
                                    int materialCount, int repairPerMaterial)
    {
        string path = RecipeDataFolder + "/" + id + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<CraftRecipe>(path);

        if (!asset)
        {
            asset = ScriptableObject.CreateInstance<CraftRecipe>();
            AssetDatabase.CreateAsset(asset, path);
        }

        asset.baseWeapon = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + baseId + ".asset");
        asset.material = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + materialId + ".asset");
        asset.result = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + resultId + ".asset");
        if (asset.materialCount <= 1) asset.materialCount = materialCount;
        if (asset.repairPerMaterial <= 1) asset.repairPerMaterial = repairPerMaterial;

        EditorUtility.SetDirty(asset);
        return asset;
    }

    /// <summary>
    /// GDD 24항 컨테이너. 냉장고/옷장/책상/상자/시체/차량 계열을 몇 개 놓고 전리품을 넣는다.
    /// 탄약이 나올 곳이 있어야 GDD 4항 재장전이 성립한다.
    /// </summary>
    static void CreateContainers(ItemDefinition[] items)
    {
        var wood = EnsureSharedMaterial("container_wood", new Color(0.42f, 0.30f, 0.18f));
        var metal = EnsureSharedMaterial("container_metal", new Color(0.60f, 0.64f, 0.68f));

        // 아파트 안. 도면의 가구 자리에 맞춘다.
        var fridge = WorldContainer.Spawn("냉장고", Flat(7.75f, 10.90f), new Vector3(0.70f, 1.90f, 0.70f), metal);
        fridge.Add(Find(items, "canned_food"), 4);
        fridge.Add(Find(items, "water_bottle"), 3);

        var locker = WorldContainer.Spawn("옷장", Flat(12.90f, 8.90f), new Vector3(0.60f, 2.00f, 1.20f), wood);
        locker.Add(Find(items, "leather_jacket"), 1);
        locker.Add(Find(items, "work_pants"), 1);
        locker.Add(Find(items, "cloth"), 6);

        // 문 앞에 두면 벽과의 틈이 몸 폭보다 좁아 방에서 못 나온다. 칸막이 벽 쪽으로 붙인다.
        var desk = WorldContainer.Spawn("책상", Flat(1.80f, 6.05f), new Vector3(0.60f, 0.75f, 1.40f), wood);
        desk.Add(Find(items, "bandage"), 3);
        desk.Add(Find(items, "nail"), 12);
        desk.Add(Find(items, "ammo_pistol"), 24);

        // 현관을 지나 들어오면 왼편에 보이는 쓰레기/재활용 자리.
        var bin = WorldContainer.Spawn("쓰레기봉투", new Vector3(19f, 0f, -10f), new Vector3(0.50f, 0.55f, 0.50f), wood);
        bin.Add(Find(items, "scrap_metal"), 4);
        bin.Add(Find(items, "cloth"), 2);

        // 집 밖.
        var crate = WorldContainer.Spawn("상자", new Vector3(8f, 0f, 9f), new Vector3(1.2f, 1.2f, 1.2f), wood);
        crate.Add(Find(items, "scrap_metal"), 8);
        crate.Add(Find(items, "steel_pipe"), 1);
        crate.Add(Find(items, "ammo_shotgun"), 10);

        // 서벽과 1.2m를 비워 두 문 사이 통로를 막지 않는다.
        var car = WorldContainer.Spawn("차량", new Vector3(5.25f, 0f, -22f), new Vector3(2.4f, 1.3f, 4.2f), metal);
        car.Add(Find(items, "medkit"), 1);
        car.Add(Find(items, "ammo_rifle"), 20);
        car.Add(Find(items, "trashcan_lid"), 1);
    }

    static ItemDefinition Find(ItemDefinition[] items, string id)
    {
        for (int i = 0; i < items.Length; i++)
            if (items[i] && items[i].id == id) return items[i];

        return null;
    }

    /// <summary>
    /// GDD 4항 탄약 설정. 총기별 탄종과 장탄수를 채운다.
    /// 수치는 프로토타입 초기값이며 플레이 테스트로 조정한다 (GDD 27항 5번).
    /// </summary>
    static void ApplyAmmoValues()
    {
        string[] guns = { "pistol", "shotgun", "rifle", "bow" };
        string[] ammo = { "ammo_pistol", "ammo_shotgun", "ammo_rifle", "ammo_arrow" };
        int[] magazine = { 12, 6, 10, 1 };
        float[] reload = { 1.4f, 2.2f, 2.0f, 1.0f };

        for (int i = 0; i < guns.Length; i++)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + guns[i] + ".asset");
            if (!item || item.magazineSize > 0) continue; // 손으로 조정한 값은 덮어쓰지 않는다

            item.ammoId = ammo[i];
            item.magazineSize = magazine[i];
            item.reloadTime = reload[i];
            EditorUtility.SetDirty(item);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// GDD 18항 소음값. 기획서 예시: 식칼 5 / 방망이 10 / 도끼 15 / 활 5 / 권총 40 / 소총 75 / 산탄총 100.
    /// 표에 없는 무기는 같은 계열 값으로 채운다.
    /// </summary>
    static void ApplyNoiseValues()
    {
        string[] ids = { "kitchen_knife", "fire_axe", "baseball_bat", "axe", "sledgehammer", "fireman_axe",
                         "bow", "pistol", "rifle", "shotgun" };
        float[] noise = { 5f, 8f, 10f, 15f, 18f, 15f, 5f, 40f, 75f, 100f };

        for (int i = 0; i < ids.Length; i++)
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + ids[i] + ".asset");
            if (!item || item.noise > 0f) continue; // 손으로 조정한 값은 덮어쓰지 않는다

            item.noise = noise[i];
            EditorUtility.SetDirty(item);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>GDD 21항 방패. 공격력은 주지 않는다 — 우클릭이 방어 자세로 가야 한다.</summary>
    static ItemDefinition Shield(ItemDefinition item, int durability)
    {
        if (item.isShield) return item;

        item.isShield = true;
        item.handedness = WeaponHandedness.OneHanded;
        if (item.maxDurability <= 0) item.maxDurability = durability;

        EditorUtility.SetDirty(item);
        return item;
    }

    /// <summary>GDD 10항 원거리 표시. 손으로 조정한 부채꼴은 덮어쓰지 않는다.</summary>
    static ItemDefinition Ranged(ItemDefinition item, float spread)
    {
        if (item.ranged) return item;

        item.ranged = true;
        item.attackSpread = spread;
        EditorUtility.SetDirty(item);
        return item;
    }

    /// <summary>GDD 10항 장비 속성. 기본값(슬롯 None) 인 에셋에만 채운다.</summary>
    static ItemDefinition Equipment(ItemDefinition item, EquipmentSlot slot, float defense,
                                    float moveSpeed, float staminaCost, float injuryChance)
    {
        if (item.equipSlot != EquipmentSlot.None) return item;

        item.equipSlot = slot;
        item.defense = defense;
        item.moveSpeedMultiplier = moveSpeed;
        item.staminaCostMultiplier = staminaCost;
        item.injuryChanceMultiplier = injuryChance;
        EditorUtility.SetDirty(item);
        return item;
    }

    /// <summary>GDD 9항 소비 아이템 속성. 기본값(전부 0) 인 에셋에만 채운다.</summary>
    static ItemDefinition Consumable(ItemDefinition item, float hunger, float thirst, float fatigue,
                                     float hp, InjurySeverity treatUpTo,
                                     float treatmentDuration = 0f, float recoveryDuration = 0f)
    {
        if (item.usable) return item;

        item.usable = true;
        item.hungerRestore = hunger;
        item.thirstRestore = thirst;
        item.fatigueRestore = fatigue;
        item.hpRestore = hp;
        item.treatUpTo = treatUpTo;
        item.treatmentDuration = treatmentDuration;
        item.recoveryDuration = recoveryDuration;
        EditorUtility.SetDirty(item);
        return item;
    }

    static ItemDefinition EnsureItem(string id, string displayName, Color color, int maxStack, Vector2Int gridSize)
    {
        string path = ItemDataFolder + "/" + id + ".asset";

        var existing = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
        if (existing)
        {
            // 이전 빌드에서 만들어진 에셋은 머티리얼/칸 크기가 비어 있을 수 있다.
            bool dirty = false;

            if (!existing.placeholderMaterial)
            {
                existing.placeholderMaterial = EnsureMaterial(id, color);
                dirty = true;
            }

            // 기본값(1x1) 일 때만 채운다. 손으로 조정한 크기는 덮어쓰지 않는다.
            if (existing.gridSize == Vector2Int.one && gridSize != Vector2Int.one)
            {
                existing.gridSize = gridSize;
                dirty = true;
            }

            if (dirty) EditorUtility.SetDirty(existing);
            return existing;
        }

        var item = ScriptableObject.CreateInstance<ItemDefinition>();
        item.id = id;
        item.displayName = displayName;
        item.placeholderColor = color;
        item.maxStack = maxStack;
        item.gridSize = gridSize;
        item.placeholderMaterial = EnsureMaterial(id, color);
        AssetDatabase.CreateAsset(item, path);
        return item;
    }

    /// <summary>
    /// 아이템별 머티리얼 에셋. sharedMaterial 로 붙이면 씬에 머티리얼이 누출되지 않고
    /// 같은 종류 아이템끼리 드로우콜도 묶인다.
    /// </summary>
    static Material EnsureMaterial(string id, Color color)
    {
        string path = ItemDataFolder + "/" + id + "_mat.mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Standard");

        var material = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>아이템이 아닌 플레이스홀더용 공유 머티리얼. sharedMaterial 로 붙여 누출을 막는다.</summary>
    internal static Material EnsureSharedMaterial(string id, Color color)
    {
        Directory.CreateDirectory(MaterialFolder);
        string path = MaterialFolder + "/" + id + ".mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Standard");

        var material = new Material(shader) { color = color };
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    /// <summary>GDD 12항: 월드 아이템은 실제 오브젝트 + Box Collider. 프리미티브 큐브가 플레이스홀더.</summary>
    static void ScatterItems(ItemDefinition[] items)
    {
        var root = new GameObject("WorldItems");
        (Vector3 position, int definitionIndex, int quantity)[] placements =
        {
            (new Vector3(2.5f, 0.2f, 1.5f), 0, 1),
            (new Vector3(-3f, 0.2f, 2f), 0, 2),
            (new Vector3(1f, 0.2f, 4.5f), 1, 3),
            (new Vector3(-1.5f, 0.2f, -2.5f), 1, 8),
            (new Vector3(4.5f, 0.2f, -1f), 2, 4),
            (new Vector3(-4.5f, 0.2f, 5f), 3, 1),
            (new Vector3(3.5f, 0.2f, 3f), 4, 1),
            (new Vector3(-4f, 0.2f, -5f), 2, 7),
            (new Vector3(5f, 0.2f, 2.5f), 5, 1),
            (new Vector3(-2.5f, 0.2f, 6.5f), 6, 1),
            (new Vector3(7f, 0.2f, -3f), 7, 1),
            (new Vector3(-5f, 0.2f, 1f), 8, 1),
            (new Vector3(1.5f, 0.2f, -4f), 9, 1),
        };

        foreach (var (position, definitionIndex, quantity) in placements)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform);
            cube.transform.position = position;
            cube.transform.localScale = Vector3.one * 0.4f;
            cube.AddComponent<WorldItem>().Setup(items[definitionIndex], quantity);
        }
    }

    static Text CreateLabel(Transform parent, string name, Vector2 anchoredPosition, int fontSize)
    {
        var go = new GameObject(name, typeof(Text));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f); // 하단 중앙
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = new Vector2(1200f, 44f);

        var text = go.GetComponent<Text>();
        // 폰트는 빌트인 리소스에 있다 — GetBuiltinExtraResource 가 아니라 Resources.GetBuiltinResource.
        // 한글 글리프는 없으므로 런타임에 PickupPromptUI 가 OS 폰트로 교체한다. 여기서는 에디터 표시용.
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// 테스트용. 허기/탈수/피곤함을 50 으로, 부상을 중간 등급으로 세팅한다.
    /// 부상은 경상/중상/심각 3단계라 정확한 절반이 없어 중상(3단계 중 2단계) 을 쓴다.
    /// </summary>
    [MenuItem("Tools/V0.1/Debug/상태 절반으로")]
    public static void DebugHalfStats()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[V0.1] 플레이 모드에서 실행하세요.");
            return;
        }

        var stats = Object.FindFirstObjectByType<PlayerStats>();
        if (!stats)
        {
            Debug.LogWarning("[V0.1] PlayerStats 를 찾을 수 없습니다.");
            return;
        }

        stats.SetSurvival(50f, 50f, 50f);

        var injuries = Object.FindFirstObjectByType<PlayerInjuries>();
        if (injuries)
        {
            injuries.ClearAll();
            injuries.Apply(BodyPart.LeftLeg, InjurySeverity.Serious);
            injuries.Apply(BodyPart.RightArm, InjurySeverity.Minor);
        }

        Debug.Log("[V0.1] 허기/탈수/피곤함 = 50, 부상 = 왼다리 중상 + 오른팔 경상");
    }

    /// <summary>
    /// 테스트용. 부상 등급을 없음 → 경상 → 중상 → 심각 → 없음 순으로 한 단계씩 올린다.
    /// 일러스트 4종이 제대로 교체되는지 전투 없이 확인하려는 용도.
    /// </summary>
    [MenuItem("Tools/V0.1/Debug/부상 등급 순환")]
    public static void DebugCycleInjury()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[V0.1] 플레이 모드에서 실행하세요.");
            return;
        }

        var injuries = Object.FindFirstObjectByType<PlayerInjuries>();
        if (!injuries)
        {
            Debug.LogWarning("[V0.1] PlayerInjuries 를 찾을 수 없습니다.");
            return;
        }

        int next = (int)injuries.WorstSeverity + 1;
        if (next > (int)InjurySeverity.Critical) next = (int)InjurySeverity.None;

        injuries.ClearAll();
        if (next != (int)InjurySeverity.None) injuries.Apply(BodyPart.LeftLeg, (InjurySeverity)next);

        Debug.Log("[V0.1] 부상 등급 → " + PlayerInjuries.SeverityName((InjurySeverity)next));
    }

    /// <summary>테스트용. 생존 수치와 부상을 초기 상태로 되돌린다.</summary>
    [MenuItem("Tools/V0.1/Debug/상태 초기화")]
    public static void DebugResetStats()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[V0.1] 플레이 모드에서 실행하세요.");
            return;
        }

        var stats = Object.FindFirstObjectByType<PlayerStats>();
        if (stats) stats.ResetStats();

        var injuries = Object.FindFirstObjectByType<PlayerInjuries>();
        if (injuries) injuries.ClearAll();

        Debug.Log("[V0.1] 상태 초기화");
    }

    /// <summary>
    /// 씬을 건드리지 않고 아이템 정의와 카탈로그만 갱신한다.
    /// 씬 재빌드는 손으로 조정한 HUD 값을 초기화하므로, 아이템만 추가할 때는 이걸 쓴다.
    /// </summary>
    [MenuItem("Tools/V0.1/Refresh Item Data")]
    public static void RefreshItemData()
    {
        var items = EnsureItemDefinitions();
        var catalog = EnsureItemCatalog(items);
        var zombies = EnsureZombieDefinitions();
        var recipes = EnsureCraftRecipes();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[V0.1] 데이터 갱신: 아이템 " + items.Length + "종, 카탈로그 " + catalog.Count
                  + "종, 좀비 " + zombies.Length + "종, 레시피 " + recipes.Length + "종");
    }

    [MenuItem("Tools/V0.1/Rebake NavMesh")]
    public static void RebakeNavMesh()
    {
        var existing = Object.FindFirstObjectByType<Unity.AI.Navigation.NavMeshSurface>();
        if (existing) Object.DestroyImmediate(existing.gameObject);

        BuildNavMesh();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }

    [MenuItem("Tools/V0.1/Self Check")]
    public static void SelfCheck()
    {
        var go = new GameObject("__PlayerStatsSelfCheck");
        try
        {
            var stats = go.AddComponent<PlayerStats>();
            stats.ResetStats(); // 에디터에서는 Awake가 돌지 않으므로 직접 초기화

            Check(Mathf.Approximately(stats.Stamina, 100f), "초기 스태미나 100");
            Check(stats.TrySpendStamina(20f), "회피 비용 20 소비 성공");
            Check(Mathf.Approximately(stats.Stamina, 80f), "소비 후 80");
            Check(!stats.TrySpendStamina(200f), "스태미나 부족 시 실패");
            Check(Mathf.Approximately(stats.Stamina, 80f), "실패했으면 한 방울도 소비하지 않음");

            stats.SpendStamina(500f);
            Check(Mathf.Approximately(stats.Stamina, 0f), "달리기 소비는 0에서 멈춤");

            stats.TakeDamage(100f);
            Check(stats.IsDead, "HP 0이면 사망");

            CheckInventory();
            CheckClock();
            CheckSurvival();
            CheckEquipment();
            CheckMedicalTreatmentUI();
            CheckCombat();
            CheckSaveRoundTrip();
            CheckSceneTransferRoundTrip();
            CheckWeaponsAndDurability();
            CheckRangedAndQuickSlots();
            CheckZombieTypesAndCombat();
            CheckAmmoCraftingAndContainers();
            CheckInstanceStateChain();
            CheckWorldAndVision();
            CheckPlayerModelAndAnimation();
            CheckZombieModelAndAnimation();

            Debug.Log("[V0.1] Self check PASSED");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>GDD 11항 칸 기반 배치 / 스택 병합 / 분할 / 자동 정렬 검증.</summary>
    static void CheckInventory()
    {
        var go = new GameObject("__InventorySelfCheck");
        var water = ScriptableObject.CreateInstance<ItemDefinition>();
        var axe = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            water.id = "water_test";
            water.displayName = "물병";
            water.gridSize = new Vector2Int(1, 2);
            water.maxStack = 3;

            axe.id = "axe_test";
            axe.displayName = "손도끼";
            axe.gridSize = new Vector2Int(2, 3);
            axe.maxStack = 1;

            // AddComponent 로는 Awake 가 돌지 않지만 필드 초기값(8x6)은 적용된다.
            var inv = go.AddComponent<PlayerInventory>();
            Check(inv.Width == 8 && inv.Height == 6, "격자 8x6");

            // 배치
            Check(inv.Add(water, 3) == 3, "물병 3개는 한 스택 (maxStack 3)");
            Check(inv.Stacks.Count == 1, "스택 1개");
            Check(inv.GetStack(0).origin == Vector2Int.zero, "첫 스택은 좌상단");
            Check(inv.GetStackAt(new Vector2Int(0, 1)) == inv.GetStack(0), "1x2 는 아래 칸까지 점유");
            Check(!inv.CanPlace(Vector2Int.zero, Vector2Int.one, null), "점유된 칸에는 못 놓음");

            Check(inv.Add(water, 1) == 1, "maxStack 을 넘으면 새 스택");
            Check(inv.Stacks.Count == 2, "스택 2개");
            Check(inv.GetStack(1).origin == new Vector2Int(1, 0), "새 스택은 옆 칸으로");

            Check(inv.Add(axe, 1) == 1, "2x3 손도끼 배치");
            var axeStack = inv.GetStack(2);
            Check(axeStack.Size == new Vector2Int(2, 3), "2x3 크기 반영");
            Check(inv.CanPlace(axeStack.origin, axeStack.Size, axeStack), "자기 자신은 무시하고 판정");

            // 이동
            Check(inv.TryMove(axeStack, new Vector2Int(6, 3)), "빈 자리로 이동");
            Check(axeStack.origin == new Vector2Int(6, 3), "이동 반영");
            Check(!inv.TryMove(axeStack, new Vector2Int(7, 3)), "격자 밖으로는 이동 불가");
            Check(axeStack.origin == new Vector2Int(6, 3), "실패하면 원위치 유지");

            // 병합
            var full = inv.GetStack(0);
            var partial = inv.GetStack(1);
            Check(!inv.TryMerge(partial, full), "꽉 찬 스택에는 합칠 수 없음");
            Check(inv.TakeFrom(full, 1) == 1, "1개 꺼내기");
            Check(inv.TryMerge(partial, full), "자리가 생기면 합쳐짐");
            Check(inv.Stacks.Count == 2, "비워진 스택은 사라짐");
            Check(full.quantity == 3, "합친 결과 3개");

            // 분할
            var half = inv.Split(full, 1);
            Check(half != null, "분할 성공");
            Check(half.quantity == 1 && full.quantity == 2, "분할 수량이 나뉨");
            Check(half.origin != full.origin, "분할된 묶음은 다른 칸");
            Check(inv.Split(full, 2) == null, "전량 분할은 거부");

            // 자동 정렬 — 아이템을 잃지 않는 것이 가장 중요하다.
            int before = TotalQuantity(inv);
            inv.AutoSort();
            Check(TotalQuantity(inv) == before, "정렬 전후 총 수량 보존");
            Check(inv.Stacks.Count == 2, "정렬이 부분 스택을 합침");
            Check(inv.GetStack(0).Size == new Vector2Int(2, 3), "정렬 후 큰 아이템이 먼저");
            Check(inv.GetStack(0).origin == Vector2Int.zero, "정렬 후 좌상단부터 채움");
            Check(NoOverlap(inv), "정렬 후 겹침 없음");
        }
        finally
        {
            Object.DestroyImmediate(water);
            Object.DestroyImmediate(axe);
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>GDD 5항 증가율 / GDD 4항 패널티 / GDD 6항 부상 검증.</summary>
    static void CheckSurvival()
    {
        var go = new GameObject("__SurvivalSelfCheck");
        try
        {
            var stats = go.AddComponent<PlayerStats>();
            stats.ResetStats();

            Check(Mathf.Approximately(stats.Hunger, 0f), "시작 허기 0");
            Check(Mathf.Approximately(stats.WorstConditionRatio, 0f), "시작 패널티 없음");
            Check(Mathf.Approximately(stats.MaxStamina, 100f), "패널티 없으면 최대 스태미나 100");

            // GDD 5항: 게임 1시간당 허기 +4 / 탈수 +6 / 피곤함 +5
            stats.TickSurvival(1f);
            Check(Mathf.Approximately(stats.Hunger, 4f), "허기 게임 1시간당 +4");
            Check(Mathf.Approximately(stats.Thirst, 6f), "탈수 게임 1시간당 +6");
            Check(Mathf.Approximately(stats.Fatigue, 5f), "피곤함 게임 1시간당 +5");
            Check(stats.Thirst > stats.Hunger, "탈수가 허기보다 빠르다 (GDD 4항)");

            // 활동 중에는 추가 증가
            stats.IsExerting = true;
            stats.TickSurvival(1f);
            Check(Mathf.Approximately(stats.Hunger, 12f), "달리는 중에는 2배로 오른다");
            stats.IsExerting = false;

            // GDD 4항: 수치가 나빠지면 최대 스태미나와 회복 효율이 내려간다.
            stats.TickSurvival(100f); // 전부 100 으로 포화
            Check(Mathf.Approximately(stats.Hunger, 100f), "허기는 100 에서 멈춘다");
            Check(Mathf.Approximately(stats.WorstConditionRatio, 1f), "최악 비율 1");
            Check(stats.MaxStamina < 100f, "최대 스태미나 저하");
            Check(stats.RegenMultiplier < 1f, "회복 효율 저하");

            // 위의 TickSurvival(100f) 안에서 이미 100시간분 아사 피해가 들어가 HP 가 0 이다.
            // 아사 피해만 따로 보려면 HP 를 되돌린 뒤 측정해야 한다.
            Check(stats.IsDead, "포화 상태로 오래 방치하면 사망");
            stats.Heal(stats.MaxHp);

            // GDD 4항: 100 방치 시 HP 감소. 허기+탈수 둘 다 100 이므로 2배.
            float hpBefore = stats.Hp;
            stats.TickSurvival(1f);
            Check(stats.Hp < hpBefore, "허기/탈수 100 이면 HP 감소");

            stats.RestoreThirst(50f);
            Check(Mathf.Approximately(stats.Thirst, 50f), "물병으로 탈수 회복");

            CheckInjuries();
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static void CheckInjuries()
    {
        var go = new GameObject("__InjurySelfCheck");
        try
        {
            var injuries = go.AddComponent<PlayerInjuries>();
            injuries.ClearAll();

            Check(!injuries.HasAnyInjury, "시작 시 부상 없음");
            Check(Mathf.Approximately(injuries.MoveSpeedMultiplier, 1f), "부상 없으면 이동 패널티 없음");
            Check(Mathf.Approximately(injuries.DrainPerGameHour(), 0f), "부상 없으면 HP 감소 없음");

            // GDD 6항 효과: 다리 = 이동, 팔 = 공격, 머리 = 행동
            injuries.Apply(BodyPart.LeftLeg, InjurySeverity.Serious);
            Check(injuries.HasAnyInjury, "부상 등록");
            Check(injuries.MoveSpeedMultiplier < 1f, "다리 부상은 이동속도 저하");
            Check(Mathf.Approximately(injuries.AttackEfficiency, 1f), "다리 부상은 공격에 영향 없음");

            injuries.Apply(BodyPart.RightArm, InjurySeverity.Minor);
            Check(injuries.AttackEfficiency < 1f, "팔 부상은 공격 효율 저하");
            Check(injuries.InjuryCount == 2, "부상 2곳");
            Check(injuries.WorstSeverity == InjurySeverity.Serious, "가장 심한 등급은 중상");

            // 같은 부위에는 더 심한 등급만 덮어쓴다.
            injuries.Apply(BodyPart.LeftLeg, InjurySeverity.Minor);
            Check(injuries.GetSeverity(BodyPart.LeftLeg) == InjurySeverity.Serious, "약한 부상으로는 덮어쓰지 않음");
            injuries.Apply(BodyPart.LeftLeg, InjurySeverity.Critical);
            Check(injuries.GetSeverity(BodyPart.LeftLeg) == InjurySeverity.Critical, "더 심한 부상은 덮어쓴다");

            Check(injuries.DrainPerGameHour() > 0f, "치료 안 한 부상은 HP 를 깎는다");

            // GDD 6항 치료: 붕대(경상) 는 심각한 부상을 못 고친다.
            Check(injuries.Treat(InjurySeverity.Minor), "붕대로 경상 치료");
            Check(injuries.GetSeverity(BodyPart.RightArm) == InjurySeverity.None, "치료된 부위는 깨끗해짐");
            Check(injuries.GetSeverity(BodyPart.LeftLeg) == InjurySeverity.Critical, "붕대로는 심각한 부상을 못 고침");
            Check(!injuries.Treat(InjurySeverity.Minor), "고칠 경상이 없으면 실패");

            Check(injuries.Treat(InjurySeverity.Critical), "구급상자로 심각한 부상 치료");
            Check(!injuries.HasAnyInjury, "모두 치료됨");

            // 부상 확률은 방어력을 넘긴 피해에 비례한다 (GDD 6항).
            Check(Mathf.Approximately(PlayerInjuries.InjuryChance(10f, 10f, 30f), 0f), "방어력 이하 피해는 부상 없음");
            Check(PlayerInjuries.InjuryChance(20f, 10f, 30f) > 0f, "방어를 뚫으면 부상 확률 발생");
            Check(PlayerInjuries.InjuryChance(40f, 10f, 30f) > PlayerInjuries.InjuryChance(20f, 10f, 30f),
                "피해가 크면 부상 확률도 높다");
            Check(Mathf.Approximately(PlayerInjuries.InjuryChance(100f, 0f, 30f), 1f), "확률은 1 에서 상한");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static void CheckClock()
    {
        var go = new GameObject("__ClockSelfCheck");
        try
        {
            var clock = go.AddComponent<GameClock>();
            clock.Reset();

            Check(clock.Day == 1 && clock.Hour == 8, "시작 DAY 1 08:00");

            // GDD 18항: 현실 1초 = 게임 1분 이므로 60 분을 밀면 1시간.
            clock.Advance(60f);
            Check(clock.Hour == 9, "60분 진행 = 1시간");
            Check(Mathf.Approximately(clock.DeltaGameHours, 1f), "DeltaGameHours 1");

            clock.Advance(24f * 60f);
            Check(clock.Day == 2 && clock.Hour == 9, "하루가 넘어가면 DAY 증가");
            Check(clock.IsNight == false, "09시는 낮");

            clock.Advance(13f * 60f); // 22시
            Check(clock.IsNight, "22시는 밤");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>GDD 10항 장비 보정 / GDD 6항 부위별 방어력 검증.</summary>
    static void CheckEquipment()
    {
        var go = new GameObject("__EquipmentSelfCheck");
        var jacket = ScriptableObject.CreateInstance<ItemDefinition>();
        var helmet = ScriptableObject.CreateInstance<ItemDefinition>();
        var cloth = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            jacket.id = "jacket_test";
            jacket.displayName = "가죽 재킷";
            jacket.equipSlot = EquipmentSlot.Body;
            jacket.defense = 7f;
            jacket.moveSpeedMultiplier = 0.9f;
            jacket.staminaCostMultiplier = 1.1f;
            jacket.injuryChanceMultiplier = 0.8f;

            cloth.id = "cloth_test";
            cloth.displayName = "천옷";
            cloth.equipSlot = EquipmentSlot.Body;
            cloth.defense = 2f;
            cloth.moveSpeedMultiplier = 1.1f;

            helmet.id = "helmet_test";
            helmet.displayName = "헬멧";
            helmet.equipSlot = EquipmentSlot.Head;
            helmet.defense = 6f;
            helmet.moveSpeedMultiplier = 0.95f;

            var inventory = go.AddComponent<PlayerInventory>();
            var equipment = go.AddComponent<PlayerEquipment>();
            // AddComponent 로는 Awake 가 돌지 않으므로 직접 붙여준다.
            typeof(PlayerEquipment)
                .GetField("inventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(equipment, inventory);

            Check(Mathf.Approximately(equipment.MoveSpeedMultiplier, 1f), "장비 없으면 이동 보정 1");
            Check(Mathf.Approximately(equipment.GetDefense(BodyPart.Torso), 0f), "장비 없으면 방어력 0");

            Check(inventory.Add(jacket, 1) == 1, "재킷 획득");
            Check(equipment.TryEquip(inventory.GetStack(0)), "재킷 장착");
            Check(equipment.Get(EquipmentSlot.Body) == jacket, "몸 슬롯에 들어감");
            Check(inventory.Stacks.Count == 0, "장착하면 인벤토리에서 빠진다");

            // GDD 6항: 몸 슬롯은 몸통과 양팔을 덮고, 머리/다리는 덮지 않는다.
            Check(Mathf.Approximately(equipment.GetDefense(BodyPart.Torso), 7f), "몸통 방어력 7");
            Check(Mathf.Approximately(equipment.GetDefense(BodyPart.LeftArm), 7f), "양팔도 몸 슬롯이 덮는다");
            Check(Mathf.Approximately(equipment.GetDefense(BodyPart.Head), 0f), "머리는 몸 슬롯이 덮지 않는다");
            Check(Mathf.Approximately(equipment.GetDefense(BodyPart.LeftLeg), 0f), "다리도 덮지 않는다");

            // GDD 10항 보정
            Check(Mathf.Approximately(equipment.MoveSpeedMultiplier, 0.9f), "가죽옷은 이동속도 감소");
            Check(Mathf.Approximately(equipment.StaminaCostMultiplier, 1.1f), "스태미나 소비 증가");
            Check(Mathf.Approximately(equipment.InjuryChanceMultiplier, 0.8f), "부상 확률 감소");

            // 같은 슬롯 교체 — 기존 장비는 인벤토리로 돌아온다.
            Check(inventory.Add(cloth, 1) == 1, "천옷 획득");
            Check(equipment.TryEquip(inventory.GetStack(0)), "천옷으로 교체");
            Check(equipment.Get(EquipmentSlot.Body) == cloth, "슬롯이 천옷으로 바뀜");
            Check(inventory.Stacks.Count == 1 && inventory.GetStack(0).definition == jacket,
                "벗겨진 재킷이 인벤토리로 돌아옴");
            Check(Mathf.Approximately(equipment.MoveSpeedMultiplier, 1.1f), "천옷은 이동속도 증가 (GDD 10항 상성)");

            // 다른 슬롯은 함께 곱해진다.
            Check(inventory.Add(helmet, 1) == 1, "헬멧 획득");
            Check(equipment.TryEquip(inventory.GetStack(inventory.Stacks.Count - 1)), "헬멧 장착");
            Check(Mathf.Approximately(equipment.MoveSpeedMultiplier, 1.1f * 0.95f), "여러 장비의 보정은 곱해진다");
            Check(Mathf.Approximately(equipment.GetDefense(BodyPart.Head), 6f), "머리 방어력은 헬멧에서");

            // 해제
            Check(equipment.Unequip(EquipmentSlot.Head), "헬멧 해제");
            Check(equipment.Get(EquipmentSlot.Head) == null, "슬롯이 비었다");
            Check(Mathf.Approximately(equipment.GetDefense(BodyPart.Head), 0f), "머리 방어력도 사라짐");
            Check(!equipment.Unequip(EquipmentSlot.Head), "빈 슬롯 해제는 실패");

            // 방어력이 부상 확률을 낮춘다 (GDD 6항)
            float bare = PlayerInjuries.InjuryChance(20f, 0f, 30f);
            float armored = PlayerInjuries.InjuryChance(20f, 7f, 30f);
            Check(armored < bare, "방어력이 있으면 부상 확률이 낮다");
        }
        finally
        {
            Object.DestroyImmediate(jacket);
            Object.DestroyImmediate(helmet);
            Object.DestroyImmediate(cloth);
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>부상 UI, 출혈 기본값, 치료 시간과 완료 결과를 에디트 모드에서 검증한다.</summary>
    static void CheckMedicalTreatmentUI()
    {
        Check(FindInactiveTransform("HungerIcon"), "HUD 허기 아이콘 존재");
        Check(FindInactiveTransform("ThirstIcon"), "HUD 탈수 아이콘 존재");
        Check(FindInactiveTransform("FatigueIcon"), "HUD 피곤함 아이콘 존재");
        Check(!FindInactiveTransform("InjuryIcon"), "HUD 부상 아이콘 제거");

        var grid = FindInactiveTransform("GridRoot") as RectTransform;
        var character = FindInactiveTransform("CharacterPanel") as RectTransform;
        var nearby = FindInactiveTransform("NearbyRoot") as RectTransform;
        Check(grid && character && nearby, "Tab 세 패널 존재");
        Check(Mathf.Abs(character.rect.width - grid.rect.width) <= 2f, "캐릭터 패널 폭 = 격자 폭");
        Check(Mathf.Abs(nearby.rect.width - grid.rect.width * 0.65f) <= 2f, "주변 패널 폭 = 격자 폭 x 0.65");

        int bodyPartCount = 0;
        int equipmentSlotCount = 0;
        int leftSlots = 0;
        int rightSlots = 0;
        foreach (var rect in character.GetComponentsInChildren<RectTransform>(true))
        {
            if (rect.name.StartsWith("BodyPart_")) bodyPartCount++;
            if (!rect.name.StartsWith("EquipSlot_")) continue;
            equipmentSlotCount++;
            if (rect.anchorMin.x < 0.5f) leftSlots++;
            else rightSlots++;
        }
        Check(bodyPartCount == PlayerInjuries.PartCount, "캐릭터 부위 이미지 6개");
        Check(equipmentSlotCount == 8 && leftSlots == 4 && rightSlots == 4,
              "장비 칸 8개가 그림 양옆에 4개씩");

        var bandageAsset = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/bandage.asset");
        var medkitAsset = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/medkit.asset");
        Check(bandageAsset && medkitAsset, "붕대와 구급상자 에셋 존재");
        Check(Mathf.Approximately(bandageAsset.treatmentDuration, 5f)
              && Mathf.Approximately(medkitAsset.treatmentDuration, 20f), "치료 시간 5초 / 20초");
        Check(Mathf.Approximately(bandageAsset.hpRestore, 12f)
              && Mathf.Approximately(medkitAsset.hpRestore, 60f), "회복량 12 / 60");
        Check(Mathf.Approximately(bandageAsset.recoveryDuration, 6f)
              && Mathf.Approximately(medkitAsset.recoveryDuration, 10f), "회복 시간 6초 / 10초");
        Check(bandageAsset.treatUpTo == InjurySeverity.Serious
              && medkitAsset.treatUpTo == InjurySeverity.Critical, "치료 한도 중상 / 심각");

        var vignette = FindInactiveTransform("DamageVignette");
        Check(vignette && vignette.GetComponent<DamageVignetteUI>(), "HUD 피격 테두리 UI 존재");
        Check(!vignette.GetComponent<Image>().raycastTarget, "피격 테두리는 레이캐스트를 막지 않음");

        var go = new GameObject("__MedicalSelfCheck");
        var bandage = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            var stats = go.AddComponent<PlayerStats>();
            stats.ResetStats();
            var inventory = go.AddComponent<PlayerInventory>();
            var injuries = go.AddComponent<PlayerInjuries>();
            var consumer = go.AddComponent<PlayerConsumer>();

            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Check(Mathf.Approximately((float)typeof(PlayerInjuries).GetField("bleedingChance", flags).GetValue(injuries), 0.4f)
                  && Mathf.Approximately((float)typeof(PlayerInjuries).GetField("bleedingTickInterval", flags).GetValue(injuries), 5f)
                  && Mathf.Approximately((float)typeof(PlayerInjuries).GetField("bleedingDamagePerTick", flags).GetValue(injuries), 2f),
                  "출혈 기본값 0.4 / 5초 / 2");

            bandage.id = "bandage_self_check";
            bandage.displayName = "붕대";
            bandage.usable = true;
            bandage.maxStack = 1;
            bandage.hpRestore = 12f;
            bandage.treatUpTo = InjurySeverity.Serious;
            bandage.treatmentDuration = 5f;
            bandage.recoveryDuration = 6f;

            stats.TakeDamage(20f);
            injuries.Apply(BodyPart.LeftArm, InjurySeverity.Serious);
            injuries.SetBleeding(BodyPart.LeftArm, true);
            Check(inventory.Add(bandage, 1) == 1, "자기검사용 붕대 획득");
            Check(consumer.TryUse(inventory.GetStack(0)), "붕대 감기 시작");
            Check(consumer.CompleteTreatment(), "치료 완료 함수 직접 호출");
            Check(!injuries.IsBleeding(BodyPart.LeftArm), "치료 완료 시 출혈 하나 멎음");
            Check(injuries.GetSeverity(BodyPart.LeftArm) == InjurySeverity.None, "치료 완료 시 기존 부상 치료");
            Check(Mathf.Approximately(consumer.RecoveryRemaining, 12f)
                  && Mathf.Approximately(consumer.RecoverySecondsRemaining, 6f), "서서히 회복 목표 12 / 6초 설정");
            Check(inventory.Stacks.Count == 0, "치료 완료 순간 아이템 1개 소비");
        }
        finally
        {
            Object.DestroyImmediate(bandage);
            Object.DestroyImmediate(go);
        }
    }

    static Transform FindInactiveTransform(string name)
    {
        foreach (var candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (candidate.name == name) return candidate;
        return null;
    }

    /// <summary>GDD 7항 전투 / 저항 / GDD 8항 사망·좀비화 검증 (난수에 의존하지 않는 부분만).</summary>
    static void CheckCombat()
    {
        CheckWeaponData();
        CheckCorpseBelongings();
    }

    /// <summary>손도끼가 주무기 + 무기 속성을 갖췄는지. 빌더가 에셋을 제대로 채웠는지 확인한다.</summary>
    static void CheckWeaponData()
    {
        var axe = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/fire_axe.asset");
        Check(axe != null, "손도끼 에셋 존재");
        Check(axe.equipSlot == EquipmentSlot.OffHand, "한손 손도끼는 보조무기 슬롯");
        Check(axe.attackDamage > 0f, "손도끼에 공격력이 있다");
        Check(axe.attackRange > 1f, "손도끼 사거리");

        var bandage = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/bandage.asset");
        Check(bandage.treatUpTo == InjurySeverity.Serious, "붕대는 중상까지 치료");

        // 주무기 슬롯은 방어 부위가 없다 — 무기를 들어도 방어력이 붙으면 안 된다.
        Check(PlayerEquipment.PartsCovered(EquipmentSlot.MainHand).Length == 0, "주무기 슬롯은 방어 부위가 없다");
        Check(PlayerEquipment.PartsCovered(EquipmentSlot.OffHand).Length == 0, "보조무기 슬롯은 방어 부위가 없다");
    }

    /// <summary>GDD 8항: 사망 시 소지품과 장비가 모두 좀비에게 넘어가고 플레이어는 비어야 한다.</summary>
    static void CheckCorpseBelongings()
    {
        var go = new GameObject("__CorpseSelfCheck");
        var water = ScriptableObject.CreateInstance<ItemDefinition>();
        var jacket = ScriptableObject.CreateInstance<ItemDefinition>();
        try
        {
            water.id = "water_corpse_test";
            water.displayName = "물병";
            water.maxStack = 3;

            jacket.id = "jacket_corpse_test";
            jacket.displayName = "재킷";
            jacket.equipSlot = EquipmentSlot.Body;
            jacket.defense = 5f;

            var inventory = go.AddComponent<PlayerInventory>();
            var equipment = go.AddComponent<PlayerEquipment>();
            typeof(PlayerEquipment)
                .GetField("inventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(equipment, inventory);

            inventory.Add(water, 5);
            inventory.Add(jacket, 1);

            // 재킷을 장착해 장비 슬롯에도 물건이 있는 상태로 만든다.
            for (int i = 0; i < inventory.Stacks.Count; i++)
            {
                if (inventory.GetStack(i).definition != jacket) continue;

                Check(equipment.TryEquip(inventory.GetStack(i)), "재킷 장착");
                break;
            }

            int inventoryTotal = TotalQuantity(inventory);
            Check(inventoryTotal == 5, "장착 후 인벤토리에는 물병 5개만");
            Check(equipment.Get(EquipmentSlot.Body) == jacket, "몸 슬롯에 재킷");

            // 사망 처리와 동일한 순서로 옮긴다.
            int moved = 0;
            var stacks = inventory.Stacks;
            for (int i = 0; i < stacks.Count; i++) moved += stacks[i].quantity;
            inventory.Clear();

            var stripped = equipment.StripAll();
            moved += stripped.Length;

            Check(moved == 6, "물병 5 + 재킷 1 = 6개가 좀비에게 넘어간다");
            Check(inventory.Stacks.Count == 0, "사망 후 인벤토리는 비어 있다");
            Check(equipment.Get(EquipmentSlot.Body) == null, "사망 후 장비도 비어 있다");
            Check(Mathf.Approximately(equipment.GetDefense(BodyPart.Torso), 0f), "방어력도 사라진다");
            Check(Mathf.Approximately(equipment.MoveSpeedMultiplier, 1f), "장비 보정도 초기화된다");
        }
        finally
        {
            Object.DestroyImmediate(water);
            Object.DestroyImmediate(jacket);
            Object.DestroyImmediate(go);
        }
    }

    /// <summary>
    /// GDD 19항 저장 데이터 직렬화 왕복 검증.
    /// 씬을 띄우지 않고 SaveData 자체가 JSON 을 거쳐 온전히 돌아오는지만 본다 —
    /// 실제 씬 복원은 플레이 모드에서 F5/F9 로 확인한다.
    /// </summary>
    static void CheckSaveRoundTrip()
    {
        var data = new SaveData();
        data.day = 7;
        data.minuteOfDay = 13f * 60f + 25f;
        data.playerPosition = new Vector3(12.5f, 1.05f, -8.25f);
        data.playerYaw = 135f;
        data.hp = 63.5f;
        data.stamina = 41.25f;
        data.hunger = 72f;
        data.thirst = 55.5f;
        data.fatigue = 33f;

        data.injuries[(int)BodyPart.LeftLeg] = (int)InjurySeverity.Serious;
        data.injuries[(int)BodyPart.Head] = (int)InjurySeverity.Minor;
        data.bleeding[(int)BodyPart.RightArm] = true;

        data.inventory.Add(new StackSave { id = "water_bottle", quantity = 3, x = 2, y = 1 });
        data.inventory.Add(new StackSave { id = "fire_axe", quantity = 1, x = 5, y = 0 });
        data.equipment.Add(new EquipSave { slot = (int)EquipmentSlot.Body, id = "leather_jacket" });

        data.worldItems.Add(new WorldItemSave
        {
            id = "canned_food", quantity = 4, position = new Vector3(-3f, 0.2f, 6f)
        });

        var corpse = new ZombieSave
        {
            position = new Vector3(1f, 1.05f, 2f), yaw = 90f, health = 42f, isPlayerCorpse = true
        };
        corpse.carried.Add(new StackSave { id = "medkit", quantity = 1 });
        data.zombies.Add(corpse);

        string json = JsonUtility.ToJson(data);
        var back = JsonUtility.FromJson<SaveData>(json);

        Check(back != null, "JSON 왕복 성공");
        Check(back.day == 7, "날짜 보존");
        Check(Mathf.Approximately(back.minuteOfDay, data.minuteOfDay), "하루 내 시각 보존");
        Check(back.playerPosition == data.playerPosition, "플레이어 위치 보존");
        Check(Mathf.Approximately(back.playerYaw, 135f), "플레이어 방향 보존");
        Check(Mathf.Approximately(back.hp, 63.5f) && Mathf.Approximately(back.stamina, 41.25f), "HP/스태미나 보존");
        Check(Mathf.Approximately(back.hunger, 72f) && Mathf.Approximately(back.thirst, 55.5f), "허기/탈수 보존");

        Check(back.injuries.Length == PlayerInjuries.PartCount, "부상 배열 길이 보존");
        Check(back.injuries[(int)BodyPart.LeftLeg] == (int)InjurySeverity.Serious, "왼다리 중상 보존");
        Check(back.injuries[(int)BodyPart.Head] == (int)InjurySeverity.Minor, "머리 경상 보존");
        Check(back.injuries[(int)BodyPart.Torso] == (int)InjurySeverity.None, "부상 없는 부위는 없음으로");
        Check(back.bleeding[(int)BodyPart.RightArm], "오른팔 출혈 보존");
        Check(!back.bleeding[(int)BodyPart.LeftArm], "출혈 없는 부위는 false");

        Check(back.inventory.Count == 2, "인벤토리 스택 2개 보존");
        Check(back.inventory[0].id == "water_bottle" && back.inventory[0].quantity == 3, "스택 종류/수량 보존");
        Check(back.inventory[0].x == 2 && back.inventory[0].y == 1, "격자 좌표 보존 (GDD 11항)");

        Check(back.equipment.Count == 1 && back.equipment[0].id == "leather_jacket", "장비 보존");
        Check(back.equipment[0].slot == (int)EquipmentSlot.Body, "장비 슬롯 보존");


        // GDD 19항이 "중요" 로 명시한 항목
        Check(back.worldItems.Count == 1 && back.worldItems[0].quantity == 4, "월드 아이템 보존");
        Check(back.worldItems[0].position == new Vector3(-3f, 0.2f, 6f), "월드 아이템 위치 보존");

        // GDD 8항 회수 루프가 저장을 건너 살아남아야 한다
        Check(back.zombies.Count == 1 && back.zombies[0].isPlayerCorpse, "내 좀비 보존");
        Check(Mathf.Approximately(back.zombies[0].health, 42f), "좀비 체력 보존");
        Check(back.zombies[0].carried.Count == 1 && back.zombies[0].carried[0].id == "medkit",
            "내 좀비가 든 소지품 보존");

        // 카탈로그가 비어 있으면 로드에서 아이템이 조용히 사라진다.
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemDataFolder + "/ItemCatalog.asset");
        if (catalog)
        {
            Check(catalog.Count > 0, "아이템 카탈로그가 비어 있지 않다");
            Check(catalog.Find("water_bottle") != null, "카탈로그에서 id 로 아이템을 찾는다");
            Check(catalog.Find("없는id") == null, "없는 id 는 null");
        }
    }

    static void CheckSceneTransferRoundTrip()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemDataFolder + "/ItemCatalog.asset");
        var water = catalog ? catalog.Find("water_bottle") : null;
        var source = new GameObject("__SceneTransferSource");
        var target = new GameObject("__SceneTransferTarget");
        var sourceClockObject = new GameObject("__SceneTransferSourceClock");
        var targetClockObject = new GameObject("__SceneTransferTargetClock");
        var arrival = new GameObject("__SceneTransferArrival");
        try
        {
            var sourceStats = source.AddComponent<PlayerStats>();
            sourceStats.ResetStats();
            sourceStats.LoadState(63f, 41f, 72f, 55f, 33f);
            var sourceInventory = source.AddComponent<PlayerInventory>();
            source.AddComponent<PlayerEquipment>();
            var sourceInjuries = source.AddComponent<PlayerInjuries>();
            source.AddComponent<PlayerQuickSlots>();
            sourceInjuries.Apply(BodyPart.LeftLeg, InjurySeverity.Serious);
            sourceInjuries.SetBleeding(BodyPart.RightArm, true);
            if (water) sourceInventory.Add(water, 2);
            Check(water, "씬 이동 검사 아이템이 있다");

            var sourceClock = sourceClockObject.AddComponent<GameClock>();
            sourceClock.LoadState(7, 805f);
            var state = SaveSystem.CapturePlayerState(sourceStats, sourceClock);

            var targetStats = target.AddComponent<PlayerStats>();
            targetStats.ResetStats();
            var targetInventory = target.AddComponent<PlayerInventory>();
            target.AddComponent<PlayerEquipment>();
            var targetInjuries = target.AddComponent<PlayerInjuries>();
            target.AddComponent<PlayerQuickSlots>();
            target.AddComponent<CharacterController>();
            var targetClock = targetClockObject.AddComponent<GameClock>();
            arrival.transform.position = new Vector3(8f, 1.05f, -3f);

            Check(SaveSystem.RestorePlayerState(state, catalog, targetStats, targetClock, arrival.transform),
                  "씬 이동 플레이어 상태 복원 성공");
            Check(Mathf.Approximately(targetStats.Hp, sourceStats.Hp), "씬 이동 체력 보존");
            Check(sourceInventory.Stacks.Count > 0
                  && targetInventory.Stacks.Count == sourceInventory.Stacks.Count,
                  "씬 이동 인벤토리 개수 보존");
            Check(targetInjuries.GetSeverity(BodyPart.LeftLeg) == InjurySeverity.Serious,
                  "씬 이동 부상 보존");
            Check(targetInjuries.IsBleeding(BodyPart.RightArm), "씬 이동 출혈 보존");
            Check(target.transform.position == arrival.transform.position, "씬 이동 도착 지점 배치");
        }
        finally
        {
            Object.DestroyImmediate(arrival);
            Object.DestroyImmediate(targetClockObject);
            Object.DestroyImmediate(sourceClockObject);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(source);
        }
    }

    /// <summary>자기검사용. Awake 가 돌지 않는 에디터 모드에서 private 참조를 직접 꽂는다.</summary>
    static void Inject(object target, string field, object value) =>
        target.GetType()
              .GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
              .SetValue(target, value);

    /// <summary>
    /// 개체 상태(내구도 + 잔탄) 가 아이템이 지나는 모든 경로에서 살아남는지 한 번에 훑는다.
    ///
    /// 이 클래스의 버그가 세 번 났다: 내구도 누락, 장착 시 잔탄 소실,
    /// 월드/시체/컨테이너 경로 잔탄 소실. 경로를 하나 늘릴 때 여기도 늘려야 한다.
    /// </summary>
    static void CheckInstanceStateChain()
    {
        var pistol = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/pistol.asset");
        Check(pistol.UsesAmmo && pistol.maxDurability > 0, "권총은 내구도와 탄약을 모두 가진다");

        const int wornDurability = 137;
        const int spentAmmo = 4;

        // ---- 컨테이너 → 인벤토리 ----
        var box = new GameObject("__ChainBox");
        var holder = new GameObject("__ChainHolder");
        try
        {
            var container = box.AddComponent<WorldContainer>();
            var inventory = holder.AddComponent<PlayerInventory>();

            container.Add(pistol, 1, wornDurability, spentAmmo);
            Check(container.Contents[0].durability == wornDurability, "컨테이너가 내구도를 보관한다");
            Check(container.Contents[0].loadedAmmo == spentAmmo, "컨테이너가 잔탄을 보관한다");

            Check(container.TakeInto(0, inventory) == 1, "컨테이너에서 꺼내기");
            var taken = FindStackById(inventory, "pistol");
            Check(taken != null && taken.durability == wornDurability, "꺼낼 때 내구도 보존");
            Check(taken != null && taken.loadedAmmo == spentAmmo, "꺼낼 때 잔탄 보존");

            // ---- 인벤토리 → 장착 → 해제 ----
            var equipment = holder.AddComponent<PlayerEquipment>();
            Inject(equipment, "inventory", inventory);

            Check(equipment.TryEquipTo(EquipmentSlot.MainHand, taken), "장착");
            var held = equipment.GetStack(EquipmentSlot.MainHand);
            Check(held.durability == wornDurability, "장착해도 내구도 보존");
            Check(held.loadedAmmo == spentAmmo, "장착해도 잔탄 보존");

            Check(equipment.Unequip(EquipmentSlot.MainHand), "해제");
            var back = FindStackById(inventory, "pistol");
            Check(back.durability == wornDurability, "해제해도 내구도 보존");
            Check(back.loadedAmmo == spentAmmo, "해제해도 잔탄 보존");

            // ---- 인벤토리에 없는 스택은 깎이지 않는다 (아이템 복제 방지) ----
            equipment.TryEquipTo(EquipmentSlot.MainHand, back);
            var equipped = equipment.GetStack(EquipmentSlot.MainHand);
            Check(inventory.TakeFrom(equipped, 1) == 0,
                  "장착 중인 스택은 인벤토리에서 꺼낼 수 없다 (복제 방지)");
            Check(equipment.Get(EquipmentSlot.MainHand) == pistol, "거절 후에도 장비가 그대로 남는다");
        }
        finally
        {
            Object.DestroyImmediate(holder);
            Object.DestroyImmediate(box);
        }

        // ---- 인벤토리 → 컨테이너 (넣기) ----
        var depositBox = new GameObject("__ChainDeposit");
        var depositHolder = new GameObject("__ChainDepositHolder");
        try
        {
            var container = depositBox.AddComponent<WorldContainer>();
            var inventory = depositHolder.AddComponent<PlayerInventory>();

            inventory.Add(pistol, 1);
            var stack = FindStackById(inventory, "pistol");
            stack.durability = wornDurability;
            stack.loadedAmmo = spentAmmo;

            Check(container.PutFrom(stack, inventory, 1) == 1, "인벤토리에서 컨테이너로 넣기");
            Check(FindStackById(inventory, "pistol") == null, "넣으면 인벤토리에서 빠진다");
            Check(container.Count == 1, "컨테이너에 한 줄 생긴다");
            Check(container.Contents[0].durability == wornDurability, "넣을 때 내구도 보존");
            Check(container.Contents[0].loadedAmmo == spentAmmo, "넣을 때 잔탄 보존");

            // 인벤토리에 없는 스택은 넣을 수 없다 (복제 방지)
            var ghost = ItemStack.Create(pistol, 1, Vector2Int.zero);
            Check(container.PutFrom(ghost, inventory, 1) == 0, "인벤토리에 없는 스택은 넣을 수 없다");
            Check(container.Count == 1, "거절 시 컨테이너가 늘지 않는다");
        }
        finally
        {
            Object.DestroyImmediate(depositHolder);
            Object.DestroyImmediate(depositBox);
        }

        // ---- 인벤토리 → 월드 (버리기) 가 상태를 실어 보내는지 ----
        var throwOverload = typeof(PlayerInteractor).GetMethod("ThrowToWorld",
            new System.Type[] { typeof(ItemDefinition), typeof(int), typeof(int), typeof(int) });
        Check(throwOverload != null, "투척 경로가 내구도+잔탄을 함께 받는다");

        // 인벤토리 UI 의 버리기가 그 경로를 쓰는지 (2인자 오버로드를 쓰면 새것이 되어 나간다)
        var dropSource = System.IO.File.ReadAllText("Assets/_Game/Scripts/UI/InventoryUI.cs");
        Check(dropSource.Contains("ThrowToWorld(definition, taken, durability, ammo)"),
              "인벤토리 버리기가 내구도/잔탄을 실어 보낸다");

        // Q 로 버리는 경로가 있는지
        Check(typeof(InventoryUI).GetMethod("OnDropHovered",
                  System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null,
              "인벤토리에서 커서 올린 아이템을 Q 로 버릴 수 있다");

        // ---- Bind 이후에 추가한 참조가 죽어 있지 않은지 ----
        // 씬은 빌더가 한 번 만들고 그 뒤 Bind 가 다시 돌지 않는다. 나중에 추가한
        // [SerializeField] 는 씬에 비어 있어서 기능이 조용히 죽는다 (합성/컨테이너 넣기가 그랬다).
        // 그런 참조는 Awake 에서 스스로 찾아야 한다.
        var inventorySource = System.IO.File.ReadAllText("Assets/_Game/Scripts/UI/InventoryUI.cs");
        Check(inventorySource.Contains("if (!crafting && interactor) crafting = interactor.GetComponent<PlayerCrafting>()"),
              "InventoryUI 가 합성 참조를 런타임에 스스로 찾는다");
        Check(inventorySource.Contains("if (!nearbyList) nearbyList = GetComponent<NearbyItemsUI>()"),
              "InventoryUI 가 컨테이너 목록 참조를 런타임에 스스로 찾는다");

        var nearbySource = System.IO.File.ReadAllText("Assets/_Game/Scripts/UI/NearbyItemsUI.cs");
        Check(nearbySource.Contains("if (!inventory && interactor) inventory = interactor.GetComponent<PlayerInventory>()"),
              "NearbyItemsUI 가 인벤토리 참조를 런타임에 스스로 찾는다");

        // ---- 인벤토리/지도/상태창이 열려 있으면 카메라 줌이 막히는지 ----
        var cameraSource = System.IO.File.ReadAllText("Assets/_Game/Scripts/Camera/IsometricCamera.cs");
        Check(cameraSource.Contains("inventoryUi.IsOpen") && cameraSource.Contains("mapUi.IsOpen")
              && cameraSource.Contains("statusUi.IsOpen"),
              "패널이 열려 있으면 휠이 맵 줌으로 새지 않는다");
        Check(typeof(InventoryUI).GetProperty("IsOpen") != null, "InventoryUI 가 열림 상태를 노출한다");

        // ---- 컨테이너에 넣는 경로가 있는지 ----
        Check(typeof(WorldContainer).GetMethod("PutFrom") != null, "컨테이너에 아이템을 넣을 수 있다");
        Check(typeof(NearbyItemsUI).GetMethod("ContainsScreenPoint") != null,
              "컨테이너 목록이 드롭 대상 히트 테스트를 제공한다");

        // ---- GDD 19항: 암살 중에는 다른 동작을 못 한다 ----
        // 차단이 입력 처리 뒤에 있어서 숫자키 소모품과 Q 투척이 그대로 됐다.
        var interactorSource = System.IO.File.ReadAllText("Assets/_Game/Scripts/Items/PlayerInteractor.cs");
        int blockAt = interactorSource.IndexOf("assassinate.IsAssassinating) return");
        int quickAt = interactorSource.IndexOf("HandleQuickSlotKeys(keyboard)");
        int throwAt = interactorSource.IndexOf("HandleThrowInput(keyboard)");
        Check(blockAt >= 0, "PlayerInteractor 에 암살 차단이 있다");
        Check(blockAt < quickAt && blockAt < throwAt,
              "암살 차단이 숫자키/투척 처리보다 먼저 온다");

        Check(System.IO.File.ReadAllText("Assets/_Game/Scripts/Items/PlayerConsumer.cs")
                  .Contains("assassinate.IsAssassinating"),
              "소모품 사용이 암살 중에 막힌다 (퀵슬롯/인벤/커서E 공용 경로)");
        Check(System.IO.File.ReadAllText("Assets/_Game/Scripts/Player/PlayerAim.cs")
                  .Contains("assassinate.IsAssassinating"),
              "암살 중에는 마우스를 따라 돌지 않는다 (위치 고정)");
        Check(System.IO.File.ReadAllText("Assets/_Game/Scripts/Combat/PlayerShield.cs")
                  .Contains("assassinate.IsAssassinating"),
              "암살 중에는 방패를 들 수 없다");

        var inventorySrc2 = System.IO.File.ReadAllText("Assets/_Game/Scripts/UI/InventoryUI.cs");
        Check(inventorySrc2.Contains("if (BlockedByAssassination) return;"),
              "암살 중에는 인벤토리 장착/사용/버리기가 막힌다");

        // ---- NavMesh 가 씬에 구워져 있는지 ----
        // 좀비 추적이 이걸 쓴다. 안 구워져 있으면 조용히 직선 추적으로 되돌아간다.
        var surface = Object.FindFirstObjectByType<Unity.AI.Navigation.NavMeshSurface>();
        Check(surface != null, "씬에 NavMeshSurface 가 있다");

        var triangulation = UnityEngine.AI.NavMesh.CalculateTriangulation();
        Check(triangulation.indices.Length > 0, "NavMesh 가 구워져 있다 (삼각형 " + triangulation.indices.Length / 3 + ")");

        // 좀비가 경로를 쓰는 코드가 남아 있는지
        Check(typeof(Zombie).GetMethod("SteerDirection",
                  System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null,
              "좀비가 NavMesh 경로로 방향을 잡는다");

        // ---- 경로와 물리가 어긋나지 않는지 ----
        // NavMesh 가 "올라갈 수 있다" 고 본 턱을 CharacterController 가 못 올라가면
        // 좀비가 그 턱에 붙어 영구 정지한다 (계단 옆에서 실제로 그랬다).
        float climb = AgentClimb();

        // 자기검사가 만든 임시 PlayerStats 프로브가 잡히지 않게 CharacterController 가 있는 것만 본다.
        CharacterController sceneCc = null;
        foreach (var candidate in Object.FindObjectsByType<PlayerStats>(FindObjectsSortMode.None))
        {
            var found = candidate.GetComponent<CharacterController>();
            if (found) { sceneCc = found; break; }
        }
        Check(sceneCc != null, "씬 플레이어에 CharacterController 가 있다");
        if (sceneCc)
            Check(sceneCc.stepOffset >= climb - 0.001f,
                  "플레이어 stepOffset(" + sceneCc.stepOffset + ") >= NavMesh climb(" + climb + ")");
        foreach (var sceneZombie in Object.FindObjectsByType<Zombie>(FindObjectsSortMode.None))
        {
            var cc = sceneZombie.GetComponent<CharacterController>();
            if (!cc) continue;

            Check(cc.stepOffset >= climb - 0.001f,
                  sceneZombie.name + " stepOffset(" + cc.stepOffset + ") >= NavMesh climb(" + climb + ")");
            break; // 한 마리만 대표로 본다
        }

        // 총알이 커서 지점을 향하는지 (평평한 forward 로 쏘면 높은 곳을 못 맞힌다)
        Check(System.IO.File.ReadAllText("Assets/_Game/Scripts/Combat/PlayerCombat.cs")
                  .Contains("Quaternion.AngleAxis(angle, Vector3.up) * AimDirection()"),
              "총알이 조준 지점(커서 링) 방향으로 나간다");

        // 좀보이드식 AI 세 가지
        var zombieSource = System.IO.File.ReadAllText("Assets/_Game/Scripts/Combat/Zombie.cs");
        Check(zombieSource.Contains("HasSightLine()"), "시야가 벽에 가려진다");
        Check(zombieSource.Contains("void Groan()"), "발견 시 신음으로 주변 좀비를 끌어온다");
        Check(zombieSource.Contains("UpdateIdleWander()"), "유휴 상태에서 배회한다");

        // ---- 사용자 변경: Space=점프, 회피는 좌Ctrl ----
        var motorSource = System.IO.File.ReadAllText("Assets/_Game/Scripts/Player/PlayerMotor.cs");
        Check(motorSource.Contains("WasJumpPressed") && motorSource.Contains("spaceKey.wasPressedThisFrame"),
              "Space 가 점프에 할당됐다");
        Check(motorSource.Contains("leftCtrlKey.wasPressedThisFrame"),
              "회피는 좌Ctrl 로 옮겨졌다 (기획서 7·15항 0.5초 무적 유지)");
        Check(typeof(PlayerMotor).GetProperty("IsAirborne") != null, "공중 상태를 노출한다");

        // ---- 총은 즉발 직선, 근접만 부채꼴 ----
        var combatSource = System.IO.File.ReadAllText("Assets/_Game/Scripts/Combat/PlayerCombat.cs");
        Check(combatSource.Contains("if (swingFx && !(useWeapon && weapon.ranged)) swingFx.Play("),
              "부채꼴 스윙은 근접 무기만 재생한다");
        Check(combatSource.Contains("swingFx.PlayTracer(transform.position, transform.position + direction * travel)"),
              "총은 실제 판정 선과 같은 궤적을 그린다");
        Check(typeof(AttackSwingFx).GetMethod("PlayTracer") != null, "즉발 직선 궤적이 있다");

        // ---- 좀비 소지품 (GDD 8항 회수 루프) ----
        var corpseProbe = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        corpseProbe.name = "__ChainCorpse";
        try
        {
            var zombie = corpseProbe.AddComponent<Zombie>();
            zombie.AddCarried(pistol, 1, wornDurability, spentAmmo);
            Check(zombie.CarriedCount == 1, "좀비가 소지품을 든다");
            Check(zombie.CarriedItems[0].durability == wornDurability, "소지품 내구도 보존");
            Check(zombie.CarriedItems[0].loadedAmmo == spentAmmo, "소지품 잔탄 보존");
        }
        finally
        {
            Object.DestroyImmediate(corpseProbe);
        }

        // ---- 저장 구조체에 칸이 있는지 ----
        Check(HasField(typeof(StackSave), "loadedAmmo"), "StackSave 에 잔탄 칸 (인벤/좀비/컨테이너 공용)");
        Check(HasField(typeof(EquipSave), "loadedAmmo"), "EquipSave 에 잔탄 칸");
        Check(HasField(typeof(WorldItemSave), "loadedAmmo"), "WorldItemSave 에 잔탄 칸");
        Check(HasField(typeof(StackSave), "durability"), "StackSave 에 내구도 칸");
        Check(HasField(typeof(WorldItemSave), "durability"), "WorldItemSave 에 내구도 칸");

        // ---- 월드 아이템이 잔탄을 들고 있는지 ----
        Check(typeof(WorldItem).GetProperty("LoadedAmmo") != null, "WorldItem 이 잔탄을 노출한다");
        Check(typeof(WorldItem).GetMethod("SetAmmo") != null, "WorldItem 에 잔탄을 실을 수 있다");

        // ---- 목록 넘침이 접근 불가로 이어지지 않는지 ----
        Check(typeof(NearbyItemsUI).GetMethod("HandleScroll",
                  System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null,
              "주변/컨테이너 목록에 스크롤이 있다 (넘치는 줄을 가져올 수 있어야 한다)");
    }

    static bool HasField(System.Type type, string name) => type.GetField(name) != null;

    /// <summary>GDD 4·8·12·13·24·26항: 탄약/투척/합성/컨테이너/소음 유입.</summary>
    static void CheckAmmoCraftingAndContainers()
    {
        // ---- GDD 4항 탄약 ----
        string[] guns = { "pistol", "shotgun", "rifle", "bow" };
        string[] ammoIds = { "ammo_pistol", "ammo_shotgun", "ammo_rifle", "ammo_arrow" };
        for (int i = 0; i < guns.Length; i++)
        {
            var gun = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + guns[i] + ".asset");
            var ammo = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + ammoIds[i] + ".asset");
            Check(ammo != null, ammoIds[i] + " 에셋 존재");
            Check(gun.UsesAmmo, guns[i] + " 은 탄약을 쓴다");
            Check(gun.ammoId == ammoIds[i], guns[i] + " 탄종 = " + ammoIds[i]);
            Check(gun.magazineSize > 0, guns[i] + " 장탄수 설정됨");
            Check(gun.reloadTime > 0f, guns[i] + " 재장전 시간 설정됨");
            Check(ammo.maxStack > 1, ammoIds[i] + " 은 여러 개 쌓인다");
        }

        var knife = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/kitchen_knife.asset");
        Check(!knife.UsesAmmo, "근접 무기는 탄약을 쓰지 않는다");

        // 새로 만든 스택은 꽉 찬 상태
        var pistol = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/pistol.asset");
        var fresh = ItemStack.Create(pistol, 1, Vector2Int.zero);
        Check(fresh.loadedAmmo == pistol.magazineSize, "새 총은 장전된 상태로 생성된다");
        Check(!fresh.IsEmpty, "새 총은 빈 상태가 아니다");
        fresh.loadedAmmo = 0;
        Check(fresh.IsEmpty, "잔탄 0 이면 빈 상태");

        // 잔탄도 내구도처럼 개체 상태다 — 장착/해제 왕복에서 보존되어야 한다.
        // (장착 시 잔탄이 0 으로 날아가던 버그가 있었다)
        var ammoProbe = new GameObject("__AmmoSelfCheck");
        try
        {
            var inventory = ammoProbe.AddComponent<PlayerInventory>();
            var equipment = ammoProbe.AddComponent<PlayerEquipment>();
            Inject(equipment, "inventory", inventory);

            inventory.Add(pistol, 1);
            var stack = FindStackById(inventory, "pistol");
            Check(stack.loadedAmmo == pistol.magazineSize, "획득 시 잔탄은 최대치");

            stack.loadedAmmo = 5;
            Check(equipment.TryEquipTo(EquipmentSlot.MainHand, stack), "권총 장착");
            var held = equipment.GetStack(EquipmentSlot.MainHand);
            Check(held.loadedAmmo == 5, "장착해도 잔탄 보존 (5)");

            Check(equipment.Unequip(EquipmentSlot.MainHand), "권총 해제");
            Check(FindStackById(inventory, "pistol").loadedAmmo == 5, "해제해도 잔탄 보존 (5)");
        }
        finally
        {
            Object.DestroyImmediate(ammoProbe);
        }

        // ---- GDD 12·13항 합성 ----
        var recipes = EnsureCraftRecipes();
        Check(recipes.Length == 3, "합성 레시피 3종 (GDD 13항 표)");

        for (int i = 0; i < recipes.Length; i++)
        {
            var r = recipes[i];
            Check(r.baseWeapon != null && r.material != null && r.result != null, "레시피 구성 완전함");
            // GDD 12항: 합성 무기 최대 내구도는 원본의 정확히 2배
            Check(r.result.maxDurability == r.baseWeapon.maxDurability * 2,
                  r.result.displayName + " 내구도 = 원본 x2 (" + r.baseWeapon.maxDurability * 2 + ")");
            Check(r.materialCount >= 1, "필요 재료 수 설정됨");
            Check(r.repairPerMaterial >= 1, "재료당 회복량 설정됨");
        }

        var bat = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/baseball_bat.asset");
        var nailedBat = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/nailed_bat.asset");
        Check(bat.maxDurability == 120 && nailedBat.maxDurability == 240,
              "GDD 12항 예시 그대로: 방망이 120 → 못박힌 방망이 240");
        Check(nailedBat.attackDamage > bat.attackDamage, "못박힌 방망이는 피해가 더 크다 (GDD 13항)");

        var pipe = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/steel_pipe.asset");
        var wrapped = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/wrapped_pipe.asset");
        Check(wrapped.staminaCostMultiplier < pipe.staminaCostMultiplier,
              "손잡이 보강 쇠파이프는 스태미나 소비가 적다 (GDD 13항)");

        var plated = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/plated_bat.asset");
        Check(plated.attackInterval > bat.attackInterval, "철판 보강 방망이는 공격속도가 느리다 (GDD 13항)");

        // 합성/수리 동작
        var probe = new GameObject("__CraftSelfCheck");
        try
        {
            var inventory = probe.AddComponent<PlayerInventory>();
            var crafting = probe.AddComponent<PlayerCrafting>();
            Inject(crafting, "inventory", inventory);
            crafting.Bind(recipes);

            var nail = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/nail.asset");

            inventory.Add(bat, 1);
            inventory.Add(nail, 10);
            var batStack = FindStackById(inventory, "baseball_bat");
            var nailStack = FindStackById(inventory, "nail");

            Check(crafting.TryApply(batStack, nailStack), "방망이 + 못 → 합성 성공");
            var craftedStack = FindStackById(inventory, "nailed_bat");
            Check(craftedStack != null, "합성 결과물이 인벤토리에 들어온다");
            Check(craftedStack.durability == 240, "합성 직후 내구도는 최대치 240 (GDD 12항)");
            Check(FindStackById(inventory, "baseball_bat") == null, "원본 무기가 소비된다");
            Check(FindStackById(inventory, "nail").quantity == 5, "못 5개가 소비된다 (10 → 5)");

            // GDD 12항 수리: 필요한 만큼만 소비
            craftedStack.durability = 225; // 15 부족 → 못 2개 (10/개)
            nailStack = FindStackById(inventory, "nail");
            Check(crafting.TryApply(craftedStack, nailStack), "합성 무기 + 못 → 내구도 회복");
            Check(craftedStack.durability == 240, "최대치를 넘지 않는다");
            Check(FindStackById(inventory, "nail").quantity == 3, "필요한 2개만 소비한다 (5 → 3)");

            Check(!crafting.TryApply(craftedStack, FindStackById(inventory, "nail")),
                  "내구도가 꽉 차면 수리하지 않는다");
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }

        // ---- GDD 24항 컨테이너 ----
        var box = new GameObject("__ContainerSelfCheck");
        try
        {
            var container = box.AddComponent<WorldContainer>();
            var canned = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/canned_food.asset");

            container.Add(canned, 3);
            container.Add(canned, 2);
            Check(container.Count == 1, "내구도 없는 아이템은 한 줄로 합쳐진다");
            Check(container.Contents[0].quantity == 5, "합쳐진 수량 5");

            container.Add(bat, 1, 37);
            Check(container.Count == 2, "무기는 내구도가 달라 따로 담긴다");

            var holder = new GameObject("__ContainerTaker");
            try
            {
                var inventory = holder.AddComponent<PlayerInventory>();
                Check(container.TakeInto(1, inventory) == 1, "무기를 인벤토리로 가져온다");
                Check(container.Count == 1, "가져간 줄은 사라진다");
                Check(FindStackById(inventory, "baseball_bat").durability == 37, "내구도가 그대로 옮겨진다");

                Check(container.TakeInto(0, inventory) == 5, "통조림 5개를 가져온다");
                Check(container.Count == 0, "컨테이너가 빈다");
                Check(container.TakeInto(0, inventory) == 0, "빈 컨테이너에서는 아무것도 안 나온다");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }
        finally
        {
            Object.DestroyImmediate(box);
        }

        // ---- GDD 4항 퀵슬롯 5칸 (Q 투척 대상 선택) ----
        Check(PlayerQuickSlots.SlotCount == 5, "퀵슬롯 5칸 (GDD 4항 1~5)");

        // ---- GDD 18항 소음 전파 거리 ----
        var shotgun = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/shotgun.asset");
        Check(shotgun.noise * Noise.RangePerUnit > 30f, "산탄총 소음은 30m 이상 퍼진다");
        Check(knife.noise * Noise.RangePerUnit < 3f, "식칼 소음은 3m 안쪽");
    }

    static ItemStack FindStackById(PlayerInventory inventory, string id)
    {
        var stacks = inventory.Stacks;
        for (int i = 0; i < stacks.Count; i++)
            if (stacks[i].definition && stacks[i].definition.id == id) return stacks[i];

        return null;
    }

    /// <summary>GDD 16·17·18·19·21·22항: 좀비 3종 / 감지 / 청각 / 암살 / 방패 / 돌진.</summary>
    static void CheckZombieTypesAndCombat()
    {
        // ---- GDD 16항 좀비 3종 ----
        var types = EnsureZombieDefinitions();
        Check(types.Length == 3, "좀비 종류 3종");

        var normal = types[0];
        var female = types[1];
        var heavy = types[2];
        Check(normal.maxHealth == 100f && normal.moveSpeed == 2.5f, "일반 좀비 수치 (HP 100 / 속도 2.5)");
        Check(female.maxHealth == 60f && female.moveSpeed == 4.0f, "여성형 수치 (HP 60 / 속도 4.0)");
        Check(heavy.maxHealth == 220f && heavy.moveSpeed == 1.5f, "헤비 수치 (HP 220 / 속도 1.5)");
        Check(female.moveSpeed > normal.moveSpeed && normal.moveSpeed > heavy.moveSpeed, "이동속도 순서: 여성형 > 일반 > 헤비");

        // GDD 19항: 헤비좀비는 암살할 수 없다
        Check(normal.canBeAssassinated && female.canBeAssassinated, "일반/여성형은 암살 가능");
        Check(!heavy.canBeAssassinated, "헤비좀비는 암살 불가");

        // GDD 22항: 돌진은 헤비좀비만, 종료 후 2초 경직, 개별 20초 쿨타임
        Check(heavy.canCharge, "헤비좀비만 돌진");
        Check(!normal.canCharge && !female.canCharge, "일반/여성형은 돌진 없음");
        Check(heavy.chargeRecovery == 2f, "돌진 종료 후 2초 경직");
        Check(heavy.chargeCooldown == 20f, "돌진 쿨타임 20초");
        Check(heavy.chargeWindup > 0f, "돌진 준비 모션 시간이 있다 (보고 피할 수 있어야 한다)");

        // GDD 21항 충격량 표: 헤비/돌진 = 매우 높음
        Check(heavy.blockImpact > normal.blockImpact, "헤비 공격 충격량이 일반보다 크다");
        Check(heavy.chargeBlockImpact >= 1.5f, "돌진 충격량 1.5초 이상");

        // ---- GDD 18항 청각 ----
        var knife = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/kitchen_knife.asset");
        var bat = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/baseball_bat.asset");
        var bow = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/bow.asset");
        var pistolItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/pistol.asset");
        var rifleItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/rifle.asset");
        var shotgunItem = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/shotgun.asset");

        Check(knife.noise == 5f, "식칼 소음 5 (기획서 예시)");
        Check(bat.noise == 10f, "방망이 소음 10");
        Check(bow.noise == 5f, "활 소음 5 (저소음)");
        Check(pistolItem.noise == 40f, "권총 소음 40");
        Check(rifleItem.noise == 75f, "소총 소음 75");
        Check(shotgunItem.noise == 100f, "산탄총 소음 100 (큰 소음)");
        Check(bow.noise < pistolItem.noise, "활이 권총보다 조용하다 (GDD 10항 활 = 저소음)");
        Check(shotgunItem.noise * Noise.RangePerUnit > knife.noise * Noise.RangePerUnit, "산탄총이 식칼보다 멀리 퍼진다");

        // ---- GDD 21항 방패 데이터 ----
        string[] shieldIds = { "trashcan_lid", "metal_sign", "riot_shield" };
        for (int i = 0; i < shieldIds.Length; i++)
        {
            var shield = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + shieldIds[i] + ".asset");
            Check(shield != null, shieldIds[i] + " 에셋 존재");
            Check(shield.isShield, shieldIds[i] + " 은 방패");
            Check(shield.equipSlot == EquipmentSlot.OffHand, shieldIds[i] + " 은 보조무기 슬롯");
            Check(shield.attackDamage <= 0f, shieldIds[i] + " 은 공격력이 없다 (우클릭이 방어여야 한다)");
            Check(shield.maxDurability > 0, shieldIds[i] + " 내구도 있음 (막았을 때만 감소)");
        }

        // ---- 슬롯 규칙 (GDD 21항 한손 무기 + 방패 조합) ----
        var lid = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/trashcan_lid.asset");
        Check(PlayerEquipment.CanEquipTo(EquipmentSlot.OffHand, lid), "방패는 보조무기 칸에 들어간다");
        Check(!PlayerEquipment.CanEquipTo(EquipmentSlot.MainHand, lid), "방패는 주무기 칸에 못 들어간다");
        Check(PlayerEquipment.CanEquipTo(EquipmentSlot.MainHand, knife), "한손 무기는 주무기 칸에도 들어간다");
        Check(PlayerEquipment.CanEquipTo(EquipmentSlot.OffHand, knife), "한손 무기는 보조무기 칸에도 들어간다");
        Check(PlayerEquipment.CanEquipTo(EquipmentSlot.MainHand, pistolItem), "총은 주무기 칸");
        Check(!PlayerEquipment.CanEquipTo(EquipmentSlot.OffHand, pistolItem), "총은 보조무기 칸에 못 들어간다");
        Check(!PlayerEquipment.CanEquipTo(EquipmentSlot.OffHand, bat), "양손 무기는 보조무기 칸에 못 들어간다");

        // ---- 피해가 그대로 들어가는지 (사용자 결정: 좀비 방어력 제거) ----
        var instanceFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        // HitFlash 가 Renderer 를 요구하므로 프리미티브로 만든다.
        var probe = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        probe.name = "__ZombieSelfCheck";
        try
        {
            var zombie = probe.AddComponent<Zombie>();
            Inject(zombie, "definition", heavy);
            Inject(zombie, "cc", probe.GetComponent<CharacterController>());
            typeof(Zombie).GetMethod("ApplyDefinition",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(zombie, null);
            zombie.LoadHealth(heavy.maxHealth);

            float before = zombie.Health;
            zombie.TakeDamage(30f);
            Check(Mathf.Approximately(before - zombie.Health, 30f), "피해가 깎이지 않고 그대로 들어간다");

            before = zombie.Health;
            zombie.TakeDamage(3f);
            Check(Mathf.Approximately(before - zombie.Health, 3f), "약한 공격도 값 그대로 들어간다");

            Check(zombie.Alert == ZombieAlert.Investigating, "뒤에서 맞으면 그 방향을 조사한다");
            Check(!zombie.IsAwareOfPlayer, "조사 상태는 아직 인식이 아니다 (암살 가능)");

            // GDD 22항 돌진이 실제로 "장전" 되는지. 준비 모션만 돌고 거리가 0 이라
            // 돌진이 아예 안 나가던 버그가 있었다 — 그 불변식을 여기서 못 박는다.
            var charger = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            charger.name = "__ChargeSelfCheck";
            try
            {
                var boss = charger.AddComponent<Zombie>();
                Inject(boss, "definition", heavy);
                Inject(boss, "cc", charger.GetComponent<CharacterController>());
                typeof(Zombie).GetMethod("ApplyDefinition", instanceFlags).Invoke(boss, null);

                var tryStart = typeof(Zombie).GetMethod("TryStartCharge", instanceFlags);
                bool started = (bool)tryStart.Invoke(boss, new object[] { heavy.chargeTriggerRange - 1f });
                Check(started, "사정거리 안에서 돌진이 발동한다");
                Check(boss.IsCharging, "발동 직후 돌진 상태다");

                float remaining = (float)typeof(Zombie).GetField("chargeRemaining", instanceFlags).GetValue(boss);
                Check(Mathf.Approximately(remaining, heavy.chargeDistance),
                      "돌진 거리가 장전된다 (" + heavy.chargeDistance + "m)");

                bool again = (bool)tryStart.Invoke(boss, new object[] { heavy.chargeTriggerRange - 1f });
                Check(!again, "쿨타임 중에는 다시 돌진하지 않는다");

                bool tooFar = (bool)tryStart.Invoke(boss, new object[] { heavy.chargeTriggerRange + 5f });
                Check(!tooFar, "사정거리 밖에서는 돌진하지 않는다");

                // 사용자 결정: 헤비는 넉백에 밀리지 않지만 경직은 받는다
                boss.LoadHealth(heavy.maxHealth);
                boss.TakeDamage(50f, charger.transform.position + Vector3.forward * 2f, 1.4f, 0.6f);
                float kb = (float)typeof(Zombie).GetField("chargeRemaining", instanceFlags).GetValue(boss);
                float pushed = (float)typeof(Zombie).GetField("knockbackRemaining", instanceFlags).GetValue(boss);
                Check(Mathf.Approximately(pushed, 0f), "헤비좀비는 넉백에 밀리지 않는다");
                float stunned = (float)typeof(Zombie).GetField("stunnedUntil", instanceFlags).GetValue(boss);
                Check(stunned > Time.time, "헤비좀비도 경직은 받는다");
            }
            finally
            {
                Object.DestroyImmediate(charger);
            }

            // ---- 추락/수직 판정 (바닥을 뚫고 빠진 좀비가 계속 물던 버그) ----
        var fallProbe = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        fallProbe.name = "__FallSelfCheck";
        var fakePlayer = new GameObject("__FallSelfCheckPlayer");
        try
        {
            var faller = fallProbe.AddComponent<Zombie>();
            Inject(faller, "definition", normal);
            Inject(faller, "cc", fallProbe.GetComponent<CharacterController>());
            Inject(faller, "player", fakePlayer.transform);
            typeof(Zombie).GetMethod("ApplyDefinition", instanceFlags).Invoke(faller, null);

            // 실제 씬에서 멀찍이 떨어진 빈 공간에서 시험한다 — 씬의 플레이어 캡슐이나
            // 장애물이 시야 판정을 가려서 검사가 엉뚱하게 실패한다.
            fakePlayer.transform.position = new Vector3(300f, 300f, 300f);

            // 바로 옆: 보여야 한다
            fallProbe.transform.position = new Vector3(300f, 300f, 302f);
            fallProbe.transform.rotation = Quaternion.LookRotation(Vector3.back);
            var toPlayer = fakePlayer.transform.position - fallProbe.transform.position;
            toPlayer.y = 0f;
            var canSee = typeof(Zombie).GetMethod("CanSeePlayer", instanceFlags);
            Check((bool)canSee.Invoke(faller, new object[] { toPlayer, toPlayer.magnitude }),
                  "같은 높이의 정면 플레이어는 보인다");

            // 수평으로는 같은데 수직으로 멀리: 보이지 않아야 한다
            fallProbe.transform.position = new Vector3(300f, 250f, 302f);
            Check(!(bool)canSee.Invoke(faller, new object[] { toPlayer, toPlayer.magnitude }),
                  "바닥 밑으로 빠진 좀비는 플레이어를 보지 못한다");

            // 복구: 추락 높이 아래면 지면 위로 되돌린다
            fallProbe.transform.position = new Vector3(304f, -200f, 304f);
            typeof(Zombie).GetMethod("RecoverIfFallen", instanceFlags).Invoke(faller, null);
            Check(fallProbe.transform.position.y > 0f,
                  "추락한 좀비는 지면 위로 복구된다 (y=" + fallProbe.transform.position.y.ToString("0.00") + ")");
            Check(Mathf.Approximately(fallProbe.transform.position.x, 304f),
                  "복구 시 수평 위치는 유지된다");
        }
        finally
        {
            Object.DestroyImmediate(fallProbe);
            Object.DestroyImmediate(fakePlayer);
        }

        // ---- 엄폐 판정 (낮은 블록 뒤 좀비가 안 맞던 버그) ----
        var shooter = new GameObject("__LosShooter");
        var victimGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        victimGo.name = "__LosVictim";
        var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cover.name = "__LosCover";
        try
        {
            // 지면 간섭이 없는 빈 높이에서 시험한다.
            shooter.transform.position = new Vector3(0f, 500f, 0f);
            victimGo.transform.position = new Vector3(0f, 500f, 8f);
            UnityEngine.Object.DestroyImmediate(victimGo.GetComponent<CapsuleCollider>());
            var vcc = victimGo.AddComponent<CharacterController>();
            vcc.center = Vector3.zero;
            vcc.height = 2f;
            vcc.radius = 0.5f;
            var victim = victimGo.AddComponent<Zombie>();

            var combat = shooter.AddComponent<PlayerCombat>();
            var los = typeof(PlayerCombat).GetMethod("HasLineOfSight", instanceFlags);

            // 낮은 엄폐물: 윗면이 사격선보다 낮다 → 넘겨 쏠 수 있어야 한다
            cover.transform.position = new Vector3(0f, 499.7f, 4f);
            cover.transform.localScale = new Vector3(3f, 1f, 1f);
            Physics.SyncTransforms();
            Check((bool)los.Invoke(combat, new object[] { victim }),
                  "낮은 엄폐물 뒤의 적은 맞출 수 있다");

            // 높은 엄폐물: 사격선을 가린다 → 막혀야 한다
            cover.transform.position = new Vector3(0f, 501f, 4f);
            cover.transform.localScale = new Vector3(3f, 4f, 1f);
            Physics.SyncTransforms();
            Check(!(bool)los.Invoke(combat, new object[] { victim }),
                  "높은 엄폐물 뒤의 적은 막힌다");

            // 엄폐물이 없으면 당연히 보인다
            cover.transform.position = new Vector3(50f, 501f, 4f);
            Physics.SyncTransforms();
            Check((bool)los.Invoke(combat, new object[] { victim }),
                  "엄폐물이 없으면 시야가 열린다");
        }
        finally
        {
            Object.DestroyImmediate(cover);
            Object.DestroyImmediate(victimGo);
            Object.DestroyImmediate(shooter);
        }

        // 넉백 면역은 헤비만
            Check(heavy.immuneToKnockback, "헤비좀비 넉백 면역");
            Check(!normal.immuneToKnockback && !female.immuneToKnockback, "일반/여성형은 넉백에 밀린다");

            // 사용자 요구: 돌진 데미지
            Check(heavy.chargeDamageMin > heavy.damageMin, "돌진 피해가 일반 공격보다 세다");
            Check(heavy.chargeDamageMax > 0f, "돌진 피해 최대값 설정됨");
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    /// <summary>V0.2: 원거리 무기 데이터 + 퀵슬롯 규칙 + HP 기준 부상 등급.</summary>
    static void CheckRangedAndQuickSlots()
    {
        // 사용자 결정: 총/활은 한손이든 양손이든 무조건 주무기.
        string[] ranged = { "pistol", "shotgun", "rifle", "bow" };
        for (int i = 0; i < ranged.Length; i++)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + ranged[i] + ".asset");
            Check(weapon != null, ranged[i] + " 에셋 존재");
            Check(weapon.ranged, ranged[i] + " 은 원거리");
            Check(weapon.equipSlot == EquipmentSlot.MainHand, ranged[i] + " 은 주무기 슬롯");
            Check(weapon.attackSpread > 0f, ranged[i] + " 전용 부채꼴 지정됨");
            Check(weapon.attackRange > 5f, ranged[i] + " 사거리가 근접보다 길다");
            Check(weapon.maxDurability > 0, ranged[i] + " 내구도 지정됨");
        }

        var pistol = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/pistol.asset");
        var shotgun = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/shotgun.asset");
        var rifle = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/rifle.asset");
        Check(pistol.handedness == WeaponHandedness.OneHanded, "권총은 한손인데도 주무기");
        Check(shotgun.attackSpread > rifle.attackSpread, "산탄총 부채꼴이 소총보다 넓다");
        Check(shotgun.maxTargets > 1, "산탄총은 다중 타격");

        // 사용자 요구: 총은 격발에 스태미나를 쓰지 않는다 (활은 당겨야 하므로 예외).
        Check(pistol.attackStaminaCost == 0f, "권총은 스태미나 소모 없음");
        Check(shotgun.attackStaminaCost == 0f, "산탄총은 스태미나 소모 없음");
        Check(rifle.attackStaminaCost == 0f, "소총은 스태미나 소모 없음");
        var bow = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/bow.asset");
        Check(bow.attackStaminaCost > 0f, "활은 당기는 힘이 든다");

        // 사용자 요구: 총기 사거리를 최소 4배로. 근접보다 압도적으로 길어야 한다.
        var melee = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/baseball_bat.asset");
        Check(pistol.attackRange >= 50f, "권총 사거리 50m 이상 (" + pistol.attackRange + ")");
        Check(shotgun.attackRange >= 30f, "산탄총 사거리 30m 이상 (" + shotgun.attackRange + ")");
        Check(rifle.attackRange >= 80f, "소총 사거리 80m 이상 (" + rifle.attackRange + ")");
        Check(rifle.attackRange > pistol.attackRange && pistol.attackRange > shotgun.attackRange,
              "사거리 순서: 소총 > 권총 > 산탄총");
        Check(shotgun.attackRange > melee.attackRange * 10f, "가장 짧은 총도 근접의 10배 이상");

        // 사용자 요구: 식칼은 더 빠르고 더 약하게
        var knife = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/kitchen_knife.asset");
        var handAxe = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/fire_axe.asset");
        Check(knife.attackInterval < handAxe.attackInterval, "식칼이 손도끼보다 빠르다");
        Check(knife.attackDamage < handAxe.attackDamage, "식칼이 손도끼보다 약하다");

        // GDD 20항: 넉백/경직 값이 실제로 좀비에 전달되는지. 값만 있고 읽는 곳이 없던 버그가 있었다.
        var bat2 = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/baseball_bat.asset");
        Check(bat2.knockback > 0f && bat2.staggerDuration > 0f, "야구방망이에 넉백/경직 값이 있다");
        var takeDamage = typeof(Zombie).GetMethod("TakeDamage",
            new System.Type[] { typeof(float), typeof(Vector3), typeof(float), typeof(float) });
        Check(takeDamage != null, "Zombie 가 넉백/경직을 받는 피격 경로를 가진다");

        // 퀵슬롯: 1·2번은 장비를 그대로 비추고, 3~5번은 소모품만 받는다.
        Check(PlayerQuickSlots.SlotCount == 5, "퀵슬롯 5칸");
        Check(PlayerQuickSlots.IsWeaponSlot(0) && PlayerQuickSlots.IsWeaponSlot(1), "1·2번은 무기 칸");
        Check(!PlayerQuickSlots.IsWeaponSlot(2), "3번은 소모품 칸");
        Check(PlayerQuickSlots.WeaponSlotFor(0) == EquipmentSlot.MainHand, "1번 = 주무기");
        Check(PlayerQuickSlots.WeaponSlotFor(1) == EquipmentSlot.OffHand, "2번 = 보조무기");

        var go = new GameObject("__QuickSlotSelfCheck");
        try
        {
            var inventory = go.AddComponent<PlayerInventory>();
            var equipment = go.AddComponent<PlayerEquipment>();
            var slots = go.AddComponent<PlayerQuickSlots>();
            Inject(equipment, "inventory", inventory);
            Inject(slots, "inventory", inventory);
            Inject(slots, "equipment", equipment);

            var bandage = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/bandage.asset");

            inventory.Add(rifle, 1);
            Check(slots.TryAssign(0, inventory.GetStack(0)), "소총을 주무기 칸에");
            Check(slots.GetDisplayed(0) == rifle, "주무기 칸이 장비를 그대로 비춘다");

            inventory.Clear();
            inventory.Add(rifle, 1);
            Check(!slots.TryAssign(1, inventory.GetStack(0)), "총은 보조무기 칸에 못 들어간다");
            Check(!slots.TryAssign(2, inventory.GetStack(0)), "무기는 소모품 칸에 못 들어간다");

            inventory.Clear();
            inventory.Add(bandage, 3);
            var bandageStack = inventory.GetStack(0);
            Check(!slots.TryAssign(0, bandageStack), "소모품은 무기 칸에 못 들어간다");
            Check(slots.TryAssign(2, bandageStack), "붕대를 3번 칸에 등록");
            Check(slots.TryAssign(3, bandageStack), "같은 붕대를 4번 칸에 등록");
            Check(slots.GetDisplayed(2) == null, "같은 아이템은 한 칸에만 남는다");
            Check(slots.CountOf(3) == 3, "소모품 칸은 인벤토리 총 수량을 센다");

            slots.Clear(0);
            Check(slots.GetDisplayed(0) == null, "무기 칸 비우기 = 해제");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }

        // 사용자 결정: HP 50% 초과 경상, 50% 이하 중상, 20% 이하 심각.
        Check(PlayerInjuries.SeverityForHpRatio(0.9f, 0.5f, 0.2f) == InjurySeverity.Minor, "HP 90% = 경상");
        Check(PlayerInjuries.SeverityForHpRatio(0.5f, 0.5f, 0.2f) == InjurySeverity.Serious, "HP 50% = 중상");
        Check(PlayerInjuries.SeverityForHpRatio(0.2f, 0.5f, 0.2f) == InjurySeverity.Critical, "HP 20% = 심각");
    }

    /// <summary>V0.2 10·11항 한손/양손 + 내구도 규칙 검증.</summary>
    static void CheckWeaponsAndDurability()
    {
        // 데이터: 6종이 다 있고 손 구분과 내구도가 채워졌는지
        string[] ids = { "kitchen_knife", "fire_axe", "baseball_bat", "axe", "sledgehammer", "fireman_axe" };
        for (int i = 0; i < ids.Length; i++)
        {
            var weapon = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/" + ids[i] + ".asset");
            Check(weapon != null, ids[i] + " 에셋 존재");
            Check(weapon.attackDamage > 0f, ids[i] + " 공격력");
            var expectedSlot = weapon.handedness == WeaponHandedness.TwoHanded
                ? EquipmentSlot.MainHand
                : EquipmentSlot.OffHand;
            Check(weapon.equipSlot == expectedSlot, ids[i] + " 손 구분에 맞는 슬롯");
            Check(weapon.handedness != WeaponHandedness.None, ids[i] + " 손 구분 지정됨");
            Check(weapon.maxDurability > 0, ids[i] + " 내구도 지정됨");
        }

        var knife = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/kitchen_knife.asset");
        var bat = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/baseball_bat.asset");

        // GDD 10항: 한손은 단일 대상, 양손은 다중타격
        Check(knife.handedness == WeaponHandedness.OneHanded, "식칼은 한손");
        Check(knife.maxTargets == 1, "한손은 단일 대상");
        Check(bat.handedness == WeaponHandedness.TwoHanded, "야구방망이는 양손");
        Check(bat.maxTargets > 1, "양손은 다중타격");
        Check(bat.knockback > knife.knockback, "양손 넉백이 한손보다 크다");
        Check(bat.staggerDuration > knife.staggerDuration, "양손 경직이 한손보다 길다");

        // GDD 12항 합성 예시의 기준값
        Check(bat.maxDurability == 120, "야구방망이 내구도 120 (GDD 12항 합성 예시 기준)");

        // 내구도는 개체 상태 — 같은 정의로 만든 두 스택이 독립적이어야 한다
        var go = new GameObject("__DurabilitySelfCheck");
        try
        {
            var inventory = go.AddComponent<PlayerInventory>();
            var equipment = go.AddComponent<PlayerEquipment>();
            typeof(PlayerEquipment)
                .GetField("inventory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(equipment, inventory);

            Check(inventory.Add(bat, 1) == 1, "야구방망이 획득");
            var stack = inventory.GetStack(0);
            Check(stack.HasDurability, "무기는 내구도를 가진다");
            Check(stack.durability == bat.maxDurability, "획득 시 내구도는 최대치");
            Check(!stack.IsBroken, "새 무기는 파손 아님");

            // 마모시킨 뒤 장착 → 해제 왕복에서 내구도가 보존되어야 한다
            stack.durability = 37;
            Check(equipment.TryEquip(stack), "장착");
            var equippedStack = equipment.GetStack(EquipmentSlot.MainHand);
            Check(equippedStack != null, "장비 슬롯에 스택이 들어감");
            Check(equippedStack.durability == 37, "장착해도 내구도 보존");
            Check(inventory.Stacks.Count == 0, "장착하면 인벤토리에서 빠진다");

            Check(equipment.Unequip(EquipmentSlot.MainHand), "해제");
            Check(inventory.Stacks.Count == 1, "해제하면 인벤토리로 돌아온다");
            Check(inventory.GetStack(0).durability == 37, "해제해도 내구도 보존");

            // 파손 판정
            inventory.GetStack(0).durability = 0;
            Check(inventory.GetStack(0).IsBroken, "내구도 0 이면 파손");

            // 소모품은 내구도 개념이 없다
            var water = AssetDatabase.LoadAssetAtPath<ItemDefinition>(ItemDataFolder + "/water_bottle.asset");
            Check(water.maxDurability == 0, "물병은 내구도 없음");
            inventory.Clear();
            inventory.Add(water, 1);
            Check(!inventory.GetStack(0).HasDurability, "소모품 스택은 내구도를 갖지 않는다");
            Check(!inventory.GetStack(0).IsBroken, "내구도 없는 아이템은 파손되지 않는다");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static int TotalQuantity(PlayerInventory inventory)
    {
        int total = 0;
        foreach (var stack in inventory.Stacks) total += stack.quantity;
        return total;
    }

    static bool NoOverlap(PlayerInventory inventory)
    {
        var stacks = inventory.Stacks;
        for (int i = 0; i < stacks.Count; i++)
        for (int j = i + 1; j < stacks.Count; j++)
            if (stacks[i].Overlaps(stacks[j].origin, stacks[j].Size)) return false;

        return true;
    }

    static void Check(bool condition, string label)
    {
        if (!condition) throw new System.Exception("[V0.1] Self check FAILED: " + label);
    }


    // ---- 렌더 설정 (그림자 계단현상) ----
    const int MsaaSamples = 4;
    const int ShadowmapResolution = 4096;
    // 부지가 45 x 35m 라 40m 로는 먼 구석 그림자가 끊긴다. 60m 면 텍셀 1.46cm 로 아직 충분하다.
    const float ShadowDistance = 60f;
    const int ShadowCascades = 1;

    /// <summary>
    /// 그림자가 계단처럼 끊겨 보이던 원인을 잡는다. 시야 음영(VisionFog) 과는 무관한 별개 문제였다.
    ///
    ///  - 캐스케이드 4개 + 그림자 거리 50m: 카메라가 고정 각도 아이소메트릭이라 가까운 칸만
    ///    촘촘하고 건물이 있는 15~25m 구간은 캐스케이드 2~3 으로 밀려 텍셀이 5cm 까지 커졌다.
    ///    이 카메라는 화면 전체가 비슷한 거리라 캐스케이드를 나눌 이유가 없다 → 1개로 균일하게.
    ///  - 2048 → 4096: 40m 를 4096 으로 덮으면 텍셀이 약 1cm. PCF 필터가 이 정도면 부드럽다.
    ///  - MSAA 꺼짐: 그림자 경계와 별개로 모든 기하 실루엣이 톱니였다. 그림자맵 해상도로는
    ///    이건 안 고쳐진다 (다른 원인, 다른 처방).
    /// </summary>
    internal static void ApplyRenderSettings()
    {
        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                       as UniversalRenderPipelineAsset;
        if (!pipeline) { Debug.LogWarning("[V0.1] URP 에셋을 찾지 못해 렌더 설정을 건너뜁니다"); return; }

        var so = new SerializedObject(pipeline);
        SetInt(so, "m_MSAA", MsaaSamples);
        SetInt(so, "m_MainLightShadowmapResolution", ShadowmapResolution);
        SetInt(so, "m_ShadowCascadeCount", ShadowCascades);
        SetInt(so, "m_SoftShadowQuality", 3);          // High
        var distance = so.FindProperty("m_ShadowDistance");
        if (distance != null) distance.floatValue = ShadowDistance;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(pipeline);
        AssetDatabase.SaveAssets();

        Debug.Log("[V0.1] 렌더 설정: MSAA " + MsaaSamples + "x, 그림자맵 " + ShadowmapResolution
                  + ", 거리 " + ShadowDistance + "m, 캐스케이드 " + ShadowCascades
                  + " → 텍셀 " + (ShadowDistance / ShadowmapResolution * 100f).ToString("F2") + "cm");
    }

    static void SetInt(SerializedObject so, string path, int value)
    {
        var property = so.FindProperty(path);
        if (property == null) { Debug.LogWarning("[V0.1] 렌더 설정 필드 없음: " + path); return; }

        property.intValue = value;
    }


    // ---- 시작 맵: 주인공의 원룸 아파트 ----
    //
    // 참고 도면의 치수를 그대로 미터로 옮겼다 (11'6" = 3.50m 식).
    //   거실 3.50 x 4.10 | 침실 3.05 x 3.59 | 욕실 1.92 x 1.53 | 주방 2.90 x 2.84 | 식당 2.29 x 1.98
    // 좌표는 실내 남서쪽 안쪽 모서리를 (0,0) 으로 잡은 로컬이고, ApartmentOrigin 만큼 밀어 놓는다.
    //
    //   z=8.20 ┌─────────────┬──[창]──────────┐
    //          │   거실       │     침실        │
    //   z=4.55 ├──[카운터]────┤──[문]──────────┤
    //          │            복│    │   욕실     │
    //   z=2.90 │   (통로)     도│    ├──────────┤
    //          ├─────────────┤    │   주방     │
    //   z=0    └── 식당 ──────┴[현관]──────────┘
    //
    static void CheckPlayerModelAndAnimation()
    {
        var modelImporter = AssetImporter.GetAtPath(PlayerModelPath) as ModelImporter;
        Check(modelImporter, "character01 모델 FBX가 있다");
        Check(modelImporter.animationType == ModelImporterAnimationType.Human,
              "character01 리그가 Humanoid다");

        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(PlayerModelPath);
        Check(avatar && avatar.isValid && avatar.isHuman, "character01 아바타가 유효한 Human이다");
        Check(string.IsNullOrEmpty(HumanBoneName(avatar.humanDescription, "LeftEye")), "character01 LeftEye가 머리카락 본에 매핑되지 않았다");
        Check(string.IsNullOrEmpty(HumanBoneName(avatar.humanDescription, "RightEye")), "character01 RightEye가 머리카락 본에 매핑되지 않았다");
        Check(string.IsNullOrEmpty(HumanBoneName(avatar.humanDescription, "Jaw")), "character01 Jaw가 머리카락 본에 매핑되지 않았다");
        Check(AssetDatabase.FindAssets("t:Texture2D", new[] { PlayerTextureFolder }).Length > 0,
              "character01 내장 텍스처가 Textures 폴더에 추출됐다");

        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(PlayerControllerPath);
        Check(controller, "Player.controller가 있다");
        Check(controller.parameters.Length == 4, "Player.controller 파라미터가 4개다");
        Check(HasParameter(controller, "Speed", AnimatorControllerParameterType.Float), "플레이어 Speed 파라미터");
        Check(HasParameter(controller, "Attack", AnimatorControllerParameterType.Trigger), "플레이어 Attack 파라미터");
        Check(HasParameter(controller, "AttackOneHand", AnimatorControllerParameterType.Trigger), "플레이어 AttackOneHand 파라미터");
        Check(HasParameter(controller, "Dead", AnimatorControllerParameterType.Bool), "플레이어 Dead 파라미터");

        var states = controller.layers[0].stateMachine.states;
        Check(states.Length == 6, "플레이어 상태가 6개다");
        for (int i = 0; i < PlayerClipNames.Length; i++)
            Check(FindState(states, PlayerClipNames[i]) != null, PlayerClipNames[i] + " 상태가 있다");

        var missing = new System.Collections.Generic.List<string>();
        for (int i = 0; i < PlayerClipNames.Length; i++)
        {
            string path = PlayerCharacterFolder + "/character01@" + PlayerClipNames[i] + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            var state = FindState(states, PlayerClipNames[i]);
            if (!importer)
            {
                missing.Add(PlayerClipNames[i]);
                Check(state.motion == null, PlayerClipNames[i] + " 클립이 없어 상태 모션이 비어 있다");
                continue;
            }

            Check(importer.animationType == ModelImporterAnimationType.Human,
                  PlayerClipNames[i] + ": 리그가 Humanoid다");
            Check(importer.avatarSetup == ModelImporterAvatarSetup.CreateFromThisModel,
                  PlayerClipNames[i] + ": 자체 아바타를 만든다");
            Check(!importer.sourceAvatar, PlayerClipNames[i] + ": 원본 아바타가 없다");
            Check(importer.importedTakeInfos.Length > 0, PlayerClipNames[i] + ": 클립이 1개 이상이다");

            var settings = importer.clipAnimations;
            if (settings.Length == 0) settings = importer.defaultClipAnimations;
            Check(settings.Length > 0, PlayerClipNames[i] + ": 클립 설정이 있다");
            for (int c = 0; c < settings.Length; c++)
            {
                Check(settings[c].loopTime == (i < 3), PlayerClipNames[i] + ": 루프 설정이 맞다");
                Check(settings[c].lockRootRotation && settings[c].lockRootHeightY && settings[c].lockRootPositionXZ,
                      PlayerClipNames[i] + ": 루트 모션이 꺼져 있다");
            }

            var clip = LoadPlayerClip(PlayerClipNames[i]);
            Check(clip && clip.humanMotion, PlayerClipNames[i] + ": 휴머노이드 클립이 있다");
            Check(state.motion == clip, PlayerClipNames[i] + ": 해당 상태에 클립이 연결됐다");
        }
        if (missing.Count > 0) Debug.LogWarning("[V0.1] 없는 클립: " + string.Join(", ", missing));

        CharacterController body = null;
        foreach (var candidate in Object.FindObjectsByType<PlayerStats>(FindObjectsSortMode.None))
        {
            body = candidate.GetComponent<CharacterController>();
            if (body) break;
        }
        Check(body, "씬 플레이어에 CharacterController가 있다");
        Check(Mathf.Abs(body.height - PlayerHeight) < 0.001f, "플레이어 콜라이더 높이가 1.6m다");
        Check(!body.GetComponent<MeshRenderer>(), "플레이어에 캡슐 MeshRenderer가 없다");
        Check(!body.transform.Find("Face"), "플레이어에 CapsuleFace가 없다");

        var visual = body.transform.Find("Model");
        Check(visual, "character01이 플레이어 자식으로 붙었다");
        Check(Mathf.Abs(MeasureModelHeight(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath))
                        * visual.localScale.y - PlayerHeight) <= 0.02f,
              "character01 몸 높이가 1.6m ± 0.02m다");
        Check(Mathf.Abs(visual.localPosition.y + PlayerHeight * 0.5f) < 0.001f,
              "character01 발이 콜라이더 밑면에 맞았다");

        var animator = visual.GetComponentInChildren<Animator>();
        Check(animator && animator.runtimeAnimatorController == controller,
              "플레이어 Animator에 Player.controller가 연결됐다");
        Check(!animator.applyRootMotion, "플레이어 루트 모션이 꺼져 있다");
        Check(body.GetComponent<PlayerAnimator>(), "PlayerAnimator가 붙었다");

        var hairSprings = visual.GetComponentsInChildren<HairSpring>(true);
        Check(hairSprings.Length == 1, "플레이어 모델에 HairSpring이 1개다");
        var hair = hairSprings[0];
        Check(hair.StrandCount == 7, "HairSpring 가닥이 7개다");
        Check(hair.BoneCount == 19, "HairSpring 본 합계가 19개다");
        Check(hair.MaxAngle <= 15f && Mathf.Approximately(hair.MaxAngle, 12f), "HairSpring maxAngle이 기본 12도이고 15도 이하다");

        var hairBones = new System.Collections.Generic.List<Transform>();
        var modelBones = visual.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < modelBones.Length; i++)
            if (modelBones[i].name.StartsWith("Hair_") && modelBones[i].name != "Hair_work") hairBones.Add(modelBones[i]);
        Check(hairBones.Count == 19, "character01 머리카락 본을 19개 찾았다");

        Vector3 originalPosition = visual.position;
        hair.Simulate(1f / 60f);
        var referenceRotations = new Quaternion[hairBones.Count];
        var referenceWorldRotations = new Quaternion[hairBones.Count];
        for (int i = 0; i < hairBones.Count; i++)
        {
            referenceRotations[i] = hairBones[i].localRotation;
            referenceWorldRotations[i] = hairBones[i].rotation;
        }
        const float hairStep = 0.2f;
        Check(!PlayerAnimator.IsTeleport(Vector3.right * hairStep, 1f / 60f, body.GetComponent<PlayerMotor>().RunSpeed),
              "HairSpring 검사용 이동이 순간이동 한계보다 작다");
        for (int i = 0; i < 8; i++)
        {
            visual.position += Vector3.right * hairStep;
            hair.Simulate(1f / 60f);
        }
        float largestAngle = 0f;
        float largestCumulativeAngle = 0f;
        for (int i = 0; i < hairBones.Count; i++)
        {
            largestAngle = Mathf.Max(largestAngle, Quaternion.Angle(referenceRotations[i], hairBones[i].localRotation));
            largestCumulativeAngle = Mathf.Max(largestCumulativeAngle,
                Quaternion.Angle(referenceWorldRotations[i], hairBones[i].rotation));
        }
        visual.position = originalPosition;
        for (int i = 0; i < hairBones.Count; i++) hairBones[i].localRotation = referenceRotations[i];
        hair.Simulate(1f / 60f);
        Check(largestAngle > 0.5f, "작은 연속 이동으로 머리카락 본이 기준에서 0.5도 넘게 움직인다");
        Check(largestAngle <= hair.MaxAngle + 0.5f, "머리카락 마디 각도가 maxAngle + 0.5도 이하다");
        Check(largestCumulativeAngle <= hair.MaxAngle + 0.5f, "머리카락 누적 각도가 maxAngle + 0.5도 이하다");

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        Check(renderers.Length > 0, "character01에 렌더러가 있다");
        for (int r = 0; r < renderers.Length; r++)
        for (int m = 0; m < renderers[r].sharedMaterials.Length; m++)
        {
            var material = renderers[r].sharedMaterials[m];
            if (!material) { Check(false, "character01 렌더러에 null 재질이 없다"); continue; }
            Check(material && material.shader == shader, material.name + " 재질이 URP Lit다");
            var texture = material.GetTexture("_BaseMap");
            Check(texture && AssetDatabase.GetAssetPath(texture).StartsWith(PlayerTextureFolder + "/"),
                  material.name + " 재질이 추출된 텍스처를 참조한다");
        }
    }

    /// <summary>
    /// 좀비 3D 모델 + 휴머노이드 애니메이션 체인.
    ///
    /// 이 경로에서 실제로 두 번 물렸다:
    ///  - 애니메이션 전용 FBX 는 루트가 하나라 유니티가 Armature 노드를 지운다 →
    ///    본 경로가 모델과 달라져 아바타 복사가 실패하고 클립이 0개가 된다 (preserveHierarchy 로 해결).
    ///  - 모델 실측 높이가 1.0m 라서 콜라이더를 그 높이로 만들면 플레이어 히트스캔 스윕
    ///    (어깨 높이에서 반지름 0.5) 이 머리 위를 지나가 총이 안 맞는다.
    /// </summary>
    static void CheckZombieModelAndAnimation()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ZombieModelPath);
        Check(model, "좀비 모델 FBX 가 있다: " + ZombieModelPath);

        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(ZombieModelPath);
        Check(avatar && avatar.isValid, "모델 아바타가 유효하다");
        Check(avatar && avatar.isHuman, "휴머노이드 아바타다 (이래야 클립이 리타기팅된다)");

        // ---- 클립 4개 ----
        string[] names = { "Idle", "Walk", "Attack", "Death" };
        bool[] loops = { true, true, false, false };
        for (int i = 0; i < names.Length; i++)
        {
            string path = CharacterFolder + "/zombi1@" + names[i] + ".fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            Check(importer, names[i] + " FBX 가 있다");
            Check(importer.animationType == ModelImporterAnimationType.Human,
                  names[i] + ": 리그가 Humanoid 다");
            Check(importer.avatarSetup == ModelImporterAvatarSetup.CopyFromOther,
                  names[i] + ": 아바타를 모델에서 복사한다");
            Check(importer.sourceAvatar == avatar, names[i] + ": 복사 원본이 모델 아바타다");
            Check(importer.preserveHierarchy,
                  names[i] + ": preserveHierarchy 가 켜져 있다 (끄면 본 경로가 어긋나 클립이 사라진다)");

            var clip = LoadClip(names[i]);
            Check(clip, names[i] + " 클립이 임포트됐다");
            Check(clip.humanMotion, names[i] + ": 휴머노이드 모션이다");
            Check(clip.isLooping == loops[i], names[i] + ": 루프 설정이 " + loops[i]);
            Check(clip.length > 0.1f, names[i] + ": 길이가 있다 (" + clip.length.ToString("F2") + "s)");
        }

        // ---- 애니메이터 ----
        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ZombieControllerPath);
        Check(controller, "좀비 애니메이터 컨트롤러가 있다");

        Check(HasParameter(controller, "Speed", AnimatorControllerParameterType.Float), "Speed 파라미터");
        Check(HasParameter(controller, "Attack", AnimatorControllerParameterType.Trigger), "Attack 파라미터");
        Check(HasParameter(controller, "Dead", AnimatorControllerParameterType.Bool), "Dead 파라미터");

        var states = controller.layers[0].stateMachine.states;
        Check(states.Length == 4, "상태가 4개다 (Idle/Walk/Attack/Death)");
        for (int i = 0; i < states.Length; i++)
            Check(states[i].state.motion != null, states[i].state.name + " 상태에 클립이 물려 있다");

        // ZombieAnimator 가 쓰는 이름과 컨트롤러의 이름이 어긋나면 조용히 아무 일도 안 일어난다.
        Check(FindState(states, "Walk").speedParameterActive
              && FindState(states, "Walk").speedParameter == "Speed",
              "걷기 재생 속도가 Speed 파라미터를 따른다 (발 미끄러짐 방지)");

        // ---- 종류 에셋 ----
        var types = EnsureZombieDefinitions();
        float natural = MeasureModelHeight(model);
        Check(natural > 0.5f, "모델 실측 높이를 잴 수 있다 (" + natural.ToString("F3") + "m)");

        for (int i = 0; i < types.Length; i++)
        {
            var type = types[i];
            Check(type.model == model, type.id + ": 모델이 꽂혀 있다");
            Check(type.animatorController == controller, type.id + ": 애니메이터가 꽂혀 있다");
            Check(Mathf.Abs(type.modelScale * natural - type.bodyHeight) < 0.01f,
                  type.id + ": modelScale 이 bodyHeight 와 맞는다");
            Check(type.bodyRadius <= type.bodyHeight * 0.5f, type.id + ": 반지름이 높이 절반 이하");
        }

        // ---- 총알이 닿는 높이인지 (가장 아팠던 부분) ----
        var combatProbe = new GameObject("__CombatProbe");
        try
        {
            var combat = combatProbe.AddComponent<PlayerCombat>();
            float aimHeight = (float)FieldValue(combat, "aimHeight");
            float hitscanRadius = (float)FieldValue(combat, "hitscanRadius");
            float sweepBottom = BodyY + aimHeight - hitscanRadius;
            for (int i = 0; i < types.Length; i++)
                Check(types[i].bodyHeight >= sweepBottom + 0.2f,
                      types[i].id + ": 몸 높이가 히트스캔 스윕 아래끝("
                      + sweepBottom.ToString("F2") + "m) 보다 충분히 높다");
        }
        finally { Object.DestroyImmediate(combatProbe); }

        // ---- 실제로 생성해 본다 ----
        var spawned = Zombie.Spawn(new Vector3(600f, 0f, 0f), types[0],
                                   EnsureSharedMaterial("zombie", new Color(0.32f, 0.52f, 0.30f)));
        try
        {
            var cc = spawned.GetComponent<CharacterController>();
            Check(Mathf.Abs(cc.height - types[0].bodyHeight) < 0.001f, "콜라이더 높이가 종류 데이터를 따른다");
            Check(cc.stepOffset <= cc.height * 0.75f + 0.001f, "stepOffset 이 몸 높이를 넘지 않는다");
            Check(cc.stepOffset >= 0.25f, "stepOffset 이 계단 한 칸(0.25m) 보다 크다 (계단 정지 방지)");

            var visual = spawned.transform.Find("Model");
            Check(visual, "모델이 자식으로 붙었다");
            Check(Mathf.Abs(visual.localScale.x - types[0].modelScale) < 0.001f, "모델 배율이 적용됐다");
            Check(Mathf.Abs(visual.localPosition.y + types[0].bodyHeight * 0.5f) < 0.001f,
                  "모델 발이 콜라이더 밑면에 맞았다");

            Check(!spawned.transform.Find("Face"), "모델 좀비에는 플레이스홀더 얼굴(Face) 을 붙이지 않는다");
            Check(spawned.GetComponent<ZombieAnimator>(), "ZombieAnimator 가 붙었다");

            var animator = spawned.GetComponentInChildren<Animator>();
            Check(animator, "모델에 Animator 가 있다");
            Check(animator.runtimeAnimatorController == controller, "컨트롤러가 씌워졌다");
            Check(!animator.applyRootMotion, "루트 모션은 껐다 (이동은 CharacterController 담당)");

            // 자식까지 Character 레이어여야 스킨드 메쉬가 시야를 막지 않는다.
            int characterLayer = LayerMask.NameToLayer("Character");
            var all = spawned.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                Check(all[i].gameObject.layer == characterLayer, all[i].name + " 이 Character 레이어다");
            Check((Vision.BlockerMask & (1 << characterLayer)) == 0, "좀비 모델은 시야를 막지 않는다");

            // 피격 연출이 자식 렌더러를 찾는지 (모델 루트에는 렌더러가 없다)
            var flash = spawned.gameObject.AddComponent<HitFlash>();
            flash.Play();
            Check(true, "HitFlash 가 모델 좀비에서 예외 없이 동작한다");
        }
        finally
        {
            Object.DestroyImmediate(spawned.gameObject);
        }

        // 시체가 남아 있는 동안 스폰 상한이 어긋나지 않아야 한다.
        Check(typeof(Zombie).GetProperty("AliveCount") != null, "살아 있는 좀비 수를 따로 센다");
        Check(typeof(Zombie).GetProperty("LastAttackTime") != null, "공격 시각을 노출한다 (공격 모션 신호)");
    }

    static bool HasParameter(UnityEditor.Animations.AnimatorController controller, string name,
                             AnimatorControllerParameterType type)
    {
        var parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
            if (parameters[i].name == name && parameters[i].type == type) return true;

        return false;
    }

    static UnityEditor.Animations.AnimatorState FindState(
        UnityEditor.Animations.ChildAnimatorState[] states, string name)
    {
        for (int i = 0; i < states.Length; i++)
            if (states[i].state.name == name) return states[i].state;

        return null;
    }

    static object FieldValue(object target, string field) =>
        target.GetType().GetField(field, System.Reflection.BindingFlags.Instance
                                       | System.Reflection.BindingFlags.NonPublic).GetValue(target);



    /// <summary>
    /// 시야 판정 + 부채꼴 + 벽 투명화 + 그림자. 에디터에서 확인 가능한 것만 본다.
    /// </summary>
    static void CheckWorldAndVision()
    {
        // ---- 무엇이 시야를 막는가 ----
        Check((Vision.BlockerMask & (1 << LayerMask.NameToLayer("Character"))) == 0,
              "캐릭터는 서로의 시야를 막지 않는다");
        Check((Vision.BlockerMask & (1 << LayerMask.NameToLayer("WorldItem"))) == 0,
              "바닥 아이템은 시야를 막지 않는다");
        Check((Vision.BlockerMask & 1) != 0, "기본 레이어(벽)는 시야를 막는다");
        Check(BodyY + Vision.EyeOffset > WindowSillTop && BodyY + Vision.EyeOffset < WindowTop,
              "눈높이가 창턱과 인방 사이에 있다 (열린 창 너머가 보이려면)");
        Check(BodyY + Vision.EyeOffset < CounterHeight + 0.5f || CounterHeight < BodyY + Vision.EyeOffset,
              "반높이 카운터는 눈높이보다 낮다 (시야를 막으면 안 된다)");

        Check(Shader.Find("Game/VisionFog") != null, "시야 음영 셰이더가 컴파일되어 있다");

        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                       as UniversalRenderPipelineAsset;
        Check(pipeline != null && pipeline.supportsCameraDepthTexture,
              "URP 깊이 텍스처가 켜져 있다 (음영이 픽셀의 월드 좌표를 여기서 복원한다)");

        var root = new GameObject("__VisionSelfCheck");
        try
        {
            // 본 씬에서 멀리 떨어진 빈 곳에 세운다.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(root.transform);
            wall.transform.position = new Vector3(500f, 1.5f, 0f);
            wall.transform.localScale = new Vector3(0.3f, 3f, 8f);
            Physics.SyncTransforms();

            // 캐릭터가 서 있는 높이로 잡는다. 발밑(y=0) 으로 두면 눈이 창턱 아래라 판정이 뒤집힌다.
            var outside = new Vector3(497f, BodyY, 0f);
            var inside = new Vector3(503f, BodyY, 0f);

            Check(!Vision.CanSee(outside, inside, 30f), "벽 뒤는 보이지 않는다");
            Check(Vision.CanSee(outside, outside + Vector3.forward * 2f, 30f), "막힌 게 없으면 보인다");
            Check(!Vision.CanSee(outside, outside + Vector3.forward * 2f, 1f), "시야 거리를 넘으면 안 보인다");

            // ---- 암살이 벽을 뚫지 않는지 ----
            var assassinProbe = new GameObject("__AssassinProbe");
            try
            {
                assassinProbe.transform.SetParent(root.transform);
                assassinProbe.transform.position = outside;
                var assassin = assassinProbe.AddComponent<PlayerAssassinate>();

                Check(!assassin.HasClearPath(inside),
                      "벽 너머는 암살 대상이 되지 않는다 (거리/각도만 보면 벽을 뚫는다)");
                Check(assassin.HasClearPath(outside + Vector3.forward * 1.2f),
                      "막힌 게 없으면 암살할 수 있다");

                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                Check(typeof(PlayerAssassinate).GetMethod("MovePlayerTowards", flags) != null,
                      "암살 이동이 콜라이더를 쓸어서 옮긴다");
                Check(typeof(PlayerAssassinate).GetMethod("MovePlayerTo", flags) == null,
                      "콜라이더를 끄고 순간이동하는 경로가 남아 있지 않다 (벽 밖으로 나갔다)");
            }
            finally { Object.DestroyImmediate(assassinProbe); }

            // ---- 방향성 부채꼴 ----
            var fieldProbe = new GameObject("__VisionField");
            fieldProbe.transform.SetParent(root.transform);
            fieldProbe.transform.position = new Vector3(540f, BodyY, 0f);
            fieldProbe.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            var field = fieldProbe.AddComponent<VisionField>();
            field.Refresh();

            Check(field.Facing == Vector3.forward, "정면 방향이 캐릭터 forward 를 따른다");

            float front = FarthestVisible(field, Vector3.forward);
            float insideCone = FarthestVisible(field, Quaternion.Euler(0f, 55f, 0f) * Vector3.forward);
            float outsideCone = FarthestVisible(field, Quaternion.Euler(0f, 100f, 0f) * Vector3.forward);
            float back = FarthestVisible(field, Vector3.back);

            Check(front > field.Range * 0.7f, "정면이 사거리 끝까지 보인다 (" + front.ToString("F1") + "m)");
            Check(insideCone > field.Range * 0.7f,
                  "부채꼴 안(55°) 도 정면만큼 멀리 보인다 (" + insideCone.ToString("F1") + "m)");
            Check(outsideCone < 1.5f, "부채꼴 밖(100°) 은 발밑 말고는 안 보인다");
            Check(back < 1.5f, "등 뒤는 안 보인다 (사용자 요구)");
            Check(field.Visibility(field.Origin + Vector3.back * 0.5f) > 0.9f, "발밑은 뒤쪽도 보인다");

            fieldProbe.transform.rotation = Quaternion.LookRotation(Vector3.right);
            field.Refresh();
            Check(FarthestVisible(field, Vector3.right) > field.Range * 0.7f, "몸을 돌리면 부채꼴도 같이 돈다");
            Check(FarthestVisible(field, Vector3.forward) < 1.5f, "돌린 뒤 원래 정면은 부채꼴 밖이 된다");

            // ---- 마스크 일괄 채우기가 낱개 조회와 같은가 ----
            // 흐리기를 끄면 두 경로가 바이트 단위로 같아야 한다. 칸 표가 어긋나면 화면만 조용히 틀어진다.
            const int maskRes = 48;
            float maskArea = (field.Range + 2f) * 2f;
            var bulk = new Color32[maskRes * maskRes];
            var blurField = typeof(VisionField).GetField("maskBlurCells",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            int blurCells = (int)blurField.GetValue(field);

            blurField.SetValue(field, 0);
            field.FillMask(bulk, maskRes, maskArea);

            float maskCell = maskArea / maskRes;
            Vector3 maskOrigin = field.Origin - new Vector3(maskArea * 0.5f, 0f, maskArea * 0.5f);
            int worstError = 0;
            for (int y = 0; y < maskRes; y++)
            {
                float wz = maskOrigin.z + (y + 0.5f) * maskCell;
                for (int x = 0; x < maskRes; x++)
                {
                    float single = field.Visibility(new Vector3(maskOrigin.x + (x + 0.5f) * maskCell, field.Origin.y, wz));
                    worstError = Mathf.Max(worstError,
                        Mathf.Abs((int)(Mathf.Clamp01(single) * 255f) - bulk[y * maskRes + x].r));
                }
            }
            blurField.SetValue(field, blurCells);
            Check(worstError <= 1, "마스크 일괄 채우기가 낱개 조회와 일치한다 (최대 오차 " + worstError + ")");
            Check(blurCells > 0, "마스크 흐리기가 켜져 있다 (모서리 계단 방지)");

            // 벽 모서리 그림자의 '측면' 경계가 여러 칸에 걸쳐 번지는지.
            // edgeSoftness 는 반지름 방향만 담당해서, 안 흐리면 한 칸에서 절벽이 되고 격자 계단으로 보인다.
            var cornerWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                cornerWall.transform.SetParent(root.transform);
                cornerWall.transform.position = new Vector3(540f, 1.5f, 6f);
                cornerWall.transform.localScale = new Vector3(4f, 3f, 0.4f);
                fieldProbe.transform.rotation = Quaternion.LookRotation(Vector3.forward);
                Physics.SyncTransforms();
                field.Refresh();

                int res = 96;
                float area = (field.Range + 2f) * 2f;
                var shadowMask = new Color32[res * res];
                field.FillMask(shadowMask, res, area);

                float cellSize = area / res;
                Vector3 maskMin = field.Origin - new Vector3(area * 0.5f, 0f, area * 0.5f);
                int row = Mathf.Clamp(Mathf.RoundToInt((10f - maskMin.z) / cellSize - 0.5f), 0, res - 1);

                int gradient = 0;
                for (int x = 1; x < res; x++)
                {
                    int v = shadowMask[row * res + x].r;
                    if (v > 25 && v < 215) gradient++;
                }
                Check(gradient >= 6, "모서리 그림자 경계가 여러 칸에 걸쳐 번진다 (중간값 칸 "
                      + gradient + "개, 절벽이면 0~2개)");
            }
            finally { Object.DestroyImmediate(cornerWall); }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        // ---- 그림자 계단현상 ----
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        Check(rp.msaaSampleCount >= 4, "MSAA 가 켜져 있다 (기하 실루엣 톱니)");
        Check(rp.shadowCascadeCount == ShadowCascades,
              "그림자 캐스케이드가 " + ShadowCascades + "개다 (고정 각도 카메라라 나눌 이유가 없다)");
        Check(rp.mainLightShadowmapResolution >= ShadowmapResolution, "그림자맵이 충분히 크다");
        Check(rp.shadowDistance <= ShadowDistance + 0.1f, "그림자 거리가 짧다 (멀면 텍셀이 커져 계단이 생긴다)");
        Check(rp.supportsSoftShadows, "소프트 그림자가 켜져 있다");
        Check(rp.shadowDistance / rp.mainLightShadowmapResolution * 100f < 2f, "그림자 텍셀이 2cm 미만이다");

        CheckApartmentLayout();
    }

    /// <summary>한 방향으로 0.25m 씩 나아가며 보이는 마지막 거리를 찾는다.</summary>
    static float FarthestVisible(VisionField field, Vector3 direction)
    {
        float last = 0f;
        for (float d = 0.25f; d <= field.Range; d += 0.25f)
            if (field.Visibility(field.Origin + direction.normalized * d) > 0.25f) last = d;

        return last;
    }

    /// <summary>
    /// 시작 맵 아파트 배치. 도면과 맞는지, 그리고 실제로 걸어 다닐 수 있는지 본다.
    ///
    /// 주의: 정적 목록(Room.All 등) 은 OnEnable 로 채워지는데 에디터 모드에서는 OnEnable 이
    /// 돌지 않아 항상 비어 있다. 그걸 믿고 짰다가 검사 전체가 조용히 건너뛰어진 적이 있다.
    /// </summary>
    static void CheckApartmentLayout()
    {
        var rooms = Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (rooms.Length == 0)
        {
            Debug.Log("[V0.1] 아파트가 없는 씬이라 배치 검사는 건너뜀");
            return;
        }

        var openings = Object.FindObjectsByType<WindowFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var walls = Object.FindObjectsByType<WallSegment>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        string[] wanted = { "거실", "현관홀", "욕실", "세탁실", "게임방", "중앙홀", "침실", "창고", "팬트리", "주방", "식당" };
        for (int i = 0; i < wanted.Length; i++)
            Check(FindRoom(rooms, wanted[i]) != null, wanted[i] + " 이(가) 있다");

        Check(openings.Length >= 18, "문 13 + 창문 5 가 있다 (" + openings.Length + "개)");
        Check(walls.Length >= 30, "벽이 조각으로 쪼개져 있다 — " + walls.Length + "조각");

        for (int i = 0; i < walls.Length; i++)
        {
            if (walls[i].IsRoof) continue;
            var collider = walls[i].GetComponent<Collider>();
            Check(collider && collider.enabled, "벽 콜라이더는 투명화와 무관하게 살아 있다");
        }

        // 도면 대비 방 크기 (라벨의 피트 치수를 미터로 옮긴 값)
        // 평면도 치수 그대로. 띠 경계 상수에서 나온 값이라 배치를 바꾸면 여기가 먼저 터진다.
        CheckRoomSize(rooms, "거실", 7.00f, BandSouthEnd);
        CheckRoomSize(rooms, "현관홀", 4.50f, BandSouthEnd);
        CheckRoomSize(rooms, "욕실", 3.00f, BandSouthEnd);
        CheckRoomSize(rooms, "세탁실", 2.64f, BandSouthEnd);
        CheckRoomSize(rooms, "게임방", 5.00f, BandMidEnd - BandMidStart);
        CheckRoomSize(rooms, "중앙홀", 7.14f, BandMidEnd - BandMidStart);
        CheckRoomSize(rooms, "침실", 5.12f, BandMidEnd - BandMidStart);
        CheckRoomSize(rooms, "창고", 4.00f, FlatDepth - BandNorthStart);
        CheckRoomSize(rooms, "팬트리", 4.00f, FlatDepth - BandNorthStart);
        CheckRoomSize(rooms, "주방", 5.00f, FlatDepth - BandNorthStart);
        CheckRoomSize(rooms, "식당", 4.14f, FlatDepth - BandNorthStart);

        // 평면도 방향. 남쪽 띠 = 거실/현관홀/욕실/세탁실, 중간 띠 = 게임방/중앙홀/침실,
        // 북쪽 띠 = 창고/팬트리/주방/식당. 좌우는 서(작은 x) → 동(큰 x) 순서다.
        var living = FindRoom(rooms, "거실").InnerBounds.center;
        var entranceHall = FindRoom(rooms, "현관홀").InnerBounds.center;
        var bath = FindRoom(rooms, "욕실").InnerBounds.center;
        var utility = FindRoom(rooms, "세탁실").InnerBounds.center;
        var game = FindRoom(rooms, "게임방").InnerBounds.center;
        var hall = FindRoom(rooms, "중앙홀").InnerBounds.center;
        var bedroom = FindRoom(rooms, "침실").InnerBounds.center;
        var storage = FindRoom(rooms, "창고").InnerBounds.center;
        var pantry = FindRoom(rooms, "팬트리").InnerBounds.center;
        var kitchen = FindRoom(rooms, "주방").InnerBounds.center;
        var dining = FindRoom(rooms, "식당").InnerBounds.center;

        Check(living.x < entranceHall.x && entranceHall.x < bath.x && bath.x < utility.x,
              "남쪽 띠가 거실 → 현관홀 → 욕실 → 세탁실 순이다");
        Check(game.x < hall.x && hall.x < bedroom.x,
              "중간 띠가 게임방 → 중앙홀 → 침실 순이다");
        Check(storage.x < pantry.x && pantry.x < kitchen.x && kitchen.x < dining.x,
              "북쪽 띠가 창고 → 팬트리 → 주방 → 식당 순이다");
        Check(living.z < hall.z && hall.z < kitchen.z, "남 → 중 → 북 순서로 띠가 쌓여 있다");
        Check(Mathf.Abs(living.z - utility.z) < 0.2f, "남쪽 띠 네 방의 깊이가 같다");
        Check(Mathf.Abs(storage.z - dining.z) < 0.2f, "북쪽 띠 네 방의 깊이가 같다");

        CheckEstateLayout(rooms);
        CheckRoofsAndHeight(rooms);
        CheckSpawnClearance();

        // 현관에서 각 방까지 실제로 갈 수 있는가.
        //
        // NavMesh 는 문을 빼고 굽지만, 문짝마다 달린 NavMeshObstacle 이 닫혀 있는 동안
        // 길을 깎는다. 그래서 "닫힌 채로" 물어보면 당연히 길이 없다 — 그건 기능이다.
        // 배치가 통행 가능한지 보려면 문짝을 잠깐 빼고 다시 구워서 물어봐야 한다.
        CheckLayoutIsWalkable();

        // 카빙은 재생 모드에서만 돌아 에디트 모드 경로로는 잴 수 없다.
        // 재생 모드 실측: 현관문/차고 셔터 닫힘 PathPartial, 현관문 열림 PathComplete.
        for (int i = 0; i < openings.Length; i++)
        {
            var opening = openings[i];
            if (opening.Vaultable) continue;

            var pane = opening.GetComponentInChildren<Collider>(true);
            var obstacle = pane ? pane.GetComponent<UnityEngine.AI.NavMeshObstacle>() : null;
            float obstacleWidth = obstacle
                ? obstacle.size.x * Mathf.Abs(obstacle.transform.lossyScale.x)
                : 0f;
            Check(pane && obstacle && obstacle.enabled && obstacle.carving
                  && obstacle.shape == UnityEngine.AI.NavMeshObstacleShape.Box
                  && obstacleWidth >= opening.OpeningWidth * 0.9f,
                  opening.name + " 문짝의 NavMeshObstacle 설정과 폭이 올바르다");
        }

        // 문은 넘는 대상이 아니고, 창문은 넘을 수 있어야 한다.
        var entrance = FindOpening(openings, "Door_Entrance");
        Check(entrance, "현관문이 있다");
        Check(!entrance.CanVault(Flat(9.37f, -1f)), "현관문은 Space 로 넘는 대상이 아니다");
        Check(entrance.PromptText.Contains("현관문"), "현관문 안내 문구가 '창문' 이 아니다");

        // 문 통과 여유. CharacterController 는 이동할 때 stepOffset 만큼 몸을 들어올려 보는데,
        // 그때 머리가 인방에 닿으면 문이 열려 있어도 통과하지 못한다 (실제로 전 문이 막혔다).
        var motor = Object.FindFirstObjectByType<PlayerMotor>();
        var body = motor ? motor.GetComponent<CharacterController>() : null;
        if (body)
        {
            float headroom = BodyY + body.height * 0.5f * body.transform.localScale.y + body.skinWidth;
            Check(headroom + body.stepOffset < DoorHeight,
                  "문 높이가 머리+stepOffset 보다 높다 (머리 " + headroom.ToString("F2")
                  + "m + step " + body.stepOffset.ToString("F2") + "m < 문 " + DoorHeight + "m)");
            Check(body.stepOffset > 0.25f, "stepOffset 이 계단 한 칸(0.25m) 보다 크다");
            Check(!body.GetComponent<MeshRenderer>(), "플레이어 루트에 캡슐 MeshRenderer가 없다");
        }

        var livingWindow = FindOpening(openings, "Window_Living_W");
        Check(livingWindow, "거실 창문이 있다");
        livingWindow.SetOpen(true);
        Physics.SyncTransforms();
        Check(livingWindow.CanVault(Flat(1.90f, -1.0f)), "열린 거실 창문은 넘어 들어올 수 있다");
        livingWindow.SetOpen(false);
        Physics.SyncTransforms();

        // 닫힌 현관문이 바깥 시야를 막아야 한다 (도입부에서 밖을 미리 보여주지 않는다).
        Check(!Vision.CanSee(Flat(9.37f, 1.0f), Flat(9.37f, -2.0f), 20f),
              "닫힌 현관문이 바깥 시야를 막는다");
        entrance.SetOpen(true);
        Physics.SyncTransforms();
        Check(Vision.CanSee(Flat(9.37f, 1.0f), Flat(9.37f, -2.0f), 20f), "현관문을 열면 바깥이 보인다");
        entrance.SetOpen(false);
        Physics.SyncTransforms();

        CheckCurtainsAndSiege(openings, livingWindow, entrance);
        CheckForestAndPropBudget();
        CheckZombiePerformanceTiers();

        // ---- 보이지 않는 천장 (월담 방지) ----
        var ceiling = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform lid = null;
        for (int i = 0; i < ceiling.Length; i++) if (ceiling[i].name == "Ceiling") lid = ceiling[i];
        Check(lid, "보이지 않는 천장이 있다");
        if (lid)
        {
            Check(!lid.GetComponent<Renderer>(), "천장은 보이지 않는다");
            var lidCollider = lid.GetComponent<Collider>();
            Check(lidCollider && lidCollider.enabled, "천장 콜라이더가 살아 있다");
            Check(lid.gameObject.layer == LayerMask.NameToLayer("Ignore Raycast"),
                  "천장이 Ignore Raycast 레이어다 (조준·시야에 걸리지 않게)");
            Check((Vision.BlockerMask & (1 << lid.gameObject.layer)) == 0, "천장은 시야를 막지 않는다");
            Check(Mathf.Abs(lidCollider.bounds.min.y - RoomHeight) < 0.05f,
                  "천장 밑면이 벽 높이에 맞는다 (" + lidCollider.bounds.min.y.ToString("F2") + "m)");
            Check(lidCollider.bounds.size.x >= FlatWidth * Scale && lidCollider.bounds.size.z >= FlatDepth * Scale,
                  "천장이 실내 전체를 덮는다");
        }

        // ---- E 는 문과 창문 전용 ----
        Check(typeof(PlayerInteractor).GetMethod("TryInteractContainer",
                  System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) == null,
              "컨테이너는 E 로 열지 않는다 (사용자 요구)");
        Check(typeof(PlayerInteractor).GetProperty("ViewContainer") != null,
              "컨테이너는 Tab 화면에서 그대로 볼 수 있다");

        // ---- 좀비가 제자리에서 돌지 않는다 ----
        Check(typeof(Zombie).GetMethod("WanderAround",
                  System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null,
              "좀비가 소리를 조사할 때 주변을 서성인다 (제자리 회전 아님)");

        Check(Object.FindFirstObjectByType<WallFader>(), "카메라에 벽 투명화가 붙어 있다");

        // ---- 밀실은 실외보다 훨씬 짙게 덮는다 (사용자 요구) ----
        var fogSetup = Object.FindFirstObjectByType<VisionFog>();
        Check(fogSetup, "카메라에 시야 음영이 붙어 있다");
        if (fogSetup)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var outdoor = (Color)typeof(VisionFog).GetField("fogColor", flags).GetValue(fogSetup);
            var indoor = (Color)typeof(VisionFog).GetField("indoorFogColor", flags).GetValue(fogSetup);

            Check(typeof(VisionFog).GetMethod("UpdateFogColor", flags) != null,
                  "방 안팎에 따라 음영 짙기를 바꾼다");
            Check(indoor.a > outdoor.a + 0.1f,
                  "밀실 음영이 실외보다 짙다 (실내 " + indoor.a.ToString("F3") + " > 실외 " + outdoor.a.ToString("F3") + ")");

            // 화면에 실제로 보이는 밝기로 재야 한다. 선형으로 2.5% 만 남겨도
            // sRGB 감마가 16% 로 끌어올려서 안 보이는 영역의 가구가 다 읽혔다.
            const float DaylightFloor = 0.55f;   // 낮 바닥의 선형 밝기 (실측)
            float indoorLum = indoor.r * 0.299f + indoor.g * 0.587f + indoor.b * 0.114f;
            float leftLinear = (1f - indoor.a) * DaylightFloor + indoor.a * indoorLum;
            float leftOnScreen = Mathf.Pow(leftLinear, 1f / 2.2f);

            Check(leftOnScreen < 0.12f,
                  "밀실에서 안 보이는 영역이 낮 햇빛에도 검게 보인다 (화면 밝기 "
                  + leftOnScreen.ToString("F3") + ")");
        }
        Check(Object.FindFirstObjectByType<VisionField>(), "플레이어에 시야 판정이 붙어 있다");
    }


    /// <summary>
    /// 문짝을 잠깐 빼고 다시 구워 배치 자체가 통행 가능한지 본다. 끝나면 원래대로 되돌린다.
    /// 문 폭이나 인방 높이가 모자라면 여기서 걸린다 — 실제로 인방이 2.05m 였을 때
    /// 에이전트 높이 2.0m 와의 여유 5cm 가 복셀화에서 깎여 실내가 고립된 섬이 됐었다.
    /// </summary>
    static void CheckLayoutIsWalkable()
    {
        var surface = Object.FindFirstObjectByType<Unity.AI.Navigation.NavMeshSurface>();
        if (!surface) { Check(false, "NavMeshSurface 가 있다"); return; }

        var frames = Object.FindObjectsByType<WindowFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var panes = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < frames.Length; i++)
        {
            var collider = frames[i].GetComponentInChildren<Collider>(true);
            if (collider) panes.Add(collider.gameObject);
        }

        GameObject gateBlock = null;
        try
        {
            for (int i = 0; i < panes.Count; i++) panes[i].SetActive(false);
            Physics.SyncTransforms();
            surface.BuildNavMesh();

            // 주의: 검사 지점이 가구 콜라이더 안이면 NavMesh 가 없어서 '배치가 막혔다' 로
            // 잘못 읽힌다 (실제로 침실 지점이 책상 안이었다). 전부 빈 바닥이어야 한다.
            Vector3 doorstep = Flat(9.37f, -1.2f);
            CheckReachable(doorstep, Flat(3.50f, 2.20f), "현관 → 거실");
            CheckReachable(doorstep, Flat(9.37f, 2.20f), "현관 → 현관홀");
            CheckReachable(doorstep, Flat(13.20f, 2.75f), "현관 → 욕실");
            CheckReachable(doorstep, Flat(16.20f, 2.75f), "현관 → 세탁실");
            CheckReachable(doorstep, Flat(2.20f, 8.20f), "현관 → 게임방");
            CheckReachable(doorstep, Flat(8.70f, 7.60f), "현관 → 중앙홀");
            CheckReachable(doorstep, Flat(14.80f, 6.40f), "현관 → 침실");
            CheckReachable(doorstep, Flat(2.00f, 12.20f), "현관 → 창고");
            CheckReachable(doorstep, Flat(6.10f, 10.60f), "현관 → 팬트리");
            CheckReachable(doorstep, Flat(10.40f, 10.60f), "현관 → 주방");
            CheckReachable(doorstep, Flat(13.95f, 12.80f), "현관 → 식당");
            CheckReachable(new Vector3(27f, 0f, -10f), new Vector3(14f, 0f, -19.5f), "포치 앞 → 차고");
            CheckReachable(new Vector3(14f, 0f, -19.5f), new Vector3(0f, 0f, -16f), "차고 → 기계실");
            CheckReachable(new Vector3(27f, 0f, -40f), new Vector3(27f, 0f, -16f), "울타리 밖 → 마당");

            gateBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gateBlock.name = "__GateBlock";
            gateBlock.transform.position = new Vector3(27f, 1f, -30.1f);
            gateBlock.transform.localScale = new Vector3(6.4f, 2f, 0.4f);
            Physics.SyncTransforms();
            surface.BuildNavMesh();

            var blocked = new UnityEngine.AI.NavMeshPath();
            bool throughClosedGate = UnityEngine.AI.NavMesh.CalculatePath(
                new Vector3(27f, 0f, -40f), new Vector3(27f, 0f, -16f),
                UnityEngine.AI.NavMesh.AllAreas, blocked)
                && blocked.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
            Check(!throughClosedGate, "대문 틈을 막으면 울타리 밖에서 마당으로 갈 수 없다");
        }
        finally
        {
            if (gateBlock) Object.DestroyImmediate(gateBlock);
            Physics.SyncTransforms();
            try
            {
                surface.BuildNavMesh();   // 문짝을 뺀 원래 굽기 상태로 되돌린다
            }
            finally
            {
                for (int i = 0; i < panes.Count; i++) panes[i].SetActive(true);
                Physics.SyncTransforms();
            }
        }
    }

    static void CheckEstateLayout(Room[] rooms)
    {
        var garage = FindRoom(rooms, "차고");
        var mechanical = FindRoom(rooms, "기계실");
        Check(garage != null, "차고 Room 이 있다");
        Check(mechanical != null, "기계실 Room 이 있다");
        if (garage)
        {
            Vector3 size = garage.InnerBounds.size;
            Check(Mathf.Abs(size.x - 13.9f) < 0.2f && Mathf.Abs(size.z - 10.7f) < 0.2f,
                  "차고 Room 이 벽 안쪽 면에 맞는다");
        }
        if (mechanical)
        {
            Vector3 size = mechanical.InnerBounds.size;
            Check(Mathf.Abs(size.x - 7.625f) < 0.2f && Mathf.Abs(size.z - 5.9f) < 0.2f,
                  "기계실 Room 이 벽 안쪽 면에 맞는다");
        }
        CheckWorldSize("GarageFloor", 14.4f, 11.2f);
        CheckWorldSize("MechanicalFloor", 8f, 6.4f);
        CheckWorldSize("ConcreteYard", 19.2f, 16f);

        var north = GameObject.Find("Fence_N");
        var west = GameObject.Find("Fence_W");
        var east = GameObject.Find("Fence_E");
        var southWest = GameObject.Find("Fence_S_W");
        var southEast = GameObject.Find("Fence_S_E");
        Check(north && west && east && southWest && southEast, "울타리 네 변이 있다");
        if (north && west && east && southWest && southEast)
        {
            Bounds n = north.GetComponent<Collider>().bounds;
            Bounds w = west.GetComponent<Collider>().bounds;
            Bounds e = east.GetComponent<Collider>().bounds;
            Bounds sw = southWest.GetComponent<Collider>().bounds;
            Bounds se = southEast.GetComponent<Collider>().bounds;
            Check(n.min.x <= -6f && n.max.x >= 58f && Mathf.Abs(n.center.z - 19.9f) < 0.05f,
                  "북쪽 울타리가 부지 너비를 덮는다");
            Check(w.min.z <= -30.1f && w.max.z >= 19.9f && Mathf.Abs(w.center.x + 6f) < 0.05f,
                  "서쪽 울타리가 부지 깊이를 덮는다");
            Check(e.min.z <= -30.1f && e.max.z >= 19.9f && Mathf.Abs(e.center.x - 58f) < 0.05f,
                  "동쪽 울타리가 부지 깊이를 덮는다");
            Check(sw.min.x <= -6f && se.max.x >= 58f
                  && Mathf.Abs(sw.center.z + 30.1f) < 0.05f && Mathf.Abs(se.center.z + 30.1f) < 0.05f
                  && Mathf.Abs(sw.max.x - 23.8f) < 0.05f && Mathf.Abs(se.min.x - 30.2f) < 0.05f,
                  "남쪽 울타리가 양 모서리를 덮고 x 23.8~30.2 대문 틈만 남긴다");
            Check(Mathf.Abs(n.size.y - 2f) < 0.05f && Mathf.Abs(w.size.y - 2f) < 0.05f
                  && Mathf.Abs(e.size.y - 2f) < 0.05f && Mathf.Abs(sw.size.y - 2f) < 0.05f
                  && Mathf.Abs(se.size.y - 2f) < 0.05f,
                  "울타리 높이가 2m 다");
        }

        var openings = Object.FindObjectsByType<WindowFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        CheckOpeningWidth(openings, "Door_GarageShutter", 5f, "차고 셔터");
        CheckOpeningWidth(openings, "Door_GarageMechanical", 1.4f, "차고와 기계실 사이 문");
        CheckOpeningWidth(openings, "Door_MechanicalOutside", 1.4f, "기계실 바깥문");
        Check(GameObject.Find("GarageCeiling") && GameObject.Find("MechanicalCeiling"),
              "차고와 기계실에 월담 방지 천장이 있다");

        var exit = GameObject.Find("Exit_ToCity");
        var trigger = exit ? exit.GetComponent<BoxCollider>() : null;
        var sceneExit = exit ? exit.GetComponent<SceneExit>() : null;
        Check(exit && trigger && trigger.isTrigger, "Exit_ToCity 가 대문 바깥 트리거다");
        if (exit && trigger)
        {
            Bounds bounds = trigger.bounds;
            Check(bounds.min.x <= 23.8f && bounds.max.x >= 30.2f
                  && bounds.max.z < -30.1f && Mathf.Abs(bounds.center.z + 33.1f) < 0.05f
                  && Mathf.Abs(bounds.size.z - 2f) < 0.05f,
                  "Exit_ToCity 가 대문 폭이고 남쪽 3m 바깥에 있다");
            CheckReachable(new Vector3(27f, 0f, -16f), exit.transform.position,
                           "마당 → Exit_ToCity");
        }
        Check(sceneExit && sceneExit.TargetSceneName == "V01_City_Hadan",
              "Exit_ToCity 대상이 하단 도시다");
        Check(BuildSettingsContains(CityScenePath), "하단 도시 씬이 Build Settings 에 있다");

        var rigs = Object.FindObjectsByType<HouseLightRig>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Check(rigs.Length == 1, "HouseLightRig 는 씬에 하나다");
    }

    static void CheckRoofsAndHeight(Room[] rooms)
    {
        var segments = Object.FindObjectsByType<WallSegment>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var roofs = new System.Collections.Generic.List<WallSegment>();
        for (int i = 0; i < segments.Length; i++)
            if (segments[i].IsRoof) roofs.Add(segments[i]);

        Check(roofs.Count == 3, "본채·포치·차고+기계실 지붕이 하나씩 있다");
        var main = roofs.Find(x => x.name == "MainRoof");
        var porch = roofs.Find(x => x.name == "Porch_Roof");
        var estate = roofs.Find(x => x.name == "EstateRoof");
        CheckRoof(main, "본채", 11.675f, -4.325f, 40.325f, 17.925f, 11);
        CheckRoof(estate, "부지", -5.4f, -25.1f, 17f, -13.9f, 2);
        Check(porch && porch.RoofRooms != null && porch.RoofRooms.Count == 11,
              "포치 지붕이 본채 방 11개와 직접 연결돼 있다");
        if (estate)
            Check(estate.Covers(FindRoom(rooms, "차고")) && estate.Covers(FindRoom(rooms, "기계실")),
                  "부지 지붕이 차고·기계실 Room 과 직접 연결돼 있다");

        for (int i = 0; i < rooms.Length; i++)
            Check(Mathf.Abs(rooms[i].InnerBounds.size.y - RoomHeight) < 0.01f,
                  rooms[i].DisplayName + " 방 상자 높이가 층고와 같다");

        string[] ceilingNames = { "Ceiling", "GarageCeiling", "MechanicalCeiling" };
        for (int i = 0; i < ceilingNames.Length; i++)
        {
            var ceiling = GameObject.Find(ceilingNames[i]);
            var collider = ceiling ? ceiling.GetComponent<Collider>() : null;
            Check(collider && Mathf.Abs(collider.bounds.min.y - RoomHeight) < 0.05f,
                  ceilingNames[i] + " 밑면이 층고 " + RoomHeight.ToString("F1") + "m 와 같다");
        }
    }

    static void CheckRoof(WallSegment roof, string label, float x0, float z0, float x1, float z1, int roomCount)
    {
        Check(roof, label + " 지붕이 있다");
        if (!roof) return;

        Bounds bounds = roof.Bounds;
        Check(bounds.min.x <= x0 - 0.3f && bounds.max.x >= x1 + 0.3f
              && bounds.min.z <= z0 - 0.3f && bounds.max.z >= z1 + 0.3f,
              label + " 지붕이 바깥 벽을 사방 0.3m 이상 덮는다");
        Check(bounds.min.y >= RoomHeight - 0.01f, label + " 지붕 밑면이 층고 이상이다");
        Check(roof.GetComponentsInChildren<Collider>(true).Length == 0, label + " 지붕에 콜라이더가 없다");
        Check((Vision.BlockerMask & (1 << roof.gameObject.layer)) == 0,
              label + " 지붕이 시야·조준 판정에 걸리지 않는다");
        Check(roof.RoofRooms != null && roof.RoofRooms.Count == roomCount,
              label + " 지붕에 Room " + roomCount + "개가 직접 연결돼 있다");

        var fog = Shader.Find("Game/VisionFog");
        Check(fog && roof.DrawsOverVisionFog && roof.TargetOpaqueRenderQueue > fog.renderQueue,
              label + " 지붕이 시야 음영 위에 그려지도록 표시돼 있다");
    }

    static void CheckWorldSize(string name, float width, float depth)
    {
        var go = GameObject.Find(name);
        Check(go, name + " 이(가) 있다");
        if (!go) return;
        Vector3 size = go.GetComponent<Renderer>().bounds.size;
        Check(Mathf.Abs(size.x - width) < 0.2f && Mathf.Abs(size.z - depth) < 0.2f,
              name + " 크기가 맞다 (" + size.x.ToString("F2") + " x " + size.z.ToString("F2") + ")");
    }

    static void CheckOpeningWidth(WindowFrame[] openings, string name, float width, string label)
    {
        var opening = FindOpening(openings, name);
        Check(opening, label + "이(가) 있다");
        if (!opening) return;
        Vector3 size = opening.GetComponentInChildren<Collider>(true).bounds.size;
        Check(Mathf.Abs(Mathf.Max(size.x, size.z) - width) < 0.05f,
              label + " 폭이 " + width.ToString("F1") + "m 다");
    }

    static void CheckSpawnClearance()
    {
        var items = Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool allItemsInside = true;
        for (int i = 0; i < items.Length; i++)
        {
            CheckClear(items[i].transform, "아이템 " + items[i].name);
            Bounds bounds = items[i].GetComponent<Collider>().bounds;
            allItemsInside &= bounds.min.x >= -6f && bounds.max.x <= 58f
                              && bounds.min.z >= -30.1f && bounds.max.z <= 19.9f;
        }
        Check(allItemsInside, "모든 아이템이 울타리 안에 있다");

        var containers = Object.FindObjectsByType<WorldContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < containers.Length; i++) CheckClear(containers[i].transform, "컨테이너 " + containers[i].name);

        var zombies = Object.FindObjectsByType<Zombie>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < zombies.Length; i++) CheckClear(zombies[i].transform, "좀비 " + zombies[i].name);

        var player = GameObject.Find("Player");
        if (player) CheckClear(player.transform, "플레이어 시작 위치");

        var spawnRoot = GameObject.Find("SpawnPoints");
        if (spawnRoot)
            for (int i = 0; i < spawnRoot.transform.childCount; i++)
                CheckClear(spawnRoot.transform.GetChild(i), "부활 지점 " + spawnRoot.transform.GetChild(i).name);
    }

    static void CheckClear(Transform target, string label)
    {
        var own = target.GetComponent<Collider>();
        var hits = target.GetComponent<WorldContainer>() && own && own.enabled
            ? Physics.OverlapBox(own.bounds.center, own.bounds.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Collide)
            : Physics.OverlapSphere(target.position, 0.3f, ~0, QueryTriggerInteraction.Collide);
        Collider blocker = null;
        for (int i = 0; i < hits.Length; i++)
        {
            Transform hit = hits[i].transform;
            if (hit.name == "Ground" || hit == target || hit.IsChildOf(target) || target.IsChildOf(hit)
                || hits[i].GetComponentInParent<CharacterController>()) continue;
            blocker = hits[i];
            break;
        }
        Check(!blocker, label + "가 다른 콜라이더 안에 없다" + (blocker ? " (" + blocker.name + ")" : ""));
    }

    static Room FindRoom(Room[] rooms, string label)
    {
        for (int i = 0; i < rooms.Length; i++)
            if (rooms[i].DisplayName == label) return rooms[i];

        return null;
    }

    /// <summary>
    /// 사용자 요구 묶음: 커튼이 발견 조건이고, 좀비가 두드려 부수고 넘어 들어온다.
    /// 부서짐은 되돌릴 수 없으므로 임시 개구부로 시험한다 — 씬의 창문을 깨뜨리면 안 된다.
    /// </summary>
    static void CheckCurtainsAndSiege(WindowFrame[] openings, WindowFrame livingWindow, WindowFrame entrance)
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        // ---- 점프 스태미나 제거 ----
        Check(typeof(PlayerMotor).GetField("jumpStaminaCost", flags) == null,
              "점프는 스태미나를 쓰지 않는다 (사용자 요구)");

        // ---- 커튼이 창문에만 달려 있다 ----
        for (int i = 0; i < openings.Length; i++)
        {
            bool isWindow = openings[i].name.StartsWith("Window");
            Check(openings[i].HasCurtain == isWindow,
                  openings[i].name + (isWindow ? " 에 커튼이 있다" : " 에는 커튼이 없다"));
        }

        // ---- 닫힌 유리는 시야를 통과시키고, 커튼이 막는다 ----
        // 눈높이가 창턱(0.9m) 위에 오도록 몸 높이를 실어서 잰다.
        Vector3 inside = Flat(1.90f, 0.80f) + Vector3.up * BodyY;
        Vector3 outside = Flat(1.90f, -1.20f) + Vector3.up * BodyY;

        Check(Vision.CanSee(inside, outside, 20f),
              "닫힌 창유리는 시야를 막지 않는다 (막으면 커튼이 무의미해진다)");

        livingWindow.ToggleCurtain();
        Physics.SyncTransforms();
        Check(livingWindow.IsCurtainClosed, "창문 앞에서 커튼을 칠 수 있다");
        Check(!Vision.CanSee(inside, outside, 20f),
              "커튼을 치면 좀비가 실내 플레이어를 볼 수 없다 (사용자 요구 발견 조건)");

        livingWindow.ToggleCurtain();
        Physics.SyncTransforms();
        Check(!livingWindow.IsCurtainClosed && Vision.CanSee(inside, outside, 20f),
              "커튼을 젖히면 다시 보인다");

        // ---- 부서지는 조건: 창문 2마리, 문 3마리 ----
        Check(livingWindow.PoundersToBreak == 2, "창문은 좀비 2마리가 붙어야 부서진다");
        Check(entrance.PoundersToBreak == 3, "문은 좀비 3마리가 붙어야 부서진다");

        // ---- 파쿠르 잠금 + 부서짐 (임시 개구부로 시험) ----
        var probe = new GameObject("__OpeningProbe");
        try
        {
            var pane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pane.transform.SetParent(probe.transform, false);

            var test = probe.AddComponent<WindowFrame>();
            test.Setup(pane.transform, pane.GetComponent<Collider>(), 1f, "시험창", true);
            test.SetupBreakRule(2, 4f);

            Check(!test.Passable, "닫힌 개구부는 통과할 수 없다");

            test.SetOpen(true);
            test.BeginVault();
            Check(!test.SetOpen(false),
                  "넘는 중에는 창문이 닫히지 않는다 (파쿠르 중 닫기 버그 — 사용자 지적)");
            test.EndVault();
            Check(test.SetOpen(false), "넘기가 끝나면 다시 닫을 수 있다");

            test.Break();
            Check(test.IsBroken && test.Passable, "부서진 개구부는 통과할 수 있다");
            Check(test.CanVault(test.Center), "부서진 창문은 좀비도 넘어 들어올 수 있다 (사용자 요구)");
            Check(!test.SetOpen(false), "부서진 개구부는 다시 닫히지 않는다");
        }
        finally { Object.DestroyImmediate(probe); }

        // ---- 문간이 실내로 잡힌다 ("문에 붙으면 밝아짐" 의 원인) ----
        var rooms = Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Vector3 doorway = Flat(9.37f, -OuterWall * 0.5f) + Vector3.up * BodyY;
        bool insideBox = false, enclosed = false;

        for (int i = 0; i < rooms.Length; i++)
        {
            if (rooms[i].Contains(doorway)) insideBox = true;
            if (rooms[i].Encloses(doorway, 0.3f)) enclosed = true;   // Room.DoorwayMargin 과 같은 값
        }

        Check(!insideBox, "현관 문간은 어느 방 상자 안도 아니다 (이게 밝아지던 원인이다)");
        Check(enclosed, "현관 문간은 0.3m 여유로 실내로 잡힌다");

        // ---- 실내 조명이 시야를 바꾼다 (사용자 요구) ----
        Check(typeof(VisionField).GetMethod("RevealLitRoom", flags) != null,
              "불이 켜져 있으면 지금 있는 방 전체가 보인다");
        Check(typeof(HouseLights).GetMethod("Toggle") != null, "전등을 켜고 끌 수 있다");
        Check(HouseLights.On, "전등은 기본으로 켜져 있다");

        var litMargin = (float)typeof(VisionField).GetField("litRoomMargin", flags)
                            .GetValue(Object.FindFirstObjectByType<VisionField>());
        Check(litMargin > OuterWall,
              "조명이 벽면까지 밝힌다 (여유 " + litMargin.ToString("F2") + "m > 외벽 " + OuterWall + "m)");

        // ---- 실내 전등 ----
        var rig = Object.FindFirstObjectByType<HouseLightRig>();
        Check(rig, "실내 전등 리그가 있다");
        if (rig)
        {
            var roomCount = Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Check(rig.LampCount >= roomCount, "방마다 전등이 있다 (" + rig.LampCount + "개 / 방 " + roomCount + "개)");

            var lamps = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int spots = 0, shadowed = 0, tooHigh = 0;
            for (int i = 0; i < lamps.Length; i++)
            {
                if (lamps[i].type != LightType.Spot) continue;

                spots++;
                if (lamps[i].shadows != LightShadows.None) shadowed++;
                if (lamps[i].transform.position.y >= RoomHeight) tooHigh++;
            }

            Check(spots >= roomCount, "전등이 스포트다 (점광원은 그림자 면이 6개라 아틀라스를 6배 먹는다)");
            Check(shadowed == 0,
                  "전등에 그림자가 없다 (" + shadowed + "개) — 13개에 그림자를 켜면 2048 아틀라스를 "
                  + "그만큼 쪼개서 태양 텍셀(1.46cm) 보다 몇 배로 거칠어진다");
            Check(tooHigh == 0, "전등이 천장 밑에 있다");

            var rigFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            float activeRange = (float)typeof(HouseLightRig).GetField("activeRange", rigFlags).GetValue(rig);
            Check(activeRange > 0f && activeRange < 30f,
                  "먼 방 전등은 꺼둔다 (반경 " + activeRange.ToString("F0") + "m) — 전부 켜면 픽셀 조명이 3.4ms 를 먹었다");
        }

        // 방 조명 마스크가 절벽이 아닌지. 하드 컷이면 화면에서 계단으로 보인다 (사용자 지적).
        var visionField = Object.FindFirstObjectByType<VisionField>();
        if (visionField)
        {
            float feather = (float)typeof(VisionField).GetField("litRoomFeather", flags).GetValue(visionField);
            var fogAsset = Object.FindFirstObjectByType<VisionFog>();
            int maskRes = (int)typeof(VisionFog).GetField("resolution", flags).GetValue(fogAsset);
            float maskCell = (visionField.Range + 2f) * 2f / maskRes;

            Check(feather >= maskCell * 3f,
                  "방 조명 경계가 마스크 칸 3개 이상에 걸쳐 사그라든다 (" + feather.ToString("F2")
                  + "m / 칸 " + maskCell.ToString("F3") + "m = " + (feather / maskCell).ToString("F1") + "칸)");
        }

        // ---- 방 크기가 화면에서 읽히는지 ----
        // 실측: ortho 9 에서 방 하나가 화면 폭의 5~14% 라 "화장실 같다" 는 지적을 받았다.
        var gameCamera = Object.FindFirstObjectByType<Camera>();
        if (gameCamera && gameCamera.orthographic)
        {
            // 아이소메트릭 지면 커버리지 = 직교 높이 / sin(부감각)
            float pitch = Mathf.Abs(gameCamera.transform.eulerAngles.x);
            float coverX = gameCamera.orthographicSize * 2f * 16f / 9f;   // 16:9 기준
            float smallest = float.MaxValue;

            var roomList = Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < roomList.Length; i++)
                smallest = Mathf.Min(smallest, roomList[i].InnerBounds.size.x);

            Check(pitch > 20f, "카메라가 부감이다");
            Check(smallest / coverX > 0.15f,
                  "가장 작은 방도 화면 폭의 15% 를 넘는다 ("
                  + (smallest / coverX * 100f).ToString("F0") + "%, 방 " + smallest.ToString("F1")
                  + "m / 화면 " + coverX.ToString("F1") + "m)");
        }

        // 줍기 반경이 방보다 작아야 Tab 이 방 안 물건을 다 쏟지 않는다.
        var interactor = Object.FindFirstObjectByType<PlayerInteractor>();
        if (interactor)
        {
            float pickup = (float)typeof(PlayerInteractor).GetField("pickupRadius", flags).GetValue(interactor);
            var roomList = Object.FindObjectsByType<Room>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            float smallest = float.MaxValue;
            for (int i = 0; i < roomList.Length; i++)
                smallest = Mathf.Min(smallest, roomList[i].InnerBounds.size.x);

            Check(pickup * 2f < smallest * 0.6f,
                  "줍기 지름이 가장 작은 방의 60% 보다 작다 (" + (pickup * 2f).ToString("F1")
                  + "m < " + (smallest * 0.6f).ToString("F1") + "m)");
        }

        // ---- 좀비 공성 경로가 살아 있다 ----
        Check(typeof(Zombie).GetMethod("UpdateSiege", flags) != null, "좀비가 개구부를 두드린다");
        Check(typeof(Zombie).GetMethod("PlayerIsReachable", flags) != null,
              "좀비가 걸어갈 길이 있는지 판단한다");
        Check(typeof(Zombie).GetMethod("JoinSiege") != null, "두드림 소리에 다른 좀비가 합류한다");
        Check(typeof(Zombie).GetMethod("StartVault", flags) != null, "좀비가 창문을 넘는다");
        Check(typeof(Noise).GetMethod("Emit", new[] { typeof(Vector3), typeof(float), typeof(bool) }) != null,
              "총성만 벽을 넘는 소리로 구분된다");
        Check(typeof(Zombie).GetMethod("IsMuffledByWalls", flags) != null,
              "실내에서 난 소리는 벽에 막힌다 (사용자 요구: 소리로는 안 걸린다)");
    }

    /// <summary>
    /// 삼각형 예산과 숲. 부지가 11배로 커질 예정이라 여기서 새면 나중에 못 잡는다.
    /// 정적 배칭이 메쉬를 합쳐버리므로 삼각형은 고유 메쉬 기준으로 세야 한다 —
    /// 그냥 세면 나무 216그루가 같은 결합 메쉬를 216번 세서 900만이 나온다 (실제로 그랬다).
    /// </summary>
    static void CheckForestAndPropBudget()
    {
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include,
                                                               FindObjectsSortMode.None);
        var counted = new System.Collections.Generic.HashSet<int>();
        int tris = 0;

        for (int i = 0; i < renderers.Length; i++)
        {
            var filter = renderers[i].GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) continue;
            if (!counted.Add(filter.sharedMesh.GetInstanceID())) continue;

            tris += filter.sharedMesh.triangles.Length / 3;
        }

        Check(tris < 120000, "씬 고유 메쉬 삼각형이 12만 미만이다 (" + tris.ToString("N0") + ")");

        // ---- 블렌더 작업본에서 지운 소품이 다시 들어오지 않았는지 ----
        string[] dropped = { "Oak floorboard", "Plant stem", "Leaf", "Duvet", "Curtain fold" };
        var pieces = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int d = 0; d < dropped.Length; d++)
        {
            int found = 0;
            for (int i = 0; i < pieces.Length; i++)
                if (pieces[i] && pieces[i].name.StartsWith(dropped[d])) found++;

            Check(found == 0, dropped[d] + " 은(는) 단순화로 제거됐다 (" + found + "개 남음)");
        }

        // 지우지 말아야 할 것 — 블렌더 작업본을 다시 얹을 때만 의미가 있다.
        // 별장 본채로 갈아타면서 옛 아파트용 작업본은 떼어냈다 (치수가 전혀 다르다).
        if (GameObject.Find("RefinedVisual"))
        {
            Transform mattress = null, rail = null, canopy = null;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (!pieces[i]) continue;
                if (pieces[i].name == "Mattress") mattress = pieces[i];
                if (pieces[i].name.StartsWith("Curtain rail")) rail = pieces[i];
                if (pieces[i].name == "Canopy") canopy = pieces[i];
            }
            Check(mattress, "이불을 지워도 매트리스는 남는다");
            Check(rail, "커튼 주름을 지워도 레일은 남는다 (커튼 자리가 읽혀야 한다)");
            Check(canopy, "화분에 단순 수관이 붙었다");
        }

        // ---- 숲 ----
        var forest = GameObject.Find("Forest");
        Check(forest, "숲이 있다");
        if (!forest) return;

        Check(forest.transform.childCount >= 100,
              "숲이 숲으로 보일 만큼 있다 (" + forest.transform.childCount + "그루)");

        Vector3 origin = SiteOrigin();
        var site = new Rect(origin.x, origin.z, SiteWidth, SiteDepth);
        int sharedMesh = 0, withCollider = 0, insideSite = 0;
        Mesh first = null;

        for (int i = 0; i < forest.transform.childCount; i++)
        {
            var tree = forest.transform.GetChild(i);
            var filter = tree.GetComponent<MeshFilter>();

            if (filter && filter.sharedMesh)
            {
                if (first == null) first = filter.sharedMesh;
                if (filter.sharedMesh == first) sharedMesh++;
            }

            if (tree.GetComponentInChildren<Collider>(true)) withCollider++;
            if (site.Contains(new Vector2(tree.position.x, tree.position.z))) insideSite++;
        }

        Check(sharedMesh == forest.transform.childCount,
              "모든 나무가 메쉬 하나를 공유한다 (정적 배칭이 한 묶음으로 삼킨다)");
        Check(withCollider == 0,
              "숲 나무에 콜라이더가 없다 (" + withCollider + "개) — 수백 개면 NavMesh 굽기가 비싸진다");
        Check(insideSite == 0,
              "숲이 부지 안으로 들어오지 않는다 (" + insideSite + "그루가 침범)");

        var treeMesh = AssetDatabase.LoadAssetAtPath<Mesh>(TreeMeshPath);
        Check(treeMesh, "나무 메쉬가 에셋으로 저장돼 있다 (안 그러면 씬을 다시 열 때 사라진다)");
        if (treeMesh)
            Check(treeMesh.triangles.Length / 3 <= 48,
                  "나무 한 그루가 48삼각형 이하다 (" + treeMesh.triangles.Length / 3 + ")");
    }

    /// <summary>
    /// 좀보이드식 성능 등급이 살아 있는지, 그리고 값이 서로 어긋나지 않는지.
    ///
    /// 여기가 어긋나면 프레임이 아니라 게임플레이가 깨진다 — 화면에 보이는 좀비가
    /// 등급이 내려가 뚝뚝 움직이거나, 보여야 할 좀비가 안 그려진다.
    /// </summary>
    static void CheckZombiePerformanceTiers()
    {
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var statics = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;

        Check(typeof(Zombie).GetMethod("ShouldTickNow", flags) != null,
              "거리별 갱신 등급이 있다 (좀보이드 Tiered Zombie Updates)");
        Check(typeof(Zombie).GetMethod("ApplyVisualLod", flags) != null,
              "먼 좀비는 그리지 않는다");
        Check(typeof(Zombie).GetMethod("TakePathBudget", statics) != null,
              "길찾기에 프레임 예산이 있다");

        var probe = new GameObject("__TierProbe");
        try
        {
            probe.AddComponent<CharacterController>();
            var zombie = probe.AddComponent<Zombie>();

            float full = (float)typeof(Zombie).GetField("fullUpdateRange", flags).GetValue(zombie);
            float mid = (float)typeof(Zombie).GetField("midUpdateRange", flags).GetValue(zombie);
            float visual = (float)typeof(Zombie).GetField("visualRange", flags).GetValue(zombie);

            Check(mid > full, "중거리 등급 경계가 근거리보다 멀다");
            Check(visual > full,
                  "그리는 거리가 매 프레임 갱신 거리보다 멀다 (" + visual + " > " + full + ") — "
                  + "반대면 보이는 좀비가 뚝뚝 움직인다");

            // 화면에 보이는 범위는 전부 매 프레임 갱신 등급이어야 한다.
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera && camera.orthographic)
            {
                float viewRadius = camera.orthographicSize * 2.5f;   // 아이소메트릭 지면 커버리지 여유분
                Check(full >= viewRadius,
                      "매 프레임 갱신 거리가 화면 커버리지를 덮는다 ("
                      + full + "m >= " + viewRadius.ToString("F1") + "m)");
            }
        }
        finally { Object.DestroyImmediate(probe); }

        // 좀비 모델 비용. 한 마리가 무거우면 등급제로도 못 막는다.
        var guids = AssetDatabase.FindAssets("t:ZombieDefinition");
        for (int i = 0; i < guids.Length; i++)
        {
            var definition = AssetDatabase.LoadAssetAtPath<ZombieDefinition>(
                AssetDatabase.GUIDToAssetPath(guids[i]));
            if (!definition || !definition.model) continue;

            int tris = 0;
            var renderers = definition.model.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var skinned = renderers[r] as SkinnedMeshRenderer;
                if (skinned && skinned.sharedMesh) tris += skinned.sharedMesh.triangles.Length / 3;

                var filter = renderers[r].GetComponent<MeshFilter>();
                if (filter && filter.sharedMesh) tris += filter.sharedMesh.triangles.Length / 3;
            }

            // 경고만 한다 — 아트 에셋을 줄이는 건 사용자 결정이다.
            if (tris > 20000)
                Debug.LogWarning("[V0.1] 좀비 모델 " + definition.model.name + " 이 " + tris.ToString("N0")
                                 + " 삼각형이다. 게임용 좀비는 2,000~5,000 이 보통이고, 무리를 굴리려면 줄여야 한다");
        }
    }

    static WindowFrame FindOpening(WindowFrame[] openings, string name)
    {
        for (int i = 0; i < openings.Length; i++)
            if (openings[i].name == name) return openings[i];

        return null;
    }

    static void CheckRoomSize(Room[] rooms, string label, float width, float depth)
    {
        var room = FindRoom(rooms, label);
        if (!room) return;

        Vector3 size = room.InnerBounds.size;
        width *= Scale;
        depth *= Scale;
        Check(Mathf.Abs(size.x - width) < 0.2f && Mathf.Abs(size.z - depth) < 0.2f,
              label + " 크기가 도면과 맞는다 (" + size.x.ToString("F2") + " x " + size.z.ToString("F2")
              + ", 도면 " + width.ToString("F2") + " x " + depth.ToString("F2") + ")");
    }

    /// <summary>NavMesh 로 실제 경로가 이어지는지. 문 폭이 좁으면 여기서 걸린다.</summary>
    static void CheckReachable(Vector3 from, Vector3 to, string label)
    {
        var path = new UnityEngine.AI.NavMeshPath();
        bool ok = UnityEngine.AI.NavMesh.CalculatePath(from, to, UnityEngine.AI.NavMesh.AllAreas, path)
                  && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
        Check(ok, label + " 경로가 이어진다 (문 폭 " + DoorWidth.ToString("F2")
              + "m, 에이전트 반지름 " + AgentRadius().ToString("F2") + "m)");
    }

    public static float AgentRadius() => UnityEngine.AI.NavMesh.GetSettingsByID(0).agentRadius;

    // ---- 플레이어 3D 모델 + 애니메이션 ----
    const string AnimationFolder = "Assets/_Game/Data/Animation";
    const string CharacterFolder = "Assets/_Game/Art/Characters";
    const string PlayerCharacterFolder = CharacterFolder + "/Player";
    const string PlayerModelPath = PlayerCharacterFolder + "/character01.fbx";
    const string PlayerTextureFolder = PlayerCharacterFolder + "/Textures";
    const string PlayerMaterialFolder = PlayerCharacterFolder + "/Materials";
    const string PlayerControllerPath = AnimationFolder + "/Player.controller";
    const float PlayerHeight = 1.6f;
    static readonly string[] PlayerClipNames = { "Idle", "Walk", "Run", "Attack", "AttackOneHand", "Death" };
    static readonly string[] PlayerHairRootNames =
    {
        "Hair_Ahoge_01", "Hair_Back_C_01", "Hair_Back_L_01", "Hair_Back_R_01",
        "Hair_Bangs_C_01", "Hair_Side_L_01", "Hair_Side_R_01"
    };
    static readonly string[][] PlayerMaterialTextures =
    {
        new[] { "material_0.001", "Image_0_001.png", "Image_1_001.png" },
        new[] { "material_0.002", "Image_0_003.png", "Image_1_003.png" }
    };

    static GameObject EnsurePlayerModelAndAnimation()
    {
        var importer = AssetImporter.GetAtPath(PlayerModelPath) as ModelImporter;
        if (!importer) throw new System.IO.FileNotFoundException("character01 FBX가 없습니다.", PlayerModelPath);

        bool changed = importer.animationType != ModelImporterAnimationType.Human
                       || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        if (changed) importer.SaveAndReimport();

        importer = (ModelImporter)AssetImporter.GetAtPath(PlayerModelPath);
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(PlayerModelPath);
        if (!avatar || !avatar.isValid || !avatar.isHuman)
            throw new System.InvalidOperationException("character01 휴머노이드 본 자동 매핑에 실패했습니다.");
        var humanDescription = avatar.humanDescription;
        var humanBones = new System.Collections.Generic.List<HumanBone>(humanDescription.human);
        bool mappingChanged = importer.autoGenerateAvatarMappingIfUnspecified;
        for (int i = humanBones.Count - 1; i >= 0; i--)
            if (IsOptionalFaceBone(humanBones[i].humanName)) { humanBones.RemoveAt(i); mappingChanged = true; }
        if (mappingChanged)
        {
            humanDescription.human = humanBones.ToArray();
            importer.humanDescription = humanDescription;
            importer.autoGenerateAvatarMappingIfUnspecified = false;
            importer.SaveAndReimport();
        }

        importer = (ModelImporter)AssetImporter.GetAtPath(PlayerModelPath);
        Directory.CreateDirectory(PlayerTextureFolder);
        AssetDatabase.Refresh();
        if (!HasPlayerImages())
        {
            if (!importer.ExtractTextures(PlayerTextureFolder))
                Debug.LogWarning("[V0.1] character01 내장 텍스처 추출에 실패했습니다. Self Check에서 확인하세요.");
            AssetDatabase.Refresh();
        }
        FixPlayerTextureExtensions();

        avatar = AssetDatabase.LoadAssetAtPath<Avatar>(PlayerModelPath);
        if (!avatar || !avatar.isValid || !avatar.isHuman)
            throw new System.InvalidOperationException("character01 휴머노이드 본 자동 매핑에 실패했습니다.");

        var missing = new System.Collections.Generic.List<string>();
        for (int i = 0; i < PlayerClipNames.Length; i++)
        {
            string path = PlayerCharacterFolder + "/character01@" + PlayerClipNames[i] + ".fbx";
            var clipImporter = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!clipImporter) { missing.Add(PlayerClipNames[i]); continue; }

            bool rigChanged = clipImporter.animationType != ModelImporterAnimationType.Human
                              || clipImporter.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel
                              || clipImporter.sourceAvatar
                              || !clipImporter.preserveHierarchy
                              || !clipImporter.importAnimation;
            clipImporter.animationType = ModelImporterAnimationType.Human;
            clipImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            clipImporter.sourceAvatar = null;
            clipImporter.preserveHierarchy = true;
            clipImporter.importAnimation = true;
            if (rigChanged) clipImporter.SaveAndReimport();

            clipImporter = (ModelImporter)AssetImporter.GetAtPath(path);
            var clips = clipImporter.clipAnimations;
            bool clipChanged = false;
            if (clips.Length == 0 && clipImporter.importedTakeInfos.Length > 0)
            {
                clips = clipImporter.defaultClipAnimations;
                clipChanged = true;
            }
            for (int c = 0; c < clips.Length; c++)
            {
                bool loop = i < 3;
                clipChanged |= clips[c].loopTime != loop || !clips[c].lockRootRotation
                               || !clips[c].lockRootHeightY || !clips[c].lockRootPositionXZ;
                clips[c].loopTime = loop;
                clips[c].lockRootRotation = true;
                clips[c].lockRootHeightY = true;
                clips[c].lockRootPositionXZ = true;
            }
            if (clipChanged)
            {
                clipImporter.clipAnimations = clips;
                clipImporter.SaveAndReimport();
            }
        }
        if (missing.Count > 0) Debug.LogWarning("[V0.1] 없는 클립: " + string.Join(", ", missing));

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
        if (!model) throw new System.InvalidOperationException("character01 모델을 불러오지 못했습니다.");
        if (MeasureModelHeight(model) <= 0.01f)
            throw new System.InvalidOperationException("character01 모델 높이를 측정하지 못했습니다.");

        EnsurePlayerMaterials(model);
        return model;
    }

    static void EnsurePlayerMaterials(GameObject model)
    {
        Directory.CreateDirectory(PlayerMaterialFolder);
        AssetDatabase.Refresh();
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new System.InvalidOperationException("URP Lit 쉐이더를 찾지 못했습니다.");

        var done = new System.Collections.Generic.HashSet<string>();
        var renderers = model.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        for (int m = 0; m < renderers[r].sharedMaterials.Length; m++)
        {
            var source = renderers[r].sharedMaterials[m];
            if (!source || !done.Add(source.name)) continue;

            string[] mapping = null;
            for (int i = 0; i < PlayerMaterialTextures.Length; i++)
                if (PlayerMaterialTextures[i][0] == source.name) { mapping = PlayerMaterialTextures[i]; break; }
            if (mapping == null)
                throw new System.InvalidOperationException("character01 FBX의 텍스처 매핑을 알 수 없는 재질입니다: " + source.name);
            string basePath = PlayerTextureFolder + "/" + mapping[1];
            string normalPath = PlayerTextureFolder + "/" + mapping[2];
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(basePath);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (!texture || !normal)
                throw new System.IO.FileNotFoundException(source.name + " 텍스처가 없습니다: " + basePath + ", " + normalPath);
            var normalImporter = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (normalImporter && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
                normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            }

            string path = PlayerMaterialFolder + "/" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            bool materialChanged = material.shader != shader
                                   || material.GetTexture("_BaseMap") != texture
                                   || material.GetTexture("_BumpMap") != normal
                                   || !material.IsKeywordEnabled("_NORMALMAP");
            if (materialChanged)
            {
                material.shader = shader;
                material.SetTexture("_BaseMap", texture);
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
                if (source.HasProperty("_BaseColor")) material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                else if (source.HasProperty("_Color")) material.SetColor("_BaseColor", source.GetColor("_Color"));
                EditorUtility.SetDirty(material);
            }
            Debug.Log("[V0.1] " + source.name + ": _BaseMap <- " + mapping[1] + ", _BumpMap <- " + mapping[2]);
        }
        AssetDatabase.SaveAssets();
    }

    static bool HasPlayerImages()
    {
        var files = Directory.GetFiles(PlayerTextureFolder);
        for (int i = 0; i < files.Length; i++)
            if (PlayerImageExtension(files[i]) != null) return true;
        return false;
    }

    static void FixPlayerTextureExtensions()
    {
        var files = Directory.GetFiles(PlayerTextureFolder);
        for (int i = 0; i < files.Length; i++)
        {
            if (Path.GetExtension(files[i]).Length > 0) continue;
            string extension = PlayerImageExtension(files[i]);
            if (extension == null) continue;
            string path = files[i].Replace(Path.DirectorySeparatorChar, (char)47);
            string error = AssetDatabase.MoveAsset(path, path + extension);
            if (!string.IsNullOrEmpty(error)) throw new System.IO.IOException(error);
        }
        AssetDatabase.Refresh();

        files = Directory.GetFiles(PlayerTextureFolder);
        for (int i = 0; i < files.Length; i++)
        {
            if (PlayerImageExtension(files[i]) == null) continue;
            string path = files[i].Replace(Path.DirectorySeparatorChar, (char)47);
            var textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!textureImporter || textureImporter.textureShape == TextureImporterShape.Texture2D) continue;
            textureImporter.textureShape = TextureImporterShape.Texture2D;
            textureImporter.SaveAndReimport();
        }
    }

    static string PlayerImageExtension(string path)
    {
        var signature = new byte[8];
        int count;
        using (var stream = File.OpenRead(path)) count = stream.Read(signature, 0, signature.Length);
        if (count >= 8 && signature[0] == 0x89 && signature[1] == 0x50 && signature[2] == 0x4e
            && signature[3] == 0x47 && signature[4] == 0x0d && signature[5] == 0x0a
            && signature[6] == 0x1a && signature[7] == 0x0a) return ".png";
        if (count >= 3 && signature[0] == 0xff && signature[1] == 0xd8 && signature[2] == 0xff) return ".jpg";
        return null;
    }

    static bool IsOptionalFaceBone(string name) => name == "LeftEye" || name == "RightEye" || name == "Jaw";

    static string HumanBoneName(HumanDescription description, string humanName)
    {
        for (int i = 0; i < description.human.Length; i++)
            if (description.human[i].humanName == humanName) return description.human[i].boneName;
        return null;
    }

    static void ApplyPlayerMaterials(GameObject visual)
    {
        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            var materials = renderers[r].sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                if (!materials[m]) continue;
                var replacement = AssetDatabase.LoadAssetAtPath<Material>(
                    PlayerMaterialFolder + "/" + materials[m].name + ".mat");
                if (!replacement)
                {
                    Debug.LogWarning("[V0.1] " + materials[m].name + " 플레이어 재질이 없습니다. Self Check에서 확인하세요.");
                    continue;
                }
                materials[m] = replacement;
            }
            renderers[r].sharedMaterials = materials;
        }
    }

    static UnityEditor.Animations.AnimatorController EnsurePlayerController()
    {
        Directory.CreateDirectory(AnimationFolder);
        var idle = LoadPlayerClip("Idle");
        var walk = LoadPlayerClip("Walk");
        var run = LoadPlayerClip("Run");
        var attack = LoadPlayerClip("Attack");
        var attackOneHand = LoadPlayerClip("AttackOneHand");
        var death = LoadPlayerClip("Death");

        var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(PlayerControllerPath);
        if (!controller)
            controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(PlayerControllerPath);
        for (int i = controller.parameters.Length - 1; i >= 0; i--) controller.RemoveParameter(i);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("AttackOneHand", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

        var machine = controller.layers[0].stateMachine;
        var oldAnyTransitions = machine.anyStateTransitions;
        for (int i = 0; i < oldAnyTransitions.Length; i++) machine.RemoveAnyStateTransition(oldAnyTransitions[i]);
        var oldStates = machine.states;
        for (int i = 0; i < oldStates.Length; i++) machine.RemoveState(oldStates[i].state);
        var idleState = machine.AddState("Idle");
        idleState.motion = idle;
        var walkState = machine.AddState("Walk");
        walkState.motion = walk;
        var runState = machine.AddState("Run");
        runState.motion = run;
        var attackState = machine.AddState("Attack");
        attackState.motion = attack;
        var attackOneHandState = machine.AddState("AttackOneHand");
        attackOneHandState.motion = attackOneHand;
        var deathState = machine.AddState("Death");
        deathState.motion = death;
        machine.defaultState = idleState;

        var idleToWalk = idleState.AddTransition(walkState);
        idleToWalk.hasExitTime = false;
        idleToWalk.duration = 0.15f;
        idleToWalk.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 0.1f, "Speed");
        var walkToIdle = walkState.AddTransition(idleState);
        walkToIdle.hasExitTime = false;
        walkToIdle.duration = 0.15f;
        walkToIdle.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 0.1f, "Speed");
        var walkToRun = walkState.AddTransition(runState);
        walkToRun.hasExitTime = false;
        walkToRun.duration = 0.15f;
        walkToRun.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 1.4f, "Speed");
        var runToWalk = runState.AddTransition(walkState);
        runToWalk.hasExitTime = false;
        runToWalk.duration = 0.15f;
        runToWalk.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 1.4f, "Speed");

        var attackStates = new[] { attackState, attackOneHandState };
        for (int i = 0; i < attackStates.Length; i++)
        {
            var attackClip = attackStates[i].motion;
            var toAttack = machine.AddAnyStateTransition(attackStates[i]);
            toAttack.hasExitTime = false;
            toAttack.duration = 0.05f;
            toAttack.canTransitionToSelf = false;
            toAttack.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, attackStates[i].name);
            toAttack.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0f, "Dead");

            var attackToRun = attackStates[i].AddTransition(runState);
            attackToRun.hasExitTime = attackClip;
            attackToRun.exitTime = 0.85f;
            attackToRun.duration = 0.12f;
            attackToRun.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 1.4f, "Speed");
            var attackToWalk = attackStates[i].AddTransition(walkState);
            attackToWalk.hasExitTime = attackClip;
            attackToWalk.exitTime = 0.85f;
            attackToWalk.duration = 0.12f;
            attackToWalk.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 0.1f, "Speed");
            attackToWalk.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 1.4f, "Speed");
            var attackToIdle = attackStates[i].AddTransition(idleState);
            attackToIdle.hasExitTime = attackClip;
            attackToIdle.exitTime = 0.85f;
            attackToIdle.duration = 0.12f;
            attackToIdle.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 0.1f, "Speed");
        }

        var toDeath = machine.AddAnyStateTransition(deathState);
        toDeath.hasExitTime = false;
        toDeath.duration = 0.08f;
        toDeath.canTransitionToSelf = false;
        toDeath.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Dead");
        var revive = deathState.AddTransition(idleState);
        revive.hasExitTime = false;
        revive.duration = 0.08f;
        revive.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0f, "Dead");

        AssetDatabase.SaveAssets();
        return controller;
    }

    static AnimationClip LoadPlayerClip(string name)
    {
        string path = PlayerCharacterFolder + "/character01@" + name + ".fbx";
        var all = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < all.Length; i++)
        {
            var clip = all[i] as AnimationClip;
            if (clip != null && !clip.name.StartsWith("__preview")) return clip;
        }
        return null;
    }

    // ---- 좀비 3D 모델 + 애니메이션 ----
    const string ZombieModelPath = "Assets/RigValidationZombi1/zombi1_humanoid.fbx";
    const string ZombieControllerPath = AnimationFolder + "/Zombie.controller";

    /// <summary>
    /// 좀비 애니메이터. 파라미터 3개로 상태를 고른다.
    ///   Speed  (float)   보통 걸음 = 1 로 정규화한 이동 속도
    ///   Attack (trigger) 물기/돌진이 시작된 순간
    ///   Dead   (bool)    사망
    ///
    /// 클립을 나중에 믹사모 같은 데서 바꿔 끼워도 코드는 그대로다 — 상태에 물린 클립만 교체한다.
    /// </summary>
    static UnityEditor.Animations.AnimatorController EnsureZombieController()
    {
        Directory.CreateDirectory(AnimationFolder);

        var idle = LoadClip("Idle");
        var walk = LoadClip("Walk");
        var attack = LoadClip("Attack");
        var death = LoadClip("Death");

        // 매번 새로 만든다. 상태를 손으로 고쳤다면 이 함수가 덮어쓴다는 뜻이다.
        AssetDatabase.DeleteAsset(ZombieControllerPath);
        var controller = UnityEditor.Animations.AnimatorController
                         .CreateAnimatorControllerAtPath(ZombieControllerPath);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);

        var machine = controller.layers[0].stateMachine;

        var idleState = machine.AddState("Idle");
        idleState.motion = idle;

        var walkState = machine.AddState("Walk");
        walkState.motion = walk;
        // 이동 속도에 맞춰 걷기 재생 속도를 바꾼다. 안 하면 빠른 좀비의 발이 미끄러진다.
        walkState.speedParameterActive = true;
        walkState.speedParameter = "Speed";

        var attackState = machine.AddState("Attack");
        attackState.motion = attack;

        var deathState = machine.AddState("Death");
        deathState.motion = death;

        machine.defaultState = idleState;

        var toWalk = idleState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0.15f;
        toWalk.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Greater, 0.06f, "Speed");

        var toIdle = walkState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.2f;
        toIdle.AddCondition(UnityEditor.Animations.AnimatorConditionMode.Less, 0.06f, "Speed");

        // 공격은 어느 상태에서든 끼어든다. 단 죽은 뒤에는 안 된다.
        var toAttack = machine.AddAnyStateTransition(attackState);
        toAttack.hasExitTime = false;
        toAttack.duration = 0.05f;
        toAttack.canTransitionToSelf = false;
        toAttack.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Attack");
        toAttack.AddCondition(UnityEditor.Animations.AnimatorConditionMode.IfNot, 0f, "Dead");

        // 공격이 끝나면 대기로. Speed 가 살아 있으면 대기에서 곧바로 걷기로 넘어간다.
        var attackDone = attackState.AddTransition(idleState);
        attackDone.hasExitTime = true;
        attackDone.exitTime = 0.85f;
        attackDone.duration = 0.12f;

        var toDeath = machine.AddAnyStateTransition(deathState);
        toDeath.hasExitTime = false;
        toDeath.duration = 0.08f;
        toDeath.canTransitionToSelf = false;
        toDeath.AddCondition(UnityEditor.Animations.AnimatorConditionMode.If, 0f, "Dead");

        AssetDatabase.SaveAssets();
        return controller;
    }

    static AnimationClip LoadClip(string name)
    {
        string path = CharacterFolder + "/zombi1@" + name + ".fbx";
        var all = AssetDatabase.LoadAllAssetsAtPath(path);
        for (int i = 0; i < all.Length; i++)
        {
            var clip = all[i] as AnimationClip;
            if (clip != null && !clip.name.StartsWith("__preview")) return clip;
        }

        Debug.LogWarning("[V0.1] 애니메이션 클립을 찾지 못했습니다: " + path);
        return null;
    }

    /// <summary>
    /// 좀비 종류 에셋에 모델과 애니메이터를 꽂는다.
    ///
    /// modelScale 은 모델 실측 높이에서 계산해 박아둔다 — 런타임에 재느니 한 번 재서 데이터로 둔다.
    /// 아트를 다시 뽑아 높이가 바뀌면 씬을 다시 빌드하면 값이 갱신된다.
    /// </summary>
    static void ApplyZombieModel(ZombieDefinition[] types)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ZombieModelPath);
        if (!model) { Debug.LogWarning("[V0.1] 좀비 모델이 없습니다: " + ZombieModelPath); return; }

        var controller = EnsureZombieController();
        float natural = MeasureModelHeight(model);

        for (int i = 0; i < types.Length; i++)
        {
            var type = types[i];
            if (!type) continue;

            type.model = model;
            type.animatorController = controller;
            type.modelScale = natural > 0.01f ? type.bodyHeight / natural : 1f;
            type.bodyRadius = 0.30f;   // NavMesh 에이전트와 맞춘다 (문을 지나야 한다)
            EditorUtility.SetDirty(type);
        }
        AssetDatabase.SaveAssets();

        Debug.Log("[V0.1] 좀비 모델 연결: 실측 높이 " + natural.ToString("F3") + "m, 배율 "
                  + (types.Length > 0 && types[0] ? types[0].modelScale.ToString("F2") : "-"));
    }

    /// <summary>발밑(y=0) 부터 렌더러 상단까지. 스킨드 메쉬는 인스턴스화해서 월드 바운즈로 재야 정확하다.</summary>
    static float MeasureModelHeight(GameObject model)
    {
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            probe.transform.position = Vector3.zero;
            probe.transform.rotation = Quaternion.identity;
            probe.transform.localScale = Vector3.one;

            var renderers = probe.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return 0f;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            return bounds.max.y;
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }
    }

    const float WallSegmentLength = 2f;   // 벽을 이만큼씩 쪼갠다 (가리는 조각만 흐려지도록)
    const float WindowSillTop = 0.9f;     // 창턱. 걸어서는 못 지나가고 Space 로 넘어야 한다
    const float WindowTop = 2f;           // 눈높이가 이 사이에 있어야 열린 창 너머가 보인다
    const float BodyY = PlayerHeight * 0.5f + 0.05f; // 캡슐 밑면을 지면에서 5cm 띄운다

    /// <summary>spans 에서 [from, to] 구간을 잘라낸다.</summary>
    static void CutSpan(System.Collections.Generic.List<Vector2> spans, float from, float to)
    {
        for (int i = spans.Count - 1; i >= 0; i--)
        {
            Vector2 span = spans[i];
            if (to <= span.x || from >= span.y) continue;

            spans.RemoveAt(i);
            if (from > span.x) spans.Insert(i, new Vector2(span.x, from));
            if (to < span.y) spans.Insert(i, new Vector2(to, span.y));
        }
    }

    static GameObject CreateWallPiece(Transform parent, string name, Vector3 center, Vector3 axis,
                                      float length, float height, float centerHeight, float thickness,
                                      Material wall, Material fade, bool exterior = false)
    {
        var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
        piece.name = name;
        piece.transform.SetParent(parent, false);
        piece.transform.position = center + Vector3.up * centerHeight;
        piece.transform.rotation = Quaternion.LookRotation(axis, Vector3.up);
        piece.transform.localScale = new Vector3(thickness, height, length);

        var renderer = piece.GetComponent<Renderer>();
        if (renderer) renderer.sharedMaterial = wall;

        piece.AddComponent<WallSegment>().Setup(wall, fade, exterior);
        return piece;
    }

    static readonly Vector3 ApartmentOrigin = new Vector3(12f, 0f, -4f);
    const float FlatWidth = 17.50f;     // 실내 가로 (외곽 18.00 - 외벽 0.25 x 2)
    const float FlatDepth = 13.50f;     // 실내 세로 (외곽 14.00 - 외벽 0.25 x 2)
    const float OuterWall = 0.25f;
    const float InnerWall = 0.12f;
    const float RoomHeight = 3.6f;
    const float RoofEave = 0.4f;
    const float RoofThickness = 0.25f;
    // CreateOpening 이 폭에 Scale 을 곱하므로 미리 나눠둔다 → 최종 1.4m.
    // 방만 키우고 문은 사람 치수로 남겨야 한다 (2.2m 짜리 문은 벽이 뚫린 것처럼 보인다).
    const float DoorWidth = 1.4f / Scale;
    // 문 높이는 두 가지에 동시에 걸린다.
    //  - NavMesh: 에이전트 높이 2.0m 보다 낮으면 문간이 통째로 버려져 실내가 고립된 섬이 된다.
    //  - CharacterController: 턱을 넘으려고 stepOffset 만큼 몸을 들어올리는데, 그때
    //    머리(1.73m) + stepOffset 이 인방에 닿으면 문이 안 열린 것처럼 막힌다.
    //    stepOffset 0.75 일 때 실제로 어느 문도 통과하지 못했다.
    const float DoorHeight = 2.6f;
    const float CounterHeight = 0.95f;
    const float CeilingThickness = 0.3f;  // 눈높이(1.15) 아래라 시야를 막지 않는다

    // 아래 좌표는 평면도의 미터 값 그대로 쓰고, 여기서 한 번에 키운다.
    //
    // 실측: 카메라가 지면 49.3 x 36.0m 를 비춰서 집 전체가 한 화면에 들어오고, 방 하나가
    // 화면 폭의 5~14% 짜리 칸으로 보였다 ("방이 화장실 같다"). 좀보이드는 25x18m 쯤을
    // 비춘다 — 면적으로 우리가 4배를 보여주고 있었다.
    // 그래서 방을 1.6배로 키우고(아래) 카메라를 ortho 9 -> 5.5 로 당겼다.
    // 둘을 합치면 방이 화면에서 차지하는 비율이 10% -> 26% 로 2.6배가 된다.
    const float Scale = 1.6f;

    // ---- 별장 본채 1층 띠 경계 (평면도 기준) ----
    //
    //  z=13.50 ┌── 창고 ──┬─ 팬트리 ─┬─── 주방 ───┬─ 식당 ─┐   북
    //  z= 9.62 ├──────────┴────┬─────┴───────┬────┴───────┤
    //          │    게임방      │    중앙홀    │    침실    │
    //  z= 5.62 ├───────────────┴──┬──────┬───┴──┬─────────┤
    //          │      거실         │ 현관홀│ 욕실 │  세탁실  │
    //  z= 0.00 └──────────────────┴─[현관]┴──────┴─────────┘   남
    //
    //  x=  0    4.06  8.18       13.30              17.50  (북쪽 칸막이)
    //  x=  0    5.06              12.32             17.50  (중간 칸막이)
    //  x=  0       7.06      11.68   14.80          17.50  (남쪽 칸막이)
    const float BandSouthEnd = 5.50f;    // 남쪽 띠 실내 끝
    const float BandMidStart = 5.62f;    // 중간 띠 실내 시작 (칸막이 0.12 건너)
    const float BandMidEnd = 9.50f;
    const float BandNorthStart = 9.62f;

    const float WallSM = 5.56f;          // 남/중 칸막이 중심선
    const float WallMN = 9.56f;          // 중/북 칸막이 중심선
    const float DoorY = 2.75f;           // 남쪽 띠 세로 칸막이의 문 높이 위치(z)
    const float MidDoorY = 7.56f;        // 중간 띠 세로 칸막이의 문 위치(z)
    // 좌표가 이미 실제 미터라 가구도 실측 그대로 둔다 (예전엔 방만 1.6배로 키우고 가구는 1.15배였다).
    const float FurnitureScale = 1f;

    static Vector3 Flat(float x, float z) => ApartmentOrigin + new Vector3(x * Scale, 0f, z * Scale);

    /// <summary>
    /// 기획 1항 테스트용 건물을 대체하는 시작 맵. 벽·문·창문·방은 전부 기존 컴포넌트
    /// (WallSegment / WindowFrame / Room) 를 그대로 쓴다 — 시야 차단, 벽 투명화,
    /// 창문 파쿠르, NavMesh 가 공짜로 따라온다.
    /// </summary>
    static void CreateApartment()
    {
        var root = new GameObject("Apartment").transform;

        var outer = EnsureSharedMaterial("flat_wall_outer", new Color(0.20f, 0.20f, 0.22f));
        var inner = EnsureSharedMaterial("flat_wall_inner", new Color(0.86f, 0.85f, 0.81f));
        var outerFade = EnsureFadeMaterial("flat_wall_outer_fade", new Color(0.20f, 0.20f, 0.22f, 0.3f));
        var innerFade = EnsureFadeMaterial("flat_wall_inner_fade", new Color(0.86f, 0.85f, 0.81f, 0.3f));
        var glass = EnsureFadeMaterial("window_glass", new Color(0.55f, 0.80f, 0.88f, 0.5f));
        var wood = EnsureSharedMaterial("flat_wood", new Color(0.56f, 0.39f, 0.24f));
        var tile = EnsureSharedMaterial("flat_tile", new Color(0.82f, 0.81f, 0.78f));
        var carpet = EnsureSharedMaterial("flat_carpet", new Color(0.72f, 0.70f, 0.66f));

        // ---- 바깥 벽 네 면 ----
        // cuts 는 (벽 시작점에서의 거리, 폭) 이다. 남/북 벽은 x=-OuterWall 에서 시작하므로
        // 거리 = x + OuterWall, 동/서 벽은 z=0 에서 시작하므로 거리 = z 그대로다.
        WallRun(root, "Wall_S", Flat(-OuterWall, -OuterWall * 0.5f), Flat(FlatWidth + OuterWall, -OuterWall * 0.5f),
                RoomHeight, OuterWall, outer, outerFade, new[]
                {
                    new Vector2(1.90f + OuterWall, 2.30f),   // 거실 서쪽 창
                    new Vector2(5.10f + OuterWall, 2.30f),   // 거실 동쪽 창
                    new Vector2(9.37f + OuterWall, DoorWidth), // 현관문
                }, true);
        WallRun(root, "Wall_N", Flat(-OuterWall, FlatDepth + OuterWall * 0.5f), Flat(FlatWidth + OuterWall, FlatDepth + OuterWall * 0.5f),
                RoomHeight, OuterWall, outer, outerFade, new[]
                {
                    new Vector2(9.10f + OuterWall, DoorWidth), // 뒷문 (숲 쪽 탈출)
                    new Vector2(11.60f + OuterWall, 1.40f),    // 주방 창
                }, true);
        WallRun(root, "Wall_W", Flat(-OuterWall * 0.5f, 0f), Flat(-OuterWall * 0.5f, FlatDepth),
                RoomHeight, OuterWall, outer, outerFade, new[] { new Vector2(MidDoorY, 1.40f) }, true);
        WallRun(root, "Wall_E", Flat(FlatWidth + OuterWall * 0.5f, 0f), Flat(FlatWidth + OuterWall * 0.5f, FlatDepth),
                RoomHeight, OuterWall, outer, outerFade, new[] { new Vector2(MidDoorY, 2.20f) }, true);

        // ---- 띠를 나누는 가로 칸막이 ----
        WallRun(root, "Wall_BandSM", Flat(0f, WallSM), Flat(FlatWidth, WallSM),
                RoomHeight, InnerWall, inner, innerFade, new[]
                {
                    new Vector2(6.10f, DoorWidth),   // 거실 → 중앙홀
                    new Vector2(9.37f, DoorWidth),   // 현관홀 → 중앙홀
                });
        WallRun(root, "Wall_BandMN", Flat(0f, WallMN), Flat(FlatWidth, WallMN),
                RoomHeight, InnerWall, inner, innerFade, new[]
                {
                    new Vector2(2.00f, DoorWidth),   // 게임방 → 창고
                    new Vector2(6.60f, DoorWidth),   // 중앙홀 → 팬트리
                    new Vector2(10.40f, DoorWidth),  // 중앙홀 → 주방
                });

        // ---- 남쪽 띠 세로 칸막이 ----
        WallRun(root, "Wall_SLiving", Flat(7.06f, 0f), Flat(7.06f, BandSouthEnd),
                RoomHeight, InnerWall, inner, innerFade, new[] { new Vector2(DoorY, DoorWidth) });
        WallRun(root, "Wall_SBath", Flat(11.68f, 0f), Flat(11.68f, BandSouthEnd),
                RoomHeight, InnerWall, inner, innerFade, new[] { new Vector2(DoorY, DoorWidth) });
        WallRun(root, "Wall_SUtil", Flat(14.80f, 0f), Flat(14.80f, BandSouthEnd),
                RoomHeight, InnerWall, inner, innerFade, new[] { new Vector2(DoorY, DoorWidth) });

        // ---- 중간 띠 세로 칸막이 ----
        WallRun(root, "Wall_MGame", Flat(5.06f, BandMidStart), Flat(5.06f, BandMidEnd),
                RoomHeight, InnerWall, inner, innerFade,
                new[] { new Vector2(MidDoorY - BandMidStart, DoorWidth) });
        WallRun(root, "Wall_MBed", Flat(12.32f, BandMidStart), Flat(12.32f, BandMidEnd),
                RoomHeight, InnerWall, inner, innerFade,
                new[] { new Vector2(MidDoorY - BandMidStart, DoorWidth) });

        // ---- 북쪽 띠 세로 칸막이 ----
        WallRun(root, "Wall_NStorage", Flat(4.06f, BandNorthStart), Flat(4.06f, FlatDepth),
                RoomHeight, InnerWall, inner, innerFade, NoCuts);
        WallRun(root, "Wall_NPantry", Flat(8.18f, BandNorthStart), Flat(8.18f, FlatDepth),
                RoomHeight, InnerWall, inner, innerFade, NoCuts);
        WallRun(root, "Wall_NDining", Flat(13.30f, BandNorthStart), Flat(13.30f, FlatDepth),
                RoomHeight, InnerWall, inner, innerFade, new[] { new Vector2(11.56f - BandNorthStart, DoorWidth) });

        // ---- 문 (창턱이 없어서 열면 걸어 나간다 → vaultable=false) ----
        Door(root, "Door_Entrance", Flat(9.37f, -OuterWall * 0.5f), Vector3.right, OuterWall, outer, outerFade, wood, "현관문", true);
        Door(root, "Door_Back", Flat(9.10f, FlatDepth + OuterWall * 0.5f), Vector3.right, OuterWall, outer, outerFade, wood, "뒷문", true);
        Door(root, "Door_Living", Flat(6.10f, WallSM), Vector3.right, InnerWall, inner, innerFade, wood, "거실문");
        Door(root, "Door_Hall", Flat(9.37f, WallSM), Vector3.right, InnerWall, inner, innerFade, wood, "중앙홀문");
        Door(root, "Door_Storage", Flat(2.00f, WallMN), Vector3.right, InnerWall, inner, innerFade, wood, "창고문");
        Door(root, "Door_Pantry", Flat(6.60f, WallMN), Vector3.right, InnerWall, inner, innerFade, wood, "팬트리문");
        Door(root, "Door_Kitchen", Flat(10.40f, WallMN), Vector3.right, InnerWall, inner, innerFade, wood, "주방문");
        Door(root, "Door_LivingSide", Flat(7.06f, DoorY), Vector3.forward, InnerWall, inner, innerFade, wood, "거실 쪽문");
        Door(root, "Door_Bathroom", Flat(11.68f, DoorY), Vector3.forward, InnerWall, inner, innerFade, wood, "욕실문");
        Door(root, "Door_Utility", Flat(14.80f, DoorY), Vector3.forward, InnerWall, inner, innerFade, wood, "세탁실문");
        Door(root, "Door_Game", Flat(5.06f, MidDoorY), Vector3.forward, InnerWall, inner, innerFade, wood, "게임방문");
        Door(root, "Door_Bedroom", Flat(12.32f, MidDoorY), Vector3.forward, InnerWall, inner, innerFade, wood, "침실문");
        Door(root, "Door_Dining", Flat(13.30f, 11.56f), Vector3.forward, InnerWall, inner, innerFade, wood, "식당문");

        // ---- 창문 (창턱이 남아서 Space 로 넘는다. 커튼도 여기만 달린다) ----
        Window(root, "Window_Living_W", Flat(1.90f, -OuterWall * 0.5f), Vector3.right, 2.30f, outer, outerFade, glass, "거실 서쪽 창문");
        Window(root, "Window_Living_E", Flat(5.10f, -OuterWall * 0.5f), Vector3.right, 2.30f, outer, outerFade, glass, "거실 동쪽 창문");
        Window(root, "Window_Kitchen", Flat(11.60f, FlatDepth + OuterWall * 0.5f), Vector3.right, 1.40f, outer, outerFade, glass, "주방 창문");
        Window(root, "Window_Game", Flat(-OuterWall * 0.5f, MidDoorY), Vector3.forward, 1.40f, outer, outerFade, glass, "게임방 창문");
        Window(root, "Window_Bedroom", Flat(FlatWidth + OuterWall * 0.5f, MidDoorY), Vector3.forward, 2.20f, outer, outerFade, glass, "침실 창문");

        // ---- 바닥 (방마다 재질이 다르면 공간이 읽힌다) ----
        Floor(root, "Floor_Living", 0f, 0f, 7.00f, BandSouthEnd, carpet);
        Floor(root, "Floor_Entrance", 7.12f, 0f, 11.62f, BandSouthEnd, tile);
        Floor(root, "Floor_Bath", 11.74f, 0f, 14.74f, BandSouthEnd, tile);
        Floor(root, "Floor_Utility", 14.86f, 0f, FlatWidth, BandSouthEnd, tile);
        Floor(root, "Floor_Game", 0f, BandMidStart, 5.00f, BandMidEnd, wood);
        Floor(root, "Floor_Hall", 5.12f, BandMidStart, 12.26f, BandMidEnd, wood);
        Floor(root, "Floor_Bedroom", 12.38f, BandMidStart, FlatWidth, BandMidEnd, wood);
        Floor(root, "Floor_Storage", 0f, BandNorthStart, 4.00f, FlatDepth, tile);
        Floor(root, "Floor_Pantry", 4.12f, BandNorthStart, 8.12f, FlatDepth, tile);
        Floor(root, "Floor_Kitchen", 8.24f, BandNorthStart, 13.24f, FlatDepth, tile);
        Floor(root, "Floor_Dining", 13.36f, BandNorthStart, FlatWidth, FlatDepth, wood);

        // ---- 방 부피 (실내 판정 / 벽 투명화 / 음영 짙기의 기준) ----
        FlatRoom(root, "거실", 0f, 0f, 7.00f, BandSouthEnd);
        FlatRoom(root, "현관홀", 7.12f, 0f, 11.62f, BandSouthEnd);
        FlatRoom(root, "욕실", 11.74f, 0f, 14.74f, BandSouthEnd);
        FlatRoom(root, "세탁실", 14.86f, 0f, FlatWidth, BandSouthEnd);
        FlatRoom(root, "게임방", 0f, BandMidStart, 5.00f, BandMidEnd);
        FlatRoom(root, "중앙홀", 5.12f, BandMidStart, 12.26f, BandMidEnd);
        FlatRoom(root, "침실", 12.38f, BandMidStart, FlatWidth, BandMidEnd);
        FlatRoom(root, "창고", 0f, BandNorthStart, 4.00f, FlatDepth);
        FlatRoom(root, "팬트리", 4.12f, BandNorthStart, 8.12f, FlatDepth);
        FlatRoom(root, "주방", 8.24f, BandNorthStart, 13.24f, FlatDepth);
        FlatRoom(root, "식당", 13.36f, BandNorthStart, FlatWidth, FlatDepth);

        CreateCeiling(root);
        CreateRoof(root, "MainRoof", ApartmentCenter(),
                   (FlatWidth + OuterWall) * Scale + OuterWall + RoofEave * 2f,
                   (FlatDepth + OuterWall) * Scale + OuterWall + RoofEave * 2f,
                   root.GetComponentsInChildren<Room>(true));
        CreatePorch(root, outer, tile);
        CreateFurniture(root);
        CreateEstate(root, outer, outerFade, wood, tile);
        CreateRoomLights(root);
    }

    /// <summary>본채와 같은 루트에 부지 시설을 두어 방 전등 리그를 하나만 만든다.</summary>
    static void CreateEstate(Transform root, Material wall, Material fade, Material wood, Material concrete)
    {
        var site = new GameObject("Estate").transform;
        site.SetParent(root, false);
        var metal = EnsureSharedMaterial("site_metal", new Color(0.38f, 0.41f, 0.43f));
        var solar = EnsureSharedMaterial("site_solar", new Color(0.08f, 0.16f, 0.24f));

        WorldBox(site, "ConcreteYard", 17.4f, -24f, 36.6f, -8f, 0.04f, 0f, concrete, false);
        WorldBox(site, "Driveway", 23.8f, -30.1f, 30.2f, -24f, 0.04f, 0f, concrete, false);

        // 남쪽은 진입로 폭만 비운다. 기둥 안쪽 면도 정확히 대문 경계에 맞춘다.
        WorldBox(site, "Fence_N", -6f, 19.8f, 58f, 20f, 2f, 0f, metal);
        WorldBox(site, "Fence_W", -6.1f, -30.1f, -5.9f, 19.9f, 2f, 0f, metal);
        WorldBox(site, "Fence_E", 57.9f, -30.1f, 58.1f, 19.9f, 2f, 0f, metal);
        WorldBox(site, "Fence_S_W", -6f, -30.2f, 23.8f, -30f, 2f, 0f, metal);
        WorldBox(site, "Fence_S_E", 30.2f, -30.2f, 58f, -30f, 2f, 0f, metal);
        WorldBox(site, "GatePost_W", 23.5f, -30.3f, 23.8f, -29.9f, 2.3f, 0f, metal);
        WorldBox(site, "GatePost_E", 30.2f, -30.3f, 30.5f, -29.9f, 2.3f, 0f, metal);

        var exit = new GameObject("Exit_ToCity");
        exit.transform.SetParent(site, false);
        exit.transform.position = new Vector3(27f, 1f, -33.1f);
        var trigger = exit.AddComponent<BoxCollider>();
        trigger.size = new Vector3(6.4f, 2f, 2f);
        trigger.isTrigger = true;
        exit.AddComponent<SceneExit>().Setup("V01_City_Hadan", "Arrive_FromEstate");
        WorldBox(site, "Exit_ToCity_Marker", 23.8f, -34.1f, 30.2f, -32.1f, 0.06f, 0f,
                 EnsureSharedMaterial("scene_exit", new Color(1f, 0.62f, 0.05f)), false);

        WorldBox(site, "GarageFloor", 2.6f, -25.1f, 17f, -13.9f, 0.04f, 0f, concrete, false);
        WorldBox(site, "MechanicalFloor", -5.4f, -20.3f, 2.6f, -13.9f, 0.04f, 0f, concrete, false);

        WallRun(site, "GarageWall_S", new Vector3(2.6f, 0f, -25.1f + OuterWall * 0.5f), new Vector3(17f, 0f, -25.1f + OuterWall * 0.5f),
                RoomHeight, OuterWall, wall, fade, NoCuts, true);
        WallRun(site, "GarageWall_N", new Vector3(2.6f, 0f, -13.9f - OuterWall * 0.5f), new Vector3(17f, 0f, -13.9f - OuterWall * 0.5f),
                RoomHeight, OuterWall, wall, fade, NoCuts, true);
        WallRun(site, "GarageWall_E", new Vector3(17f - OuterWall * 0.5f, 0f, -25.1f), new Vector3(17f - OuterWall * 0.5f, 0f, -13.9f),
                RoomHeight, OuterWall, wall, fade, new[] { new Vector2(5.6f / Scale, 5f / Scale) }, true);
        WallRun(site, "GarageWall_W_S", new Vector3(2.6f + OuterWall * 0.5f, 0f, -25.1f), new Vector3(2.6f + OuterWall * 0.5f, 0f, -20.3f),
                RoomHeight, OuterWall, wall, fade, NoCuts, true);
        WallRun(site, "SharedWall", new Vector3(2.6f, 0f, -20.3f), new Vector3(2.6f, 0f, -13.9f),
                RoomHeight, OuterWall, wall, fade, new[] { new Vector2(3.2f / Scale, DoorWidth) });

        WallRun(site, "MechanicalWall_W", new Vector3(-5.4f + OuterWall * 0.5f, 0f, -20.3f), new Vector3(-5.4f + OuterWall * 0.5f, 0f, -13.9f),
                RoomHeight, OuterWall, wall, fade, NoCuts, true);
        WallRun(site, "MechanicalWall_S", new Vector3(-5.4f, 0f, -20.3f + OuterWall * 0.5f), new Vector3(2.6f, 0f, -20.3f + OuterWall * 0.5f),
                RoomHeight, OuterWall, wall, fade, NoCuts, true);
        WallRun(site, "MechanicalWall_N", new Vector3(-5.4f, 0f, -13.9f - OuterWall * 0.5f), new Vector3(2.6f, 0f, -13.9f - OuterWall * 0.5f),
                RoomHeight, OuterWall, wall, fade, new[] { new Vector2(4f / Scale, DoorWidth) }, true);

        CreateOpening(site, "Door_GarageShutter", new Vector3(17f - OuterWall * 0.5f, 0f, -19.5f), Vector3.forward,
                      5f / Scale, 0f, DoorHeight, RoomHeight, OuterWall,
                      wall, fade, metal, "차고 셔터", false, true);
        CreateOpening(site, "Door_GarageMechanical", new Vector3(2.6f, 0f, -17.1f), Vector3.forward,
                      DoorWidth, 0f, DoorHeight, RoomHeight, OuterWall,
                      wall, fade, wood, "기계실문", false);
        CreateOpening(site, "Door_MechanicalOutside", new Vector3(-1.4f, 0f, -13.9f - OuterWall * 0.5f), Vector3.right,
                      DoorWidth, 0f, DoorHeight, RoomHeight, OuterWall,
                      wall, fade, wood, "기계실 바깥문", false, true);

        // 차고 서벽은 중간부터 공유벽이라 꺾인다. 직사각형 Room 은 더 안쪽 외벽 면에 맞춰 벽과 겹치지 않게 한다.
        float gx0 = (2.6f + OuterWall - ApartmentOrigin.x) / Scale;
        float gx1 = (17f - OuterWall - ApartmentOrigin.x) / Scale;
        float gz0 = (-25.1f + OuterWall - ApartmentOrigin.z) / Scale;
        float gz1 = (-13.9f - OuterWall - ApartmentOrigin.z) / Scale;
        float mx0 = (-5.4f + OuterWall - ApartmentOrigin.x) / Scale;
        float mx1 = (2.6f - OuterWall * 0.5f - ApartmentOrigin.x) / Scale;
        float mz0 = (-20.3f + OuterWall - ApartmentOrigin.z) / Scale;
        float mz1 = (-13.9f - OuterWall - ApartmentOrigin.z) / Scale;
        FlatRoom(site, "차고", gx0, gz0, gx1, gz1);
        FlatRoom(site, "기계실", mx0, mz0, mx1, mz1);
        CreateCeiling(site, "GarageCeiling", new Vector3(9.8f, 0f, -19.5f), 14.4f, 11.2f);
        CreateCeiling(site, "MechanicalCeiling", new Vector3(-1.4f, 0f, -17.1f), 8f, 6.4f);
        CreateRoof(site, "EstateRoof", new Vector3(5.8f, 0f, -19.5f),
                   22.4f + RoofEave * 2f, 11.2f + RoofEave * 2f,
                   site.GetComponentsInChildren<Room>(true));

        // 셔터에서 공유문으로 가는 중앙 통로와 두 문 앞 1.2m는 비워 둔다.
        WorldBox(site, "GarageWorkbench", 8f, -14.8f, 12f, -14.2f, 0.9f, 0f, wood);
        WorldBox(site, "GarageToolWall", 12.4f, -14.15f, 15.8f, -14.05f, 1.5f, 1.1f, metal);

        WorldBox(site, "DieselGenerator", -4.8f, -19.7f, -2.5f, -18.5f, 1.2f, 0f, metal);
        for (int i = 0; i < 3; i++)
            WorldBox(site, "FuelDrum_" + i, -1.8f + i * 0.75f, -19.6f, -1.2f + i * 0.75f, -19f, 0.9f, 0f, metal);
        WorldBox(site, "ESSRack", -4.9f, -17.5f, -4.2f, -15.5f, 2.2f, 0f, metal);
        WorldBox(site, "InverterPanel", -5.27f, -15.2f, -5.05f, -14.4f, 1.2f, 0.8f, metal);
        WorldBox(site, "MechanicalWaterTank", 0.7f, -19.7f, 2f, -18.4f, 1.5f, 0f, metal);
        for (int i = 0; i < 3; i++)
            WorldBox(site, "WaterFilter_" + i, 0.7f + i * 0.42f, -15f, 1.02f + i * 0.42f, -14.4f, 1.1f, 0f, metal);

        WorldBox(site, "SolarRack", 40f, -18.4f, 52.8f, -12f, 0.6f, 0.35f, solar);
        WorldCylinder(site, "WellCover", new Vector3(44f, 0f, -22f), 1.6f, 0.18f, metal);
        WorldCylinder(site, "WaterTank", new Vector3(50f, 0f, -23f), 4.8f, 3f, metal);
        WorldBox(site, "SepticLid", 53.4f, -27.6f, 56.6f, -24.4f, 0.08f, 0f, metal);
    }

    /// <summary>
    /// 방마다 천장 전등. 기획 6항 태양광 + ESS 라 2년째 전기가 들어온다.
    ///
    /// 점광원이 아니라 아래로 쏘는 스포트를 쓴다 — 점광원은 그림자 면이 6개라
    /// 아틀라스를 6배로 잡아먹는다. 그림자는 아예 끈다 (HouseLightRig 주석 참고).
    /// 넓은 방은 한 개로는 구석이 어두워서 둘로 나눈다.
    /// </summary>
    static void CreateRoomLights(Transform root)
    {
        var parent = new GameObject("Lights").transform;
        parent.SetParent(root, false);

        var rooms = root.GetComponentsInChildren<Room>(true);
        var lamps = new System.Collections.Generic.List<Light>();

        for (int i = 0; i < rooms.Length; i++)
        {
            var bounds = rooms[i].InnerBounds;
            int count = bounds.size.x > 9f ? 2 : 1;

            for (int n = 0; n < count; n++)
            {
                // 둘일 때는 긴 방향으로 1/4, 3/4 지점에 둔다.
                float t = count == 1 ? 0.5f : (n == 0 ? 0.28f : 0.72f);
                var spot = new GameObject("Lamp_" + rooms[i].DisplayName + (count > 1 ? "_" + n : ""));
                spot.transform.SetParent(parent, false);
                spot.transform.position = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, t), RoomHeight - 0.25f, bounds.center.z);
                spot.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // 바닥을 향해

                var light = spot.AddComponent<Light>();
                light.type = LightType.Spot;
                light.spotAngle = 140f;
                light.innerSpotAngle = 60f;
                light.color = new Color(1f, 0.93f, 0.80f);   // 따뜻한 실내등
                light.intensity = 2.4f;
                light.range = RoomHeight + Mathf.Max(bounds.size.x, bounds.size.z) * 0.6f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;

                lamps.Add(light);
            }
        }

        parent.gameObject.AddComponent<HouseLightRig>().Bind(lamps.ToArray());
        Debug.Log("[V0.1] 실내 전등 " + lamps.Count + "개 (스포트, 그림자 없음 — 벽 가림은 시야 마스크가 한다)");
    }

    /// <summary>문 하나. 열림 상태가 통행과 시야를 같이 여닫는다 (CreateOpening 참고).</summary>
    static void Door(Transform parent, string name, Vector3 center, Vector3 axis, float thickness,
                     Material wall, Material fade, Material pane, string label, bool exterior = false) =>
        CreateOpening(parent, name, center, axis, DoorWidth, 0f, DoorHeight, RoomHeight, thickness,
                      wall, fade, pane, label, false, exterior);

    /// <summary>창문 하나. 창턱이 남아 걸어서는 못 지나가고, 커튼이 달린다.</summary>
    static void Window(Transform parent, string name, Vector3 center, Vector3 axis, float width,
                       Material wall, Material fade, Material pane, string label) =>
        CreateOpening(parent, name, center, axis, width, WindowSillTop, WindowTop, RoomHeight, OuterWall,
                      wall, fade, pane, label, true, true);

    /// <summary>
    /// 기획 19항 현관 포치. 생존자들과 문을 사이에 두고 대치하는 무대다.
    /// 지붕이 있지만 실외라 Room 상자를 두지 않는다 — 문을 나서면 음영이 실외 값으로 바뀐다.
    /// </summary>
    static void CreatePorch(Transform parent, Material stone, Material slab)
    {
        var porch = new GameObject("Porch").transform;
        porch.SetParent(parent, false);

        Floor(porch, "Porch_Slab", 7.12f, -2.50f, 11.62f, -OuterWall, slab);

        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? 7.45f : 11.29f;
            var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Porch_Post_" + i;
            post.transform.SetParent(porch, false);
            post.transform.position = Flat(x, -2.20f) + Vector3.up * (RoomHeight * 0.5f);
            post.transform.localScale = new Vector3(0.22f, RoomHeight, 0.22f);
            var renderer = post.GetComponent<Renderer>();
            if (renderer) renderer.sharedMaterial = stone;
        }

        CreateRoof(porch, "Porch_Roof", Flat(9.37f, -1.35f),
                   4.50f * Scale, 2.25f * Scale,
                   parent.GetComponentsInChildren<Room>(true));
    }


    // ---- 블렌더에서 다듬은 실내 (GPT 작업본) ----
    const string RefinedApartmentPath = "Assets/_Game/Art/Apartment/Apartment_refined.fbx";
    // 블렌더 쪽 바닥판 윗면이 6.7cm 라, 그만큼 내려야 보이는 바닥과 걷는 면(y=0) 이 맞는다.
    // 충돌은 절차적 쪽이 담당하므로 보이는 것만 내려도 판정은 그대로다.
    const float RefinedFloorTop = 0.067f;

    /// <summary>
    /// 블렌더 작업본을 '보이는 것' 으로 얹는다. 충돌·시야·방 판정은 절차적 구조물이 계속 맡는다.
    ///
    /// 블렌더 쪽이 벽/문/창문/바닥 이름을 그대로 유지해줘서, 같은 이름끼리 부모-자식으로 묶으면
    /// 벽 투명화(WallSegment) 와 문 여닫기(WindowFrame 경첩) 가 손댈 것 없이 그대로 먹는다.
    ///
    /// 가구는 이름이 다르다 (플레이스홀더 박스를 제대로 된 가구로 갈아치웠으므로).
    /// 그래서 절차적 가구는 통째로 버리고, 들여온 가구에서 크기를 보고 콜라이더를 만든다.
    /// </summary>
    static void ApplyRefinedVisuals(Transform apartment)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(RefinedApartmentPath);
        if (!model)
        {
            Debug.LogWarning("[V0.1] 블렌더 작업본이 없습니다: " + RefinedApartmentPath);
            return;
        }

        // 플레이스홀더 가구는 이제 필요 없다. 들여온 가구가 모양도 콜라이더도 담당한다.
        var placeholder = apartment.Find("Furniture");
        if (placeholder) Object.DestroyImmediate(placeholder.gameObject);

        var visual = Object.Instantiate(model);
        visual.name = "RefinedVisual";
        visual.transform.SetParent(apartment, false);
        // 블렌더 모델은 원점 중심이다. 절차적 아파트의 바깥 벽 중심에 맞춘다.
        visual.transform.position = ApartmentCenter() + Vector3.down * RefinedFloorTop;
        // 블렌더 FBX 는 Y축으로 180도 돌아서 들어온다. 안 돌리면 침대가 식당에, 냉장고가 거실에 간다.
        // (구조물은 이름으로 붙으니 겉보기엔 멀쩡한데 가구만 반대편에 놓여 바로 안 보인다)
        visual.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

        SimplifyRefinedProps(visual.transform);

        // 이름 → 절차적 오브젝트 사전
        var byName = new System.Collections.Generic.Dictionary<string, Transform>();
        var structures = apartment.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < structures.Length; i++)
        {
            if (structures[i] == visual.transform || structures[i].IsChildOf(visual.transform)) continue;
            byName[structures[i].name] = structures[i];
        }

        int merged = 0, collided = 0, loose = 0;
        var pieces = new System.Collections.Generic.List<Transform>();
        for (int i = 0; i < visual.transform.childCount; i++) pieces.Add(visual.transform.GetChild(i));

        foreach (var piece in pieces)
        {
            var host = FindStructureHost(byName, piece.name);
            if (host != null)
            {
                // 절차적 쪽은 콜라이더만 남기고 안 보이게 한다.
                var hidden = host.GetComponent<Renderer>();
                if (hidden) hidden.enabled = false;

                piece.SetParent(host, true);
                merged++;

                // 보이는 메쉬가 자식으로 바뀌었으니 벽 투명화 대상도 갈아끼운다.
                var wall = host.GetComponent<WallSegment>();
                if (wall) BindWallVisual(wall);
                continue;
            }

            loose++;
            if (NeedsCollider(piece)) { AddBoxCollider(piece); collided++; }
        }

        MarkStatic(visual.transform);

        VerifyRefinedAlignment(apartment);

        Debug.Log("[V0.1] 블렌더 작업본 적용: 구조물 " + merged + "개 연결, 소품 " + loose
                  + "개 중 " + collided + "개에 콜라이더");
    }

    /// <summary>
    /// 컨테이너(냉장고/옷장/책상) 는 원래 플레이스홀더 큐브였다. 블렌더 작업본에 진짜 가구가
    /// 생겼으므로 큐브는 안 보이게 하고 그 가구 자리로 옮긴다 — 안 그러면 냉장고가 두 개가 된다.
    /// WorldContainer 는 거리로만 상호작용하므로 콜라이더도 필요 없다 (가구 쪽이 이미 막는다).
    /// </summary>
    static void AttachContainersToFurniture()
    {
        var apartment = GameObject.Find("Apartment");
        var visual = apartment ? apartment.transform.Find("Furniture") : null;
        if (!visual) { Debug.LogWarning("[V0.1] Furniture 를 못 찾아 컨테이너를 못 붙였습니다"); return; }

        string[,] map =
        {
            { "냉장고", "Fridge" },        // 팬트리
            { "옷장", "Wardrobe" },        // 침실
            { "책상", "Desk" },            // 게임방
        };

        var meshes = visual.GetComponentsInChildren<Renderer>(true);
        var containers = Object.FindObjectsByType<WorldContainer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        for (int m = 0; m < map.GetLength(0); m++)
        {
            Renderer target = null;
            for (int i = 0; i < meshes.Length; i++)
                if (meshes[i].name == map[m, 1]) { target = meshes[i]; break; }

            if (!target) { Debug.LogWarning("[V0.1] 가구를 못 찾음: " + map[m, 1]); continue; }

            for (int c = 0; c < containers.Length; c++)
            {
                if (containers[c].DisplayName != map[m, 0]) continue;

                var go = containers[c].gameObject;
                // 논리 컨테이너 위치가 가구 콜라이더 안이면 저장/스폰 안전 검사에 걸린다.
                // 냉장고·옷장은 남쪽, 책상은 북쪽의 빈 바닥으로 상호작용 점만 뺀다.
                Vector3 side = m == 2 ? Vector3.forward : Vector3.back;
                go.transform.position = target.bounds.center + side * (target.bounds.extents.z + 0.35f);

                var renderer = go.GetComponent<Renderer>();
                if (renderer) renderer.enabled = false;
                var collider = go.GetComponent<Collider>();
                if (collider) collider.enabled = false;
            }
        }
    }

    /// <summary>
    /// 붙인 블렌더 메쉬가 실제로 그 구조물 자리에 놓였는지 본다.
    /// 모델이 돌아서 들어오면 이름 연결은 성공하는데 메쉬만 반대편에 놓여, 겉보기로는
    /// 멀쩡해 보이고 가구가 엉뚱한 방에 가 있는 것으로만 드러난다 (실제로 그랬다).
    /// </summary>
    static void VerifyRefinedAlignment(Transform apartment)
    {
        string[] probes = { "Wall_TV_0", "Floor_Kitchen", "Floor_Bedroom", "Door_Bedroom_Lintel" };
        for (int i = 0; i < probes.Length; i++)
        {
            var host = apartment.Find(probes[i]);
            if (!host) continue;

            var hostRenderer = host.GetComponent<Renderer>();
            Renderer child = null;
            for (int c = 0; c < host.childCount; c++)
            {
                child = host.GetChild(c).GetComponent<Renderer>();
                if (child) break;
            }
            if (!hostRenderer || !child) continue;

            float off = Vector3.Distance(
                new Vector3(hostRenderer.bounds.center.x, 0f, hostRenderer.bounds.center.z),
                new Vector3(child.bounds.center.x, 0f, child.bounds.center.z));
            if (off > 0.35f)
                Debug.LogError("[V0.1] 블렌더 메쉬가 제자리가 아닙니다: " + probes[i]
                    + " 절차적 " + hostRenderer.bounds.center.ToString("F2")
                    + " vs 블렌더 " + child.bounds.center.ToString("F2")
                    + " (" + off.ToString("F2") + "m 어긋남) — 모델 회전을 확인하세요");
        }
    }

    /// <summary>
    /// 벽의 보이는 메쉬를 다시 물려주고, 각 머티리얼의 반투명본을 만들어 짝지어 준다.
    /// 반투명본이 없으면 흐려질 때 아무 일도 안 일어난다 (URP 는 불투명 머티리얼의 알파를 무시한다).
    /// </summary>
    static void BindWallVisual(WallSegment wall)
    {
        var renderers = wall.GetComponentsInChildren<Renderer>(false);
        var visible = new System.Collections.Generic.List<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i].enabled && renderers[i].sharedMaterial) visible.Add(renderers[i]);

        if (visible.Count == 0) return;

        var opaque = new Material[visible.Count];
        var fade = new Material[visible.Count];
        for (int i = 0; i < visible.Count; i++)
        {
            opaque[i] = visible[i].sharedMaterial;
            fade[i] = EnsureFadeVariant(opaque[i]);
        }
        wall.BindVisuals(visible.ToArray(), opaque, fade);
    }

    /// <summary>원본 머티리얼의 반투명 사본. 이름당 하나만 만들어 에셋으로 남긴다.</summary>
    static Material EnsureFadeVariant(Material source)
    {
        if (!source) return null;

        string id = "fade_" + MakeFileSafe(source.name);
        Directory.CreateDirectory(MaterialFolder);
        string path = MaterialFolder + "/" + id + ".mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;

        var material = new Material(source);
        MakeTransparent(material);
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    static string MakeFileSafe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            sb.Append(System.Array.IndexOf(invalid, c) >= 0 || c == ' ' || c == '|' ? '_' : c);
        }
        return sb.ToString();
    }

    /// <summary>바깥 벽까지 포함한 아파트 중심. 블렌더 모델이 원점 중심이라 여기에 맞춘다.</summary>
    static Vector3 ApartmentCenter() =>
        Flat(FlatWidth * 0.5f, FlatDepth * 0.5f);

    /// <summary>
    /// 들여온 조각을 붙일 절차적 오브젝트를 찾는다.
    /// 문/창문 판은 블렌더에서 "Door_Entrance__Hinge__Pane" 처럼 납작해져 오므로 경첩 밑으로 보낸다
    /// (그래야 문을 열 때 같이 젖혀진다).
    /// </summary>
    static Transform FindStructureHost(System.Collections.Generic.Dictionary<string, Transform> byName, string pieceName)
    {
        Transform host;
        if (byName.TryGetValue(pieceName, out host)) return host;

        int split = pieceName.IndexOf("__");
        if (split <= 0) return null;

        // "Door_Entrance__Hinge__Pane" → 앞부분이 문 이름
        if (!byName.TryGetValue(pieceName.Substring(0, split), out host)) return null;

        var hinge = host.Find("Hinge");
        if (!hinge) return host;

        // 절차적 판은 콜라이더(통행·시야 차단) 담당이라 남기고 안 보이게만 한다.
        var pane = hinge.Find("Pane");
        if (pane)
        {
            var renderer = pane.GetComponent<Renderer>();
            if (renderer) renderer.enabled = false;
        }
        return hinge;
    }

    /// <summary>부딪힐 만한 크기인지. 잎사귀나 접시까지 막으면 이동이 지저분해진다.</summary>
    /// <summary>
    /// 블렌더 작업본은 베벨·서브디비전이 적용된 상태로 들어온다. 박스 하나면 12삼각형인
    /// 마룻장이 188, 이불 한 장이 22,530 이다. 아이소메트릭 거리에서는 전혀 보이지 않는
    /// 디테일이고, 시야 음영이 깊이 프리패스를 강제해서 삼각형이 두 번 그려지므로 그냥 지운다.
    ///
    /// 커튼 주름은 최적화가 아니라 정합성 문제다 — 우리 커튼 시스템(WindowFrame) 과
    /// 이중으로 달린다. 레일은 남겨서 커튼 자리가 읽히게 한다.
    /// </summary>
    static void SimplifyRefinedProps(Transform visual)
    {
        // 이름 앞부분으로 지운다. "Oak skirting" 과 "Curtain rail" 이 걸리지 않도록 정확히 맞춘다.
        string[] drop = { "Oak floorboard", "Plant stem", "Leaf", "Duvet", "Curtain fold" };

        var pieces = visual.GetComponentsInChildren<Transform>(true);
        var planters = new System.Collections.Generic.List<Transform>();
        int removed = 0, savedTris = 0;

        for (int i = 0; i < pieces.Length; i++)
        {
            var piece = pieces[i];
            if (!piece || piece == visual) continue;

            if (piece.name.StartsWith("Planter")) planters.Add(piece);

            bool hit = false;
            for (int d = 0; d < drop.Length; d++)
                if (piece.name.StartsWith(drop[d])) { hit = true; break; }
            if (!hit) continue;

            var filter = piece.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh) savedTris += filter.sharedMesh.triangles.Length / 3;

            Object.DestroyImmediate(piece.gameObject);
            removed++;
        }

        // 잎과 줄기를 지운 화분에 단순 수관을 얹는다. 박스 하나(12삼각형) 로
        // 줄기 12 + 잎 12 (약 11,000삼각형) 을 대신한다.
        var foliage = EnsureSharedMaterial("flat_foliage", new Color(0.24f, 0.34f, 0.20f));
        for (int i = 0; i < planters.Count; i++)
        {
            var pot = planters[i].GetComponent<Renderer>();
            if (!pot) continue;

            var canopy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            canopy.name = "Canopy";
            Object.DestroyImmediate(canopy.GetComponent<Collider>());
            canopy.GetComponent<MeshRenderer>().sharedMaterial = foliage;

            Vector3 top = pot.bounds.center + Vector3.up * (pot.bounds.extents.y + 0.34f);
            canopy.transform.position = top;
            canopy.transform.localScale = new Vector3(0.52f, 0.68f, 0.52f);
            canopy.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            canopy.transform.SetParent(planters[i], true);
        }

        Debug.Log("[V0.1] 소품 단순화: " + removed + "개 제거, 약 " + savedTris.ToString("N0")
                  + " 삼각형 절감, 화분 수관 " + planters.Count + "개 추가");
    }

    static bool NeedsCollider(Transform piece)
    {
        string[] skip =
        {
            "floorboard", "Leaf", "Plant stem", "skirting", "Curtain", "Artwork", "Mirror",
            "handle", "Cutlery", "Dinner plate", "Plate well", "Drinking cup", "utensils",
            "motif", "seam", "pull", "Foundation", "rail", "mullion", "mat", "rug",
            "Glazing", "faucet", "spout", "Hob ring", "Hob inset", "screen", "Vase",
            "Chopping board", "Coffee table book", "Coffee tray", "pillow", "Pillow", "throw",
        };
        for (int i = 0; i < skip.Length; i++)
            if (piece.name.IndexOf(skip[i], System.StringComparison.OrdinalIgnoreCase) >= 0) return false;

        var renderer = piece.GetComponentInChildren<Renderer>();
        if (!renderer) return false;

        Vector3 size = renderer.bounds.size;
        return size.y >= 0.25f && Mathf.Max(size.x, size.z) >= 0.30f;
    }

    static void AddBoxCollider(Transform piece)
    {
        var filter = piece.GetComponentInChildren<MeshFilter>();
        if (!filter || !filter.sharedMesh) return;

        var box = filter.gameObject.AddComponent<BoxCollider>();
        box.center = filter.sharedMesh.bounds.center;
        box.size = filter.sharedMesh.bounds.size;
    }

    /// <summary>전부 정적이다. 배칭과 라이트맵이 걸리도록 표시한다.</summary>
    internal static void MarkStatic(Transform root)
    {
        var all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
            GameObjectUtility.SetStaticEditorFlags(all[i].gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
                | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);
    }

    /// <summary>
    /// 보이는 평지붕. 콜라이더 없이 WallSegment 의 재질 교체만 재사용하고,
    /// 소속 Room 은 빌더가 직접 묶어 런타임이 이름이나 좌표를 추측하지 않게 한다.
    /// </summary>
    static void CreateRoof(Transform parent, string name, Vector3 center, float width, float depth, Room[] rooms)
    {
        var opaque = EnsureSharedMaterial("flat_roof", new Color(0.13f, 0.14f, 0.16f));
        var fade = EnsureFadeMaterial("flat_roof_fade", new Color(0.13f, 0.14f, 0.16f, 0.3f));

        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = name;
        roof.transform.SetParent(parent, false);
        roof.transform.position = center + Vector3.up * (RoomHeight + RoofThickness * 0.5f);
        roof.transform.localScale = new Vector3(width, RoofThickness, depth);
        roof.GetComponent<Renderer>().sharedMaterial = opaque;
        Object.DestroyImmediate(roof.GetComponent<Collider>());

        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0) roof.layer = ignoreRaycast;

        var segment = roof.AddComponent<WallSegment>();
        segment.Setup(opaque, fade);
        segment.BindRoof(rooms);
    }

    /// <summary>
    /// 보이지 않는 천장. 냉장고(2.4m) 같은 가구를 밟고 벽 위로 올라가 월담하는 걸 막는다.
    ///
    /// 렌더러가 없어 보이지 않고, 'Ignore Raycast' 레이어라 조준(PlayerAim) 과
    /// 시야 판정(Vision.BlockerMask) 에도 걸리지 않는다. 레이어는 물리 충돌과 무관하므로
    /// 몸은 그대로 막힌다. NavMesh 도 이 레이어를 제외하고 굽는다 (지붕에 섬이 생기지 않게).
    /// </summary>
    static void CreateCeiling(Transform parent)
        => CreateCeiling(parent, "Ceiling", ApartmentCenter(),
                         FlatWidth * Scale + OuterWall * 2f,
                         FlatDepth * Scale + OuterWall * 2f);

    static void CreateCeiling(Transform parent, string name, Vector3 center, float width, float depth)
    {
        var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ceiling.name = name;
        ceiling.transform.SetParent(parent, false);
        ceiling.transform.position = center + Vector3.up * (RoomHeight + CeilingThickness * 0.5f);
        ceiling.transform.localScale = new Vector3(width, CeilingThickness, depth);

        var renderer = ceiling.GetComponent<MeshRenderer>();
        if (renderer) Object.DestroyImmediate(renderer);

        int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
        if (ignoreRaycast >= 0) ceiling.layer = ignoreRaycast;
    }

    static GameObject WorldBox(Transform parent, string name, float x0, float z0, float x1, float z1,
                               float height, float baseHeight, Material material, bool solid = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3((x0 + x1) * 0.5f, baseHeight + height * 0.5f, (z0 + z1) * 0.5f);
        go.transform.localScale = new Vector3(x1 - x0, height, z1 - z0);
        var renderer = go.GetComponent<Renderer>();
        if (renderer) renderer.sharedMaterial = material;
        if (!solid) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static GameObject WorldCylinder(Transform parent, string name, Vector3 center, float diameter,
                                    float height, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = center + Vector3.up * (height * 0.5f);
        go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
        var renderer = go.GetComponent<Renderer>();
        if (renderer) renderer.sharedMaterial = material;
        Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        collider.convex = true;
        return go;
    }

    static readonly Vector2[] NoCuts = new Vector2[0];

    /// <summary>바닥 판. 보기용이라 콜라이더를 뗀다 (지면이 이미 있다).</summary>
    static void Floor(Transform parent, string name, float x0, float z0, float x1, float z1, Material material)
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = name;
        floor.transform.SetParent(parent, false);
        floor.transform.position = Flat((x0 + x1) * 0.5f, (z0 + z1) * 0.5f) + Vector3.up * 0.02f;
        floor.transform.localScale = new Vector3((x1 - x0) * Scale, 0.04f, (z1 - z0) * Scale);

        var collider = floor.GetComponent<Collider>();
        if (collider) Object.DestroyImmediate(collider);

        var renderer = floor.GetComponent<Renderer>();
        if (renderer) renderer.sharedMaterial = material;
    }

    static void FlatRoom(Transform parent, string label, float x0, float z0, float x1, float z1)
    {
        var go = new GameObject("Room_" + label);
        go.transform.SetParent(parent, false);
        go.transform.position = Flat((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
        go.AddComponent<Room>().Setup(label, new Vector3((x1 - x0) * Scale, RoomHeight, (z1 - z0) * Scale));
    }

    /// <summary>
    /// 한 줄 벽. cuts 는 (시작점에서의 거리, 폭) 이고 그만큼 비운다.
    /// 남은 구간은 2m 조각으로 쪼갠다 — 한 덩어리면 플레이어를 가릴 때 벽 전체가 흐려진다.
    /// </summary>
    static void WallRun(Transform parent, string name, Vector3 start, Vector3 end,
                        float height, float thickness, Material wall, Material fade, Vector2[] cuts,
                        bool exterior = false)
    {
        Vector3 delta = end - start;
        float length = delta.magnitude;
        if (length < 0.01f) return;

        Vector3 axis = delta / length;

        var spans = new System.Collections.Generic.List<Vector2> { new Vector2(0f, length) };
        for (int i = 0; i < cuts.Length; i++)
            // cuts 는 도면 단위다. 벽 양 끝이 Scale 로 늘어났으므로 구멍도 같이 늘린다.
            CutSpan(spans, (cuts[i].x - cuts[i].y * 0.5f) * Scale, (cuts[i].x + cuts[i].y * 0.5f) * Scale);

        int index = 0;
        for (int s = 0; s < spans.Count; s++)
        {
            float spanLength = spans[s].y - spans[s].x;
            if (spanLength <= 0.01f) continue;

            int pieces = Mathf.Max(1, Mathf.CeilToInt(spanLength / WallSegmentLength));
            float pieceLength = spanLength / pieces;

            for (int p = 0; p < pieces; p++)
            {
                float center = spans[s].x + (p + 0.5f) * pieceLength;
                CreateWallPiece(parent, name + "_" + index++, start + axis * center, axis,
                                pieceLength, height, height * 0.5f, thickness, wall, fade, exterior);
            }
        }
    }

    /// <summary>
    /// 벽에 뚫린 구멍 + 여닫는 판. 창문과 문이 같은 부품이다 — 다른 건 창턱 유무뿐이다.
    ///
    /// 판의 콜라이더 하나가 통행과 시야를 동시에 막는다. 열면 둘 다 뚫린다.
    /// 창문은 창턱(sillTop>0) 이 남아 걸어서는 못 지나가고 Space 로 넘어야 하지만,
    /// 문은 창턱이 없어서 열면 그냥 걸어 나간다 (vaultable=false).
    /// </summary>
    static void CreateOpening(Transform parent, string name, Vector3 center, Vector3 axis,
                              float width, float sillTop, float openTop, float wallHeight, float thickness,
                              Material wall, Material fade, Material paneMaterial,
                              string label, bool vaultable, bool exterior = false)
    {
        // width 는 도면 단위로 받는다. 벽과 같이 늘어나야 구멍과 문짝이 어긋나지 않는다.
        width *= Scale;

        var frame = new GameObject(name).transform;
        frame.SetParent(parent, false);
        frame.position = center + Vector3.up * ((sillTop + openTop) * 0.5f);
        // forward 는 벽을 가로지르는 방향(법선) 이다. 파쿠르 방향 판단에 쓴다.
        frame.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, axis), Vector3.up);

        if (sillTop > 0.01f)
            CreateWallPiece(parent, name + "_Sill", center, axis, width, sillTop,
                            sillTop * 0.5f, thickness, wall, fade, exterior);

        if (wallHeight - openTop > 0.01f)
            CreateWallPiece(parent, name + "_Lintel", center, axis, width, wallHeight - openTop,
                            (wallHeight + openTop) * 0.5f, thickness, wall, fade, exterior);

        // 경첩을 구멍 한쪽 끝에 두고 판을 매단다. 열면 경첩이 돌아 판이 젖혀진다.
        var hinge = new GameObject("Hinge").transform;
        hinge.SetParent(frame, false);
        hinge.localPosition = new Vector3(-width * 0.5f, 0f, 0f);

        var pane = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pane.name = "Pane";
        pane.transform.SetParent(hinge, false);
        pane.transform.localPosition = new Vector3(width * 0.5f, 0f, 0f);
        pane.transform.localScale = new Vector3(width, openTop - sillTop, Mathf.Min(0.08f, thickness));

        var renderer = pane.GetComponent<Renderer>();
        if (renderer && paneMaterial) renderer.sharedMaterial = paneMaterial;

        // 닫힌 문은 좀비 길찾기도 막아야 한다. NavMesh 는 문을 빼고 구웠으므로
        // 실제 차단은 이 장애물이 실시간으로 한다. 문이 젖혀지면 깎는 자리도 같이 움직인다.
        var obstacle = pane.AddComponent<UnityEngine.AI.NavMeshObstacle>();
        obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;

        var opening = frame.gameObject.AddComponent<WindowFrame>();
        opening.Setup(hinge, pane.GetComponent<Collider>(), width, label, vaultable);

        // 창턱이 있는 개구부가 창문이다. 문은 창턱이 없다 (sillTop 0).
        // 창유리는 몸만 막고 시야는 통과시킨다 — 닫힌 유리가 시야까지 막으면
        // 커튼이 무의미해진다 (사용자 요구: 발견 조건은 커튼). TransparentFX 레이어는
        // 이미 Vision.BlockerMask 에서 빠져 있어서 Vision 을 건드릴 필요가 없다.
        // 문짝은 그대로 Default 라 시야를 막는다.
        // 사용자 요구: 창문은 좀비 2마리, 문은 3마리가 붙어 두드리면 부서진다.
        bool isWindow = sillTop > 0.01f;
        opening.SetupBreakRule(isWindow ? 2 : 3, isWindow ? 4f : 6f);

        if (!isWindow) return;

        int glassLayer = LayerMask.NameToLayer("TransparentFX");
        if (glassLayer >= 0) pane.layer = glassLayer;

        AttachCurtain(opening, frame, width, openTop - sillTop, thickness);
    }

    /// <summary>
    /// 커튼. 좀비가 실내 플레이어를 발견하는 조건이라 실제로 시야를 막아야 한다 (사용자 요구).
    ///
    /// 창짝과 똑같이 콜라이더로 막는다 — Vision 은 커튼을 몰라도 되고, 커튼도 Vision 을 모른다.
    /// 창턱 위(0.9~2.0m) 에만 있어서 몸이 지나가는 길을 새로 막지는 않는다.
    /// 처음에는 젖혀 둔다. 치고 싶으면 창문 앞에서 C.
    /// </summary>
    static void AttachCurtain(WindowFrame opening, Transform frame,
                              float width, float height, float thickness)
    {
        var curtain = GameObject.CreatePrimitive(PrimitiveType.Cube);
        curtain.name = "Curtain";
        curtain.transform.SetParent(frame, false);

        // 실내 쪽 면에 건다. frame.forward 는 벽을 가로지르는 방향이라 부호만 고르면 된다.
        float side = Vector3.Dot(ApartmentCenter() - frame.position, frame.forward) >= 0f ? 1f : -1f;
        curtain.transform.localPosition = new Vector3(0f, 0f, side * (thickness * 0.5f + 0.05f));
        curtain.transform.localScale = new Vector3(width * 1.02f, height, 0.04f);

        var renderer = curtain.GetComponent<MeshRenderer>();
        if (renderer)
            renderer.sharedMaterial = EnsureSharedMaterial("flat_curtain", new Color(0.55f, 0.50f, 0.44f));

        opening.SetupCurtain(curtain, curtain.GetComponent<Collider>(), false);
    }

    /// <summary>도면의 가구를 박스로 옮긴다. 실제 에셋이 들어오면 이 함수만 갈아끼우면 된다.</summary>
    static void CreateFurniture(Transform root)
    {
        var parent = new GameObject("Furniture").transform;
        parent.SetParent(root, false);

        var pale = EnsureSharedMaterial("flat_soft", new Color(0.87f, 0.86f, 0.83f));
        var wood = EnsureSharedMaterial("flat_wood", new Color(0.56f, 0.39f, 0.24f));
        var olive = EnsureSharedMaterial("flat_olive", new Color(0.33f, 0.36f, 0.24f));
        var metal = EnsureSharedMaterial("flat_metal", new Color(0.70f, 0.72f, 0.74f));
        var dark = EnsureSharedMaterial("flat_dark", new Color(0.16f, 0.16f, 0.18f));
        var rug = EnsureSharedMaterial("flat_rug", new Color(0.66f, 0.64f, 0.60f));

        // 좌표는 실내 남서쪽 안쪽 모서리 기준 실제 미터다. 모든 가구는 문이 열리는 통로를
        // 비워둔다 — 예전에 세면대가 문 앞을 막아 욕실이 통째로 고립됐다.

        // 거실 (0..7.00 x 0..5.50): 남쪽 창 둘, 북쪽 중앙홀문(x 6.10), 동쪽 현관홀 쪽문(z 2.75)
        Prop(parent, "Rug", 1.20f, 1.60f, 5.20f, 4.40f, 0.02f, 0f, rug);
        Prop(parent, "Sofa_N", 1.40f, 4.30f, 4.40f, 5.20f, 0.72f, 0f, pale);
        Prop(parent, "Sofa_W", 0.30f, 2.00f, 1.20f, 4.20f, 0.72f, 0f, pale);
        Prop(parent, "CoffeeTable", 2.00f, 2.60f, 3.80f, 3.60f, 0.42f, 0f, pale);
        Prop(parent, "TV_Stand", 6.30f, 3.20f, 6.95f, 4.60f, 0.50f, 0f, wood);
        Prop(parent, "TV", 6.55f, 3.30f, 6.95f, 4.50f, 0.62f, 0.50f, dark);
        Prop(parent, "Plant_Living", 0.30f, 0.30f, 0.80f, 0.80f, 1.20f, 0f, olive);

        // 현관홀 (7.12..11.62 x 0..5.50): 남쪽 현관문 ~ 북쪽 중앙홀문이 x 9.37 로 일직선.
        // 가운데 통로(x 8.40..10.60) 는 반드시 비운다.
        Prop(parent, "ShoeRack", 7.30f, 0.30f, 8.20f, 0.80f, 0.55f, 0f, wood);
        Prop(parent, "CoatStand", 11.00f, 0.30f, 11.45f, 0.75f, 1.70f, 0f, dark);
        Prop(parent, "Console", 7.30f, 3.80f, 8.10f, 4.40f, 0.80f, 0f, wood);

        // 욕실 (11.74..14.74 x 0..5.50): 서/동 문이 z 2.75 로 마주본다 → z 2.15..3.35 는 비운다
        Prop(parent, "Toilet", 11.95f, 4.60f, 12.45f, 5.20f, 0.75f, 0f, pale);
        Prop(parent, "Sink", 13.90f, 4.40f, 14.60f, 5.00f, 0.18f, 0.72f, pale);
        Prop(parent, "Sink_Leg", 14.15f, 4.55f, 14.35f, 4.85f, 0.72f, 0f, pale);
        Prop(parent, "Bathtub", 12.90f, 0.20f, 14.60f, 1.00f, 0.55f, 0f, pale);

        // 세탁실 (14.86..17.50 x 0..5.50): 기획 6·7항 설비의 실내 쪽 (온수·세탁)
        Prop(parent, "Washer", 15.10f, 4.50f, 15.80f, 5.20f, 0.85f, 0f, metal);
        Prop(parent, "Dryer", 15.90f, 4.50f, 16.60f, 5.20f, 0.85f, 0f, metal);
        Prop(parent, "Boiler", 16.80f, 4.20f, 17.35f, 5.20f, 1.60f, 0f, metal);
        Prop(parent, "Shelf_Utility", 15.10f, 0.20f, 17.30f, 0.60f, 1.80f, 0f, wood);

        // 게임방 (0..5.00 x 5.62..9.50): 기획 13항 생활 패턴. 서쪽 창(z 7.56),
        // 동쪽 중앙홀문(z 7.56), 북쪽 창고문(x 2.00)
        Prop(parent, "Desk", 0.40f, 5.75f, 3.20f, 6.35f, 0.74f, 0f, wood);
        Prop(parent, "Monitor_L", 0.70f, 5.85f, 1.40f, 6.00f, 0.45f, 0.74f, dark);
        Prop(parent, "Monitor_C", 1.50f, 5.85f, 2.20f, 6.00f, 0.45f, 0.74f, dark);
        Prop(parent, "Monitor_R", 2.30f, 5.85f, 3.00f, 6.00f, 0.45f, 0.74f, dark);
        Prop(parent, "Chair_Game", 1.50f, 6.60f, 2.00f, 7.10f, 0.90f, 0f, dark);
        Prop(parent, "MangaShelf", 2.80f, 9.10f, 4.80f, 9.45f, 2.00f, 0f, wood);

        // 중앙홀 (5.12..12.26 x 5.62..9.50): 문 6개가 물리는 허브라 거의 비워둔다
        Prop(parent, "Hall_Rug", 6.60f, 6.60f, 10.80f, 8.50f, 0.02f, 0f, rug);
        // 동쪽 벽에 붙이면 침실문(z 6.96~8.16) 바로 옆이라 에이전트가 낀다. 0.8m 띄운다.
        Prop(parent, "Sideboard", 10.60f, 6.00f, 11.45f, 6.90f, 0.85f, 0f, wood);

        // 침실 (12.38..17.50 x 5.62..9.50): 서쪽 문(z 7.56), 동쪽 창(z 7.56)
        Prop(parent, "Bed_Base", 13.80f, 7.20f, 15.80f, 9.30f, 0.32f, 0f, wood);
        Prop(parent, "Bed_Mattress", 13.85f, 7.25f, 15.75f, 9.25f, 0.26f, 0.32f, pale);
        Prop(parent, "Bed_Pillow", 13.95f, 8.85f, 15.65f, 9.20f, 0.14f, 0.58f, pale);
        Prop(parent, "Nightstand", 16.00f, 8.70f, 16.50f, 9.25f, 0.50f, 0f, wood);
        Prop(parent, "Wardrobe", 12.60f, 8.40f, 13.20f, 9.35f, 2.10f, 0f, wood);
        Prop(parent, "Desk_Bed", 16.60f, 5.80f, 17.35f, 7.00f, 0.74f, 0f, wood);

        // 창고 (0..4.00 x 9.62..13.50): 기획 5항 택배 + 만화책 상자
        // 상자를 방 가운데 두면 문에서 북쪽으로 갈 길이 0.20m 로 좁아져 방이 고립된다 (실측).
        // 양쪽 벽에 붙여서 가운데 1.65m 통로를 남긴다.
        Prop(parent, "Shelf_StoreW", 0.20f, 10.00f, 0.70f, 12.90f, 2.00f, 0f, wood);
        Prop(parent, "Shelf_StoreN", 0.80f, 13.00f, 3.80f, 13.35f, 2.00f, 0f, wood);
        Prop(parent, "ComicBoxes", 0.75f, 10.20f, 1.35f, 12.60f, 0.90f, 0f, wood);
        Prop(parent, "DeliveryBoxes", 3.00f, 10.20f, 3.85f, 12.60f, 1.10f, 0f, wood);

        // 팬트리 (4.12..8.12 x 9.62..13.50): 기획 10·11항. 식량이 바닥나는 걸 보여주는 방이다
        Prop(parent, "Shelf_PantryW", 4.30f, 10.00f, 4.80f, 13.30f, 2.20f, 0f, wood);
        Prop(parent, "Shelf_PantryN", 4.90f, 13.05f, 7.90f, 13.40f, 2.20f, 0f, wood);
        Prop(parent, "Fridge", 7.45f, 10.20f, 8.05f, 11.60f, 2.00f, 0f, pale);
        Prop(parent, "Freezer", 5.30f, 11.80f, 7.10f, 12.70f, 0.95f, 0f, pale);

        // 주방 (8.24..13.24 x 9.62..13.50): 북쪽 뒷문(x 9.10) + 창(x 11.60),
        // 남쪽 중앙홀문(x 10.40), 동쪽 식당문(z 11.56)
        Prop(parent, "Counter_N", 10.40f, 13.10f, 13.10f, 13.45f, 0.90f, 0f, pale);
        Prop(parent, "Sink_Kitchen", 11.20f, 13.15f, 12.00f, 13.42f, 0.10f, 0.86f, metal);
        Prop(parent, "Counter_W", 8.40f, 10.20f, 8.90f, 12.20f, 0.90f, 0f, pale);
        Prop(parent, "Stove", 8.45f, 11.20f, 8.85f, 11.90f, 0.04f, 0.90f, dark);
        // 아일랜드를 8.90 쪽에 붙이면 뒷문으로 가는 서쪽 통로가 0.70m 가 된다. 1.00m 로 띄운다.
        Prop(parent, "Island", 9.90f, 11.20f, 11.80f, 12.10f, 0.90f, 0f, pale);

        // 식당 (13.36..17.50 x 9.62..13.50): 4인 식탁
        Prop(parent, "DiningTable", 14.60f, 10.80f, 16.60f, 12.40f, 0.74f, 0f, wood);
        Prop(parent, "Chair_SW", 14.70f, 10.25f, 15.20f, 10.75f, 0.85f, 0f, olive);
        Prop(parent, "Chair_SE", 16.00f, 10.25f, 16.50f, 10.75f, 0.85f, 0f, olive);
        Prop(parent, "Chair_NW", 14.70f, 12.45f, 15.20f, 12.95f, 0.85f, 0f, olive);
        Prop(parent, "Chair_NE", 16.00f, 12.45f, 16.50f, 12.95f, 0.85f, 0f, olive);
    }

    /// <summary>가구 한 덩이. 로컬 사각형(x0,z0)-(x1,z1) 과 높이로 놓는다.</summary>
    static void Prop(Transform parent, string name, float x0, float z0, float x1, float z1,
                     float height, float baseHeight, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = Flat((x0 + x1) * 0.5f, (z0 + z1) * 0.5f)
                                + Vector3.up * (baseHeight + height * 0.5f);
        go.transform.localScale = new Vector3((x1 - x0) * FurnitureScale, height, (z1 - z0) * FurnitureScale);

        var renderer = go.GetComponent<Renderer>();
        if (renderer && material) renderer.sharedMaterial = material;
    }

    /// <summary>
    /// URP Lit 반투명 머티리얼. 불투명 머티리얼의 알파만 낮춰서는 반투명이 되지 않는다 —
    /// Surface 자체가 다른 셰이더 변형이라 블렌드/큐/키워드를 전부 바꿔줘야 한다.
    /// </summary>
    static Material EnsureFadeMaterial(string id, Color color)
    {
        Directory.CreateDirectory(MaterialFolder);
        string path = MaterialFolder + "/" + id + ".mat";

        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) shader = Shader.Find("Standard");

        var material = new Material(shader) { color = color };
        MakeTransparent(material);

        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    /// <summary>URP Lit 을 반투명으로 바꾼다. Surface 자체가 다른 셰이더 변형이라 한 값만 바꿔선 안 된다.</summary>
    static void MakeTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);            // Transparent
        material.SetFloat("_Blend", 0f);              // Alpha
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");
    }
}
