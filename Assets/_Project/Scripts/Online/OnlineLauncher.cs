using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PointOfYou
{
    /// <summary>
    /// Arranque por línea de comandos, para probar el online con builds sin tocar nada a mano.
    ///   -pou-host [puerto]                  hostea por IP directa (con -pou-loopback escucha solo en 127.0.0.1)
    ///   -pou-client &lt;ip&gt; [puerto]           se conecta por IP directa
    ///   -pou-code-host &lt;archivo&gt;            crea partida con código y lo escribe en el archivo
    ///   -pou-code-join &lt;archivo&gt;            espera ese archivo y se une con el código
    ///   -pou-bot "t:acción,t:acción"        el jugador local lo maneja un bot (t en segundos desde que aparece)
    ///                                       acciones: up, down, left, right, stop, jump, grab, release, tp=x;y;z (solo host)
    ///   -pou-report &lt;archivo.jsonl&gt;         escribe el estado 4 veces por segundo (una línea JSON por muestra)
    ///   -pou-capture &lt;archivo.png&gt; &lt;t&gt;      guarda la pantalla del jugador local a los t segundos
    ///   -pou-map &lt;archivo&gt;                 antes de conectar elige ese mapa (JSON o código POY1:)
    ///   -pou-quit &lt;segundos&gt;                cierra el juego
    /// </summary>
    public class OnlineLauncher : MonoBehaviour
    {
        string[] args;
        readonly List<(float time, string action)> botSteps = new();
        ScriptedInputSource bot;
        string reportPath, capturePath, codeFile;
        float captureAt = -1f, quitAt = -1f, nextReport, localStart = -1f;
        bool started, captured, joiningByCode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Environment.GetCommandLineArgs().Any(a => a.StartsWith("-pou-"))) return;
            if (FindAnyObjectByType<OnlineLauncher>()) return;
            var go = new GameObject("OnlineLauncher");
            DontDestroyOnLoad(go);
            go.AddComponent<OnlineLauncher>();
        }

        void Awake()
        {
            args = Environment.GetCommandLineArgs();
            reportPath = Arg("-pou-report");
            quitAt = ParseFloat(Arg("-pou-quit"), -1f);
            capturePath = Arg("-pou-capture");
            captureAt = ParseFloat(Arg("-pou-capture", 2), -1f);

            string script = Arg("-pou-bot");
            if (script != null)
            {
                bot = new ScriptedInputSource("Bot");
                NetworkPlayer.LocalInputOverride = bot;
                foreach (var step in script.Split(','))
                {
                    var parts = step.Split(new[] { ':' }, 2);
                    if (parts.Length == 2) botSteps.Add((ParseFloat(parts[0], 0f), parts[1].Trim()));
                }
                botSteps.Sort((a, b) => a.time.CompareTo(b.time));
            }
            if (reportPath != null) File.WriteAllText(reportPath, "");
            Log($"args: {string.Join(" ", args.Skip(1))}");
        }

        string Arg(string name, int offset = 1)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + offset < args.Length && !args[i + offset].StartsWith("-pou-") ? args[i + offset] : null;
        }

        bool Has(string name) => Array.IndexOf(args, name) >= 0;

        static float ParseFloat(string s, float fallback) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        static void Log(string msg) => Debug.Log($"[Launcher] {msg}");

        void Update()
        {
            var online = OnlineSession.Instance;
            if (!started && online)
            {
                started = true;
                string mapFile = Arg("-pou-map");
                if (mapFile != null && MapRuntime.Instance)
                {
                    string text = File.ReadAllText(mapFile).Trim();
                    MapData map = null;
                    if (text.StartsWith("{")) map = MapData.FromJson(text);
                    else if (!MapData.TryFromCode(text, out map, out string error)) Log($"mapa inválido: {error}");
                    if (map != null)
                    {
                        MapRuntime.Instance.Select(map);
                        Log($"mapa elegido: {map.name} ({map.pieces.Count} piezas)");
                    }
                }
                if (Has("-pou-host"))
                {
                    ushort port = (ushort)ParseFloat(Arg("-pou-host"), OnlineSession.DefaultPort);
                    Log($"host directo en puerto {port}: {online.HostDirect(port, Has("-pou-loopback") ? "127.0.0.1" : "0.0.0.0")}");
                }
                else if (Has("-pou-client"))
                {
                    string ip = Arg("-pou-client") ?? "127.0.0.1";
                    ushort port = (ushort)ParseFloat(Arg("-pou-client", 2), OnlineSession.DefaultPort);
                    Log($"cliente directo a {ip}:{port}: {online.JoinDirect(ip, port)}");
                }
                else if (Has("-pou-code-host"))
                {
                    codeFile = Arg("-pou-code-host");
                    online.HostWithCode();
                }
                else if (Has("-pou-code-join"))
                {
                    codeFile = Arg("-pou-code-join");
                    joiningByCode = true;
                }
            }

            if (online && codeFile != null)
            {
                if (Has("-pou-code-host") && online.JoinCode != null && !File.Exists(codeFile))
                {
                    File.WriteAllText(codeFile, online.JoinCode);
                    Log($"código de partida: {online.JoinCode}");
                }
                if (joiningByCode && File.Exists(codeFile))
                {
                    joiningByCode = false;
                    string code = File.ReadAllText(codeFile).Trim();
                    Log($"uniéndose con código {code}");
                    online.JoinWithCode(code);
                }
            }
            if (online && !string.IsNullOrEmpty(online.Status) && Time.frameCount % 120 == 0) Log($"estado: {online.Status}");

            var local = LocalPlayer();
            if (local && localStart < 0f)
            {
                localStart = Time.time;
                Log($"jugador local {local.DisplayName} listo");
            }
            if (localStart >= 0f) RunBot(local, Time.time - localStart);

            if (capturePath != null && !captured && localStart >= 0f && Time.time - localStart >= captureAt && local && local.View)
            {
                captured = true;
                Capture(local.View.Cam, capturePath);
            }

            if (reportPath != null && Time.time >= nextReport)
            {
                nextReport = Time.time + 0.25f;
                File.AppendAllText(reportPath, Snapshot(local) + "\n");
            }

            if (quitAt > 0f && Time.realtimeSinceStartup >= quitAt)
            {
                Log("fin");
                quitAt = -1f;
                if (online && Net.IsOnline) Unity.Netcode.NetworkManager.Singleton.Shutdown();
                Application.Quit();
            }
        }

        static PlayerCharacter LocalPlayer()
        {
            var gm = GameManager.Instance;
            return gm ? gm.Players.FirstOrDefault(p => p.View) : null;
        }

        void RunBot(PlayerCharacter local, float t)
        {
            if (bot == null) return;
            while (botSteps.Count > 0 && botSteps[0].time <= t)
            {
                string action = botSteps[0].action;
                botSteps.RemoveAt(0);
                Log($"bot t={t:F1}: {action}");
                switch (action)
                {
                    case "up": bot.MoveValue = Vector2.up; break;
                    case "down": bot.MoveValue = Vector2.down; break;
                    case "left": bot.MoveValue = Vector2.left; break;
                    case "right": bot.MoveValue = Vector2.right; break;
                    case "stop": bot.MoveValue = Vector2.zero; break;
                    case "jump": bot.PressJump(); break;
                    case "grab": bot.Grab = true; break;
                    case "release": bot.Grab = false; break;
                    default:
                        if (action.StartsWith("tp=") && local && local.Simulate)
                        {
                            var v = action.Substring(3).Split(';').Select(s => ParseFloat(s, 0f)).ToArray();
                            local.Respawn(new Vector3(v[0], v[1], v[2]), local.transform.rotation);
                        }
                        break;
                }
            }
        }

        static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "[{0:F3},{1:F3},{2:F3}]", v.x, v.y, v.z);
        static string F(float f) => f.ToString("F3", CultureInfo.InvariantCulture);
        static string B(bool b) => b ? "true" : "false";

        static string Snapshot(PlayerCharacter local)
        {
            var gm = GameManager.Instance;
            var sb = new StringBuilder("{");
            sb.Append($"\"t\":{F(Time.realtimeSinceStartup)},");
            sb.Append($"\"role\":\"{(Net.IsHost ? "host" : Net.IsReplica ? "client" : "offline")}\",");
            sb.Append($"\"map\":\"{(MapRuntime.Instance ? MapRuntime.Instance.CurrentName : "?")}\",");
            if (gm)
            {
                var observer = local ? gm.ObserverOf(local) : null;
                sb.Append($"\"local\":{(local ? local.Index : -1)},");
                sb.Append($"\"observer\":{(observer ? observer.Index : -1)},");
                sb.Append($"\"lookTarget\":{(local && local.LookTarget ? local.LookTarget.Index : -1)},");
                if (local && local.View)
                {
                    var cam = local.View.transform;
                    Vector3 toOwner = (local.ChestPosition - cam.position).normalized;
                    sb.Append($"\"camPos\":{V(cam.position)},\"camAim\":{F(Vector3.Dot(cam.forward, toOwner))},");
                    if (observer) sb.Append($"\"observerEye\":{V(observer.EyePosition)},");
                }
                sb.Append("\"players\":[");
                sb.Append(string.Join(",", gm.Players.Select(p =>
                    $"{{\"index\":{p.Index},\"pos\":{V(p.transform.position)},\"simulate\":{B(p.Simulate)},\"view\":{B(p.View)}}}")));
                sb.Append("],");
                sb.Append($"\"checkpoint\":{(gm.CurrentCheckpoint ? gm.CurrentCheckpoint.Order : -1)},\"complete\":{F(gm.CompleteElapsed)},");
            }
            sb.Append("\"plates\":[");
            sb.Append(string.Join(",", FindObjectsByType<PressurePlate>(FindObjectsSortMode.None).OrderBy(p => p.name)
                .Select(p => $"{{\"name\":\"{p.name}\",\"pressed\":{B(p.IsPressed)}}}")));
            sb.Append("],\"doors\":[");
            sb.Append(string.Join(",", FindObjectsByType<Door>(FindObjectsSortMode.None).OrderBy(d => d.name)
                .Select(d => $"{{\"name\":\"{d.name}\",\"open\":{F(d.OpenAmount)}}}")));
            sb.Append("],\"boxes\":[");
            sb.Append(string.Join(",", FindObjectsByType<Respawnable>(FindObjectsSortMode.None)
                .Select(r => $"{{\"name\":\"{r.name}\",\"pos\":{V(r.transform.position)},\"net\":{B(r.GetComponent<Unity.Netcode.NetworkObject>())}}}")));
            sb.Append("]}");
            return sb.ToString();
        }

        static void Capture(Camera cam, string path)
        {
            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24);
            var previousTarget = cam.targetTexture;
            var previousRect = cam.rect;
            var active = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                cam.Render();
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                Log($"captura: {path}");
            }
            catch (Exception e)
            {
                Log($"no se pudo capturar: {e.Message}");
            }
            finally
            {
                cam.targetTexture = previousTarget;
                cam.rect = previousRect;
                RenderTexture.active = active;
                rt.Release();
                Destroy(rt);
            }
        }
    }
}
