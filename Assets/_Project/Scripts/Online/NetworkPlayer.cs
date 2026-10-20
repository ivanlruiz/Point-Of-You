using Unity.Netcode;
using UnityEngine;

namespace PointOfYou
{
    /// <summary>Lo que cada cliente le manda al host sobre su jugador.</summary>
    public struct NetInput : INetworkSerializeByMemcpy
    {
        public Vector2 Move;
        /// <summary>Hacia dónde mira, en el piso, la cámara del cliente (grados). Define "adelante".</summary>
        public float CameraYaw;
        /// <summary>Contadores en vez de bools: un salto entre dos ticks de red no se pierde.</summary>
        public byte JumpCount;
        public byte ResetCount;
        public bool Grab;
        public bool Tank;
        /// <summary>El input viaja sin garantía de orden: el host descarta lo que llegue más viejo que lo último.</summary>
        public uint Sequence;
        /// <summary>Ida y vuelta que mide el cliente (ms): el host lo usa para darle margen al saltar.</summary>
        public ushort RttMs;
    }

    /// <summary>
    /// Jugador online. El host simula la física con el input que manda el dueño;
    /// la posición vuelve a todos por NetworkTransform.
    /// </summary>
    [RequireComponent(typeof(PlayerCharacter))]
    public class NetworkPlayer : NetworkBehaviour
    {
        /// <summary>Para tests/bots: reemplaza los dispositivos de esta PC como input del jugador local.</summary>
        public static PlayerInputSource LocalInputOverride { get; set; }
        /// <summary>
        /// Ida y vuelta al host del jugador de esta PC, en ms (0 en el host o sin medir). Se mide con un eco propio:
        /// el de Netcode solo se actualiza con mensajes confiables, que ahora casi no viajan.
        /// </summary>
        public static float LocalRttMs { get; private set; }

        // Cada cuánto como mínimo se reenvía el input aunque no cambie (si se pierde un paquete, el siguiente lo corrige).
        const float InputResendInterval = 1f / 30f;
        const float InputMinInterval = 1f / 60f; // la física corre a 50 Hz: más seguido no sirve
        const float PingInterval = 0.5f;

        readonly NetworkVariable<int> index = new(-1);
        readonly NetworkVariable<bool> grabbing = new();
        readonly NetworkVariable<Vector3> grabPoint = new();

        PlayerCharacter character;
        LocalDevicesInputSource devices;
        int pendingIndex = -1;
        bool registered;
        byte jumpCount, resetCount;
        readonly DirectionMemory directionMemory = new();
        NetInput serverInput;      // en el host: lo último que llegó del dueño
        NetInput lastSent;
        uint sequence;
        float lastSentAt = float.NegativeInfinity;
        float nextPing;

        public PlayerCharacter Character => character;
        public NetInput CurrentInput => serverInput;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LocalInputOverride = null;
            LocalRttMs = 0f;
        }

        void Awake() => character = GetComponent<PlayerCharacter>();

        /// <summary>El host decide el número de jugador antes de spawnearlo.</summary>
        public void AssignIndex(int value) => pendingIndex = value;

        public override void OnNetworkSpawn()
        {
            character.Simulate = IsServer;
            if (IsServer && pendingIndex >= 0) index.Value = pendingIndex;
            if (IsOwner) devices = new LocalDevicesInputSource();
            index.OnValueChanged += OnIndexChanged;
            TryRegister();
        }

        public override void OnNetworkDespawn()
        {
            index.OnValueChanged -= OnIndexChanged;
            if (IsOwner) LocalRttMs = 0f;
            if (registered && GameManager.Instance) GameManager.Instance.Unregister(character);
            registered = false;
        }

        void OnIndexChanged(int previous, int current) => TryRegister();

        void TryRegister()
        {
            if (registered || index.Value < 0 || !GameManager.Instance) return;
            registered = true;
            var source = IsServer ? new NetworkInputSource(this, IsOwner ? "Esta PC (host)" : "Online") : null;
            GameManager.Instance.Register(character, index.Value, source, localView: IsOwner);
            // Mirar alrededor es local: lo maneja quien juega en esta PC, no viaja por la red.
            if (IsOwner && character.View) character.View.LookSource = LocalInputOverride ?? devices;
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner) SendLocalInput();
            if (IsOwner && !IsServer && Time.unscaledTime >= nextPing)
            {
                nextPing = Time.unscaledTime + PingInterval;
                PingRpc(Time.realtimeSinceStartup);
            }

            if (IsServer)
            {
                grabbing.Value = character.IsGrabbing;
                if (character.IsGrabbing) grabPoint.Value = character.GrabPoint;
                // A un cliente con demora le llega tarde la noticia de que se cayó del borde: se le da más
                // "coyote time" (la mitad de su ida y vuelta, hasta 0,2 s) para que el salto que vio a tiempo valga.
                character.ExtraCoyoteTime = IsOwner ? 0f : Mathf.Min(0.2f, serverInput.RttMs / 2000f);
            }
            else
            {
                character.SetRemoteGrab(grabbing.Value, grabPoint.Value);
            }
        }

        void SendLocalInput()
        {
            var gm = GameManager.Instance;
            PlayerInputSource source = LocalInputOverride ?? devices;
            bool typing = gm && gm.UiHasKeyboardFocus;
            if (source == null) return;

            if (source.JumpPressed && !typing) jumpCount++;
            if (source.ResetPressed && !typing) resetCount++;

            float yaw = 0f;
            if (character.View)
            {
                character.View.GetPlanarBasis(out var forward, out _);
                if (gm && gm.Controls == GameManager.ControlMode.CameraMemory)
                    forward = directionMemory.Apply(forward, typing ? Vector2.zero : source.Move, Time.time);
                yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            }

            var next = new NetInput
            {
                Move = typing ? Vector2.zero : source.Move,
                CameraYaw = yaw,
                JumpCount = jumpCount,
                ResetCount = resetCount,
                Grab = !typing && source.GrabHeld,
                Tank = gm && gm.Controls == GameManager.ControlMode.Tank,
                RttMs = (ushort)Mathf.Min(LocalRttMs, ushort.MaxValue),
            };

            // Se manda apenas cambia (menos demora) y, si no cambia, cada tanto igual (por si se perdió un paquete).
            float now = Time.unscaledTime;
            bool changed = !SameInput(next, lastSent);
            if (now - lastSentAt < InputMinInterval) return;
            if (!changed && now - lastSentAt < InputResendInterval) return;
            next.Sequence = ++sequence;
            lastSent = next;
            lastSentAt = now;
            SubmitInputRpc(next);
        }

        static bool SameInput(in NetInput a, in NetInput b) =>
            a.Move == b.Move && Mathf.Abs(Mathf.DeltaAngle(a.CameraYaw, b.CameraYaw)) < 0.5f && a.JumpCount == b.JumpCount &&
            a.ResetCount == b.ResetCount && a.Grab == b.Grab && a.Tank == b.Tank;

        /// <summary>Input del dueño al host: sin garantía de entrega (llega antes); el número de secuencia descarta los atrasados.</summary>
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void SubmitInputRpc(NetInput received)
        {
            if (received.Sequence <= serverInput.Sequence && serverInput.Sequence - received.Sequence < uint.MaxValue / 2) return;
            serverInput = received;
        }

        // Eco para medir el ping: incluye la espera de cuadro de cada lado, que es la demora que se siente.
        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void PingRpc(float sentAt) => PongRpc(sentAt);

        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Unreliable)]
        void PongRpc(float sentAt)
        {
            float sample = (Time.realtimeSinceStartup - sentAt) * 1000f;
            LocalRttMs = LocalRttMs <= 0f ? sample : Mathf.Lerp(LocalRttMs, sample, 0.3f);
        }
    }

    /// <summary>En el host: el input que llegó por red, con la interfaz de cualquier otra fuente.</summary>
    public sealed class NetworkInputSource : PlayerInputSource
    {
        readonly NetworkPlayer player;
        readonly string label;
        byte lastJump, lastReset;

        public NetworkInputSource(NetworkPlayer player, string label)
        {
            this.player = player;
            this.label = label;
        }

        public override string Label => label;
        public override bool IsConnected => player && player.IsSpawned;
        public override Vector2 Move => Vector2.ClampMagnitude(player.CurrentInput.Move, 1f);
        public override bool GrabHeld => player.CurrentInput.Grab;
        public override bool? TankMode => player.CurrentInput.Tank;

        public override bool JumpPressed
        {
            get
            {
                byte j = player.CurrentInput.JumpCount;
                bool pressed = j != lastJump;
                lastJump = j;
                return pressed;
            }
        }

        public override bool ResetPressed
        {
            get
            {
                byte r = player.CurrentInput.ResetCount;
                bool pressed = r != lastReset;
                lastReset = r;
                return pressed;
            }
        }

        public override bool TryGetMoveForward(out Vector3 forward)
        {
            float rad = player.CurrentInput.CameraYaw * Mathf.Deg2Rad;
            forward = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            return true;
        }

        public override bool SameDevice(PlayerInputSource other) => ReferenceEquals(this, other);
    }
}
