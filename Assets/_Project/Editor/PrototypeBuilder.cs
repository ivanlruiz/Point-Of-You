using System;
using System.IO;
using System.Reflection;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PointOfYou.EditorTools
{
    /// <summary>
    /// Arma la sala gris del prototipo por código: materiales, prefabs (local y de red) y la escena.
    /// Correrlo de nuevo la regenera desde cero (los cambios hechos a mano en la escena se pierden).
    ///
    /// Desde el Editor: menú Point of You → Reconstruir escena prototipo.
    /// Por línea de comandos: unity run &lt;proyecto&gt; -- -executeMethod PointOfYou.EditorTools.PrototypeBuilder.Build
    /// </summary>
    public static class PrototypeBuilder
    {
        const string Root = "Assets/_Project";
        const string MaterialDir = Root + "/Materials";
        const string PrefabDir = Root + "/Prefabs";
        const string SceneDir = Root + "/Scenes";
        public const string ScenePath = SceneDir + "/Prototype.unity";
        const string PlayerPrefabPath = PrefabDir + "/Player.prefab";
        const string NetworkPlayerPrefabPath = PrefabDir + "/NetworkPlayer.prefab";
        const string NetworkBoxPrefabPath = PrefabDir + "/NetworkBox.prefab";
        const string LevelStatePrefabPath = PrefabDir + "/NetworkLevelState.prefab";

        static readonly Color DoorOneColor = new(0.95f, 0.62f, 0.15f);
        static readonly Color DoorTwoColor = new(0.15f, 0.72f, 0.88f);

        [MenuItem("Point of You/Reconstruir escena prototipo")]
        static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            EnsureFolder(MaterialDir);
            EnsureFolder(PrefabDir);
            EnsureFolder(SceneDir);

            var m = new Materials();
            BuildPlayerPrefab(m, PlayerPrefabPath, networked: false);
            BuildPlayerPrefab(m, NetworkPlayerPrefabPath, networked: true);
            BuildNetworkBoxPrefab(m);
            BuildLevelStatePrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // NewScene descarga assets sin uso: las referencias a los prefabs recién guardados quedan inválidas, hay que recargarlas.
            var playerPrefab = Load<PlayerCharacter>(PlayerPrefabPath);
            SetupLighting();

            var level = new GameObject("Nivel").transform;
            var start = BuildZoneStart(level, m, out var plateA);
            var plateB = BuildDoorOne(level, m, plateA);
            BuildZoneWindows(level, m, plateB);
            BuildZoneLedge(level, m, Load<NetworkObject>(NetworkBoxPrefabPath));

            var overview = new GameObject("Cámara general").AddComponent<Camera>();
            overview.transform.SetPositionAndRotation(new Vector3(-12f, 30f, 25f), Quaternion.identity);
            overview.transform.LookAt(new Vector3(0f, 0f, 26f));
            overview.fieldOfView = 60f;
            overview.clearFlags = CameraClearFlags.SolidColor;
            overview.backgroundColor = new Color(0.56f, 0.66f, 0.78f);
            overview.depth = -10f;

            var gm = new GameObject("GameManager").AddComponent<GameManager>();
            Set(gm, "playerPrefab", playerPrefab);
            Set(gm, "startCheckpoint", start);
            Set(gm, "overviewCamera", overview);
            var online = gm.gameObject.AddComponent<OnlineSession>();
            Set(online, "playerPrefab", Load<NetworkObject>(NetworkPlayerPrefabPath));
            Set(online, "levelStatePrefab", Load<NetworkObject>(LevelStatePrefabPath));

            // Mapas hechos por jugadores: el armador (reemplaza al prototipo cuando hay un mapa elegido) y el editor.
            var maps = new GameObject("Mapas");
            var mapRuntime = maps.AddComponent<MapRuntime>();
            Set(mapRuntime, "prototypeRoot", level.gameObject);
            Set(mapRuntime, "prototypeStart", start);
            Set(mapRuntime, "networkBoxPrefab", Load<NetworkObject>(NetworkBoxPrefabPath));
            Set(mapRuntime, "floorMaterial", m.Floor);
            Set(mapRuntime, "wallMaterial", m.Wall);
            Set(mapRuntime, "blockMaterial", m.Ledge);
            Set(mapRuntime, "stoneMaterial", m.Stone);
            Set(mapRuntime, "boxMaterial", m.Box);
            Set(mapRuntime, "goalMaterial", m.Goal);
            Set(mapRuntime, "doorMaterial", m.DoorOne);
            Set(mapRuntime, "plateMaterial", m.PlateOne);
            var mapEditor = maps.AddComponent<MapEditor>();
            Set(mapEditor, "cursorMaterial", m.Goal);
            Set(mapEditor, "gridMaterial", m.Wall);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[PointOfYou] Escena prototipo construida en {ScenePath}");
        }

        // ---------------------------------------------------------------- zonas

        // Zona 0: inicio. Los jugadores aparecen en ronda mirándose.
        static Checkpoint BuildZoneStart(Transform level, Materials m, out PressurePlate plateA)
        {
            var zone = Group("Z0 Inicio", level);
            Block("Piso", zone, new Vector3(-6f, -0.5f, 0f), new Vector3(6f, 0f, 12f), m.Floor);
            Block("Pared fondo", zone, new Vector3(-6.5f, 0f, -0.5f), new Vector3(6.5f, 4f, 0f), m.Wall);
            Block("Pared izq", zone, new Vector3(-6.5f, 0f, 0f), new Vector3(-6f, 4f, 12f), m.Wall);
            Block("Pared der", zone, new Vector3(6f, 0f, 0f), new Vector3(6.5f, 4f, 12f), m.Wall);

            plateA = Plate("Placa A", zone, new Vector3(-3.5f, 0f, 9f), m.PlateOne, DoorOneColor);

            return MakeCheckpoint("Checkpoint 0 (inicio)", zone, 0,
                new Vector3(-6f, 0f, 0f), new Vector3(6f, 3f, 12f), new Vector3(0f, 0f, 5f),
                "1 · LA PUERTA — Las placas amarillas abren la puerta mientras alguien las pisa. Hay una de cada lado.");
        }

        // Pared con la puerta 1 (placa A de este lado, placa B del otro).
        static PressurePlate BuildDoorOne(Transform level, Materials m, PressurePlate plateA)
        {
            var zone = Group("Puerta 1", level);
            Block("Pared izq", zone, new Vector3(-6.5f, 0f, 12f), new Vector3(-1.25f, 4f, 12.5f), m.Wall);
            Block("Pared der", zone, new Vector3(1.25f, 0f, 12f), new Vector3(6.5f, 4f, 12.5f), m.Wall);
            Block("Dintel", zone, new Vector3(-1.25f, 2.6f, 12f), new Vector3(1.25f, 4f, 12.5f), m.Wall);

            var plateB = Plate("Placa B", zone, new Vector3(3.5f, 0f, 14.2f), m.PlateOne, DoorOneColor);
            MakeDoor("Puerta 1", zone, new Vector3(-1.25f, 0f, 12.1f), new Vector3(1.25f, 2.6f, 12.4f), m.DoorOne,
                new[] { plateA, plateB }, new Vector3(0f, -2.65f, 0f));
            return plateB;
        }

        // Zona 2: el foso con piedras. Uno cruza saltando; los demás lo miran por las ventanas
        // desde el pasillo seguro. La placa C, al otro lado, abre la puerta del pasillo.
        static void BuildZoneWindows(Transform level, Materials m, PressurePlate plateB)
        {
            var zone = Group("Z2 Ventanas", level);

            Block("Piso entrada", zone, new Vector3(-6f, -0.5f, 12f), new Vector3(6f, 0f, 16f), m.Floor);
            Block("Pared izq", zone, new Vector3(-6.5f, -6f, 12.5f), new Vector3(-6f, 4f, 36f), m.Wall);
            Block("Pared der", zone, new Vector3(6f, 0f, 12.5f), new Vector3(6.5f, 4f, 36f), m.Wall);

            // Foso (x -6..1.5, z 16..30.5): sin piso. Paredes interiores para que no se vea "por abajo".
            Block("Foso frente", zone, new Vector3(-6f, -6f, 15.5f), new Vector3(1.5f, -0.5f, 16f), m.Wall);
            Block("Foso fondo", zone, new Vector3(-6f, -6f, 30.5f), new Vector3(1.5f, -0.5f, 31f), m.Wall);
            Block("Abismo", zone, new Vector3(-6f, -8.5f, 15.5f), new Vector3(1.5f, -8f, 31f), m.Abyss, collider: false);

            var stones = Group("Piedras", zone);
            Vector2[] stoneCenters =
            {
                new(-1.0f, 17.8f), new(-3.5f, 19.6f), new(-1.5f, 22.0f),
                new(-3.8f, 24.2f), new(-1.4f, 26.4f), new(-3.6f, 28.6f),
            };
            for (int i = 0; i < stoneCenters.Length; i++)
            {
                var c = stoneCenters[i];
                Block($"Piedra {i + 1}", stones, new Vector3(c.x - 0.75f, -0.5f, c.y - 0.75f), new Vector3(c.x + 0.75f, 0f, c.y + 0.75f), m.Stone);
            }

            // Pasillo seguro (x 2..6) separado del foso por un muro con ventanas.
            Block("Piso pasillo", zone, new Vector3(2f, -0.5f, 16f), new Vector3(6f, 0f, 32.5f), m.Floor);
            var divider = Group("Muro con ventanas", zone);
            Block("Zócalo", divider, new Vector3(1.5f, -6f, 16f), new Vector3(2f, 1f, 32.5f), m.Wall);
            Block("Viga", divider, new Vector3(1.5f, 2.3f, 16f), new Vector3(2f, 4f, 32.5f), m.Wall);
            float[,] posts = { { 16f, 17.8f }, { 20.2f, 22.8f }, { 25.2f, 27.8f }, { 30.2f, 32.5f } };
            for (int i = 0; i < posts.GetLength(0); i++)
                Block($"Columna {i + 1}", divider, new Vector3(1.5f, 1f, posts[i, 0]), new Vector3(2f, 2.3f, posts[i, 1]), m.Wall);

            // Llegada.
            Block("Piso llegada foso", zone, new Vector3(-6f, -0.5f, 30.5f), new Vector3(1.5f, 0f, 32.5f), m.Floor);
            Block("Piso llegada", zone, new Vector3(-6f, -0.5f, 32.5f), new Vector3(6f, 0f, 36f), m.Floor);

            var plateC = Plate("Placa C", zone, new Vector3(-3f, 0f, 34.2f), m.PlateTwo, DoorTwoColor);
            MakeDoor("Puerta 2 (pasillo)", zone, new Vector3(2f, 0f, 32.35f), new Vector3(6f, 3f, 32.65f), m.DoorTwo,
                new[] { plateC }, new Vector3(0f, -3.05f, 0f));

            MakeCheckpoint("Checkpoint 1 (foso)", zone, 1,
                new Vector3(-6f, 0f, 12.5f), new Vector3(6f, 3f, 16f), new Vector3(0f, 0f, 14.25f),
                "2 · LAS VENTANAS — Uno cruza saltando por las piedras; el resto lo acompaña por el pasillo y lo mira por las ventanas. La placa celeste del otro lado abre el pasillo.");
            MakeCheckpoint("Checkpoint 2 (llegada)", zone, 2,
                new Vector3(-6f, 0f, 32.5f), new Vector3(6f, 3f, 36f), new Vector3(0f, 0f, 34.25f),
                "¡Llegaste! Quedate sobre la placa celeste hasta que pasen todos por el pasillo.");
        }

        // Zona 3: cornisa alta. Hace falta la caja para subir. Desde abajo no se ve la cornisa,
        // así que quien sube depende de que su observador también busque altura.
        static void BuildZoneLedge(Transform level, Materials m, NetworkObject networkBoxPrefab)
        {
            var zone = Group("Z3 Cornisa", level);
            Block("Piso", zone, new Vector3(-6f, -0.5f, 36f), new Vector3(6f, 0f, 52f), m.Floor);
            Block("Pared izq", zone, new Vector3(-6.5f, 0f, 36f), new Vector3(-6f, 5f, 52f), m.Wall);
            Block("Pared der", zone, new Vector3(6f, 0f, 36f), new Vector3(6.5f, 5f, 52f), m.Wall);
            Block("Pared fondo", zone, new Vector3(-6.5f, 0f, 52f), new Vector3(6.5f, 5f, 52.5f), m.Wall);
            Block("Cornisa", zone, new Vector3(-6f, 0f, 46f), new Vector3(6f, 2.1f, 52f), m.Ledge);

            var box = MakeBox(m);
            box.transform.SetParent(zone, false);
            box.transform.position = new Vector3(3f, 0.5f, 40f);
            Set(box.AddComponent<NetworkReplacement>(), "prefab", networkBoxPrefab);

            var goal = new GameObject("Meta");
            goal.transform.SetParent(zone, false);
            goal.transform.position = new Vector3(0f, 3.2f, 50f);
            var goalArea = goal.AddComponent<BoxCollider>();
            goalArea.isTrigger = true;
            goalArea.size = new Vector3(12f, 2.2f, 4f);
            Visual(PrimitiveType.Cylinder, "Mástil", goal.transform, new Vector3(4.5f, 0.15f, 0.5f), new Vector3(0.1f, 1.25f, 0.1f), m.Wall);
            var flag = Visual(PrimitiveType.Cube, "Bandera", goal.transform, new Vector3(5f, 1.1f, 0.5f), new Vector3(1f, 0.6f, 0.05f), m.Goal);
            goal.AddComponent<GoalZone>();
            Set(goal.GetComponent<GoalZone>(), "flag", flag.transform);

            MakeCheckpoint("Checkpoint 3 (cornisa)", zone, 3,
                new Vector3(-6f, 0f, 36f), new Vector3(6f, 3f, 46f), new Vector3(0f, 0f, 38.5f),
                "3 · LA CORNISA — Lleguen todos arriba, a la bandera. Usen la caja (mantener agarrar para arrastrarla). Ojo: desde abajo no se ve lo que pasa arriba.");
        }

        // ---------------------------------------------------------------- jugador

        static void BuildPlayerPrefab(Materials m, string path, bool networked)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            if (networked) root.AddComponent<NetworkObject>();
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.angularDamping = 0.05f;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.6f;
            capsule.radius = 0.35f;
            capsule.center = Vector3.zero;
            capsule.sharedMaterial = m.PlayerPhysics;

            var player = root.AddComponent<PlayerCharacter>();

            var body = Visual(PrimitiveType.Capsule, "Cuerpo", root.transform, Vector3.zero, new Vector3(0.7f, 0.8f, 0.7f), m.PlayerBase);
            Visual(PrimitiveType.Cube, "Pechera", root.transform, new Vector3(0f, 0.2f, 0.32f), new Vector3(0.3f, 0.22f, 0.08f), m.Badge);

            var head = new GameObject("Cabeza").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1f, 0f);
            var headMesh = Visual(PrimitiveType.Sphere, "Cabeza malla", head, Vector3.zero, Vector3.one * 0.56f, m.PlayerBase);
            var eyeL = Visual(PrimitiveType.Sphere, "Ojo izq", head, new Vector3(-0.1f, 0.05f, 0.24f), Vector3.one * 0.1f, m.Eye);
            var eyeR = Visual(PrimitiveType.Sphere, "Ojo der", head, new Vector3(0.1f, 0.05f, 0.24f), Vector3.one * 0.1f, m.Eye);

            var chest = new GameObject("Pecho").transform;
            chest.SetParent(root.transform, false);
            chest.localPosition = new Vector3(0f, 0.3f, 0f);

            var hand = new GameObject("Mano").transform;
            hand.SetParent(root.transform, false);
            hand.localPosition = new Vector3(0f, 0.15f, 0.6f);
            var handMesh = Visual(PrimitiveType.Sphere, "Mano malla", hand, Vector3.zero, Vector3.one * 0.2f, m.PlayerBase);

            Set(player, "head", head);
            Set(player, "chest", chest);
            Set(player, "hand", hand);
            Set(player, "tintRenderers", new Object[] { body.GetComponent<Renderer>(), handMesh.GetComponent<Renderer>() });
            Set(player, "lightTintRenderers", new Object[] { headMesh.GetComponent<Renderer>() });
            Set(player, "headRenderers", new Object[] { headMesh.GetComponent<Renderer>(), eyeL.GetComponent<Renderer>(), eyeR.GetComponent<Renderer>() });
            Set(player, "handRenderer", handMesh.GetComponent<Renderer>());

            if (networked)
            {
                // El host simula; los clientes reciben posición y giro (solo en Y: el cuerpo nunca se inclina).
                var nt = root.AddComponent<NetworkTransform>();
                nt.SyncRotAngleX = false;
                nt.SyncRotAngleZ = false;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                root.AddComponent<NetworkRigidbody>();
                root.AddComponent<NetworkPlayer>();
            }

            SavePrefab(root, path, networked);
        }

        static GameObject MakeBox(Materials m)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Caja";
            box.GetComponent<Renderer>().sharedMaterial = m.Box;
            var body = box.AddComponent<Rigidbody>();
            body.mass = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            box.AddComponent<Respawnable>();
            return box;
        }

        static void BuildNetworkBoxPrefab(Materials m)
        {
            var box = MakeBox(m);
            box.name = "NetworkBox";
            box.AddComponent<NetworkObject>();
            var nt = box.AddComponent<NetworkTransform>();
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            box.AddComponent<NetworkRigidbody>();
            SavePrefab(box, NetworkBoxPrefabPath, networked: true);
        }

        static void BuildLevelStatePrefab()
        {
            var go = new GameObject("NetworkLevelState");
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkLevelState>();
            SavePrefab(go, LevelStatePrefabPath, networked: true);
        }

        static void SavePrefab(GameObject root, string path, bool networked)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            if (networked) AssignNetworkHash(path);
        }

        /// <summary>
        /// Netcode identifica los prefabs de red por un hash que genera al validar el asset en el Editor.
        /// Creado por script ese paso no siempre corre: lo forzamos y verificamos.
        /// </summary>
        static void AssignNetworkHash(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var networkObject = asset.GetComponent<NetworkObject>();
            typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(networkObject, null);
            uint hash = (uint)typeof(NetworkObject).GetField("GlobalObjectIdHash", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(networkObject);
            if (hash == 0) throw new InvalidOperationException($"{path}: Netcode no generó el GlobalObjectIdHash");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        static T Load<T>(string path) where T : Component => AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<T>();

        // ---------------------------------------------------------------- piezas

        static PressurePlate Plate(string name, Transform parent, Vector3 floorPosition, Material material, Color linkColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = floorPosition;
            var visual = Visual(PrimitiveType.Cube, "Placa", go.transform, new Vector3(0f, 0.03f, 0f), new Vector3(1.6f, 0.06f, 1.6f), material);
            var plate = go.AddComponent<PressurePlate>();
            Set(plate, "plateRenderer", visual.GetComponent<Renderer>());
            Set(plate, "idleColor", linkColor * 0.55f);
            Set(plate, "pressedColor", linkColor);
            return plate;
        }

        static Door MakeDoor(string name, Transform parent, Vector3 min, Vector3 max, Material material, PressurePlate[] plates, Vector3 openOffset)
        {
            var go = Block(name, parent, min, max, material, isStatic: false);
            go.AddComponent<Rigidbody>().isKinematic = true;
            var door = go.AddComponent<Door>();
            Set(door, "plates", plates);
            Set(door, "openOffset", openOffset);
            return door;
        }

        static Checkpoint MakeCheckpoint(string name, Transform parent, int order, Vector3 min, Vector3 max, Vector3 spawnFloorCenter, string objective)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = (min + max) * 0.5f;
            var area = go.AddComponent<BoxCollider>();
            area.isTrigger = true;
            area.size = max - min;
            var cp = go.AddComponent<Checkpoint>();
            Set(cp, "order", order);
            Set(cp, "objective", objective);
            Set(cp, "spawnCenter", spawnFloorCenter + Vector3.up * 0.9f - go.transform.position);
            return cp;
        }

        static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        /// <summary>Caja de geometría definida por sus esquinas (más fácil de leer que centro + tamaño).</summary>
        static GameObject Block(string name, Transform parent, Vector3 min, Vector3 max, Material material, bool collider = true, bool isStatic = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = (min + max) * 0.5f;
            go.transform.localScale = max - min;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.isStatic = isStatic;
            return go;
        }

        /// <summary>Primitiva solo visual (sin collider).</summary>
        static GameObject Visual(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static void SetupLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.48f, 0.52f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.24f);
            RenderSettings.fog = false;

            var sun = new GameObject("Sol").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(58f, -30f, 0f);
            RenderSettings.sun = sun;
        }

        // ---------------------------------------------------------------- utilidades

        static void Set(Object target, string property, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property) ?? throw new ArgumentException($"{target.GetType().Name} no tiene el campo '{property}'");
            switch (value)
            {
                case Object[] array:
                    p.arraySize = array.Length;
                    for (int i = 0; i < array.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = array[i];
                    break;
                case Object o: p.objectReferenceValue = o; break;
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case string s: p.stringValue = s; break;
                case Color c: p.colorValue = c; break;
                case Vector3 v: p.vector3Value = v; break;
                default: throw new ArgumentException($"Tipo no soportado: {value?.GetType()}");
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        sealed class Materials
        {
            public readonly Material Floor = Lit("Piso", new Color(0.64f, 0.65f, 0.68f));
            public readonly Material Wall = Lit("Pared", new Color(0.47f, 0.49f, 0.54f));
            public readonly Material Ledge = Lit("Cornisa", new Color(0.56f, 0.58f, 0.62f));
            public readonly Material Stone = Lit("Piedra", new Color(0.36f, 0.5f, 0.64f));
            public readonly Material Abyss = Lit("Abismo", new Color(0.07f, 0.08f, 0.11f));
            public readonly Material Box = Lit("Caja", new Color(0.62f, 0.43f, 0.25f));
            public readonly Material Goal = Lit("Meta", new Color(0.2f, 0.85f, 0.35f), emission: new Color(0.1f, 0.45f, 0.18f));
            public readonly Material DoorOne = Lit("Puerta 1", DoorOneColor * 0.85f);
            public readonly Material DoorTwo = Lit("Puerta 2", DoorTwoColor * 0.85f);
            public readonly Material PlateOne = Lit("Placa 1", DoorOneColor * 0.55f, emission: Color.black);
            public readonly Material PlateTwo = Lit("Placa 2", DoorTwoColor * 0.55f, emission: Color.black);
            public readonly Material PlayerBase = Lit("Jugador", Color.white, smoothness: 0.35f);
            public readonly Material Badge = Lit("Pechera", new Color(0.95f, 0.95f, 0.95f), smoothness: 0.5f);
            public readonly Material Eye = Lit("Ojo", new Color(0.04f, 0.04f, 0.05f), smoothness: 0.8f);
            public readonly PhysicsMaterial PlayerPhysics = PhysicsMat("Jugador (física)");

            static Material Lit(string name, Color color, float smoothness = 0.15f, Color? emission = null)
            {
                string path = $"{MaterialDir}/{name}.mat";
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!mat)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                color.a = 1f;
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                if (emission.HasValue)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emission.Value);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static PhysicsMaterial PhysicsMat(string name)
            {
                string path = $"{MaterialDir}/{name}.asset";
                var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
                if (!mat)
                {
                    mat = new PhysicsMaterial(name);
                    AssetDatabase.CreateAsset(mat, path);
                }
                // Sin fricción: el personaje no se "pega" a las paredes; el frenado lo hace el controlador.
                mat.staticFriction = 0f;
                mat.dynamicFriction = 0f;
                mat.frictionCombine = PhysicsMaterialCombine.Minimum;
                mat.bounciness = 0f;
                mat.bounceCombine = PhysicsMaterialCombine.Minimum;
                EditorUtility.SetDirty(mat);
                return mat;
            }
        }
    }
}
