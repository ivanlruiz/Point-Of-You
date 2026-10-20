using UnityEngine;

namespace PointOfYou
{
    /// <summary>Meta del nivel: se completa cuando TODOS los jugadores están adentro.</summary>
    [RequireComponent(typeof(BoxCollider))]
    public class GoalZone : MonoBehaviour
    {
        [SerializeField] Transform flag;

        BoxCollider area;
        Vector3 flagRestScale;

        void Awake()
        {
            area = GetComponent<BoxCollider>();
            area.isTrigger = true;
            if (flag) flagRestScale = flag.localScale;
        }

        /// <summary>Para piezas armadas en tiempo de juego (editor de mapas).</summary>
        public void Configure(Transform flagTransform)
        {
            flag = flagTransform;
            if (flag) flagRestScale = flag.localScale;
        }

        void Update()
        {
            var gm = GameManager.Instance;
            if (!gm) return;

            if (flag && gm.LevelComplete)
                flag.localScale = Vector3.Scale(flagRestScale, new Vector3(1f, 1f + 0.15f * Mathf.Sin(Time.time * 8f), 1f));

            if (gm.LevelComplete || gm.Players.Count == 0 || Net.IsReplica) return;
            var bounds = area.bounds;
            foreach (var p in gm.Players)
                if (!bounds.Contains(p.transform.position)) return;
            gm.CompleteLevel();
        }
    }
}
