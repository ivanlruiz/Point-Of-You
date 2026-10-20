using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PointOfYou
{
    /// <summary>
    /// Editor de mapas dentro del juego. Edita un <see cref="MapData"/> y lo arma con <see cref="MapRuntime"/>
    /// en cada cambio. Desde acá se prueba el mapa (con jugadores locales), se guarda y se comparte como código.
    /// </summary>
    public class MapEditor : MonoBehaviour
    {
        sealed class ToolInfo
        {
            public PieceType Type;
            public string Name;
            public Key Key;
            public string KeyLabel;
            public bool Paint;     // se arrastra para pintar un rectángulo
            public int W = 1, D = 1;
            public bool Rotates;
            public bool Channel;
            public Color Color;
        }

        static readonly ToolInfo[] Tools =
        {
            new() { Type = PieceType.Floor, Name = "Piso", Key = Key.Digit1, KeyLabel = "1", Paint = true, Color = new Color(0.75f, 0.76f, 0.8f) },
            new() { Type = PieceType.Wall, Name = "Pared", Key = Key.Digit2, KeyLabel = "2", Paint = true, Color = new Color(0.47f, 0.49f, 0.54f) },
            new() { Type = PieceType.Window, Name = "Ventana", Key = Key.Digit3, KeyLabel = "3", Paint = true, Color = new Color(0.6f, 0.75f, 0.9f) },
            new() { Type = PieceType.Block, Name = "Bloque", Key = Key.Digit4, KeyLabel = "4", Paint = true, Color = new Color(0.56f, 0.58f, 0.62f) },
            new() { Type = PieceType.Stone, Name = "Piedra", Key = Key.Digit5, KeyLabel = "5", Paint = true, Color = new Color(0.36f, 0.5f, 0.64f) },
            new() { Type = PieceType.Plate, Name = "Placa", Key = Key.Digit6, KeyLabel = "6", W = 2, D = 2, Channel = true },
            // De 1 celda: en un vano ancho alguien la traba con el cuerpo y otro pasa al costado.
            new() { Type = PieceType.Door, Name = "Puerta", Key = Key.Digit7, KeyLabel = "7", W = 1, D = 1, Rotates = true, Channel = true },
            new() { Type = PieceType.Box, Name = "Caja", Key = Key.Digit8, KeyLabel = "8", Color = new Color(0.62f, 0.43f, 0.25f) },
            new() { Type = PieceType.Start, Name = "Inicio", Key = Key.Digit9, KeyLabel = "9", W = 3, D = 3, Color = new Color(0.35f, 0.75f, 1f) },
            new() { Type = PieceType.Checkpoint, Name = "Checkpoint", Key = Key.Digit0, KeyLabel = "0", W = 3, D = 3, Color = new Color(0.3f, 0.95f, 0.85f) },
            new() { Type = PieceType.Goal, Name = "Meta", Key = Key.Minus, KeyLabel = "-", W = 3, D = 3, Color = new Color(0.2f, 0.85f, 0.35f) },
        };

        const int MaxUndo = 100;
        const int GridHalf = 30;

        [SerializeField] Material cursorMaterial;
        [SerializeField] Material gridMaterial;

        GameManager gm;
        MapRuntime runtime;
        MapData map;
        readonly List<string> undo = new();
        int toolIndex, level, rotation, channel;
        bool dirty;
        string status = "";

        Camera cam;
        Transform gridRoot, cursor;
        Material cursorTint;
        Vector3 pivot;
        float yaw = 30f, pitch = 55f, distance = 28f;

        Vector2Int hoverCell, dragStart;
        bool hoverValid, dragging;

        bool textFocus, clearFocus, showLoad;
        List<string> loadList = new();
        readonly List<Rect> uiAreas = new();

        public static MapEditor Instance { get; private set; }
        public MapData Map => map;
        public bool IsEditing => gm && gm.Editor == GameManager.EditorMode.Editing;
        public bool IsTesting => gm && gm.Editor == GameManager.EditorMode.Testing;
        public int Level => level;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- abrir / cerrar / probar

        /// <summary>Abre el editor con una copia del mapa dado (null: mapa nuevo).</summary>
        public void Open(MapData initial)
        {
            gm = GameManager.Instance;
            runtime = MapRuntime.Instance;
            if (!gm || !runtime || Net.IsOnline) return;

            gm.ClearLocalPlayers();
            map = initial ?? MapData.CreateDefault();
            undo.Clear();
            dirty = false;
            showLoad = false;
            status = initial == null ? "Mapa nuevo. Clic izquierdo pone, arrastrá para pintar, clic derecho borra." : $"Editando \"{map.name}\".";
            EnsureVisuals();
            gm.Editor = GameManager.EditorMode.Editing;
            SetEditorVisible(true);
            Rebuild();
            FrameMap();
        }

        public void Close()
        {
            if (!gm) return;
            gm.ClearLocalPlayers();
            gm.Editor = GameManager.EditorMode.Off;
            SetEditorVisible(false);
            runtime.Select(map); // queda elegido para jugar desde la pantalla de inicio
        }

        public bool StartTest()
        {
            if (map.FindFirst(PieceType.Start) == null)
            {
                status = "Falta el Inicio (tecla 9): es donde aparecen los jugadores.";
                return false;
            }
            gm.Editor = GameManager.EditorMode.Testing;
            SetEditorVisible(false);
            runtime.Build(map);
            return true;
        }

        public void StopTest()
        {
            gm.ClearLocalPlayers();
            gm.Editor = GameManager.EditorMode.Editing;
            SetEditorVisible(true);
            Rebuild();
        }

        /// <summary>F5 mientras se prueba: el mapa vuelve al estado inicial y los jugadores al Inicio.</summary>
        public void RestartTest()
        {
            foreach (var p in gm.Players) p.Release();
            runtime.Build(map);
        }

        void Rebuild() => runtime.Build(map, editing: IsEditing);

        // ---------------------------------------------------------------- edición (también la usan los tests)

        static ToolInfo ToolFor(PieceType type) => System.Array.Find(Tools, t => t.Type == type);

        void PushUndo()
        {
            undo.Add(map.ToJson());
            if (undo.Count > MaxUndo) undo.RemoveAt(0);
            dirty = true;
        }

        /// <summary>Pone una pieza. Las pintables ocupan el rectángulo a-b; las de tamaño fijo se centran en a.</summary>
        public void Place(PieceType type, Vector2Int a, Vector2Int b, int atLevel, int rot = 0, int ch = 0)
        {
            var tool = ToolFor(type);
            var piece = new MapPiece { type = type, y = Mathf.Clamp(atLevel, 0, MapData.MaxLevel), channel = tool.Channel ? ch : 0 };
            if (tool.Paint)
            {
                var min = Vector2Int.Min(a, b);
                var max = Vector2Int.Max(a, b);
                piece.x = min.x;
                piece.z = min.y;
                piece.w = Mathf.Min(max.x - min.x + 1, 60);
                piece.d = Mathf.Min(max.y - min.y + 1, 60);
            }
            else
            {
                piece.w = tool.W;
                piece.d = tool.D;
                piece.rot = tool.Rotates ? rot % 4 : 0;
                var f = piece.Footprint;
                piece.x = a.x - (f.x - 1) / 2;
                piece.z = a.y - (f.y - 1) / 2;
            }
            piece.x = Mathf.Clamp(piece.x, -MapData.MaxCoordinate, MapData.MaxCoordinate);
            piece.z = Mathf.Clamp(piece.z, -MapData.MaxCoordinate, MapData.MaxCoordinate);

            PushUndo();
            if (type == PieceType.Start) map.pieces.RemoveAll(p => p.type == PieceType.Start);
            map.pieces.Add(piece);
            Rebuild();
        }

        /// <summary>Borra la última pieza puesta que ocupa esa celda en ese nivel.</summary>
        public bool EraseAt(Vector2Int cell, int atLevel)
        {
            int index = map.pieces.FindLastIndex(p => p.y == atLevel && p.Covers(cell.x, cell.y));
            if (index < 0) return false;
            PushUndo();
            map.pieces.RemoveAt(index);
            Rebuild();
            return true;
        }

        public bool Undo()
        {
            if (undo.Count == 0) return false;
            map = MapData.FromJson(undo[^1]);
            undo.RemoveAt(undo.Count - 1);
            Rebuild();
            status = "Deshecho.";
            return true;
        }

        void Save()
        {
            if (string.IsNullOrWhiteSpace(map.name)) map.name = "Mapa sin nombre";
            string path = MapLibrary.Save(map);
            dirty = false;
            status = $"Guardado: {path}";
        }

        void LoadMap(string fileName)
        {
            try
            {
                var loaded = MapLibrary.Load(fileName);
                PushUndo();
                map = loaded;
                Rebuild();
                FrameMap();
                dirty = false;
                status = $"Abierto \"{map.name}\".";
            }
            catch (System.Exception e)
            {
                status = $"No se pudo abrir: {e.Message}";
            }
            showLoad = false;
        }

        void CopyCode()
        {
            string code = map.ToCode();
            GUIUtility.systemCopyBuffer = code;
            status = $"Código copiado al portapapeles ({code.Length} caracteres). Pegalo en un chat; el otro lo usa con \"Pegar código de mapa\".";
        }

        void PasteCode()
        {
            if (!MapData.TryFromCode(GUIUtility.systemCopyBuffer, out var pasted, out string error))
            {
                status = error;
                return;
            }
            PushUndo();
            map = pasted;
            Rebuild();
            FrameMap();
            status = $"Pegado \"{map.name}\" ({map.pieces.Count} piezas).";
        }

        /// <summary>Reemplaza el mapa por uno procedural (semilla al azar) para retocarlo a mano.</summary>
        void GenerateIntoEditor()
        {
            int seed = Random.Range(1, 100000);
            PushUndo();
            map = MapGenerator.Generate(seed, new GeneratorSettings());
            Rebuild();
            FrameMap();
            status = $"Generado con la semilla {seed}. Ctrl+Z vuelve al mapa anterior.";
        }

        void NewMap()
        {
            PushUndo();
            map = MapData.CreateDefault();
            Rebuild();
            FrameMap();
            status = "Mapa nuevo.";
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (!gm || gm.Editor == GameManager.EditorMode.Off) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (IsTesting)
            {
                if (kb != null && kb.f10Key.wasPressedThisFrame) StopTest();
                return;
            }

            if (kb != null && !textFocus) HandleKeys(kb);
            if (!IsEditing) return; // HandleKeys pudo cerrar el editor o empezar la prueba
            HandleCamera(kb, mouse);
            HandleMouse(mouse);
            UpdateCursorVisual();
        }

        void HandleKeys(Keyboard kb)
        {
            bool ctrl = kb.ctrlKey.isPressed;
            for (int i = 0; i < Tools.Length; i++)
                if (kb[Tools[i].Key].wasPressedThisFrame) toolIndex = i;
            if (kb.rKey.wasPressedThisFrame) rotation = (rotation + 1) % 4;
            if (kb.cKey.wasPressedThisFrame) channel = (channel + 1) % MapRuntime.ChannelColors.Length;
            if (kb.pageUpKey.wasPressedThisFrame) ChangeLevel(1);
            if (kb.pageDownKey.wasPressedThisFrame) ChangeLevel(-1);
            if (kb.fKey.wasPressedThisFrame) FrameMap();
            if (ctrl && kb.zKey.wasPressedThisFrame) Undo();
            if (ctrl && kb.sKey.wasPressedThisFrame) Save();
            if (kb.pKey.wasPressedThisFrame) StartTest();
            else if (kb.escapeKey.wasPressedThisFrame)
            {
                if (showLoad) showLoad = false;
                else Close();
            }
        }

        void ChangeLevel(int delta)
        {
            level = Mathf.Clamp(level + delta, 0, MapData.MaxLevel);
            status = $"Altura {level} ({level * MapData.LevelHeight:0.0} m). Un bloque mide 2 alturas; el salto llega a ~2,8.";
        }

        void HandleCamera(Keyboard kb, Mouse mouse)
        {
            float dt = Time.unscaledDeltaTime;
            if (kb != null && !textFocus && !kb.ctrlKey.isPressed)
            {
                var forward = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
                var right = new Vector3(forward.z, 0f, -forward.x);
                var move = Vector3.zero;
                if (kb.wKey.isPressed) move += forward;
                if (kb.sKey.isPressed) move -= forward;
                if (kb.dKey.isPressed) move += right;
                if (kb.aKey.isPressed) move -= right;
                float speed = distance * (kb.shiftKey.isPressed ? 1.8f : 0.9f);
                pivot += move.normalized * (speed * dt);
                if (kb.qKey.isPressed) yaw -= 90f * dt;
                if (kb.eKey.isPressed) yaw += 90f * dt;
            }

            if (mouse != null)
            {
                bool orbit = mouse.middleButton.isPressed || (kb != null && kb.altKey.isPressed && mouse.leftButton.isPressed);
                if (orbit)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * 0.25f;
                    pitch = Mathf.Clamp(pitch - delta.y * 0.25f, 15f, 88f);
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !IsOverUi(mouse.position.ReadValue()))
                {
                    if (kb != null && kb.shiftKey.isPressed) ChangeLevel(scroll > 0f ? 1 : -1);
                    else distance = Mathf.Clamp(distance * (scroll > 0f ? 0.88f : 1.14f), 5f, 120f);
                }
            }

            pivot.y = level * MapData.LevelHeight;
            cam.transform.position = pivot + Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;
            cam.transform.LookAt(pivot);
            gridRoot.position = new Vector3(Mathf.Round(pivot.x), pivot.y + 0.01f, Mathf.Round(pivot.z));
        }

        void HandleMouse(Mouse mouse)
        {
            hoverValid = false;
            if (mouse == null) return;
            Vector2 mp = mouse.position.ReadValue();
            bool overUi = IsOverUi(mp);

            Ray ray = cam.ScreenPointToRay(mp);
            float planeY = level * MapData.LevelHeight;
            if (Mathf.Abs(ray.direction.y) > 1e-4f)
            {
                float t = (planeY - ray.origin.y) / ray.direction.y;
                if (t > 0f)
                {
                    Vector3 hit = ray.GetPoint(t);
                    hoverCell = new Vector2Int(Mathf.FloorToInt(hit.x), Mathf.FloorToInt(hit.z));
                    hoverValid = true;
                }
            }

            bool alt = Keyboard.current != null && Keyboard.current.altKey.isPressed;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (overUi) return;
                clearFocus = true;
                if (hoverValid && !alt)
                {
                    dragging = true;
                    dragStart = hoverCell;
                }
            }
            if (dragging && mouse.leftButton.wasReleasedThisFrame)
            {
                dragging = false;
                if (hoverValid) Place(Tools[toolIndex].Type, dragStart, hoverCell, level, rotation, channel);
            }
            if (mouse.rightButton.wasPressedThisFrame && !overUi && hoverValid)
            {
                clearFocus = true;
                if (!EraseAt(hoverCell, level)) status = $"No hay nada en altura {level} en esa celda (PgUp/PgDn cambia la altura).";
            }
        }

        bool IsOverUi(Vector2 screenPosition)
        {
            float scale = Screen.height / 1080f;
            var gui = new Vector2(screenPosition.x / scale, (Screen.height - screenPosition.y) / scale);
            foreach (var r in uiAreas)
                if (r.Contains(gui)) return true;
            return false;
        }

        // ---------------------------------------------------------------- visuales del editor

        void EnsureVisuals()
        {
            if (cam) return;
            cam = new GameObject("Cámara del editor").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.56f, 0.66f, 0.78f);
            cam.fieldOfView = 55f;
            cam.farClipPlane = 600f;
            cam.depth = 50f;

            var lineMaterial = new Material(gridMaterial);
            lineMaterial.SetColor("_BaseColor", new Color(0.12f, 0.14f, 0.18f));
            gridRoot = new GameObject("Grilla del editor").transform;
            for (int i = -GridHalf; i <= GridHalf; i++)
            {
                bool major = i % 5 == 0;
                float thickness = major ? 0.05f : 0.02f;
                Line(new Vector3(i, 0f, 0f), new Vector3(thickness, 0.01f, GridHalf * 2f), lineMaterial);
                Line(new Vector3(0f, 0f, i), new Vector3(GridHalf * 2f, 0.01f, thickness), lineMaterial);
            }

            cursorTint = new Material(cursorMaterial);
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "Cursor del editor";
            Destroy(c.GetComponent<Collider>());
            c.GetComponent<Renderer>().sharedMaterial = cursorTint;
            cursor = c.transform;
        }

        void Line(Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(gridRoot, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void SetEditorVisible(bool visible)
        {
            if (!cam) return;
            cam.enabled = visible;
            gridRoot.gameObject.SetActive(visible);
            cursor.gameObject.SetActive(visible);
        }

        void UpdateCursorVisual()
        {
            cursor.gameObject.SetActive(hoverValid);
            if (!hoverValid) return;
            var tool = Tools[toolIndex];
            Vector2Int min, size;
            if (tool.Paint)
            {
                var a = dragging ? dragStart : hoverCell;
                min = Vector2Int.Min(a, hoverCell);
                size = Vector2Int.Max(a, hoverCell) - min + Vector2Int.one;
            }
            else
            {
                size = tool.Rotates && rotation % 2 == 1 ? new Vector2Int(tool.D, tool.W) : new Vector2Int(tool.W, tool.D);
                min = hoverCell - new Vector2Int((size.x - 1) / 2, (size.y - 1) / 2);
            }
            float y = level * MapData.LevelHeight;
            cursor.position = new Vector3(min.x + size.x * 0.5f, y + 0.05f, min.y + size.y * 0.5f);
            cursor.localScale = new Vector3(size.x - 0.04f, 0.08f, size.y - 0.04f);
            Color color = tool.Channel ? MapRuntime.ChannelColors[channel] : tool.Color;
            cursorTint.SetColor("_BaseColor", color);
            cursorTint.SetColor("_EmissionColor", color * 0.5f);
        }

        void FrameMap()
        {
            var b = runtime.LevelBounds;
            pivot = new Vector3(b.center.x, level * MapData.LevelHeight, b.center.z);
            distance = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.9f, 12f, 90f);
        }

        // ---------------------------------------------------------------- interfaz

        GUIStyle label, small, button, selected, field, panelLabel;
        Texture2D panel;

        void OnGUI()
        {
            if (!IsEditing) return;
            EnsureStyles();
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale, height = 1080f;

            if (clearFocus)
            {
                clearFocus = false;
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
            }
            if (Event.current.type == EventType.Layout) uiAreas.Clear();

            // Fila 1: piezas
            var top = new Rect(0f, 0f, width, 124f);
            Area(top);
            GUI.DrawTexture(top, panel);
            float x = 10f;
            for (int i = 0; i < Tools.Length; i++)
            {
                var t = Tools[i];
                if (GUI.Button(new Rect(x, 10f, 118f, 46f), $"{t.KeyLabel}  {t.Name}", i == toolIndex ? selected : button)) toolIndex = i;
                x += 124f;
            }
            if (GUI.Button(new Rect(x + 10f, 10f, 160f, 46f), "Generar", button)) GenerateIntoEditor();

            // Fila 2: altura, rotación, canal, nombre y archivo
            x = 10f;
            GUI.Label(new Rect(x, 68f, 150f, 46f), $"Altura {level}", label);
            if (GUI.Button(new Rect(x + 110f, 66f, 40f, 46f), "-", button)) ChangeLevel(-1);
            if (GUI.Button(new Rect(x + 154f, 66f, 40f, 46f), "+", button)) ChangeLevel(1);
            x += 206f;
            if (GUI.Button(new Rect(x, 66f, 150f, 46f), $"R  Girar ({rotation * 90}°)", button)) rotation = (rotation + 1) % 4;
            x += 156f;
            GUI.color = MapRuntime.ChannelColors[channel];
            if (GUI.Button(new Rect(x, 66f, 190f, 46f), $"C  Canal {MapRuntime.ChannelNames[channel]}", button))
                channel = (channel + 1) % MapRuntime.ChannelColors.Length;
            GUI.color = Color.white;
            x += 200f;
            GUI.SetNextControlName("mapName");
            map.name = GUI.TextField(new Rect(x, 66f, 260f, 46f), map.name, 40, field);
            x += 266f;
            if (GUI.Button(new Rect(x, 66f, 110f, 46f), dirty ? "Guardar*" : "Guardar", button)) Save();
            x += 116f;
            if (GUI.Button(new Rect(x, 66f, 100f, 46f), "Abrir", button)) { showLoad = !showLoad; loadList = MapLibrary.List(); }
            x += 106f;
            if (GUI.Button(new Rect(x, 66f, 160f, 46f), "Copiar código", button)) CopyCode();
            x += 166f;
            if (GUI.Button(new Rect(x, 66f, 150f, 46f), "Pegar código", button)) PasteCode();
            x += 156f;
            if (GUI.Button(new Rect(x, 66f, 90f, 46f), "Nuevo", button)) NewMap();
            x += 96f;
            if (GUI.Button(new Rect(x, 66f, 120f, 46f), "P  Probar", selected)) StartTest();
            x += 126f;
            if (GUI.Button(new Rect(x, 66f, 90f, 46f), "Salir", button)) Close();

            // Abrir mapa guardado
            if (showLoad)
            {
                float h = Mathf.Min(80f + loadList.Count * 50f, 700f);
                var r = new Rect(width - 430f, 140f, 410f, h);
                Area(r);
                GUI.DrawTexture(r, panel);
                GUI.Label(new Rect(r.x + 14f, r.y + 10f, 380f, 40f), loadList.Count == 0 ? "No hay mapas guardados." : "Mapas guardados:", panelLabel);
                for (int i = 0; i < loadList.Count && 60f + i * 50f < h - 40f; i++)
                    if (GUI.Button(new Rect(r.x + 14f, r.y + 56f + i * 50f, 382f, 44f), loadList[i], button)) LoadMap(loadList[i]);
            }

            // Ayuda y estado
            var bottom = new Rect(0f, height - 96f, width, 96f);
            Area(bottom);
            GUI.DrawTexture(bottom, panel);
            GUI.Label(new Rect(14f, height - 90f, width - 28f, 34f),
                "Clic izq: poner (arrastrá para pintar)  ·  Clic der: borrar  ·  WASD mover  ·  Q/E o rueda del medio: girar  ·  Rueda: zoom  ·  " +
                "PgUp/PgDn o Shift+rueda: altura  ·  F: encuadrar  ·  Ctrl+Z deshacer  ·  Ctrl+S guardar  ·  P probar  ·  Esc salir", small);
            GUI.Label(new Rect(14f, height - 54f, width - 28f, 40f), $"{map.pieces.Count} piezas  ·  {status}", label);

            textFocus = GUI.GetNameOfFocusedControl() == "mapName";
        }

        void Area(Rect r)
        {
            if (Event.current.type == EventType.Layout) uiAreas.Add(r);
        }

        void EnsureStyles()
        {
            if (label != null) return;
            panel = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            panel.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.65f));
            panel.Apply();
            label = new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true };
            label.normal.textColor = Color.white;
            panelLabel = new GUIStyle(label) { fontStyle = FontStyle.Bold };
            small = new GUIStyle(label) { fontSize = 17 };
            button = new GUIStyle(GUI.skin.button) { fontSize = 18 };
            selected = new GUIStyle(button) { fontStyle = FontStyle.Bold };
            selected.normal.textColor = new Color(1f, 0.85f, 0.3f);
            selected.hover.textColor = selected.normal.textColor;
            field = new GUIStyle(GUI.skin.textField) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
        }
    }
}
