using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PointOfYou.Tests
{
    /// <summary>Mapas hechos por jugadores: formato, armado, piezas que funcionan y el editor.</summary>
    public class MapTests
    {
        GameManager gm;
        MapRuntime runtime;

        [UnitySetUp]
        public IEnumerator LoadPrototype()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            gm = GameManager.Instance;
            runtime = MapRuntime.Instance;
            Assert.IsNotNull(gm, "La escena no tiene GameManager");
            Assert.IsNotNull(runtime, "La escena no tiene MapRuntime");
        }

        [UnityTearDown]
        public IEnumerator BackToPrototype()
        {
            // El mapa elegido sobrevive a recargar la escena: dejarlo en el prototipo para los demás tests.
            if (MapEditor.Instance && gm && gm.Editor != GameManager.EditorMode.Off) MapEditor.Instance.Close();
            if (MapRuntime.Instance) MapRuntime.Instance.Select(null);
            yield return null;
        }

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        /// <summary>Piso de 12x16, inicio centrado en (0.5, 0.5), meta en z 10..13, placa celeste en (4,4) y puerta celeste en z=7.</summary>
        static MapData PlateAndDoorMap()
        {
            var map = MapData.CreateDefault("Placa y puerta");
            map.pieces.Add(new MapPiece { type = PieceType.Plate, x = 3, z = 3, w = 2, d = 2, channel = 1 });
            map.pieces.Add(new MapPiece { type = PieceType.Door, x = -1, z = 7, w = 3, d = 1, channel = 1 });
            return map;
        }

        [UnityTest]
        public IEnumerator CodigoDeMapa_IdaYVuelta()
        {
            var map = PlateAndDoorMap();
            map.name = "Prueba ñandú";
            string code = map.ToCode();
            Assert.IsTrue(code.StartsWith("POY1:"));
            Assert.IsTrue(MapData.TryFromCode(code, out var back, out string error), error);
            Assert.AreEqual(map.ToJson(), back.ToJson());
            Assert.IsFalse(MapData.TryFromCode("hola", out _, out _));
            Assert.IsFalse(MapData.TryFromCode("POY1:esto-no-es-base64", out _, out _));
            yield break;
        }

        [UnityTest]
        public IEnumerator MapaPropio_ReemplazaAlPrototipo_YLaPlacaAbreLaPuerta()
        {
            runtime.Select(PlateAndDoorMap());
            yield return null;
            Assert.IsTrue(runtime.IsCustom);
            Assert.IsNull(GameObject.Find("Nivel"), "El prototipo debería quedar oculto");
            Assert.AreEqual(1, runtime.Plates.Count);
            Assert.AreEqual(1, runtime.Doors.Count);

            var a = gm.Join(new ScriptedInputSource("A"), remember: false);
            yield return Seconds(0.6f);
            var flat = new Vector3(a.transform.position.x, 0f, a.transform.position.z);
            Assert.Less(Vector3.Distance(flat, new Vector3(0.5f, 0f, 0.5f)), 1.6f, "Debería aparecer en el Inicio del mapa");
            Assert.IsTrue(a.IsGrounded, "Debería estar parado sobre el piso del mapa");

            var door = runtime.Doors[0];
            Assert.IsFalse(door.IsOpen);
            a.Respawn(runtime.Plates[0].transform.position + Vector3.up * 0.9f, Quaternion.identity);
            yield return Seconds(1.2f);
            Assert.IsTrue(runtime.Plates[0].IsPressed);
            Assert.IsTrue(door.IsOpen, "La placa celeste debería abrir la puerta celeste");
        }

        [UnityTest]
        public IEnumerator PlacaDeOtraSala_NoAbreEstaPuerta()
        {
            // Dos "salas" del mismo color (amarillo) a 30 m: cada placa abre solo la puerta que le queda cerca.
            var map = MapData.CreateDefault("Mismo color, salas lejanas");
            map.pieces[0].d = 48; // piso de z -2 a 46
            map.pieces.Add(new MapPiece { type = PieceType.Plate, x = 2, z = 2, w = 2, d = 2, channel = 0 });
            map.pieces.Add(new MapPiece { type = PieceType.Door, x = 0, z = 8, channel = 0 });
            map.pieces.Add(new MapPiece { type = PieceType.Door, x = 0, z = 38, channel = 0 });
            map.pieces.Add(new MapPiece { type = PieceType.Plate, x = 2, z = 41, w = 2, d = 2, channel = 0 });
            runtime.Select(map);
            var near = runtime.Doors[0];
            var far = runtime.Doors[1];
            Assert.AreEqual(1, near.PlateCount, "La puerta cercana tiene solo su placa");
            Assert.AreEqual(1, far.PlateCount, "La lejana, solo la suya");
            Assert.AreEqual(0, runtime.UnlinkedDoorCount);

            var a = gm.Join(new ScriptedInputSource("A"), remember: false);
            yield return Seconds(0.3f);
            a.Respawn(runtime.Plates[0].transform.position + Vector3.up * 0.9f, Quaternion.identity);
            yield return Seconds(1.2f);
            Assert.IsTrue(near.IsOpen, "La placa de la sala 1 abre su puerta");
            Assert.Less(far.OpenAmount, 0.01f, "...pero no la del mismo color a 30 m");

            var farPlate = runtime.Plates.First(p => p != runtime.Plates[0]);
            a.Respawn(farPlate.transform.position + Vector3.up * 0.9f, Quaternion.identity);
            yield return Seconds(2.5f);
            Assert.IsTrue(far.IsOpen, "La placa de la sala 2 abre la puerta de la sala 2");
            Assert.Less(near.OpenAmount, 0.05f, "...y la de la sala 1 se cerró");
        }

        [UnityTest]
        public IEnumerator MapaPropio_LaMetaSeCompleta()
        {
            runtime.Select(PlateAndDoorMap());
            var a = gm.Join(new ScriptedInputSource("A"), remember: false);
            yield return Seconds(0.3f);
            Assert.IsFalse(gm.LevelComplete);
            a.Respawn(new Vector3(0.5f, 0.9f, 11.5f), Quaternion.identity);
            yield return Seconds(0.5f);
            Assert.IsTrue(gm.LevelComplete, "Con el único jugador en la meta, el nivel debería completarse");
        }

        [UnityTest]
        public IEnumerator Editor_PonerBorrarDeshacer_YProbar()
        {
            var editor = MapEditor.Instance;
            Assert.IsNotNull(editor, "La escena no tiene MapEditor");
            editor.Open(null);
            Assert.AreEqual(GameManager.EditorMode.Editing, gm.Editor);
            int n = editor.Map.pieces.Count;

            editor.Place(PieceType.Wall, new Vector2Int(0, 5), new Vector2Int(4, 5), 0);
            Assert.AreEqual(n + 1, editor.Map.pieces.Count);
            var wall = editor.Map.pieces[^1];
            Assert.AreEqual((5, 1), (wall.w, wall.d), "Arrastrar de (0,5) a (4,5) pinta una pared de 5x1");

            editor.Place(PieceType.Start, new Vector2Int(3, 3), new Vector2Int(3, 3), 0);
            Assert.AreEqual(n + 1, editor.Map.pieces.Count, "Solo puede haber un Inicio: el nuevo reemplaza al anterior");
            var start = editor.Map.FindFirst(PieceType.Start);
            Assert.AreEqual((2, 2), (start.x, start.z), "Las piezas de 3x3 se centran en la celda");

            Assert.IsTrue(editor.EraseAt(new Vector2Int(2, 5), 0));
            Assert.AreEqual(n, editor.Map.pieces.Count);
            Assert.IsNull(editor.Map.pieces.Find(p => p.type == PieceType.Wall));
            Assert.IsTrue(editor.Undo());
            Assert.AreEqual(n + 1, editor.Map.pieces.Count);

            Assert.IsTrue(editor.StartTest());
            Assert.AreEqual(GameManager.EditorMode.Testing, gm.Editor);
            var a = gm.Join(new ScriptedInputSource("A"), remember: false);
            yield return Seconds(0.6f);
            Assert.IsTrue(a.IsGrounded, "Probando, el jugador debería estar sobre el piso del mapa");
            var flat = new Vector3(a.transform.position.x, 0f, a.transform.position.z);
            Assert.Less(Vector3.Distance(flat, new Vector3(3.5f, 0f, 3.5f)), 1.6f, "Debería aparecer en el Inicio nuevo");

            editor.StopTest();
            yield return null;
            Assert.AreEqual(0, gm.Players.Count, "Al volver al editor no quedan jugadores");
            Assert.AreEqual(GameManager.EditorMode.Editing, gm.Editor);

            editor.Close();
            Assert.AreEqual(GameManager.EditorMode.Off, gm.Editor);
            Assert.IsTrue(runtime.IsCustom, "Al salir, el mapa editado queda elegido para jugar");
        }
    }
}
