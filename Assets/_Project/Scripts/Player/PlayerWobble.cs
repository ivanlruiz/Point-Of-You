using UnityEngine;

namespace PointOfYou
{
    /// <summary>
    /// Bamboleo cosmético: el cuerpo se inclina con la inercia, se aplasta al caer, la cabeza va un poco atrasada
    /// y los brazos se balancean, se agitan en el aire y se estiran hacia lo que se agarra.
    /// Sale de cómo se mueve la raíz (no de la física), así que se ve igual en el host y en los clientes.
    /// Nunca mueve el ojo: la cámara de quien mira desde esta cabeza usa <see cref="PlayerCharacter.EyePosition"/>,
    /// que es fijo respecto de la raíz.
    /// </summary>
    [DefaultExecutionOrder(-10)] // antes de PlayerCharacter.LateUpdate, que orienta la cabeza
    public class PlayerWobble : MonoBehaviour
    {
        [Header("Partes")]
        [Tooltip("Pivote en el centro del cuerpo: se inclina.")]
        [SerializeField] Transform visual;
        [Tooltip("Pivote en los pies: se estira y se aplasta (no contiene la cabeza, para no deformarla).")]
        [SerializeField] Transform torso;
        [SerializeField] Transform head;
        [SerializeField] Transform leftShoulder;
        [SerializeField] Transform rightShoulder;

        [Header("Inclinación")]
        [SerializeField] float leanStiffness = 140f;
        [SerializeField] float leanDamping = 11f;
        [Tooltip("Cuánto empuja la aceleración de la raíz a la parte de arriba (inercia).")]
        [SerializeField] float leanInertia = 0.5f;
        [Tooltip("Inclinación hacia adelante al correr, en metros a 1 m de altura por m/s.")]
        [SerializeField] float runLean = 0.015f;
        [Tooltip("Tope: tan(12,4°). Con más, el pecho se acerca al ojo de esta cabeza.")]
        [SerializeField] float maxLean = 0.22f;
        [SerializeField] float headWhip = 0.3f;

        [Header("Aplastar y estirar")]
        [SerializeField] float squashStiffness = 260f;
        [SerializeField] float squashDamping = 12f;
        [Tooltip("Estirado máximo: deja el tope del cuerpo a 0,9 m, debajo del ojo (1 m).")]
        [SerializeField] float maxStretch = 0.0625f;
        [SerializeField] float maxSquash = 0.16f;

        [Header("Brazos")]
        [SerializeField] float stride = 1.2f;
        [SerializeField] float armSwing = 35f;
        [SerializeField] float armRest = 10f;
        [SerializeField] float armAirRaise = 110f;
        [SerializeField] float armLength = 0.5f;
        [SerializeField] float maxReach = 0.95f;

        // Cuánto sube el tope del cuerpo por unidad de estirado (alto total del cuerpo).
        const float BodyHeight = 1.6f;
        const float TeleportDistance = 3f;
        const float MaxStep = 1f / 120f;

        PlayerCharacter player;
        Vector3 headBase, leftBase, rightBase;
        bool hasLast;
        Vector3 lastPosition, velocity, lastVelocity;
        float recentFall;  // velocidad de caída de hace un momento (se va olvidando)
        bool rising;
        Vector3 lean, leanVelocity;
        float squash, squashVelocity;
        float walkPhase, air, grabBlend;

        /// <summary>Inclinación actual del cuerpo en grados (para tests).</summary>
        public float LeanDegrees => Mathf.Atan(lean.magnitude) * Mathf.Rad2Deg;
        public float Squash => squash;

        void Awake()
        {
            player = GetComponent<PlayerCharacter>();
            if (head) headBase = head.localPosition;
            if (leftShoulder) leftBase = leftShoulder.localPosition;
            if (rightShoulder) rightBase = rightShoulder.localPosition;
        }

        /// <summary>Después de un teletransporte: arrancar quieto, sin una inercia gigante.</summary>
        public void ResetMotion()
        {
            hasLast = false;
            velocity = lastVelocity = Vector3.zero;
            recentFall = 0f;
            rising = false;
            lean = leanVelocity = Vector3.zero;
            squash = squashVelocity = 0f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !visual) return;

            Vector3 position = transform.position;
            if (!hasLast || (position - lastPosition).sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                ResetMotion();
                hasLast = true;
                lastPosition = position;
            }

            Vector3 raw = (position - lastPosition) / dt;
            lastPosition = position;
            lastVelocity = velocity;
            velocity = Vector3.Lerp(velocity, raw, 1f - Mathf.Exp(-20f * dt));
            Vector3 accel = Vector3.ClampMagnitude((velocity - lastVelocity) / dt, 60f);
            Vector3 flatVelocity = new(velocity.x, 0f, velocity.z);
            Vector3 flatAccel = new(accel.x, 0f, accel.z);

            // Aterrizaje: venía cayendo rápido y ya no. Se recuerda la caída un momento porque en los clientes
            // la interpolación de red reparte el frenazo en varios cuadros.
            recentFall = Mathf.Max(recentFall * Mathf.Exp(-dt / 0.1f), -raw.y);
            if (recentFall > 2.5f && raw.y > -0.8f)
            {
                squashVelocity -= Mathf.Min(recentFall * 0.4f, 3f);
                recentFall = 0f;
            }
            // Despegue: estira (una vez por salto).
            if (raw.y > 3f && !rising)
            {
                squashVelocity += 1f;
                rising = true;
            }
            else if (raw.y < 0.5f) rising = false;

            // Con la velocidad de este cuadro: al tocar el piso el estirado se suelta enseguida y no tapa el aplastado.
            float stretchTarget = Mathf.Clamp(Mathf.Abs(raw.y) * 0.012f, 0f, maxStretch);
            Vector3 leanTarget = flatVelocity * runLean;

            // Resortes con pasos chicos: estables aunque un cuadro dure mucho.
            for (float left = Mathf.Min(dt, 0.1f); left > 0f; left -= MaxStep)
            {
                float h = Mathf.Min(MaxStep, left);
                leanVelocity += (leanStiffness * (leanTarget - lean) - leanDamping * leanVelocity - flatAccel * leanInertia) * h;
                lean += leanVelocity * h;
                squashVelocity += (squashStiffness * (stretchTarget - squash) - squashDamping * squashVelocity) * h;
                squash += squashVelocity * h;
            }
            lean = Vector3.ClampMagnitude(lean, maxLean);
            squash = Mathf.Clamp(squash, -maxSquash, maxStretch);

            // Inclinar alrededor del centro del cuerpo: el tope se corre ~0,2 m como mucho y queda lejos del ojo.
            Quaternion tilt = Quaternion.identity;
            if (lean.sqrMagnitude > 1e-8f)
                tilt = Quaternion.AngleAxis(Mathf.Atan(lean.magnitude) * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, lean.normalized));
            visual.rotation = tilt * transform.rotation;

            // Aplastar desde los pies, conservando más o menos el volumen.
            if (torso) torso.localScale = new Vector3(1f - squash * 0.5f, 1f + squash, 1f - squash * 0.5f);
            float rise = squash * BodyHeight;

            // La cabeza acompaña el estirado y se va un poco más lejos que el cuerpo (latigazo).
            if (head)
            {
                head.localPosition = headBase + Vector3.up * rise;
                head.position += lean * headWhip;
            }

            UpdateArms(dt, flatVelocity, rise);
        }

        void UpdateArms(float dt, Vector3 flatVelocity, float rise)
        {
            if (!leftShoulder || !rightShoulder) return;
            float speed = flatVelocity.magnitude;
            walkPhase = Mathf.Repeat(walkPhase + speed * dt * (2f * Mathf.PI / stride), 2f * Mathf.PI);
            air = Mathf.MoveTowards(air, Mathf.Abs(velocity.y) > 1.2f ? 1f : 0f, 6f * dt);
            grabBlend = Mathf.MoveTowards(grabBlend, player && player.ShowsGrab ? 1f : 0f, 8f * dt);

            float swing = Mathf.Sin(walkPhase) * armSwing * Mathf.Clamp01(speed / 4.5f) * (1f - air);
            float outward = armRest + air * (armAirRaise + Mathf.Sin(Time.time * 16f) * 12f);
            Vector3 lift = Vector3.up * rise * 0.7f;
            Arm(leftShoulder, leftBase + lift, swing, -outward);
            Arm(rightShoulder, rightBase + lift, -swing, outward);
        }

        void Arm(Transform shoulder, Vector3 basePosition, float swing, float outward)
        {
            shoulder.localPosition = basePosition;
            Quaternion walk = visual.rotation * Quaternion.Euler(swing, 0f, outward);
            float length = 1f;
            Quaternion rotation = walk;
            if (grabBlend > 0f && player)
            {
                Vector3 toHand = player.ShownGrabPoint - shoulder.position;
                if (toHand.sqrMagnitude > 1e-4f)
                {
                    Quaternion reach = Quaternion.FromToRotation(Vector3.down, toHand);
                    rotation = Quaternion.Slerp(walk, reach, grabBlend);
                    length = Mathf.Lerp(1f, Mathf.Clamp(toHand.magnitude, 0.25f, maxReach) / armLength, grabBlend);
                }
            }
            shoulder.rotation = rotation;
            shoulder.localScale = new Vector3(1f, length, 1f);
        }
    }
}
