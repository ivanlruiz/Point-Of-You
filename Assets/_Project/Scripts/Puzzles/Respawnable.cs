using UnityEngine;

namespace PointOfYou
{
    /// <summary>Objeto físico (ej. una caja) que vuelve a su lugar si se cae al vacío.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Respawnable : MonoBehaviour
    {
        Rigidbody rb;
        Vector3 startPosition;
        Quaternion startRotation;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        void FixedUpdate()
        {
            if (Net.IsReplica) return;
            float killHeight = GameManager.Instance ? GameManager.Instance.KillHeight : -6f;
            if (transform.position.y < killHeight) ResetToStart();
        }

        public void ResetToStart()
        {
            if (GameManager.Instance) GameManager.Instance.ReleaseGrabsOn(rb);
            rb.position = startPosition;
            rb.rotation = startRotation;
            transform.SetPositionAndRotation(startPosition, startRotation);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            // Online: que los clientes la vean aparecer, no volar desde el abismo.
            var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (nt && nt.IsSpawned && nt.IsServer) nt.Teleport(startPosition, startRotation, transform.localScale);
        }
    }
}
