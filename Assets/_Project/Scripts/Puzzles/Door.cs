using UnityEngine;

namespace PointOfYou
{
    /// <summary>
    /// Puerta cinemática que se abre mientras sus placas estén presionadas.
    /// No se cierra sobre un cuerpo físico (no aplasta jugadores).
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public class Door : MonoBehaviour
    {
        public enum Logic { Any, All }

        [SerializeField] PressurePlate[] plates;
        [SerializeField] Logic logic = Logic.Any;
        [SerializeField] Vector3 openOffset = new(0f, -2.7f, 0f);
        [SerializeField] float openSpeed = 4f;
        [SerializeField] float closeSpeed = 2f;

        readonly Collider[] overlap = new Collider[8];
        Rigidbody rb;
        BoxCollider box;
        Vector3 closedPosition;

        public float OpenAmount { get; private set; }
        public bool IsOpen => OpenAmount > 0.98f;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            box = GetComponent<BoxCollider>();
            closedPosition = transform.position;
        }

        bool ShouldOpen()
        {
            if (plates == null || plates.Length == 0) return false;
            bool any = false, all = true;
            foreach (var p in plates)
            {
                if (!p) continue;
                any |= p.IsPressed;
                all &= p.IsPressed;
            }
            return logic == Logic.Any ? any : all;
        }

        void FixedUpdate()
        {
            float target = ShouldOpen() ? 1f : 0f;
            float distance = Mathf.Max(openOffset.magnitude, 0.01f);
            float speed = target > OpenAmount ? openSpeed : closeSpeed;
            float next = Mathf.MoveTowards(OpenAmount, target, speed / distance * Time.fixedDeltaTime);
            if (next < OpenAmount && IsBlocked(closedPosition + openOffset * next)) return;
            OpenAmount = next;
            rb.MovePosition(closedPosition + openOffset * OpenAmount);
        }

        bool IsBlocked(Vector3 position)
        {
            Vector3 scale = transform.lossyScale;
            Vector3 center = position + transform.rotation * Vector3.Scale(box.center, scale);
            Vector3 half = Vector3.Scale(box.size, scale) * 0.5f + Vector3.one * 0.02f;
            int n = Physics.OverlapBoxNonAlloc(center, half, overlap, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var other = overlap[i].attachedRigidbody;
                if (other && other != rb && !other.isKinematic) return true;
            }
            return false;
        }
    }
}
