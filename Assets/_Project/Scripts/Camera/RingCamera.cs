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
        [SerializeField] float rotationSharpness = 16f;
        [SerializeField] Vector3 soloOffset = new(0f, 3.2f, -5.5f);
        [SerializeField] float lookHeightOffset = 0.15f;

        [Header("Mirar alrededor")]
        [SerializeField] float maxLookYaw = 170f;
        [SerializeField] float maxLookPitch = 70f;
        [Tooltip("Segundos sin mover el mouse / stick antes de volver a apuntar al dueño.")]
        [SerializeField] float recenterDelay = 0.6f;
        [SerializeField] float recenterSharpness = 5f;

        Vector3 velocity;
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
            transform.position = DesiredPosition();
            velocity = Vector3.zero;
            Aim(1f);
        }

        public void SetObserver(PlayerCharacter observer) => Observer = observer;

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
            transform.position = Vector3.SmoothDamp(transform.position, DesiredPosition(), ref velocity, positionSmoothTime);
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

        void Aim(float t)
        {
            // La puntería al dueño se suaviza; el desvío del mouse se aplica directo (si no, se siente "chicloso").
            Vector3 dir = Owner.ChestPosition + Vector3.up * lookHeightOffset - transform.position;
            if (dir.sqrMagnitude > 1e-4f)
            {
                Vector3 up = Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.98f ? aimRotation * Vector3.up : Vector3.up;
                aimRotation = Quaternion.Slerp(aimRotation, Quaternion.LookRotation(dir, up), t);
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
