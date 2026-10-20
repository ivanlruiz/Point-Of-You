using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace PointOfYou
{
    /// <summary>Código de mapa (puede ser largo: no entra en un FixedString).</summary>
    public struct MapCodePayload : INetworkSerializable, IEquatable<MapCodePayload>
    {
        public string Code;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            Code ??= "";
            serializer.SerializeValue(ref Code);
        }

        public bool Equals(MapCodePayload other) => (Code ?? "") == (other.Code ?? "");
    }

    /// <summary>
    /// Estado del nivel que el host replica a los clientes: qué mapa se juega, placas, puertas,
    /// checkpoint y meta. Lo spawnea el host al empezar (no hay NetworkObjects puestos en la escena).
    /// </summary>
    public class NetworkLevelState : NetworkBehaviour
    {
        readonly NetworkVariable<MapCodePayload> mapCode = new();
        readonly NetworkVariable<int> plateBits = new();
        readonly NetworkVariable<int> checkpointOrder = new();
        readonly NetworkVariable<float> completeElapsed = new(-1f);
        NetworkList<float> doorAmounts;

        PressurePlate[] plates = Array.Empty<PressurePlate>();
        Door[] doors = Array.Empty<Door>();
        int scannedVersion = -1;

        void Awake() => doorAmounts = new NetworkList<float>();

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                var runtime = MapRuntime.Instance;
                mapCode.Value = new MapCodePayload { Code = runtime && runtime.IsCustom ? runtime.Current.ToCode() : "" };
            }
            else
            {
                ApplyHostMap(mapCode.Value.Code);
                mapCode.OnValueChanged += (_, current) => ApplyHostMap(current.Code);
            }
            Scan();
        }

        /// <summary>En los clientes: arma el mismo mapa que el host (o vuelve al prototipo).</summary>
        static void ApplyHostMap(string code)
        {
            var runtime = MapRuntime.Instance;
            if (!runtime) return;
            if (string.IsNullOrEmpty(code))
            {
                if (runtime.IsCustom) runtime.Build(null);
                return;
            }
            if (runtime.IsCustom && runtime.Current.ToCode() == code) return;
            if (MapData.TryFromCode(code, out var map, out string error)) runtime.Build(map);
            else Debug.LogError($"[Online] El mapa del host no se pudo leer: {error}");
        }

        /// <summary>
        /// Busca placas y puertas en el mismo orden en todas las PCs. En un mapa del editor el orden lo da la lista de
        /// piezas (idéntica en todos); en el prototipo, la ruta en la jerarquía.
        /// </summary>
        void Scan()
        {
            scannedVersion = MapRuntime.BuildVersion;
            var runtime = MapRuntime.Instance;
            if (runtime && runtime.IsCustom)
            {
                plates = runtime.Plates.ToArray();
                doors = runtime.Doors.ToArray();
            }
            else
            {
                plates = FindObjectsByType<PressurePlate>(FindObjectsSortMode.None).OrderBy(p => Path(p.transform)).ToArray();
                doors = FindObjectsByType<Door>(FindObjectsSortMode.None).OrderBy(d => Path(d.transform)).ToArray();
            }

            if (IsServer && IsSpawned)
            {
                doorAmounts.Clear();
                foreach (var d in doors) doorAmounts.Add(d.OpenAmount);
            }
        }

        static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;

        void Update()
        {
            if (!IsSpawned) return;
            var gm = GameManager.Instance;
            if (!gm) return;
            if (scannedVersion != MapRuntime.BuildVersion) Scan();

            if (IsServer)
            {
                int bits = 0;
                for (int i = 0; i < plates.Length; i++)
                    if (plates[i] && plates[i].IsPressed) bits |= 1 << i;
                plateBits.Value = bits;

                for (int i = 0; i < doors.Length && i < doorAmounts.Count; i++)
                    if (doors[i] && Mathf.Abs(doorAmounts[i] - doors[i].OpenAmount) > 0.001f)
                        doorAmounts[i] = doors[i].OpenAmount;

                checkpointOrder.Value = gm.CurrentCheckpoint ? gm.CurrentCheckpoint.Order : 0;
                completeElapsed.Value = gm.LevelComplete ? gm.CompleteElapsed : -1f;
            }
            else
            {
                for (int i = 0; i < plates.Length; i++)
                    if (plates[i]) plates[i].SetReplicated((plateBits.Value & (1 << i)) != 0);
                for (int i = 0; i < doors.Length && i < doorAmounts.Count; i++)
                    if (doors[i]) doors[i].SetReplicated(doorAmounts[i]);
                gm.ApplyReplicatedLevel(checkpointOrder.Value, completeElapsed.Value);
            }
        }
    }
}
