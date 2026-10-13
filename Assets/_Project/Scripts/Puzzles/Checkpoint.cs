using UnityEngine;

namespace PointOfYou
{
    /// <summary>
    /// Zona de guardado. Cuando un jugador entra, el equipo reaparece acá.
    /// Los puntos de aparición forman una ronda mirando al centro, así todos se ven al empezar.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class Checkpoint : MonoBehaviour
    {
        static readonly float[] SpawnAngles = { 180f, 0f, 270f, 90f };

        [SerializeField] int order;
        [SerializeField, TextArea] string objective;
        [Tooltip("Centro de la ronda de aparición, en espacio local (a la altura del centro del personaje).")]
        [SerializeField] Vector3 spawnCenter;
        [SerializeField] float spawnRadius = 1.4f;

        BoxCollider area;

        public int Order => order;
        public string Objective => objective;

        void Awake()
        {
            area = GetComponent<BoxCollider>();
            area.isTrigger = true;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (!gm) return;
            var bounds = area.bounds;
            foreach (var p in gm.Players)
            {
                if (!bounds.Contains(p.transform.position)) continue;
                gm.ReachCheckpoint(this);
                return;
            }
        }

        public void GetSpawn(int index, out Vector3 position, out Quaternion rotation)
        {
            Vector3 center = transform.TransformPoint(spawnCenter);
            position = center + Quaternion.Euler(0f, SpawnAngles[index % SpawnAngles.Length], 0f) * Vector3.forward * spawnRadius;
            rotation = Quaternion.LookRotation(center - position, Vector3.up);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            for (int i = 0; i < SpawnAngles.Length; i++)
            {
                GetSpawn(i, out var pos, out var rot);
                Gizmos.DrawWireSphere(pos, 0.35f);
                Gizmos.DrawLine(pos, pos + rot * Vector3.forward * 0.6f);
            }
        }
    }
}
