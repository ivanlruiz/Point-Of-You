using UnityEngine;

namespace PointOfYou
{
    /// <summary>
    /// Personaje físico (cápsula con Rigidbody): moverse, saltar y agarrar cosas u otros jugadores.
    /// El movimiento es relativo a la cámara del jugador, que está en la cabeza de OTRO personaje.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public class PlayerCharacter : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Partes")]
        [SerializeField] Transform head;
        [SerializeField] Transform chest;
        [SerializeField] Transform hand;
        [Tooltip("Se tiñen con el color del jugador.")]
        [SerializeField] Renderer[] tintRenderers;
        [Tooltip("Se tiñen con una versión clara del color del jugador.")]
        [SerializeField] Renderer[] lightTintRenderers;
        [Tooltip("Se ocultan en la cámara que está dentro de esta cabeza.")]
        [SerializeField] Renderer[] headRenderers;
        [SerializeField] Renderer handRenderer;

        [Header("Movimiento")]
        [SerializeField] float moveSpeed = 4.5f;
        [SerializeField] float groundAcceleration = 45f;
        [SerializeField] float airAcceleration = 14f;
        [SerializeField] float turnSpeed = 600f;
        [SerializeField] float tankTurnSpeed = 160f;
        [SerializeField] float jumpHeight = 1.4f;
        [SerializeField] float fallGravityMultiplier = 1.7f;
        [SerializeField] float coyoteTime = 0.1f;
        [SerializeField] float jumpBufferTime = 0.15f;

        [Header("Suelo")]
        [SerializeField] float groundProbeRadius = 0.3f;
        [SerializeField] float groundSkin = 0.1f;
        [SerializeField] float maxGroundSlope = 55f;

        [Header("Agarre")]
        [SerializeField] float grabRadius = 0.45f;
        [SerializeField] float grabSpring = 400f;
        [SerializeField] float grabDamper = 25f;
        [SerializeField] float grabBreakForce = 900f;

        Rigidbody rb;
        CapsuleCollider capsule;
        SpringJoint grabJoint;
        Rigidbody grabbedBody;
        float lastGroundedTime = float.NegativeInfinity;
        float jumpBufferedUntil = float.NegativeInfinity;
        float jumpLockedUntil;
        bool remoteGrabbing;
        Vector3 remoteGrabPoint;
        readonly RaycastHit[] groundHits = new RaycastHit[8];
        readonly Collider[] grabOverlap = new Collider[16];

        /// <summary>
        /// Si este personaje corre su física acá. Offline siempre; online solo en el host
        /// (en los clientes es una copia que mueve la red).
        /// </summary>
        public bool Simulate { get; set; } = true;

        public int Index { get; private set; }
        public Color Color { get; private set; } = Color.white;
        public PlayerInputSource InputSource { get; private set; }
        public RingCamera View { get; private set; }
        /// <summary>El jugador al que mira esta cabeza (es decir, la pantalla que se ve desde acá).</summary>
        public PlayerCharacter LookTarget { get; set; }

        public Rigidbody Body => rb;
        public Transform Head => head;
        public Vector3 EyePosition => head.position;
        public Vector3 ChestPosition => chest.position;
        public Renderer[] HeadRenderers => headRenderers;
        public bool IsGrounded { get; private set; }
        public bool IsGrabbing => grabJoint != null;
        public Rigidbody GrabbedBody => grabbedBody;
        public Vector3 GrabPoint => IsGrabbing && grabbedBody
            ? grabbedBody.transform.TransformPoint(grabJoint.connectedAnchor)
            : hand.position;
        public string DisplayName => $"J{Index + 1}";

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            if (handRenderer) handRenderer.enabled = false;
        }

        public void Initialize(int index, Color color, PlayerInputSource input, RingCamera view)
        {
            Index = index;
            Color = color;
            InputSource = input;
            View = view;

            foreach (var r in tintRenderers)
                if (r) r.material.SetColor(BaseColorId, color);
            var light = Color.Lerp(color, Color.white, 0.55f);
            foreach (var r in lightTintRenderers)
                if (r) r.material.SetColor(BaseColorId, light);

            if (view) view.Init(this);
        }

        /// <summary>Estado de agarre que llega por red (para dibujar la mano en los clientes).</summary>
        public void SetRemoteGrab(bool grabbing, Vector3 point)
        {
            remoteGrabbing = grabbing;
            remoteGrabPoint = point;
        }

        void Update()
        {
            if (InputSource == null || !Simulate) return;

            if (InputSource.JumpPressed)
                jumpBufferedUntil = Time.time + jumpBufferTime;

            if (InputSource.ResetPressed && GameManager.Instance)
                GameManager.Instance.Respawn(this);

            bool grabHeld = InputSource.GrabHeld;
            if (grabHeld && !IsGrabbing) TryGrab();
            else if (!grabHeld && IsGrabbing) Release();
        }

        void FixedUpdate()
        {
            if (!Simulate) return;
            float dt = Time.fixedDeltaTime;
            IsGrounded = Time.time >= jumpLockedUntil && ProbeGround();
            if (IsGrounded) lastGroundedTime = Time.time;

            Vector3 wish = ComputeWishDirection(out float tankYaw);

            // Velocidad horizontal: acelerar hacia la deseada (también frena cuando no hay input).
            Vector3 v = rb.linearVelocity;
            var horizontal = new Vector3(v.x, 0f, v.z);
            float accel = IsGrounded ? groundAcceleration : airAcceleration;
            Vector3 next = Vector3.MoveTowards(horizontal, wish * moveSpeed, accel * dt);
            rb.AddForce(next - horizontal, ForceMode.VelocityChange);

            // Orientación.
            if (!Mathf.Approximately(tankYaw, 0f))
            {
                rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, tankYaw * tankTurnSpeed * dt, 0f));
            }
            else if (wish.sqrMagnitude > 0.01f && !IsTankMode)
            {
                var target = Quaternion.LookRotation(wish, Vector3.up);
                rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, target, turnSpeed * dt));
            }

            // Salto con buffer y coyote time.
            if (Time.time <= jumpBufferedUntil && Time.time - lastGroundedTime <= coyoteTime)
            {
                float jumpSpeed = Mathf.Sqrt(2f * -Physics.gravity.y * jumpHeight);
                rb.AddForce(Vector3.up * (jumpSpeed - rb.linearVelocity.y), ForceMode.VelocityChange);
                jumpBufferedUntil = float.NegativeInfinity;
                lastGroundedTime = float.NegativeInfinity;
                jumpLockedUntil = Time.time + 0.15f;
                IsGrounded = false;
            }

            // Caída más pesada: el salto se siente menos flotante.
            if (!IsGrounded && rb.linearVelocity.y < 0f)
                rb.AddForce(Physics.gravity * (fallGravityMultiplier - 1f), ForceMode.Acceleration);
        }

        void LateUpdate()
        {
            // La cabeza mira al jugador que esta cabeza "filma", así se lee quién mira a quién.
            float t = 1f - Mathf.Exp(-10f * Time.deltaTime);
            if (LookTarget)
            {
                Vector3 d = LookTarget.ChestPosition - head.position;
                if (d.sqrMagnitude > 0.01f)
                    head.rotation = Quaternion.Slerp(head.rotation, Quaternion.LookRotation(d, Vector3.up), t);
            }
            else
            {
                head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.identity, t);
            }

            if (handRenderer)
            {
                bool showHand = Simulate ? IsGrabbing : remoteGrabbing;
                handRenderer.enabled = showHand;
                if (showHand)
                    handRenderer.transform.position = Simulate ? GrabPoint : remoteGrabPoint;
                else
                    handRenderer.transform.localPosition = Vector3.zero;
            }
        }

        bool IsTankMode => InputSource?.TankMode ??
            (GameManager.Instance && GameManager.Instance.Controls == GameManager.ControlMode.Tank);

        Vector3 ComputeWishDirection(out float tankYaw)
        {
            tankYaw = 0f;
            Vector2 m = InputSource != null ? InputSource.Move : Vector2.zero;
            if (m.sqrMagnitude < 0.0001f) return Vector3.zero;

            if (IsTankMode)
            {
                tankYaw = m.x;
                return transform.forward * m.y;
            }

            Vector3 fwd = Vector3.forward, right = Vector3.right;
            if (InputSource.TryGetMoveForward(out var sentForward))
            {
                fwd = sentForward;
                right = Vector3.Cross(Vector3.up, fwd);
            }
            else if (View)
            {
                View.GetPlanarBasis(out fwd, out right);
            }
            return Vector3.ClampMagnitude(fwd * m.y + right * m.x, 1f);
        }

        bool ProbeGround()
        {
            float castDistance = capsule.height * 0.5f - groundProbeRadius + groundSkin;
            int n = Physics.SphereCastNonAlloc(rb.position + capsule.center, groundProbeRadius, Vector3.down,
                groundHits, castDistance, ~0, QueryTriggerInteraction.Ignore);
            float minNormalY = Mathf.Cos(maxGroundSlope * Mathf.Deg2Rad);
            for (int i = 0; i < n; i++)
            {
                var hit = groundHits[i];
                if (hit.distance <= 0f) continue; // superpuesto al inicio (el propio cuerpo)
                var other = hit.collider.attachedRigidbody;
                if (other == rb) continue;
                if (other != null && other == grabbedBody) continue; // no pararse sobre lo que uno sostiene
                if (hit.normal.y < minNormalY) continue;
                return true;
            }
            return false;
        }

        void TryGrab()
        {
            Vector3 p = hand.position;
            int n = Physics.OverlapSphereNonAlloc(p, grabRadius, grabOverlap, ~0, QueryTriggerInteraction.Ignore);
            Rigidbody best = null;
            Vector3 bestPoint = default;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var col = grabOverlap[i];
                var other = col.attachedRigidbody;
                if (other == null || other == rb || other.isKinematic) continue;
                Vector3 cp = col.ClosestPoint(p);
                float d = (cp - p).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = other;
                    bestPoint = cp;
                }
            }
            if (!best) return;

            grabbedBody = best;
            grabJoint = gameObject.AddComponent<SpringJoint>();
            grabJoint.connectedBody = best;
            grabJoint.autoConfigureConnectedAnchor = false;
            grabJoint.anchor = transform.InverseTransformPoint(p);
            grabJoint.connectedAnchor = best.transform.InverseTransformPoint(bestPoint);
            grabJoint.spring = grabSpring;
            grabJoint.damper = grabDamper;
            grabJoint.minDistance = 0f;
            grabJoint.maxDistance = 0.05f;
            grabJoint.breakForce = grabBreakForce;
            grabJoint.enableCollision = false;
        }

        public void Release()
        {
            if (grabJoint) Destroy(grabJoint);
            grabJoint = null;
            grabbedBody = null;
        }

        void OnJointBreak(float breakForce)
        {
            grabJoint = null;
            grabbedBody = null;
        }

        public void Respawn(Vector3 position, Quaternion rotation)
        {
            Release();
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            jumpBufferedUntil = float.NegativeInfinity;
        }

        void OnDrawGizmosSelected()
        {
            if (!hand) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(hand.position, grabRadius);
        }
    }
}
