using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PointOfYou
{
    /// <summary>
    /// Orquesta la partida local: ingreso de jugadores, anillo de miradas, pantalla dividida,
    /// checkpoints, reaparición y HUD.
    ///
    /// El anillo: J1 mira a J2, J2 mira a J3, ... y el último mira a J1.
    /// La pantalla de cada jugador se ve desde la cabeza de quien lo mira a él.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        public const int MaxPlayers = 4;
        public enum ControlMode { CameraRelative, Tank }

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
        static ControlMode controls = ControlMode.CameraRelative;
        static bool assistMarkers;
        static bool ringReversed;
        static bool showHelp;

        readonly List<PlayerCharacter> players = new();
        Checkpoint checkpoint;
        float startTime = -1f;
        float completeTime = -1f;

        public IReadOnlyList<PlayerCharacter> Players => players;
        public ControlMode Controls => controls;
        public bool AssistMarkers => assistMarkers;
        public bool RingReversed => ringReversed;
        public bool LevelComplete => completeTime >= 0f;
        public float KillHeight => killHeight;
        public Checkpoint CurrentCheckpoint => checkpoint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null;
            rememberedInputs.Clear();
            controls = ControlMode.CameraRelative;
            assistMarkers = false;
            ringReversed = false;
            showHelp = false;
        }

        void Awake()
        {
            if (Instance && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
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
            HandleJoining();
            HandleGlobalKeys();
            foreach (var p in players)
                if (p.transform.position.y < killHeight) Respawn(p);
        }

        // ---------------------------------------------------------------- jugadores

        void HandleJoining()
        {
            if (players.Count >= MaxPlayers) return;

            if (KeyboardInputSource.JoinPressed(KeyboardInputSource.Side.Left))
                TryJoin(new KeyboardInputSource(KeyboardInputSource.Side.Left));
            if (KeyboardInputSource.JoinPressed(KeyboardInputSource.Side.Right))
                TryJoin(new KeyboardInputSource(KeyboardInputSource.Side.Right));

            foreach (var pad in Gamepad.all)
            {
                if (players.Count >= MaxPlayers) break;
                if (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)
                    TryJoin(new GamepadInputSource(pad));
            }
        }

        void TryJoin(PlayerInputSource candidate)
        {
            foreach (var p in players)
                if (p.InputSource.SameDevice(candidate)) return;
            Join(candidate);
        }

        public PlayerCharacter Join(PlayerInputSource source, bool remember = true)
        {
            if (source == null || players.Count >= MaxPlayers) return null;

            int index = players.Count;
            if (startTime < 0f) startTime = Time.time;

            GetSpawn(index, out var position, out var rotation);
            var player = Instantiate(playerPrefab, position, rotation);
            player.name = $"J{index + 1}";
            player.Initialize(index, playerColors[index % playerColors.Length], source, CreateView(index));
            players.Add(player);

            if (remember && !rememberedInputs.Contains(source)) rememberedInputs.Add(source);

            RefreshRing();
            RefreshLayout();
            return player;
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

        void RefreshRing()
        {
            int n = players.Count;
            int step = ringReversed ? -1 : 1;
            for (int i = 0; i < n; i++)
            {
                players[i].View.SetObserver(n > 1 ? players[Mod(i - step, n)] : null);
                players[i].LookTarget = n > 1 ? players[Mod(i + step, n)] : null;
            }
        }

        void RefreshLayout()
        {
            int n = players.Count;
            if (overviewCamera)
            {
                overviewCamera.enabled = n == 0 || n == 3;
                overviewCamera.rect = n == 0 ? new Rect(0f, 0f, 1f, 1f) : new Rect(0.5f, 0f, 0.5f, 0.5f);
            }

            for (int i = 0; i < n; i++)
                players[i].View.Cam.rect = ViewportFor(i, n);
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
            GetSpawn(player.Index, out var position, out var rotation);
            player.Respawn(position, rotation);
        }

        void GetSpawn(int index, out Vector3 position, out Quaternion rotation)
        {
            if (checkpoint)
            {
                checkpoint.GetSpawn(index, out position, out rotation);
                return;
            }
            position = new Vector3(index * 1.5f, 1f, 0f);
            rotation = Quaternion.identity;
        }

        public void ReachCheckpoint(Checkpoint cp)
        {
            if (!checkpoint || cp.Order > checkpoint.Order) checkpoint = cp;
        }

        public void CompleteLevel()
        {
            if (!LevelComplete) completeTime = Time.time;
        }

        void HandleGlobalKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) showHelp = !showHelp;
            if (kb.f2Key.wasPressedThisFrame)
                controls = controls == ControlMode.CameraRelative ? ControlMode.Tank : ControlMode.CameraRelative;
            if (kb.f3Key.wasPressedThisFrame) assistMarkers = !assistMarkers;
            if (kb.f4Key.wasPressedThisFrame)
            {
                ringReversed = !ringReversed;
                RefreshRing();
            }
            if (kb.f5Key.wasPressedThisFrame) Restart();
        }

        void Restart()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else Debug.LogWarning("[PointOfYou] La escena no está en Build Settings: no se puede reiniciar con F5.");
        }

        // ---------------------------------------------------------------- HUD (IMGUI: suficiente para el prototipo)

        GUIStyle labelStyle, smallStyle, titleStyle, centerStyle;
        Texture2D panelTexture, markerTexture;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();

            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale, height = 1080f;

            if (players.Count == 0)
            {
                DrawTitle(width, height);
                return;
            }

            foreach (var p in players)
            {
                Rect px = p.View.Cam.pixelRect;
                var r = new Rect(px.x / scale, (Screen.height - px.yMax) / scale, px.width / scale, px.height / scale);
                DrawViewportFrame(r);
                DrawViewportLabel(p, r);
                if (assistMarkers) DrawMarker(p, scale);
            }

            if (players.Count == 3 && overviewCamera)
            {
                Rect px = overviewCamera.pixelRect;
                var r = new Rect(px.x / scale, (Screen.height - px.yMax) / scale, px.width / scale, px.height / scale);
                DrawViewportFrame(r);
                DrawPanelText(new Vector2(r.x + 12, r.y + 12), "Vista general", smallStyle);
            }

            DrawBottomBar(width, height);

            if (LevelComplete) DrawComplete(width, height);
            if (showHelp) DrawHelp(width, height);
        }

        void EnsureStyles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 24, richText = true, wordWrap = false };
            labelStyle.normal.textColor = Color.white;
            smallStyle = new GUIStyle(labelStyle) { fontSize = 18 };
            titleStyle = new GUIStyle(labelStyle) { fontSize = 72, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            centerStyle = new GUIStyle(labelStyle) { fontSize = 22, alignment = TextAnchor.UpperCenter, wordWrap = true };

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
            var observer = p.View.Observer;
            string seenBy = observer ? $"visto por {Tag(observer)}" : "cámara de prueba (jugás solo)";
            DrawPanelText(new Vector2(r.x + 12f, r.y + 12f), $"{Tag(p)}  ·  {seenBy}", labelStyle);
            DrawPanelText(new Vector2(r.x + 12f, r.y + 52f), p.InputSource.Label, smallStyle);
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
                hit.rigidbody != p.Body;
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

        void DrawBottomBar(float width, float height)
        {
            string objective = checkpoint ? checkpoint.Objective : "";
            if (players.Count == 1)
                objective = "Estás solo: la cámara te sigue de atrás. Sumá otro jugador para probar la mecánica.";
            if (!string.IsNullOrEmpty(objective))
            {
                float w = Mathf.Min(1100f, width - 40f);
                float h = centerStyle.CalcHeight(new GUIContent(objective), w - 30f) + 14f;
                var rect = new Rect((width - w) * 0.5f, height - h - 56f, w, h);
                GUI.DrawTexture(rect, panelTexture);
                GUI.Label(new Rect(rect.x + 15f, rect.y + 7f, w - 30f, h), objective, centerStyle);
            }

            string join = players.Count < MaxPlayers ? "Sumarse: ESPACIO · ENTER · A (joystick)    " : "";
            string mode = controls == ControlMode.CameraRelative ? "relativos a la cámara" : "tanque";
            string status = $"{join}F1 ayuda · F2 controles: {mode} · F3 marcador: {(assistMarkers ? "sí" : "no")} · F4 anillo · F5 reiniciar";
            Vector2 size = smallStyle.CalcSize(new GUIContent(status));
            DrawPanelText(new Vector2((width - size.x) * 0.5f - 10f, height - 44f), status, smallStyle);
        }

        void DrawTitle(float width, float height)
        {
            GUI.DrawTexture(new Rect(0f, height * 0.5f - 230f, width, 400f), panelTexture);
            GUI.Label(new Rect(0f, height * 0.5f - 210f, width, 100f), "POINT OF YOU", titleStyle);
            string body =
                "Prototipo · Cada pantalla te muestra a VOS, desde los ojos de otro jugador.\n" +
                "Con 2 se miran entre sí. Con 3 o más es una ronda: J1 mira a J2, J2 mira a J3... y el último mira a J1.\n\n" +
                "Para sumarte apretá:  ESPACIO (teclado izquierdo, WASD)  ·  ENTER (teclado derecho, flechas)  ·  A / Start (joystick)\n" +
                "Hasta 4 jugadores en esta PC.  F1 muestra todos los controles.";
            GUI.Label(new Rect(60f, height * 0.5f - 90f, width - 120f, 260f), body, centerStyle);
            if (showHelp) DrawHelp(width, height);
        }

        void DrawComplete(float width, float height)
        {
            float t = completeTime - Mathf.Max(startTime, 0f);
            string time = $"{(int)(t / 60f):00}:{(int)(t % 60f):00}";
            GUI.DrawTexture(new Rect(0f, height * 0.5f - 110f, width, 200f), panelTexture);
            GUI.Label(new Rect(0f, height * 0.5f - 100f, width, 100f), "¡NIVEL COMPLETO!", titleStyle);
            GUI.Label(new Rect(0f, height * 0.5f + 10f, width, 60f), $"Tiempo: {time}   ·   F5 para jugar de nuevo", centerStyle);
        }

        void DrawHelp(float width, float height)
        {
            const string help =
                "<b>CONTROLES</b>\n\n" +
                "<b>Teclado izquierdo</b>   WASD mover · ESPACIO saltar · SHIFT IZQ / E agarrar (mantener) · R reaparecer\n" +
                "<b>Teclado derecho</b>   FLECHAS mover · ENTER / NUM 0 saltar · SHIFT DER / CTRL DER agarrar · BORRAR reaparecer\n" +
                "<b>Joystick</b>   stick / cruceta mover · A saltar · gatillos / RB / X agarrar · SELECT reaparecer\n\n" +
                "<b>Cómo funciona</b>\n" +
                "Tu pantalla está en la cabeza de otro jugador y siempre te apunta a vos.\n" +
                "\"Arriba\" te aleja de quien te mira; izquierda y derecha te hacen girar a su alrededor.\n" +
                "Si te tapa una pared, te perdés de vista: pedile al otro que se mueva.\n\n" +
                "<b>Teclas de prueba</b>\n" +
                "F2 alterna controles relativos a la cámara / tanque · F3 marcador si no te ves · F4 invierte el sentido del anillo · F5 reinicia";
            float w = Mathf.Min(1300f, width - 60f);
            float h = centerStyle.CalcHeight(new GUIContent(help), w - 40f) + 30f;
            var rect = new Rect((width - w) * 0.5f, (height - h) * 0.5f, w, h);
            GUI.DrawTexture(rect, panelTexture);
            GUI.DrawTexture(rect, panelTexture);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 15f, w - 40f, h), help, centerStyle);
        }
    }
}
