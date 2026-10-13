using Unity.Netcode;
using UnityEngine;

namespace PointOfYou
{
    /// <summary>
    /// Objeto de escena (ej. la caja) que online se reemplaza por una versión de red:
    /// el host spawnea el prefab en este lugar y todas las PCs apagan el original.
    /// </summary>
    public class NetworkReplacement : MonoBehaviour
    {
        [SerializeField] NetworkObject prefab;

        public NetworkObject Prefab => prefab;
    }
}
