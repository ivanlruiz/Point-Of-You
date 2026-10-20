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

        [Header("Empujones")]
        [Tooltip("Velocidad (m/s) con la que otro jugador te tiene que llevar por delante para que te tambalees. Corriendo es 4,5.")]
        [SerializeField] float shoveThreshold = 3f;
        [Tooltip("Parte de esa velocidad que se suma además del choque físico.")]
        [SerializeField] float shoveBoost = 0.3f;
        [SerializeField] float shovePop = 1f;
        [Tooltip("Control mínimo (0-1) justo después de un golpe fuerte.")]
        [SerializeField] float minControl = 0.05f;
        [Tooltip("Segundos para recuperar el control entero desde el mínimo.")]
        [SerializeField] float controlRecovery = 0.6f;

        Rigidbody rb;
        CapsuleCollider capsule;
        SpringJoint grabJoint;
        Rigidbody grabbedBody;
        float lastGroundedTime = float.NegativeInfinity;
        float jumpBufferedUntil = float.NegativeInfinity;
        float jumpLockedUntil;
        bool remoteGrabbing;
        Vector3 remoteGrabPoint;
        bool regrabLocked; // después de perder un agarre hay que soltar el botón antes de volver a agarrar
        Vector3 eyeLocal;
        PlayerWobble wobble;
        float control = 1f;
        Vector3 expectedVelocity; // horizontal: la que pidió el control en este paso de física
        readonly DirectionMemory directionMemory = new();
        readonly RaycastHit[] groundHits = new RaycastHit[8];
        readonly Collider[] grabOverlap = new Collider[16];

        /// <summary>
        /// Si este personaje corre su física acá. Offline siempre; online solo en el host
        /// (en los clientes es una copia que mueve la red).
        /// </summary>
        public bool Simulate { get; set; } = true;
        /// <summary>Margen extra para saltar después de dejar el piso (online, según la demora del dueño).</summary>
        public float ExtraCoyoteTime { get; set; }

        public int Index { get; private set; }
        public Color Color { get; private set; } = Color.white;
        public PlayerInputSource InputSource { get; private set; }
        public RingCamera View { get; private set; }
        /// <summary>El jugador al que mira esta cabeza (es decir, la pantalla que se ve desde acá).</summary>
        public PlayerCharacter LookTarget { get; set; }

        public Rigidbody Body => rb;
        public Transform Head => head;
        /// <summary>
        /// Dónde va la cámara de quien mira desde esta cabeza. Fijo respecto de la raíz: el bamboleo mueve la
        /// cabeza que ven los demás, pero no la cámara.
        /// </summary>
        public Vector3 EyePosition => transform.TransformPoint(eyeLocal);
        public Vector3 ChestPosition => chest.position;
        public Renderer[] HeadRenderers => headRenderers;
        public bool IsGrounded { get; private set; }
        public bool IsGrabbing => grabJoint != null;
        public Rigidbody GrabbedBody => grabbedBody;
        public Vector3 GrabPoint => IsGrabbing && grabbedBody
            ? grabbedBody.transform.TransformPoint(grabJoint.connectedAnchor)
            : hand.position;
        public string DisplayName => $"J{Index + 1}";
        /// <summary>Si la mano se ve agarrando algo (también en los clientes, con el estado que llega por red).</summary>
        public bool ShowsGrab => Simulate ? IsGrabbing : remoteGrabbing;
        public Vector3 ShownGrabPoint => Simulate ? GrabPoint : remoteGrabPoint;
        /// <summary>1 = control total; baja después de un empujón fuerte y se recupera solo.</summary>
        public float ControlFactor => control;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            wobble = GetComponent<PlayerWobble>();
            eyeLocal = transform.InverseTransformPoint(head.position);
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
            if (!grabHeld) regrabLocked = false;
            if (grabHeld && !IsGrabbing && !regrabLocked) TryGrab();
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
            // Después de un empujón el control vuelve de a poco: mientras tanto, el golpe se desliza.
            control = Mathf.MoveTowards(control, 1f, dt * (1f - minControl) / controlRecovery);
            Vector3 v = rb.linearVelocity;
            var horizontal = new Vector3(v.x, 0f, v.z);
            float accel = (IsGrounded ? groundAcceleration : airAcceleration) * control;
            Vector3 next = Vector3.MoveTowards(horizontal, wish * moveSpeed, accel * dt);
            rb.AddForce(next - horizontal, ForceMode.VelocityChange);
            expectedVelocity = next;

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
            if (Time.time <= jumpBufferedUntil && Time.time - lastGroundedTime <= coyoteTime + ExtraCoyoteTime)
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
                bool showHand = ShowsGrab;
                handRenderer.enabled = showHand;
                if (showHand)
                    handRenderer.transform.position = ShownGrabPoint;
                else
                    handRenderer.transform.localPosition = Vector3.zero;
            }
        }

        /// <summary>
        /// Empujón: cambia la velocidad y te quita el control un rato, según qué tan fuerte fue.
        /// Solo donde corre la física (offline o en el host).
        /// </summary>
        public void Shove(Vector3 velocityChange)
        {
            if (!Simulate) return;
            rb.AddForce(velocityChange, ForceMode.VelocityChange);
            Stagger(new Vector2(velocityChange.x, velocityChange.z).magnitude);
        }

        void Stagger(float strength) =>
            control = Mathf.Min(control, Mathf.Lerp(1f, minControl, Mathf.Clamp01(strength / 3f)));

        /// <summary>
        /// Choque con otro jugador. Se decide al primer contacto con las velocidades que cada uno pidió antes del
        /// paso de física (el impulso real se reparte en varios pasos con el contacto especulativo). Te tambaleás si
        /// el otro venía hacia vos más rápido que vos hacia él: chocar contra alguien que viene de frente o frenar
        /// contra alguien quieto no cuenta. Paredes, puertas y cajas tampoco.
        /// </summary>
        void OnCollisionEnter(Collision collision)
        {
            if (!Simulate) return;
            var other = collision.rigidbody ? collision.rigidbody.GetComponent<PlayerCharacter>() : null;
            if (!other || other.Body.isKinematic) return;

            Vector3 toMe = transform.position - other.transform.position;
            toMe.y = 0f;
            if (toMe.sqrMagnitude < 1e-4f) return;
            toMe.Normalize();
            float theirs = Vector3.Dot(other.expectedVelocity, toMe);
            float mine = -Vector3.Dot(expectedVelocity, toMe);
            float hit = Mathf.Clamp(theirs - Mathf.Max(mine, 0f), 0f, theirs + mine);
            if (hit < shoveThreshold) return;
            rb.AddForce(toMe * (hit * shoveBoost) + Vector3.up * shovePop, ForceMode.VelocityChange);
            Stagger(hit);
        }

        bool IsTankMode => InputSource?.TankMode ??
            (GameManager.Instance && GameManager.Instance.Controls == GameManager.ControlMode.Tank);

        Vector3 ComputeWishDirection(out float tankYaw)
        {
            tankYaw = 0f;
            Vector2 m = InputSource != null ? InputSource.Move : Vector2.zero;

            if (IsTankMode)
            {
                directionMemory.Reset();
                tankYaw = m.x;
                return transform.forward * m.y;
            }

            Vector3 fwd = Vector3.forward, right = Vector3.right;
            if (InputSource != null && InputSource.TryGetMoveForward(out var sentForward))
            {
                // Online: el cliente ya aplicó la memoria de dirección antes de mandar su "adelante".
                fwd = sentForward;
                right = Vector3.Cross(Vector3.up, fwd);
            }
            else if (View)
            {
                View.GetPlanarBasis(out fwd, out right);
                if (GameManager.Instance && GameManager.Instance.Controls == GameManager.ControlMode.CameraMemory)
                {
                    fwd = directionMemory.Apply(fwd, m, Time.time);
                    right = Vector3.Cross(Vector3.up, fwd);
                }
            }
            if (m.sqrMagnitude < 0.0001f) return Vector3.zero;
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

        public void Release(bool requireButtonRelease = false)
        {
            if (requireButtonRelease && IsGrabbing) regrabLocked = true;
            if (grabJoint) Destroy(grabJoint);
            grabJoint = null;
            grabbedBody = null;
        }

        void OnJointBreak(float breakForce)
        {
            regrabLocked = true;
            grabJoint = null;
            grabbedBody = null;
        }

        public void Respawn(Vector3 position, Quaternion rotation)
        {
            Release(requireButtonRelease: true);
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            jumpBufferedUntil = float.NegativeInfinity;
            directionMemory.Reset();
            control = 1f;
            if (wobble) wobble.ResetMotion();
            // Online: Teleport hace que los clientes lo vean aparecer en vez de interpolar un vuelo.
            var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (nt && nt.IsSpawned && nt.IsServer) nt.Teleport(position, rotation, transform.localScale);
        }

        void OnDrawGizmosSelected()
        {
            if (!hand) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(hand.position, grabRadius);
        }
    }
}
