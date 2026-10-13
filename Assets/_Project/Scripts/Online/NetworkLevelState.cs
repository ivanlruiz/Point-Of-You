using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace PointOfYou
{
    /// <summary>
    /// Estado del nivel que el host replica a los clientes: placas, puertas, checkpoint y meta.
    /// Lo spawnea el host al empezar (no hay NetworkObjects puestos en la escena).
    /// </summary>
    public class NetworkLevelState : NetworkBehaviour
    {
        readonly NetworkVariable<int> plateBits = new();
        readonly NetworkVariable<int> checkpointOrder = new();
        readonly NetworkVariable<float> completeElapsed = new(-1f);
        NetworkList<float> doorAmounts;

        PressurePlate[] plates;
        Door[] doors;

        void Awake()
        {
            doorAmounts = new NetworkList<float>();
            // Mismo orden en todas las PCs: por ruta en la jerarquía.
            plates = FindObjectsByType<PressurePlate>(FindObjectsSortMode.None).OrderBy(p => Path(p.transform)).ToArray();
            doors = FindObjectsByType<Door>(FindObjectsSortMode.None).OrderBy(d => Path(d.transform)).ToArray();
        }

        static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            foreach (var d in doors) doorAmounts.Add(d.OpenAmount);
        }

        void Update()
        {
            if (!IsSpawned) return;
            var gm = GameManager.Instance;
            if (!gm) return;

            if (IsServer)
            {
                int bits = 0;
                for (int i = 0; i < plates.Length; i++)
                    if (plates[i].IsPressed) bits |= 1 << i;
                plateBits.Value = bits;

                for (int i = 0; i < doors.Length && i < doorAmounts.Count; i++)
                    if (Mathf.Abs(doorAmounts[i] - doors[i].OpenAmount) > 0.001f)
                        doorAmounts[i] = doors[i].OpenAmount;

                checkpointOrder.Value = gm.CurrentCheckpoint ? gm.CurrentCheckpoint.Order : 0;
                completeElapsed.Value = gm.LevelComplete ? gm.CompleteElapsed : -1f;
            }
            else
            {
                for (int i = 0; i < plates.Length; i++)
                    plates[i].SetReplicated((plateBits.Value & (1 << i)) != 0);
                for (int i = 0; i < doors.Length && i < doorAmounts.Count; i++)
                    doors[i].SetReplicated(doorAmounts[i]);
                gm.ApplyReplicatedLevel(checkpointOrder.Value, completeElapsed.Value);
            }
        }
    }
}
