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

        readonly NetworkVariable<int> index = new(-1);
        readonly NetworkVariable<NetInput> input = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> grabbing = new();
        readonly NetworkVariable<Vector3> grabPoint = new();

        PlayerCharacter character;
        LocalDevicesInputSource devices;
        int pendingIndex = -1;
        bool registered;
        byte jumpCount, resetCount;

        public PlayerCharacter Character => character;
        public NetInput CurrentInput => input.Value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LocalInputOverride = null;

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
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner) SendLocalInput();

            if (IsServer)
            {
                grabbing.Value = character.IsGrabbing;
                if (character.IsGrabbing) grabPoint.Value = character.GrabPoint;
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
                yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            }

            input.Value = new NetInput
            {
                Move = typing ? Vector2.zero : source.Move,
                CameraYaw = yaw,
                JumpCount = jumpCount,
                ResetCount = resetCount,
                Grab = !typing && source.GrabHeld,
                Tank = gm && gm.Controls == GameManager.ControlMode.Tank,
            };
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
