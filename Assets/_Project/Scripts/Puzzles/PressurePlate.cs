using UnityEngine;

namespace PointOfYou
{
    /// <summary>Placa que queda presionada mientras haya un cuerpo físico (jugador o caja) encima.</summary>
    public class PressurePlate : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] Vector3 areaSize = new(1.6f, 0.5f, 1.6f);
        [SerializeField] float minMass = 0.5f;
        [SerializeField] Renderer plateRenderer;
        [SerializeField] Color idleColor = new(0.45f, 0.32f, 0.08f);
        [SerializeField] Color pressedColor = new(1f, 0.75f, 0.2f);
        [SerializeField] float pressDepth = 0.04f;

        readonly Collider[] overlap = new Collider[16];
        MaterialPropertyBlock block;
        Vector3 restLocalPosition;
        float pressAmount;

        public bool IsPressed { get; private set; }

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (plateRenderer) restLocalPosition = plateRenderer.transform.localPosition;
            ApplyVisual();
        }

        void FixedUpdate()
        {
            Vector3 center = transform.position + transform.up * (areaSize.y * 0.5f + 0.02f);
            int n = Physics.OverlapBoxNonAlloc(center, areaSize * 0.5f, overlap, transform.rotation, ~0,
                QueryTriggerInteraction.Ignore);
            bool pressed = false;
            for (int i = 0; i < n && !pressed; i++)
            {
                var body = overlap[i].attachedRigidbody;
                pressed = body && !body.isKinematic && body.mass >= minMass;
            }
            IsPressed = pressed;
        }

        void Update()
        {
            pressAmount = Mathf.MoveTowards(pressAmount, IsPressed ? 1f : 0f, Time.deltaTime * 8f);
            ApplyVisual();
        }

        void ApplyVisual()
        {
            if (!plateRenderer) return;
            plateRenderer.transform.localPosition = restLocalPosition + Vector3.down * (pressDepth * pressAmount);
            plateRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, Color.Lerp(idleColor, pressedColor, pressAmount));
            block.SetColor(EmissionColorId, pressedColor * (0.8f * pressAmount));
            plateRenderer.SetPropertyBlock(block);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.35f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position + transform.up * (areaSize.y * 0.5f + 0.02f), transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, areaSize);
        }
    }
}
