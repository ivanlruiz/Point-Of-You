using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PointOfYou
{
    /// <summary>
    /// Orquesta la partida: ingreso de jugadores, anillo de miradas, pantalla dividida,
    /// checkpoints, reaparición y HUD. Sirve igual para local (varios en una PC) y online
    /// (un jugador por PC; ver <see cref="OnlineSession"/>).
    ///
    /// El anillo: J1 mira a J2, J2 mira a J3, ... y el último mira a J1.
    /// La pantalla de cada jugador se ve desde la cabeza de quien lo mira a él.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public const int MaxPlayers = 4;
        /// <summary>
        /// CameraMemory: relativo a la cámara, pero mientras mantenés una dirección el eje queda fijo (no te curva
        /// el movimiento cuando la cámara gira). CameraRelative: sigue a la cámara cuadro a cuadro. Tank: como un tanque.
        /// </summary>
        public enum ControlMode { CameraMemory, CameraRelative, Tank }
        /// <summary>Estado del editor de mapas: editando (sin jugadores ni HUD) o probando el mapa.</summary>
        public enum EditorMode { Off, Editing, Testing }

        public static GameManager Instance { get; private set; }

        [SerializeField] PlayerCharacter playerPrefab;
        [SerializeField] Checkpoint startCheckpoint;
        [SerializeField] Camera overviewCamera;
        [SerializeField] float killHeight = -6f;
        [SerializeField] Color skyColor = new(0.56f, 0.66f, 0.78f);
        [SerializeField] Color[] playerColors =
        {
            new(0.92f, 0.26f, 0.22f),
            new(0.22f, 0.46f, 0.96f),
            new(0.25f, 0.76f, 0.32f),
            new(0.98f, 0.78f, 0.15f),
        };

        // Sobreviven a F5 (recargar escena) para no tener que volver a sumarse.
        static readonly List<PlayerInputSource> rememberedInputs = new();
        static ControlMode controls = ControlMode.CameraMemory;
        static bool assistMarkers;
        static bool ringReversed;
        static bool showHelp;
        static bool cursorReleased;

        readonly List<PlayerCharacter> players = new();
        // Cada jugador reaparece en el último checkpoint que tocó ÉL (si fuera del equipo, R serviría para saltear puzzles).
        readonly Dictionary<PlayerCharacter, Checkpoint> playerCheckpoints = new();
        readonly Dictionary<PlayerCharacter, float> disconnectedSince = new();
        readonly Dictionary<string, float> joinHold = new();
        const float JoinHoldSeconds = 0.8f;
        Checkpoint[] checkpoints;
        Checkpoint checkpoint; // el más avanzado del equipo: define el objetivo que muestra el HUD
        bool paused;
        string toast = "";
        float toastUntil;
        float startTime = -1f;
        float completeElapsed = -1f;

        public IReadOnlyList<PlayerCharacter> Players => players;
        public ControlMode Controls => controls;
        public bool AssistMarkers => assistMarkers;
        public bool RingReversed => ringReversed;
        public bool LevelComplete => completeElapsed >= 0f;
        public float CompleteElapsed => completeElapsed;
        public float KillHeight => killHeight;
        public Checkpoint CurrentCheckpoint => checkpoint;
        /// <summary>Hay un campo de texto del HUD con el foco: no tomar teclas como input de juego.</summary>
        public bool UiHasKeyboardFocus { get; private set; }
        /// <summary>Lo maneja <see cref="MapEditor"/>.</summary>
        public EditorMode Editor { get; set; }

        bool ShowTitle => players.Count == 0 && !Net.IsOnline && Editor == EditorMode.Off;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            rememberedInputs.Clear();
            controls = ControlMode.CameraMemory;
            assistMarkers = false;
            ringReversed = false;
            showHelp = false;
            cursorReleased = false;
        }

        void Awake()
        {
            if (Instance && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            checkpoints = FindObjectsByType<Checkpoint>(FindObjectsSortMode.None).OrderBy(c => c.Order).ToArray();
            checkpoint = startCheckpoint;
            if (!GetComponent<AudioListener>()) gameObject.AddComponent<AudioListener>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            foreach (var source in rememberedInputs.ToArray())
                if (source.IsConnected) Join(source, remember: false);
            RefreshLayout();
        }

        void Update()
        {
            bool connecting = OnlineSession.Instance && OnlineSession.Instance.Busy;
            if (!Net.IsOnline && !connecting && !UiHasKeyboardFocus && Editor != EditorMode.Editing) HandleJoining();
            HandleGlobalKeys();
            UpdateCursor();
            foreach (var p in players)
                if (p.Simulate && p.transform.position.y < killHeight) Respawn(p);
            if (!Net.IsOnline) RemoveDisconnectedPlayers();
        }

        /// <summary>Un joystick desconectado deja una estatua que traba la meta: después de un momento se lo saca.</summary>
        void RemoveDisconnectedPlayers()
        {
            foreach (var p in players.ToArray())
            {
                if (p.InputSource == null || p.InputSource.IsConnected)
                {
                    disconnectedSince.Remove(p);
                    continue;
                }
                if (!disconnectedSince.TryGetValue(p, out float since)) disconnectedSince[p] = Time.unscaledTime;
                else if (Time.unscaledTime - since > 1.5f)
                {
                    ShowToast($"{p.DisplayName} se desconectó y salió de la partida.");
                    RemoveLocalPlayer(p);
                }
            }
        }

        // ---------------------------------------------------------------- jugadores

        /// <summary>
        /// En la pantalla de inicio alcanza con apretar. Con el nivel en marcha hay que MANTENER el botón un momento:
        /// un A suelto no puede sumar a alguien y reacomodar el anillo de todos.
        /// </summary>
        void HandleJoining()
        {
            if (players.Count >= MaxPlayers || paused) return;
            bool instant = players.Count == 0;
            var kb = Keyboard.current;
            if (kb != null)
            {
                JoinGesture("teclado-izq", kb.spaceKey.wasPressedThisFrame, kb.spaceKey.isPressed, instant,
                    () => new KeyboardInputSource(KeyboardInputSource.Side.Left));
                bool enterDown = kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                bool enterHeld = kb.enterKey.isPressed || kb.numpadEnterKey.isPressed;
                JoinGesture("teclado-der", enterDown, enterHeld, instant, () => new KeyboardInputSource(KeyboardInputSource.Side.Right));
            }
            foreach (var pad in Gamepad.all)
            {
                if (players.Count >= MaxPlayers) break;
                var device = pad;
                JoinGesture($"pad-{pad.deviceId}", pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame,
                    pad.buttonSouth.isPressed || pad.startButton.isPressed, instant, () => new GamepadInputSource(device));
            }
        }

        void JoinGesture(string key, bool pressedThisFrame, bool held, bool instant, Func<PlayerInputSource> create)
        {
            if (instant)
            {
                if (pressedThisFrame) TryJoin(create());
                return;
            }
            if (!held)
            {
                joinHold.Remove(key);
                return;
            }
            if (!joinHold.TryGetValue(key, out float since))
            {
                joinHold[key] = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - since < JoinHoldSeconds) return;
            joinHold[key] = float.PositiveInfinity; // no volver a disparar hasta soltar
            TryJoin(create());
        }

        void TryJoin(PlayerInputSource candidate)
        {
            foreach (var p in players)
                if (p.InputSource != null && p.InputSource.SameDevice(candidate)) return;
            Join(candidate);
        }

        /// <summary>Suma un jugador local (modo pantalla dividida).</summary>
        public PlayerCharacter Join(PlayerInputSource source, bool remember = true)
        {
            if (source == null || Net.IsOnline) return null;
            int index = NextFreeIndex();
            if (index < 0) return null;

            GetSpawn(index, out var position, out var rotation);
            var player = Instantiate(playerPrefab, position, rotation);
            Register(player, index, source, localView: true);

            if (remember && !rememberedInputs.Contains(source)) rememberedInputs.Add(source);
            return player;
        }

        /// <summary>
        /// Registra un personaje ya creado (local u online). Solo los que se juegan en esta PC
        /// tienen pantalla propia (<paramref name="localView"/>).
        /// </summary>
        public void Register(PlayerCharacter player, int index, PlayerInputSource source, bool localView)
        {
            if (startTime < 0f) startTime = Time.time;
            player.name = $"J{index + 1}";
            player.Initialize(index, playerColors[index % playerColors.Length], source, localView ? CreateView(index) : null);
            if (player.View) player.View.LookSource = source;
            if (checkpoint) playerCheckpoints[player] = checkpoint; // el que entra a mitad de nivel arranca donde va el equipo
            players.Add(player);
            players.Sort((a, b) => a.Index.CompareTo(b.Index));
            RefreshRing();
            RefreshLayout();
        }

        public void Unregister(PlayerCharacter player)
        {
            if (!players.Remove(player)) return;
            playerCheckpoints.Remove(player);
            disconnectedSince.Remove(player);
            ReleaseGrabsOn(player.Body);
            if (player.View) Destroy(player.View.gameObject);
            RefreshRing();
            RefreshLayout();
        }

        /// <summary>Saca a un jugador local (menú de pausa o joystick desconectado).</summary>
        public void RemoveLocalPlayer(PlayerCharacter player)
        {
            if (Net.IsOnline || !players.Contains(player)) return;
            rememberedInputs.RemoveAll(r => player.InputSource != null && r.SameDevice(player.InputSource));
            Unregister(player);
            Destroy(player.gameObject);
        }

        /// <summary>Suelta cualquier agarre sobre ese cuerpo (si no, el SpringJoint del otro lo sigue hasta el respawn).</summary>
        public void ReleaseGrabsOn(Rigidbody body)
        {
            if (!body) return;
            foreach (var p in players)
                if (p.GrabbedBody == body) p.Release(requireButtonRelease: true);
        }

        public int NextFreeIndex()
        {
            for (int i = 0; i < MaxPlayers; i++)
                if (!players.Exists(p => p.Index == i)) return i;
            return -1;
        }

        RingCamera CreateView(int index)
        {
            var go = new GameObject($"Vista J{index + 1}");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = skyColor;
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;
            cam.depth = index;
            return go.AddComponent<RingCamera>();
        }

        static int Mod(int a, int n) => (a % n + n) % n;

        /// <summary>Quién mira a este jugador (desde qué cabeza se ve su pantalla).</summary>
        public PlayerCharacter ObserverOf(PlayerCharacter player)
        {
            int n = players.Count;
            int i = players.IndexOf(player);
            if (n < 2 || i < 0) return null;
            return players[Mod(i - (ringReversed ? -1 : 1), n)];
        }

        void RefreshRing()
        {
            int n = players.Count;
            int step = ringReversed ? -1 : 1;
            for (int i = 0; i < n; i++)
            {
                if (players[i].View) players[i].View.SetObserver(n > 1 ? players[Mod(i - step, n)] : null);
                players[i].LookTarget = n > 1 ? players[Mod(i + step, n)] : null;
            }
        }

        void RefreshLayout()
        {
            var views = players.Where(p => p.View).Select(p => p.View).ToList();
            int n = views.Count;
            if (overviewCamera)
            {
                overviewCamera.enabled = n == 0 || n == 3;
                overviewCamera.rect = n == 0 ? new Rect(0f, 0f, 1f, 1f) : new Rect(0.5f, 0f, 0.5f, 0.5f);
            }

            for (int i = 0; i < n; i++)
                views[i].Cam.rect = ViewportFor(i, n);
        }

        static Rect ViewportFor(int i, int n)
        {
            switch (n)
            {
                case 1: return new Rect(0f, 0f, 1f, 1f);
                case 2: return new Rect(i * 0.5f, 0f, 0.5f, 1f);
                default:
                    // 2x2: arriba-izq, arriba-der, abajo-izq, abajo-der (con 3 jugadores, el 4º cuadro es la vista general).
                    return new Rect((i % 2) * 0.5f, i < 2 ? 0.5f : 0f, 0.5f, 0.5f);
            }
        }

        // ---------------------------------------------------------------- nivel

        public void Respawn(PlayerCharacter player)
        {
            GetSpawn(player.Index, CheckpointOf(player), out var position, out var rotation);
            ReleaseGrabsOn(player.Body);
            player.Respawn(position, rotation);
            // Corte en vez de vuelo: la cámara del que reaparece y la que vive en su cabeza saltan directo.
            foreach (var p in players)
                if (p.View && (p == player || p.View.Observer == player)) p.View.Snap();
        }

        public Checkpoint CheckpointOf(PlayerCharacter player) =>
            playerCheckpoints.TryGetValue(player, out var cp) && cp ? cp : startCheckpoint;

        /// <summary>Dónde aparece alguien que entra ahora: donde va el equipo.</summary>
        public void GetSpawn(int index, out Vector3 position, out Quaternion rotation) =>
            GetSpawn(index, checkpoint, out position, out rotation);

        static void GetSpawn(int index, Checkpoint at, out Vector3 position, out Quaternion rotation)
        {
            if (at)
            {
                at.GetSpawn(index, out position, out rotation);
                return;
            }
            position = new Vector3(index * 1.5f, 1f, 0f);
            rotation = Quaternion.identity;
        }

        public void ReachCheckpoint(Checkpoint cp, PlayerCharacter player)
        {
            var current = CheckpointOf(player);
            if (!current || cp.Order > current.Order) playerCheckpoints[player] = cp;
            if (!checkpoint || cp.Order > checkpoint.Order) checkpoint = cp;
        }

        public void CompleteLevel()
        {
            if (!LevelComplete) completeElapsed = Time.time - Mathf.Max(startTime, 0f);
        }

        /// <summary>Vuelve todo al inicio sin recargar la escena (lo usa el host online).</summary>
        public void ResetLevel()
        {
            checkpoint = startCheckpoint;
            playerCheckpoints.Clear();
            completeElapsed = -1f;
            startTime = Time.time;
            foreach (var p in players)
                if (p.Simulate) Respawn(p);
        }

        /// <summary>Cambia el nivel activo (lo llama <see cref="MapRuntime"/> al armar un mapa).</summary>
        public void SetLevel(Checkpoint start, Bounds levelBounds)
        {
            startCheckpoint = start;
            checkpoints = FindObjectsByType<Checkpoint>(FindObjectsSortMode.None).OrderBy(c => c.Order).ToArray();
            checkpoint = start;
            playerCheckpoints.Clear();
            completeElapsed = -1f;
            startTime = players.Count > 0 ? Time.time : -1f;
            FrameOverview(levelBounds);
            foreach (var p in players)
                if (p.Simulate) Respawn(p);
        }

        void FrameOverview(Bounds b)
        {
            if (!overviewCamera) return;
            float size = Mathf.Max(b.size.x, b.size.z);
            Vector3 direction = new Vector3(-0.4f, 1f, 0f).normalized;
            overviewCamera.transform.position = b.center + direction * (size * 0.55f + 12f);
            overviewCamera.transform.LookAt(b.center);
        }

        /// <summary>Saca a todos los jugadores locales (el editor lo usa al volver de "Probar").</summary>
        public void ClearLocalPlayers()
        {
            foreach (var p in players)
            {
                if (p.View) Destroy(p.View.gameObject);
                Destroy(p.gameObject);
            }
            players.Clear();
            playerCheckpoints.Clear();
            disconnectedSince.Clear();
            rememberedInputs.Clear();
            paused = false;
            completeElapsed = -1f;
            startTime = -1f;
            RefreshLayout();
        }

        /// <summary>En los clientes online: checkpoint y meta según el host.</summary>
        public void ApplyReplicatedLevel(int checkpointOrder, float completedAfter)
        {
            if (!checkpoint || checkpoint.Order != checkpointOrder)
                checkpoint = checkpoints.FirstOrDefault(c => c.Order == checkpointOrder) ?? checkpoint;
            completeElapsed = completedAfter;
        }

        void HandleGlobalKeys()
        {
            var kb = Keyboard.current;
            if (kb == null || UiHasKeyboardFocus || Editor == EditorMode.Editing) return;
            if (kb.f1Key.wasPressedThisFrame) showHelp = !showHelp;
            if (kb.f2Key.wasPressedThisFrame)
                controls = (ControlMode)(((int)controls + 1) % 3);
            if (kb.f3Key.wasPressedThisFrame) assistMarkers = !assistMarkers;
            if (kb.f4Key.wasPressedThisFrame)
            {
                if (Net.IsOnline)
                {
                    ShowToast("F4 (invertir el anillo) solo funciona jugando en una PC.");
                }
                else
                {
                    ringReversed = !ringReversed;
                    RefreshRing();
                }
            }
            if (kb.f5Key.wasPressedThisFrame) Restart();
            if (kb.f6Key.wasPressedThisFrame && Net.IsOnline && OnlineSession.Instance) OnlineSession.Instance.Leave();
        }

        /// <summary>
        /// Durante el juego el mouse queda capturado para "mirar alrededor". Esc abre la pausa (y suelta el mouse);
        /// fuera de la pausa, un clic lo vuelve a capturar.
        /// </summary>
        void UpdateCursor()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool playing = players.Count > 0 && Editor != EditorMode.Editing;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !UiHasKeyboardFocus)
            {
                if (playing) SetPaused(!paused);
                else cursorReleased = true;
            }
            if (!playing) paused = false;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && playing && !paused) cursorReleased = false;

            bool capture = playing && !paused && !cursorReleased && Application.isFocused;
            var mode = capture ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState == mode) return;
            Cursor.lockState = mode;
            Cursor.visible = !capture;
        }

        void SetPaused(bool value)
        {
            paused = value;
            cursorReleased = value;
        }

        void ShowToast(string message, float seconds = 4f)
        {
            toast = message;
            toastUntil = Time.unscaledTime + seconds;
        }

        void Restart()
        {
            if (Editor == EditorMode.Testing)
            {
                if (MapEditor.Instance) MapEditor.Instance.RestartTest();
                return;
            }
            if (Net.IsOnline)
            {
                if (OnlineSession.Instance) OnlineSession.Instance.RequestRestart();
                return;
            }
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else Debug.LogWarning("[PointOfYou] La escena no está en Build Settings: no se puede reiniciar con F5.");
        }

        // ---------------------------------------------------------------- HUD (IMGUI: suficiente para el prototipo)

        GUIStyle labelStyle, smallStyle, titleStyle, centerStyle, smallCenterStyle, buttonStyle, fieldStyle;
        Texture2D panelTexture, markerTexture;
        string codeInput = "";
        string addressInput = "127.0.0.1";
        string mapStatus = "";

        void OnGUI()
        {
            EnsureStyles();
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale, height = 1080f;

            if (Editor == EditorMode.Editing)
            {
                UiHasKeyboardFocus = false;
                return; // el editor dibuja su propia interfaz
            }

            if (ShowTitle)
            {
                // El menú necesita todos los eventos (clicks y teclas), no solo el repintado.
                DrawTitle(width, height);
                return;
            }
            UiHasKeyboardFocus = false;
            if (Event.current.type == EventType.Repaint) DrawHud(width, height, scale);
            if (paused) DrawPause(width, height);
        }

        void DrawHud(float width, float height, float scale)
        {
            foreach (var p in players)
            {
                if (!p.View) continue;
                Rect px = p.View.Cam.pixelRect;
                var r = new Rect(px.x / scale, (Screen.height - px.yMax) / scale, px.width / scale, px.height / scale);
                DrawViewportFrame(r);
                DrawViewportLabel(p, r);
                if (assistMarkers) DrawMarker(p, scale);
            }

            if (overviewCamera && overviewCamera.enabled)
            {
                Rect px = overviewCamera.pixelRect;
                var r = new Rect(px.x / scale, (Screen.height - px.yMax) / scale, px.width / scale, px.height / scale);
                DrawViewportFrame(r);
                DrawPanelText(new Vector2(r.x + 12, r.y + 12), Net.IsOnline ? "Esperando tu jugador…" : "Vista general", smallStyle);
            }

            if (Net.IsOnline) DrawOnlineBar(width);
            DrawBottomBar(width, height);

            if (LevelComplete) DrawComplete(width, height);
            if (showHelp) DrawHelp(width, height);
            if (Time.unscaledTime < toastUntil && !string.IsNullOrEmpty(toast))
            {
                Vector2 size = labelStyle.CalcSize(new GUIContent(toast));
                DrawPanelText(new Vector2((width - size.x) * 0.5f - 10f, height * 0.22f), toast, labelStyle);
            }
        }

        void DrawPause(float width, float height)
        {
            var local = Net.IsOnline ? new List<PlayerCharacter>() : players.Where(p => p.View).ToList();
            float h = 330f + local.Count * 58f;
            var r = new Rect((width - 520f) * 0.5f, (height - h) * 0.5f, 520f, h);
            GUI.DrawTexture(r, panelTexture);
            GUI.DrawTexture(r, panelTexture);
            GUI.Label(new Rect(r.x, r.y + 18f, r.width, 60f), "<b>PAUSA</b>", centerStyle);
            float y = r.y + 80f;
            float x = r.x + 40f, w = r.width - 80f;

            if (GUI.Button(new Rect(x, y, w, 50f), "Seguir (Esc)", buttonStyle)) SetPaused(false);
            y += 58f;
            if (Editor == EditorMode.Testing)
            {
                if (GUI.Button(new Rect(x, y, w, 50f), "Volver al editor (F10)", buttonStyle) && MapEditor.Instance)
                    MapEditor.Instance.StopTest();
            }
            else if (Net.IsOnline)
            {
                if (GUI.Button(new Rect(x, y, w, 50f), "Salir de la partida online (F6)", buttonStyle) && OnlineSession.Instance)
                    OnlineSession.Instance.Leave();
            }
            else if (GUI.Button(new Rect(x, y, w, 50f), "Volver al inicio", buttonStyle))
            {
                ClearLocalPlayers();
                var scene = SceneManager.GetActiveScene();
                if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            }
            y += 58f;

            foreach (var p in local)
            {
                string label = $"Sacar a {p.DisplayName} ({p.InputSource?.Label ?? "?"})";
                if (GUI.Button(new Rect(x, y, w, 50f), label, buttonStyle)) RemoveLocalPlayer(p);
                y += 58f;
            }

            if (GUI.Button(new Rect(x, y, w, 50f), "Salir del juego", buttonStyle)) Application.Quit();
            y += 66f;
            GUI.Label(new Rect(r.x, y, r.width, 40f), "Para sumar a alguien: mantené ESPACIO · ENTER · A", smallCenterStyle);
        }

        void EnsureStyles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, richText = true, wordWrap = false };
            labelStyle.normal.textColor = Color.white;
            smallStyle = new GUIStyle(labelStyle) { fontSize = 18 };
            titleStyle = new GUIStyle(labelStyle) { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            centerStyle = new GUIStyle(labelStyle) { fontSize = 22, alignment = TextAnchor.UpperCenter, wordWrap = true };
            smallCenterStyle = new GUIStyle(centerStyle) { fontSize = 18 };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 22 };
            fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 24, alignment = TextAnchor.MiddleCenter };

            panelTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            panelTexture.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.6f));
            panelTexture.Apply();
            markerTexture = Texture2D.whiteTexture;
        }

        static string Tag(PlayerCharacter p) => $"<color=#{ColorUtility.ToHtmlStringRGB(p.Color)}><b>{p.DisplayName}</b></color>";

        void DrawPanelText(Vector2 at, string text, GUIStyle style)
        {
            Vector2 size = style.CalcSize(new GUIContent(text));
            GUI.DrawTexture(new Rect(at.x, at.y, size.x + 20f, size.y + 8f), panelTexture);
            GUI.Label(new Rect(at.x + 10f, at.y + 4f, size.x + 4f, size.y), text, style);
        }

        void DrawViewportFrame(Rect r)
        {
            const float t = 2f;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), panelTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), panelTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), panelTexture);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), panelTexture);
        }

        void DrawViewportLabel(PlayerCharacter p, Rect r)
        {
            var observer = ObserverOf(p);
            string seenBy = observer ? $"visto por {Tag(observer)}" : "cámara de prueba (jugás solo)";
            DrawPanelText(new Vector2(r.x + 12f, r.y + 12f), $"{Tag(p)}  ·  {seenBy}", labelStyle);
            DrawPanelText(new Vector2(r.x + 12f, r.y + 52f), p.InputSource?.Label ?? "Esta PC", smallStyle);
        }

        // Ayuda opcional (F3): si no te ves en tu pantalla, un marcador indica dónde estás.
        void DrawMarker(PlayerCharacter p, float scale)
        {
            Camera cam = p.View.Cam;
            Vector3 target = p.ChestPosition;
            Vector3 sp = cam.WorldToScreenPoint(target);
            Rect px = cam.pixelRect;
            bool behind = sp.z < 0f;
            bool onScreen = !behind && px.Contains(new Vector2(sp.x, sp.y));
            bool occluded = onScreen &&
                Physics.Linecast(cam.transform.position, target, out var hit, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.transform.root != p.transform.root;
            if (onScreen && !occluded) return;

            if (behind)
            {
                sp.x = px.xMax - (sp.x - px.xMin);
                sp.y = px.yMin;
            }
            float margin = 28f * scale;
            float x = Mathf.Clamp(sp.x, px.xMin + margin, px.xMax - margin);
            float y = Mathf.Clamp(sp.y, px.yMin + margin, px.yMax - margin);
            var gui = new Vector2(x / scale, (Screen.height - y) / scale);

            GUI.DrawTexture(new Rect(gui.x - 15f, gui.y - 15f, 30f, 30f), panelTexture);
            GUI.color = p.Color;
            GUI.DrawTexture(new Rect(gui.x - 11f, gui.y - 11f, 22f, 22f), markerTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(gui.x + 18f, gui.y - 14f, 200f, 30f), occluded ? "detrás de algo" : "fuera de cuadro", smallStyle);
        }

        void DrawOnlineBar(float width)
        {
            var online = OnlineSession.Instance;
            string where = online && online.JoinCode != null ? $"código <b>{online.JoinCode}</b>" : "IP directa";
            string role = Net.IsHost ? "sos el host" : Net.IsConnected ? "conectado" : "conectando…";
            string mapName = MapRuntime.Instance ? MapRuntime.Instance.CurrentName : "Prototipo";
            string text = $"ONLINE · {where} · {role} · mapa {mapName} · {players.Count}/{MaxPlayers} jugadores · F6 salir";
            if (online && !string.IsNullOrEmpty(online.Status)) text += $"\n{online.Status}";
            Vector2 size = labelStyle.CalcSize(new GUIContent(text));
            DrawPanelText(new Vector2((width - size.x) * 0.5f - 10f, 12f), text, labelStyle);
        }

        void DrawBottomBar(float width, float height)
        {
            string objective = checkpoint ? checkpoint.Objective : "";
            if (players.Count == 1)
                objective = Net.IsOnline
                    ? "Esperando a que se sume alguien: pasales el código."
                    : "Estás solo: la cámara te sigue de atrás. Sumá otro jugador para probar la mecánica.";
            if (!string.IsNullOrEmpty(objective))
            {
                float w = Mathf.Min(1100f, width - 40f);
                float h = centerStyle.CalcHeight(new GUIContent(objective), w - 30f) + 14f;
                var rect = new Rect((width - w) * 0.5f, height - h - 56f, w, h);
                GUI.DrawTexture(rect, panelTexture);
                GUI.Label(new Rect(rect.x + 15f, rect.y + 7f, w - 30f, h), objective, centerStyle);
            }

            string join = !Net.IsOnline && players.Count < MaxPlayers ? "Sumarse: mantené ESPACIO · ENTER · A    " : "";
            if (Editor == EditorMode.Testing) join = "PROBANDO MAPA · F10 volver al editor    " + join;
            string mode = controls switch
            {
                ControlMode.CameraMemory => "cámara con memoria",
                ControlMode.CameraRelative => "cámara",
                _ => "tanque",
            };
            string status = $"{join}Esc pausa · F1 ayuda · F2 controles: {mode} · F3 marcador: {(assistMarkers ? "sí" : "no")} · F4 anillo · F5 reiniciar";
            Vector2 size = smallStyle.CalcSize(new GUIContent(status));
            DrawPanelText(new Vector2((width - size.x) * 0.5f - 10f, height - 44f), status, smallStyle);
        }

        void DrawTitle(float width, float height)
        {
            var online = OnlineSession.Instance;
            bool busy = online && online.Busy;

            GUI.DrawTexture(new Rect(0f, 120f, width, 880f), panelTexture);
            GUI.Label(new Rect(0f, 140f, width, 100f), "POINT OF YOU", titleStyle);
            string body =
                "Prototipo · Cada pantalla te muestra a VOS, desde los ojos de otro jugador.\n" +
                "Con 2 se miran entre sí. Con 3 o más es una ronda: J1 mira a J2, J2 mira a J3... y el último mira a J1.\n\n" +
                "<b>EN ESTA PC</b> (hasta 4, pantalla dividida): apretá  ESPACIO (WASD)  ·  ENTER (flechas)  ·  A / Start (joystick)";
            GUI.Label(new Rect(60f, 250f, width - 120f, 160f), body, centerStyle);

            float cx = width * 0.5f;

            // Mapa con el que se juega (local u online): el prototipo o uno hecho con el editor.
            GUI.enabled = !busy;
            GUI.Label(new Rect(0f, 405f, width, 40f), "<b>MAPA</b>", centerStyle);
            if (GUI.Button(new Rect(cx - 545f, 448f, 230f, 48f), "Pegar código de mapa", buttonStyle)) PasteMapCode();
            if (GUI.Button(new Rect(cx - 300f, 448f, 50f, 48f), "<", buttonStyle)) CycleMap(-1);
            string mapName = MapRuntime.Instance ? MapRuntime.Instance.CurrentName : "Prototipo";
            GUI.Label(new Rect(cx - 245f, 452f, 390f, 44f), $"<b>{mapName}</b>", centerStyle);
            if (GUI.Button(new Rect(cx + 150f, 448f, 50f, 48f), ">", buttonStyle)) CycleMap(1);
            if (GUI.Button(new Rect(cx + 215f, 448f, 230f, 48f), "Editor de mapas", buttonStyle) && MapEditor.Instance)
                MapEditor.Instance.Open(MapRuntime.SelectedMap?.Clone());
            if (!string.IsNullOrEmpty(mapStatus))
                GUI.Label(new Rect(60f, 500f, width - 120f, 30f), mapStatus, smallCenterStyle);

            GUI.Label(new Rect(0f, 530f, width, 40f), "<b>ONLINE</b> (una persona por PC, desde cualquier casa; se juega el mapa de quien crea la partida)", centerStyle);

            if (GUI.Button(new Rect(cx - 300f, 575f, 600f, 56f), "Crear partida online", buttonStyle) && online)
                online.HostWithCode();

            GUI.Label(new Rect(cx - 300f, 655f, 120f, 50f), "Código:", labelStyle);
            GUI.SetNextControlName("code");
            codeInput = GUI.TextField(new Rect(cx - 180f, 651f, 260f, 52f), codeInput, 12, fieldStyle).ToUpperInvariant();
            if (GUI.Button(new Rect(cx + 100f, 651f, 200f, 52f), "Unirse", buttonStyle) && online)
                online.JoinWithCode(codeInput);

            GUI.Label(new Rect(0f, 735f, width, 34f), "IP directa (misma red o pruebas en esta PC; por internet hay que abrir el puerto 7777):", smallCenterStyle);
            GUI.SetNextControlName("address");
            addressInput = GUI.TextField(new Rect(cx - 300f, 775f, 260f, 46f), addressInput, 64, fieldStyle);
            if (GUI.Button(new Rect(cx - 30f, 775f, 160f, 46f), "Hostear", buttonStyle) && online)
                online.HostDirect();
            if (GUI.Button(new Rect(cx + 140f, 775f, 160f, 46f), "Conectar", buttonStyle) && online)
                online.JoinDirect(addressInput);
            GUI.enabled = true;

            if (online && !string.IsNullOrEmpty(online.Status))
                GUI.Label(new Rect(60f, 840f, width - 120f, 100f), online.Status, centerStyle);

            GUI.Label(new Rect(0f, 950f, width, 40f), "F1 muestra todos los controles.", centerStyle);

            UiHasKeyboardFocus = !string.IsNullOrEmpty(GUI.GetNameOfFocusedControl());
            if (showHelp && Event.current.type == EventType.Repaint) DrawHelp(width, height);
        }

        /// <summary>Pasa al mapa guardado anterior/siguiente (el primero es siempre el prototipo).</summary>
        void CycleMap(int direction)
        {
            var names = new List<string> { null };
            names.AddRange(MapLibrary.List());
            var selected = MapRuntime.SelectedMap;
            int i = Mathf.Max(0, names.IndexOf(selected == null ? null : MapLibrary.FileNameFor(selected.name)));
            i = Mod(i + direction, names.Count);
            try
            {
                var map = names[i] == null ? null : MapLibrary.Load(names[i]);
                if (MapRuntime.Instance) MapRuntime.Instance.Select(map);
                mapStatus = names.Count == 1 ? "Todavía no hay mapas guardados: hacé uno con el editor o pegá un código." : "";
            }
            catch (Exception e)
            {
                mapStatus = $"No se pudo abrir \"{names[i]}\": {e.Message}";
            }
        }

        /// <summary>Toma un código de mapa del portapapeles, lo guarda en esta PC y lo elige.</summary>
        void PasteMapCode()
        {
            if (!MapData.TryFromCode(GUIUtility.systemCopyBuffer, out var map, out string error))
            {
                mapStatus = error;
                return;
            }
            bool same = MapLibrary.List().Contains(MapLibrary.FileNameFor(map.name)) &&
                        MapLibrary.Load(MapLibrary.FileNameFor(map.name)).ToJson() == map.ToJson();
            if (!same) MapLibrary.SaveAsNew(map);
            if (MapRuntime.Instance) MapRuntime.Instance.Select(map);
            mapStatus = $"Mapa \"{map.name}\" listo para jugar ({map.pieces.Count} piezas).";
        }

        void DrawComplete(float width, float height)
        {
            string time = $"{(int)(completeElapsed / 60f):00}:{(int)(completeElapsed % 60f):00}";
            string again = Net.IsOnline && !Net.IsHost ? "El host puede reiniciar con F5" : "F5 para jugar de nuevo";
            GUI.DrawTexture(new Rect(0f, height * 0.5f - 110f, width, 200f), panelTexture);
            GUI.Label(new Rect(0f, height * 0.5f - 100f, width, 100f), "¡NIVEL COMPLETO!", titleStyle);
            GUI.Label(new Rect(0f, height * 0.5f + 10f, width, 60f), $"Tiempo: {time}   ·   {again}", centerStyle);
        }

        void DrawHelp(float width, float height)
        {
            const string help =
                "<b>CONTROLES</b>\n\n" +
                "<b>Teclado izquierdo</b>   WASD mover · ESPACIO saltar · SHIFT IZQ / E agarrar (mantener) · R reaparecer\n" +
                "<b>Teclado derecho</b>   FLECHAS mover · ENTER / NUM 0 saltar · SHIFT DER / CTRL DER agarrar · BORRAR reaparecer\n" +
                "<b>Joystick</b>   stick / cruceta mover · A saltar · gatillos / RB / X agarrar · SELECT reaparecer\n" +
                "<b>Mirar alrededor</b>   mouse (jugador de WASD) o stick derecho; al soltarlo la vista vuelve a apuntarte · ESC suelta el mouse\n" +
                "Online cualquiera de esos controles maneja a tu jugador.\n\n" +
                "<b>Cómo funciona</b>\n" +
                "Tu pantalla está en la cabeza de otro jugador y siempre te apunta a vos.\n" +
                "\"Arriba\" te aleja de quien te mira; izquierda y derecha te hacen girar a su alrededor.\n" +
                "Si te tapa una pared, te perdés de vista: pedile al otro que se mueva.\n\n" +
                "<b>Teclas de prueba</b>\n" +
                "F2 controles relativos a la cámara / tanque · F3 marcador si no te ves · F4 invierte el sentido del anillo · F5 reinicia · F6 sale de la partida online";
            float w = Mathf.Min(1300f, width - 60f);
            float h = centerStyle.CalcHeight(new GUIContent(help), w - 40f) + 30f;
            var rect = new Rect((width - w) * 0.5f, (height - h) * 0.5f, w, h);
            GUI.DrawTexture(rect, panelTexture);
            GUI.DrawTexture(rect, panelTexture);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 15f, w - 40f, h), help, centerStyle);
        }
    }
}
