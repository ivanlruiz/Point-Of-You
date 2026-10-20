using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PointOfYou.Tests
{
    /// <summary>
    /// Verifica el generador de mapas procedurales (diseñado y escrito por Gemini "Argon"):
    /// determinismo, validez estructural, que se arme sin errores y que sus placas, puertas y meta funcionen.
    /// </summary>
    public class ProcGenTests
    {
        GameManager gm;
        MapRuntime runtime;

        [UnitySetUp]
        public IEnumerator LoadPrototype()
        {
            if (MapRuntime.Instance) MapRuntime.Instance.Select(null);
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            gm = GameManager.Instance;
            runtime = MapRuntime.Instance;
        }

        [UnityTearDown]
        public IEnumerator BackToPrototype()
        {
            if (MapRuntime.Instance) MapRuntime.Instance.Select(null);
            yield return null;
        }

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        static GeneratorSettings Settings(int players, int difficulty, int rooms = 5) =>
            new() { players = players, difficulty = difficulty, rooms = rooms };

        [Test]
        public void Determinista_MismaSemillaMismoMapa()
        {
            var a = MapGenerator.Generate(1234, Settings(3, 2)).ToJson();
            var b = MapGenerator.Generate(1234, Settings(3, 2)).ToJson();
            var c = MapGenerator.Generate(4321, Settings(3, 2)).ToJson();
            Assert.AreEqual(a, b, "La misma semilla tiene que dar el mismo mapa");
            Assert.AreNotEqual(a, c, "Semillas distintas deberían dar mapas distintos");
        }

        [UnityTest]
        public IEnumerator Validos_YSeArmanSinErrores_180Combinaciones()
        {
            int count = 0;
            for (int seed = 1; seed <= 20; seed++)
            for (int players = 2; players <= 4; players++)
            for (int difficulty = 1; difficulty <= 3; difficulty++)
            {
                var map = MapGenerator.Generate(seed, Settings(players, difficulty));
                string id = $"semilla {seed}, {players}J, dificultad {difficulty}";
                var problems = MapGenerator.Validate(map);
                Assert.IsEmpty(problems, $"{id}: {string.Join(" | ", problems)}");
                Assert.Less(map.pieces.Count, 600, $"{id}: demasiadas piezas");
                Assert.LessOrEqual(map.pieces.Count(p => p.type == PieceType.Plate), 31, $"{id}: más de 31 placas");
                Assert.AreEqual(1, map.pieces.Count(p => p.type == PieceType.Start), $"{id}: tiene que haber un solo Inicio");
                Assert.IsTrue(MapData.TryFromCode(map.ToCode(), out var back, out _) && back.ToJson() == map.ToJson(), $"{id}: el código para compartir no vuelve igual");
                runtime.Build(map); // si tira una excepción, el test falla
                Assert.AreEqual(0, runtime.UnlinkedDoorCount, $"{id}: hay puertas sin ninguna placa de su color cerca");
                count++;
                if (count % 30 == 0) yield return null;
            }
            Assert.AreEqual(180, count);
        }

        /// <summary>
        /// Chequeo geométrico (independiente del Validate del generador): las dos celdas de paso de cada puerta
        /// tienen piso y no son pared/ventana, y cada placa tiene piso libre bajo sus 4 celdas.
        /// </summary>
        static System.Collections.Generic.List<string> AccessProblems(MapData map)
        {
            var walk = new System.Collections.Generic.HashSet<Vector2Int>();
            var solid = new System.Collections.Generic.Dictionary<Vector2Int, PieceType>();
            foreach (var p in map.pieces)
            {
                var f = p.Footprint;
                for (int x = p.x; x < p.x + f.x; x++)
                for (int z = p.z; z < p.z + f.y; z++)
                {
                    var c = new Vector2Int(x, z);
                    if (p.type is PieceType.Floor or PieceType.Stone or PieceType.Block) walk.Add(c);
                    else if (p.type is PieceType.Wall or PieceType.Window) solid[c] = p.type;
                }
            }
            var problems = new System.Collections.Generic.List<string>();
            foreach (var p in map.pieces)
            {
                if (p.type == PieceType.Door)
                {
                    var sides = p.rot % 2 == 0
                        ? new[] { new Vector2Int(p.x, p.z - 1), new Vector2Int(p.x, p.z + 1) }
                        : new[] { new Vector2Int(p.x - 1, p.z), new Vector2Int(p.x + 1, p.z) };
                    foreach (var s in sides)
                        if (solid.TryGetValue(s, out var t)) problems.Add($"puerta canal {p.channel} en ({p.x},{p.z}): su paso {s} es {t}");
                        else if (!walk.Contains(s)) problems.Add($"puerta canal {p.channel} en ({p.x},{p.z}): su paso {s} es vacío");
                }
                else if (p.type == PieceType.Plate)
                {
                    for (int x = p.x; x < p.x + 2; x++)
                    for (int z = p.z; z < p.z + 2; z++)
                    {
                        var c = new Vector2Int(x, z);
                        if (solid.TryGetValue(c, out var t)) problems.Add($"placa canal {p.channel} en ({p.x},{p.z}): {c} es {t}");
                        else if (!walk.Contains(c)) problems.Add($"placa canal {p.channel} en ({p.x},{p.z}): {c} sin piso");
                    }
                }
            }
            return problems;
        }

        [Test]
        public void PuertasYPlacas_SeAlcanzan_180Combinaciones()
        {
            var failures = new System.Collections.Generic.List<string>();
            for (int seed = 1; seed <= 20; seed++)
            for (int players = 2; players <= 4; players++)
            for (int difficulty = 1; difficulty <= 3; difficulty++)
                foreach (var problem in AccessProblems(MapGenerator.Generate(seed, Settings(players, difficulty))))
                    failures.Add($"semilla {seed} {players}J D{difficulty}: {problem}");
            Assert.IsEmpty(failures, $"{failures.Count} problemas de acceso. Primeros: {string.Join(" | ", failures.Take(6))}");
        }

        static int ChannelOf(Component c)
        {
            for (int i = 0; i < MapRuntime.ChannelNames.Length; i++)
                if (c.name.Contains($"(canal {MapRuntime.ChannelNames[i]})")) return i;
            return -1;
        }

        [UnityTest]
        public IEnumerator PlacasAbrenSusPuertas_YLaMetaSeCompleta([Values(7, 42, 2024)] int seed)
        {
            var map = MapGenerator.Generate(seed, Settings(2, 2));
            runtime.Select(map);
            var a = gm.Join(new ScriptedInputSource("A"), remember: false);
            var b = gm.Join(new ScriptedInputSource("B"), remember: false);
            yield return Seconds(0.5f);
            Assert.IsTrue(a.IsGrounded, $"semilla {seed}: los jugadores deberían aparecer sobre piso");

            foreach (var door in runtime.Doors)
            {
                Assert.Greater(door.PlateCount, 0, $"semilla {seed}: {door.name} no tiene placa conectada");
                var plate = door.Plates[0];
                Assert.AreEqual(ChannelOf(door), ChannelOf(plate), $"semilla {seed}: {door.name} quedó conectada a una placa de otro color");
                a.Respawn(plate.transform.position + Vector3.up * 0.9f, Quaternion.identity);
                yield return Seconds(1.2f);
                Assert.IsTrue(plate.IsPressed, $"semilla {seed}: {plate.name} no se presiona con un jugador encima");
                Assert.IsTrue(door.IsOpen, $"semilla {seed}: {plate.name} no abre {door.name}");
                gm.Respawn(a);
                yield return null;
            }

            var goal = Object.FindAnyObjectByType<GoalZone>();
            Assert.IsNotNull(goal, $"semilla {seed}: no hay meta");
            Vector3 center = goal.transform.position + Vector3.down * 0.2f;
            a.Respawn(center + Vector3.left * 0.6f, Quaternion.identity);
            b.Respawn(center + Vector3.right * 0.6f, Quaternion.identity);
            yield return Seconds(0.6f);
            Assert.IsTrue(gm.LevelComplete, $"semilla {seed}: con los dos en la meta debería completarse");
        }

        [UnityTest]
        public IEnumerator Captura_MapasGenerados()
        {
            string dir = Path.Combine(Application.dataPath, "..", "TestResults", "procgen");
            Directory.CreateDirectory(dir);
            foreach (var (seed, players, difficulty) in new[] { (7, 2, 1), (42, 2, 2), (2024, 3, 2), (99, 4, 3) })
            {
                var map = MapGenerator.Generate(seed, Settings(players, difficulty));
                runtime.Select(map);
                yield return Seconds(0.3f);
                RenderAllCameras(Path.Combine(dir, $"semilla{seed}_{players}J_dif{difficulty}.png"));
                File.WriteAllText(Path.Combine(dir, $"semilla{seed}_{players}J_dif{difficulty}.txt"), map.ToCode());
            }
        }

        static void RenderAllCameras(string path)
        {
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24);
            var active = RenderTexture.active;
            var cams = Camera.allCameras.OrderBy(c => c.depth).ToArray();
            for (int pass = 0; pass < 2; pass++)
            {
                RenderTexture.active = rt;
                GL.Clear(true, true, Color.black);
                foreach (var cam in cams)
                {
                    var previous = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = previous;
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = active;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            rt.Release();
            Object.Destroy(rt);
        }
    }
}
