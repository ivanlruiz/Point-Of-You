using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace PointOfYou
{
    /// <summary>
    /// Arma el nivel a partir de un <see cref="MapData"/> (o deja el prototipo de la escena si no hay mapa elegido).
    /// Lo usan el editor, el modo local y el online (los clientes reciben el código del mapa del host).
    /// </summary>
    [DefaultExecutionOrder(-50)] // después de GameManager.Awake y antes de cualquier Start
    public class MapRuntime : MonoBehaviour
    {
        public static readonly Color[] ChannelColors =
        {
            new(0.95f, 0.62f, 0.15f), // amarillo
            new(0.15f, 0.72f, 0.88f), // celeste
            new(0.90f, 0.30f, 0.75f), // magenta
            new(0.55f, 0.85f, 0.20f), // verde
        };
        public static readonly string[] ChannelNames = { "amarillo", "celeste", "magenta", "verde" };

        [Header("Prototipo de la escena")]
        [SerializeField] GameObject prototypeRoot;
        [SerializeField] Checkpoint prototypeStart;
        [SerializeField] NetworkObject networkBoxPrefab;

        [Header("Materiales")]
        [SerializeField] Material floorMaterial;
        [SerializeField] Material wallMaterial;
        [SerializeField] Material blockMaterial;
        [SerializeField] Material stoneMaterial;
        [SerializeField] Material boxMaterial;
        [SerializeField] Material goalMaterial;
        [SerializeField] Material doorMaterial;
        [SerializeField] Material plateMaterial;

        // Sobrevive a recargar la escena (F5) y a volver del online.
        static MapData selectedMap;

        GameObject customRoot;
        readonly List<PressurePlate> plates = new();
        readonly List<Door> doors = new();
        readonly Dictionary<string, Material> tinted = new();
        Bounds bounds;

        public static MapRuntime Instance { get; private set; }
        /// <summary>Mapa elegido para jugar (null = el prototipo de la escena).</summary>
        public static MapData SelectedMap => selectedMap;
        /// <summary>Cambia cada vez que se arma un nivel: quien guardó referencias a piezas debe volver a buscarlas.</summary>
        public static int BuildVersion { get; private set; }

        /// <summary>Mapa armado ahora (null = prototipo).</summary>
        public MapData Current { get; private set; }
        public bool IsCustom => Current != null;
        public string CurrentName => Current != null ? Current.name : "Prototipo";
        public IReadOnlyList<PressurePlate> Plates => plates;
        public IReadOnlyList<Door> Doors => doors;
        /// <summary>Puertas que no tienen ninguna placa de su color cerca (no se pueden abrir).</summary>
        public int UnlinkedDoorCount { get; private set; }

        /// <summary>
        /// Margen para considerar "igual de cerca" a otra puerta del mismo color (puertas de varias hojas,
        /// o una puerta con placas de los dos lados).
        /// </summary>
        const float LinkSlack = 4f;
        public Bounds LevelBounds => bounds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            selectedMap = null;
            BuildVersion = 0;
        }

        void Awake()
        {
            Instance = this;
            if (selectedMap != null) Build(selectedMap);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Elige el mapa con el que se juega (null = prototipo) y lo arma.</summary>
        public void Select(MapData map)
        {
            selectedMap = map;
            Build(map);
        }

        /// <summary>Arma el mapa. Con <paramref name="editing"/> las cajas quedan quietas (modo editor).</summary>
        public void Build(MapData map, bool editing = false)
        {
            if (customRoot)
            {
                customRoot.SetActive(false); // que las búsquedas de este frame ya no lo encuentren
                Destroy(customRoot);
                customRoot = null;
            }
            plates.Clear();
            doors.Clear();
            UnlinkedDoorCount = 0;
            Current = map;
            BuildVersion++;

            var gm = GameManager.Instance;
            if (map == null)
            {
                if (prototypeRoot) prototypeRoot.SetActive(true);
                bounds = new Bounds(new Vector3(0f, 0f, 26f), new Vector3(14f, 6f, 54f));
                if (gm) gm.SetLevel(prototypeStart, bounds);
                return;
            }

            if (prototypeRoot) prototypeRoot.SetActive(false);
            customRoot = new GameObject($"Mapa: {map.name}");

            Checkpoint start = null;
            var checkpoints = new List<Checkpoint>();
            var platesByChannel = new List<PressurePlate>[ChannelColors.Length];
            for (int i = 0; i < platesByChannel.Length; i++) platesByChannel[i] = new List<PressurePlate>();
            var pendingDoors = new List<(Door door, int channel)>();
            bounds = new Bounds();
            bool hasBounds = false;

            for (int i = 0; i < map.pieces.Count; i++)
            {
                var p = map.pieces[i];
                var f = p.Footprint;
                float h = p.BaseHeight;
                var min = new Vector3(p.x, h, p.z);
                var center = new Vector3(p.x + f.x * 0.5f, h, p.z + f.y * 0.5f);
                var pieceBounds = new Bounds(center, new Vector3(f.x, 1f, f.y));
                if (hasBounds) bounds.Encapsulate(pieceBounds);
                else { bounds = pieceBounds; hasBounds = true; }
                int channel = Mathf.Clamp(p.channel, 0, ChannelColors.Length - 1);
                string label = $"{i:000} {p.type}";

                switch (p.type)
                {
                    case PieceType.Floor:
                        Block(label, min + new Vector3(0, -0.5f, 0), new Vector3(f.x, 0.5f, f.y), floorMaterial);
                        break;
                    case PieceType.Stone:
                        Block(label, min + new Vector3(0, -0.5f, 0), new Vector3(f.x, 0.5f, f.y), stoneMaterial);
                        break;
                    case PieceType.Block:
                        Block(label, min, new Vector3(f.x, 1f, f.y), blockMaterial);
                        break;
                    case PieceType.Wall:
                        Block(label, min, new Vector3(f.x, 3f, f.y), wallMaterial);
                        break;
                    case PieceType.Window:
                        // Zócalo + dintel con el hueco en el medio: se ve a través pero no se pasa (1,3 m < 1,6 m del personaje).
                        var window = Group(label);
                        Block("Zócalo", min, new Vector3(f.x, 1f, f.y), wallMaterial, window);
                        Block("Dintel", min + new Vector3(0, 2.3f, 0), new Vector3(f.x, 0.7f, f.y), wallMaterial, window);
                        break;
                    case PieceType.Plate:
                        platesByChannel[channel].Add(MakePlate(label, center, channel));
                        break;
                    case PieceType.Door:
                        pendingDoors.Add((MakeDoor(label, center, p.rot, f, channel), channel));
                        break;
                    case PieceType.Box:
                        MakeBox(label, center + Vector3.up * 0.5f, editing);
                        break;
                    case PieceType.Start:
                    case PieceType.Checkpoint:
                        var cp = MakeCheckpoint(label, center, f, p.type == PieceType.Start, map.name);
                        if (p.type == PieceType.Start) { if (!start) start = cp; }
                        else checkpoints.Add(cp);
                        break;
                    case PieceType.Goal:
                        MakeGoal(label, center, f);
                        break;
                }
            }

            // Orden de los checkpoints: el de inicio es 0 y el resto según el orden en que se pusieron.
            for (int i = 0; i < checkpoints.Count; i++)
                checkpoints[i].Configure(i + 1, $"Checkpoint {i + 1} · {map.name}", new Vector3(0f, -0.6f, 0f), 1f);

            // Cada placa abre las puertas de su color que le quedan MÁS CERCA (con un margen para puertas de varias
            // hojas). Antes abría todas las de su color del mapa: con 4 colores repetidos en salas lejanas, una caja
            // olvidada sobre una placa de la sala 1 dejaba abierta una puerta de la sala 5.
            var linked = new Dictionary<Door, List<PressurePlate>>();
            foreach (var (door, _) in pendingDoors) linked[door] = new List<PressurePlate>();
            for (int channel = 0; channel < platesByChannel.Length; channel++)
            {
                var sameColor = pendingDoors.FindAll(d => d.channel == channel);
                if (sameColor.Count == 0) continue;
                foreach (var plate in platesByChannel[channel])
                {
                    float nearest = float.MaxValue;
                    foreach (var (door, _) in sameColor) nearest = Mathf.Min(nearest, LinkDistance(plate, door));
                    foreach (var (door, _) in sameColor)
                        if (LinkDistance(plate, door) <= nearest + LinkSlack) linked[door].Add(plate);
                }
            }
            UnlinkedDoorCount = 0;
            foreach (var (door, _) in pendingDoors)
            {
                door.Configure(linked[door].ToArray(), Vector3.down * 2.65f);
                if (linked[door].Count == 0) UnlinkedDoorCount++;
                doors.Add(door);
            }
            foreach (var list in platesByChannel) plates.AddRange(list);

            // En los clientes online las cajas son las del host (versión de red): se apagan las locales.
            if (Net.IsReplica)
                foreach (var r in customRoot.GetComponentsInChildren<NetworkReplacement>())
                    r.gameObject.SetActive(false);

            if (!start) Debug.LogWarning($"[Mapas] \"{map.name}\" no tiene Inicio: se aparece en el origen.");
            if (gm) gm.SetLevel(start, bounds);
        }

        // Placa (en el piso) contra la base de la puerta: la altura cuenta igual que la distancia horizontal.
        static float LinkDistance(PressurePlate plate, Door door) =>
            Vector3.Distance(plate.transform.position, door.transform.position + Vector3.down * 1.3f);

        // ---------------------------------------------------------------- piezas

        Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(customRoot.transform, false);
            return t;
        }

        GameObject Block(string name, Vector3 min, Vector3 size, Material material, Transform parent = null, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent ? parent : customRoot.transform, false);
            go.transform.position = min + size * 0.5f;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        GameObject Visual(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        Material Tint(Material source, Color color, string key)
        {
            if (tinted.TryGetValue(key, out var m) && m) return m;
            m = new Material(source) { name = key };
            color.a = 1f;
            m.SetColor("_BaseColor", color);
            tinted[key] = m;
            return m;
        }

        PressurePlate MakePlate(string name, Vector3 center, int channel)
        {
            var root = Group($"{name} (canal {ChannelNames[channel]})");
            root.position = center;
            Color c = ChannelColors[channel];
            var visual = Visual(PrimitiveType.Cube, "Placa", root, center + Vector3.up * 0.03f, new Vector3(1.6f, 0.06f, 1.6f),
                Tint(plateMaterial, c * 0.55f, $"placa{channel}"));
            var plate = root.gameObject.AddComponent<PressurePlate>();
            plate.Configure(visual.GetComponent<Renderer>(), c * 0.55f, c);
            return plate;
        }

        Door MakeDoor(string name, Vector3 center, int rot, Vector2Int footprint, int channel)
        {
            Vector3 size = rot % 2 == 0 ? new Vector3(footprint.x, 2.6f, 0.3f) : new Vector3(0.3f, 2.6f, footprint.y);
            var go = Block($"{name} (canal {ChannelNames[channel]})", center + new Vector3(-size.x * 0.5f, 0f, -size.z * 0.5f), size,
                Tint(doorMaterial, ChannelColors[channel] * 0.85f, $"puerta{channel}"));
            go.AddComponent<Rigidbody>().isKinematic = true;
            return go.AddComponent<Door>();
        }

        void MakeBox(string name, Vector3 center, bool editing)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(customRoot.transform, false);
            go.transform.position = center;
            go.GetComponent<Renderer>().sharedMaterial = boxMaterial;
            var body = go.AddComponent<Rigidbody>();
            body.mass = 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.isKinematic = editing;
            go.AddComponent<Respawnable>();
            if (networkBoxPrefab) go.AddComponent<NetworkReplacement>().Configure(networkBoxPrefab);
        }

        Checkpoint MakeCheckpoint(string name, Vector3 center, Vector2Int footprint, bool isStart, string mapName)
        {
            var root = Group(isStart ? $"{name} (inicio)" : name);
            root.position = center + Vector3.up * 1.5f;
            var area = root.gameObject.AddComponent<BoxCollider>();
            area.isTrigger = true;
            area.size = new Vector3(footprint.x, 3f, footprint.y);
            Color color = isStart ? new Color(0.35f, 0.75f, 1f) : new Color(0.3f, 0.95f, 0.85f);
            Visual(PrimitiveType.Cube, "Marca", root, center + Vector3.up * 0.015f, new Vector3(footprint.x - 0.2f, 0.03f, footprint.y - 0.2f),
                Tint(goalMaterial, color * 0.6f, isStart ? "inicio" : "control"));
            var cp = root.gameObject.AddComponent<Checkpoint>();
            if (isStart)
                cp.Configure(0, $"{mapName.ToUpperInvariant()} — Lleguen todos a la bandera.", new Vector3(0f, -0.6f, 0f), 1f);
            return cp;
        }

        void MakeGoal(string name, Vector3 center, Vector2Int footprint)
        {
            var root = Group(name);
            root.position = center + Vector3.up * 1.1f;
            var area = root.gameObject.AddComponent<BoxCollider>();
            area.isTrigger = true;
            area.size = new Vector3(footprint.x, 2.2f, footprint.y);
            Visual(PrimitiveType.Cube, "Marca", root, center + Vector3.up * 0.015f, new Vector3(footprint.x - 0.2f, 0.03f, footprint.y - 0.2f), goalMaterial);
            Vector3 corner = center + new Vector3(footprint.x * 0.5f - 0.4f, 0f, footprint.y * 0.5f - 0.4f);
            Visual(PrimitiveType.Cylinder, "Mástil", root, corner + Vector3.up * 1.25f, new Vector3(0.1f, 1.25f, 0.1f), wallMaterial);
            var flag = Visual(PrimitiveType.Cube, "Bandera", root, corner + new Vector3(-0.5f, 2.2f, 0f), new Vector3(1f, 0.6f, 0.05f), goalMaterial);
            root.gameObject.AddComponent<GoalZone>().Configure(flag.transform);
        }
    }
}
