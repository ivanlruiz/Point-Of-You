using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PointOfYou
{
    /// <summary>
    /// Partida online: crear / unirse con código (Unity Multiplayer Services + Relay, sin abrir puertos)
    /// o por IP directa (LAN y pruebas). El host simula la física; los clientes mandan su input.
    /// Un jugador por PC.
    /// </summary>
    public class OnlineSession : MonoBehaviour
    {
        public const ushort DefaultPort = 7777;

        [SerializeField] NetworkObject playerPrefab;
        [SerializeField] NetworkObject levelStatePrefab;

        // El NetworkManager vive entre escenas (DontDestroyOnLoad): lo que se le registra, también.
        static readonly HashSet<GameObject> registeredPrefabs = new();
        static ISession session;
        static string pendingMessage;

        NetworkManager subscribed;
        bool reloadRequested;

        public static OnlineSession Instance { get; private set; }

        public string Status { get; private set; } = "";
        public bool Busy { get; private set; }
        public string JoinCode => session?.Code;
        public bool IsRelaySession => session != null;
        public int ConnectedCount => Net.IsHost ? NetworkManager.Singleton.ConnectedClientsIds.Count : -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            registeredPrefabs.Clear();
            session = null;
            pendingMessage = null;
        }

        void Awake()
        {
            Instance = this;
            if (pendingMessage != null)
            {
                Status = pendingMessage;
                pendingMessage = null;
            }
        }

        void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (!reloadRequested) return;
            reloadRequested = false;
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex >= 0 ? scene.buildIndex : 0);
        }

        // ---------------------------------------------------------------- conexión

        /// <summary>Crea una partida con código para que se sumen desde otras casas.</summary>
        public async void HostWithCode()
        {
            if (Busy || Net.IsOnline) return;
            Busy = true;
            try
            {
                EnsureNetworkManager();
                Status = "Conectando con Unity Services…";
                await SignInAsync();
                Status = "Creando partida…";
                var options = new SessionOptions { MaxPlayers = GameManager.MaxPlayers, IsPrivate = true }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Status = "";
            }
            catch (Exception e)
            {
                session = null;
                Status = Describe(e);
                Debug.LogException(e);
            }
            finally
            {
                Busy = false;
            }
        }

        /// <summary>Se une a una partida con el código que pasó el host.</summary>
        public async void JoinWithCode(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (Busy || Net.IsOnline || code.Length == 0) return;
            Busy = true;
            try
            {
                EnsureNetworkManager();
                Status = "Conectando con Unity Services…";
                await SignInAsync();
                Status = $"Entrando a {code}…";
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                Status = "";
            }
            catch (Exception e)
            {
                session = null;
                Status = Describe(e);
                Debug.LogException(e);
            }
            finally
            {
                Busy = false;
            }
        }

        /// <summary>Hostea por IP directa (LAN, o pruebas en la misma PC). Afuera de la LAN hay que abrir el puerto.</summary>
        public bool HostDirect(ushort port = DefaultPort, string listenAddress = "0.0.0.0")
        {
            if (Busy || Net.IsOnline) return false;
            var nm = EnsureNetworkManager();
            Transport(nm).SetConnectionData("127.0.0.1", port, listenAddress);
            bool ok = nm.StartHost();
            Status = ok ? "" : $"No se pudo abrir el puerto {port}.";
            return ok;
        }

        public bool JoinDirect(string address, ushort port = DefaultPort)
        {
            if (Busy || Net.IsOnline) return false;
            var nm = EnsureNetworkManager();
            Transport(nm).SetConnectionData((address ?? "").Trim(), port);
            bool ok = nm.StartClient();
            Status = ok ? $"Conectando a {address}:{port}…" : "No se pudo iniciar la conexión.";
            return ok;
        }

        /// <summary>Sale de la partida y vuelve a la pantalla de inicio.</summary>
        public async void Leave()
        {
            var current = session;
            session = null;
            try
            {
                if (current != null) await current.LeaveAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Online] Error al salir de la sesión: {e.Message}");
            }
            var nm = NetworkManager.Singleton;
            if (nm && nm.IsListening) nm.Shutdown();
            reloadRequested = true;
        }

        static async Task SignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // Perfil al azar por proceso: así dos instancias en la misma PC (o Multiplayer Play Mode)
                // no comparten el mismo jugador anónimo.
                var options = new InitializationOptions();
                options.SetProfile("p" + UnityEngine.Random.Range(0, int.MaxValue).ToString("x"));
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        static string Describe(Exception e)
        {
            string msg = e.Message ?? e.GetType().Name;
            string lower = msg.ToLowerInvariant();
            if (lower.Contains("project") && (lower.Contains("link") || lower.Contains("id")))
                return "El proyecto no está vinculado a Unity Cloud (Edit > Project Settings > Services). Mientras tanto se puede jugar por IP directa.";
            if (e is SessionException se && se.Error == SessionError.SessionNotFound)
                return "No existe una partida con ese código.";
            return $"No se pudo conectar: {msg}";
        }

        static UnityTransport Transport(NetworkManager nm) => (UnityTransport)nm.NetworkConfig.NetworkTransport;

        NetworkManager EnsureNetworkManager()
        {
            var nm = NetworkManager.Singleton;
            if (!nm)
            {
                var go = new GameObject("NetworkManager");
                go.SetActive(false); // configurar antes de que corra su Awake/OnEnable
                nm = go.AddComponent<NetworkManager>();
                var transport = go.AddComponent<UnityTransport>();
                // El mapa del host viaja comprimido al conectarse; el límite por defecto (6 KB) no alcanza para mapas grandes.
                transport.MaxPayloadSize = 128 * 1024;
                nm.NetworkConfig ??= new NetworkConfig();
                nm.NetworkConfig.NetworkTransport = transport;
                nm.NetworkConfig.EnableSceneManagement = false; // todos cargan la misma escena; nada puesto en escena es de red
                nm.NetworkConfig.ConnectionApproval = false;
                go.SetActive(true);
            }

            Register(nm, playerPrefab);
            Register(nm, levelStatePrefab);
            foreach (var r in FindObjectsByType<NetworkReplacement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Register(nm, r.Prefab);

            Subscribe(nm);
            return nm;
        }

        static void Register(NetworkManager nm, NetworkObject prefab)
        {
            if (!prefab || !registeredPrefabs.Add(prefab.gameObject)) return;
            nm.AddNetworkPrefab(prefab.gameObject);
        }

        void Subscribe(NetworkManager nm)
        {
            if (subscribed == nm) return;
            Unsubscribe();
            subscribed = nm;
            nm.OnServerStarted += OnServerStarted;
            nm.OnClientStarted += OnClientStarted;
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.OnClientStopped += OnStopped;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed.OnServerStarted -= OnServerStarted;
            subscribed.OnClientStarted -= OnClientStarted;
            subscribed.OnClientConnectedCallback -= OnClientConnected;
            subscribed.OnClientDisconnectCallback -= OnClientDisconnected;
            subscribed.OnClientStopped -= OnStopped;
            subscribed = null;
        }

        // ---------------------------------------------------------------- eventos de red

        void OnClientStarted()
        {
            // En todas las PCs: los objetos de escena que tienen versión de red se apagan.
            foreach (var r in FindObjectsByType<NetworkReplacement>(FindObjectsSortMode.None))
                if (!NetworkManager.Singleton.IsServer) r.gameObject.SetActive(false);
        }

        void OnServerStarted()
        {
            var nm = NetworkManager.Singleton;
            Instantiate(levelStatePrefab).Spawn(destroyWithScene: true);
            foreach (var r in FindObjectsByType<NetworkReplacement>(FindObjectsSortMode.None))
            {
                Instantiate(r.Prefab, r.transform.position, r.transform.rotation).Spawn(destroyWithScene: true);
                r.gameObject.SetActive(false);
            }
            if (GameManager.Instance) GameManager.Instance.ResetLevel();
            if (nm.IsHost) SpawnPlayer(nm.LocalClientId);
        }

        void OnClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm.IsServer)
            {
                SpawnPlayer(clientId);
            }
            else if (clientId == nm.LocalClientId)
            {
                Status = "";
            }
        }

        void SpawnPlayer(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            var gm = GameManager.Instance;
            if (!gm || nm.SpawnManager.GetPlayerNetworkObject(clientId)) return;

            int index = gm.NextFreeIndex();
            if (index < 0)
            {
                nm.DisconnectClient(clientId, "La partida está llena.");
                return;
            }

            gm.GetSpawn(index, out var position, out var rotation);
            var player = Instantiate(playerPrefab, position, rotation);
            player.GetComponent<NetworkPlayer>().AssignIndex(index);
            player.SpawnAsPlayerObject(clientId, destroyWithScene: true);
        }

        void OnClientDisconnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm && !nm.IsServer && clientId == nm.LocalClientId)
            {
                string reason = string.IsNullOrEmpty(nm.DisconnectReason) ? "" : $" ({nm.DisconnectReason})";
                pendingMessage = $"Se cortó la conexión con la partida{reason}.";
            }
        }

        void OnStopped(bool wasHost)
        {
            session = null;
            reloadRequested = true; // volver a la pantalla de inicio en el próximo frame
        }

        // ---------------------------------------------------------------- comandos del host

        /// <summary>F5 online: solo el host reinicia el nivel (sin recargar la escena).</summary>
        public void RequestRestart()
        {
            if (Net.IsHost)
            {
                if (GameManager.Instance) GameManager.Instance.ResetLevel();
                foreach (var r in FindObjectsByType<Respawnable>(FindObjectsSortMode.None)) r.ResetToStart();
            }
            else
            {
                Status = "Solo quien creó la partida puede reiniciar.";
            }
        }
    }
}
