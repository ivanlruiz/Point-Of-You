using UnityEngine;
using UnityEngine.Rendering;

namespace PointOfYou
{
    /// <summary>
    /// La cámara de un jugador. Vive en la cabeza de OTRO jugador (su "observador") y apunta
    /// siempre al dueño. Solo hereda la posición de esa cabeza, nunca su rotación.
    /// Si el dueño juega solo, cae en una cámara de seguimiento simple para poder probar.
    ///
    /// Con el mouse / stick derecho se puede mirar alrededor desde esa cabeza; al soltar,
    /// la vista vuelve sola a apuntar al dueño.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RingCamera : MonoBehaviour
    {
        [SerializeField] float positionSmoothTime = 0.03f;
        [Tooltip("Más lento que el horizontal: los saltos del observador no sacuden la pantalla del otro.")]
        [SerializeField] float verticalSmoothTime = 0.2f;
        [SerializeField] float rotationSharpness = 16f;
        [Tooltip("Tope de giro (°/s): sin esto, pasar pegado al observador da latigazos de cámara.")]
        [SerializeField] float maxTurnSpeed = 240f;
        [Tooltip("Si la cabeza del observador salta más que esto (reaparecer, cambio de anillo), la cámara corta en vez de volar.")]
        [SerializeField] float snapDistance = 3f;
        [SerializeField] Vector3 soloOffset = new(0f, 3.2f, -5.5f);
        [SerializeField] float lookHeightOffset = 0.15f;

        [Header("Mirar alrededor")]
        [SerializeField] float maxLookYaw = 170f;
        [SerializeField] float maxLookPitch = 70f;
        [Tooltip("Segundos sin mover el mouse / stick antes de volver a apuntar al dueño.")]
        [SerializeField] float recenterDelay = 0.6f;
        [SerializeField] float recenterSharpness = 5f;

        Vector3 velocity;
        float verticalVelocity;
        PlayerCharacter hiddenObserver;
        Quaternion aimRotation = Quaternion.identity; // apuntando al dueño, sin el desvío del mouse
        Vector2 lookOffset;                           // x: grados a la derecha, y: grados hacia arriba
        float lastLookTime = float.NegativeInfinity;

        public PlayerCharacter Owner { get; private set; }
        public PlayerCharacter Observer { get; private set; }
        public Camera Cam { get; private set; }
        /// <summary>De dónde sale el "mirar alrededor" (solo en la PC donde se juega a este jugador).</summary>
        public PlayerInputSource LookSource { get; set; }
        public Vector2 LookOffset => lookOffset;

        void Awake() => Cam = GetComponent<Camera>();

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            RestoreHiddenHead();
        }

        public void Init(PlayerCharacter owner)
        {
            Owner = owner;
            Snap();
        }

        public void SetObserver(PlayerCharacter observer)
        {
            if (observer == Observer) return;
            Observer = observer;
            if (Owner) Snap();
        }

        /// <summary>Corte de cámara: va directo a la cabeza del observador apuntando al dueño, sin viajar.</summary>
        public void Snap()
        {
            if (!Owner) return;
            transform.position = DesiredPosition();
            velocity = Vector3.zero;
            verticalVelocity = 0f;
            Aim(1f, capTurn: false);
        }

        /// <summary>Ejes de movimiento en el plano del piso, según hacia dónde mira esta cámara.</summary>
        public void GetPlanarBasis(out Vector3 forward, out Vector3 right)
        {
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (f.sqrMagnitude < 0.01f) f = Vector3.ProjectOnPlane(transform.up, Vector3.up); // mirando recto abajo
            forward = f.normalized;
            right = Vector3.Cross(Vector3.up, forward);
        }

        void LateUpdate()
        {
            if (!Owner) return;
            Vector3 desired = DesiredPosition();
            Vector3 position = transform.position;
            if ((desired - position).sqrMagnitude > snapDistance * snapDistance)
            {
                // También cubre a los clientes online, que no saben cuándo alguien reapareció.
                UpdateLook();
                Snap();
                return;
            }
            var horizontal = Vector3.SmoothDamp(new Vector3(position.x, 0f, position.z), new Vector3(desired.x, 0f, desired.z),
                ref velocity, positionSmoothTime);
            float y = Mathf.SmoothDamp(position.y, desired.y, ref verticalVelocity, verticalSmoothTime);
            transform.position = new Vector3(horizontal.x, y, horizontal.z);
            UpdateLook();
            Aim(1f - Mathf.Exp(-rotationSharpness * Time.deltaTime));
        }

        Vector3 DesiredPosition() => Observer ? Observer.EyePosition : Owner.ChestPosition + soloOffset;

        void UpdateLook()
        {
            Vector2 delta = LookSource?.Look ?? Vector2.zero;
            if (delta.sqrMagnitude > 1e-6f)
            {
                lookOffset.x = Mathf.Clamp(lookOffset.x + delta.x, -maxLookYaw, maxLookYaw);
                lookOffset.y = Mathf.Clamp(lookOffset.y + delta.y, -maxLookPitch, maxLookPitch);
                lastLookTime = Time.time;
            }
            else if (Time.time - lastLookTime > recenterDelay)
            {
                lookOffset = Vector2.Lerp(lookOffset, Vector2.zero, 1f - Mathf.Exp(-recenterSharpness * Time.deltaTime));
            }
        }

        void Aim(float t, bool capTurn = true)
        {
            // La puntería al dueño se suaviza (con tope de velocidad); el desvío del mouse se aplica directo.
            Vector3 dir = Owner.ChestPosition + Vector3.up * lookHeightOffset - transform.position;
            if (dir.sqrMagnitude > 1e-4f)
            {
                Vector3 up = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.98f ? aimRotation * Vector3.up : Vector3.up;
                var smoothed = Quaternion.Slerp(aimRotation, Quaternion.LookRotation(dir, up), t);
                aimRotation = capTurn ? Quaternion.RotateTowards(aimRotation, smoothed, maxTurnSpeed * Time.deltaTime) : smoothed;
            }
            Vector3 aim = aimRotation.eulerAngles;
            float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, aim.x) - lookOffset.y, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, aim.y + lookOffset.x, 0f);
        }

        // La cámara está dentro de la cabeza del observador: la ocultamos solo para esta cámara
        // (sigue proyectando sombra y las demás pantallas la ven normal).
        void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != Cam || !Observer) return;
            hiddenObserver = Observer;
            foreach (var r in hiddenObserver.HeadRenderers)
                if (r) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        void OnEndCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam == Cam) RestoreHiddenHead();
        }

        void RestoreHiddenHead()
        {
            if (!hiddenObserver) return;
            foreach (var r in hiddenObserver.HeadRenderers)
                if (r) r.shadowCastingMode = ShadowCastingMode.On;
            hiddenObserver = null;
        }
    }
}
