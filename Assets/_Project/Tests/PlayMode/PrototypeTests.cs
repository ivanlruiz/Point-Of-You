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
    /// Verifica la mecánica central con jugadores manejados por código:
    /// el anillo de cámaras, el movimiento relativo a la cámara, salto, agarre y puzzles.
    /// </summary>
    public class PrototypeTests
    {
        GameManager gm;

        [UnitySetUp]
        public IEnumerator LoadPrototype()
        {
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            gm = GameManager.Instance;
            Assert.IsNotNull(gm, "La escena Prototype no tiene GameManager");
        }

        PlayerCharacter Bot(string name) => gm.Join(new ScriptedInputSource(name), remember: false);
        static ScriptedInputSource InputOf(PlayerCharacter p) => (ScriptedInputSource)p.InputSource;

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        static void AssertViewFromObserverHead(PlayerCharacter p)
        {
            var view = p.View.transform;
            Assert.Less(Vector3.Distance(view.position, p.View.Observer.EyePosition), 0.15f,
                $"La cámara de {p.DisplayName} no está en la cabeza de {p.View.Observer.DisplayName}");
            Vector3 toOwner = (p.ChestPosition - view.position).normalized;
            Assert.Greater(Vector3.Dot(view.forward, toOwner), 0.98f, $"La cámara de {p.DisplayName} no lo está mirando");
        }

        [UnityTest]
        public IEnumerator DosJugadores_CadaUnoSeVeDesdeLaCabezaDelOtro()
        {
            var a = Bot("A");
            var b = Bot("B");
            yield return Seconds(0.6f);

            Assert.AreSame(b, a.View.Observer);
            Assert.AreSame(a, b.View.Observer);
            Assert.AreSame(b, a.LookTarget);
            Assert.AreSame(a, b.LookTarget);
            AssertViewFromObserverHead(a);
            AssertViewFromObserverHead(b);
            Assert.AreEqual(new Rect(0f, 0f, 0.5f, 1f), a.View.Cam.rect);
            Assert.AreEqual(new Rect(0.5f, 0f, 0.5f, 1f), b.View.Cam.rect);
        }

        [UnityTest]
        public IEnumerator TresJugadores_ElAnilloEsCircular()
        {
            var p1 = Bot("1");
            var p2 = Bot("2");
            var p3 = Bot("3");
            yield return Seconds(0.6f);

            // J1 mira a J2, J2 mira a J3, J3 mira a J1.
            Assert.AreSame(p2, p1.LookTarget);
            Assert.AreSame(p3, p2.LookTarget);
            Assert.AreSame(p1, p3.LookTarget);
            // Por lo tanto: la pantalla de J1 está en la cabeza de J3, etc.
            Assert.AreSame(p3, p1.View.Observer);
            Assert.AreSame(p1, p2.View.Observer);
            Assert.AreSame(p2, p3.View.Observer);
            AssertViewFromObserverHead(p1);
            AssertViewFromObserverHead(p2);
            AssertViewFromObserverHead(p3);
        }

        [UnityTest]
        public IEnumerator MirarAlrededor_GiraYVuelveSola()
        {
            var a = Bot("A");
            Bot("B");
            yield return Seconds(0.6f);
            AssertViewFromObserverHead(a);

            InputOf(a).LookValue = new Vector2(3f, 0f); // 3° por frame hacia la derecha
            yield return Seconds(0.3f);
            InputOf(a).LookValue = Vector2.zero;
            yield return null;

            var view = a.View.transform;
            Vector3 toOwner = (a.ChestPosition - view.position).normalized;
            Assert.Greater(a.View.LookOffset.x, 10f, "El mouse debería haber girado la vista");
            Assert.Less(Vector3.Dot(view.forward, toOwner), 0.9f, "La vista ya no debería apuntar al jugador");
            Assert.Less(Vector3.Distance(view.position, a.View.Observer.EyePosition), 0.15f, "Mirar alrededor no mueve la cámara de la cabeza");

            yield return Seconds(2f);
            AssertViewFromObserverHead(a);
        }

        [UnityTest]
        public IEnumerator Arriba_TeAlejaDeQuienTeMira()
        {
            var a = Bot("A");
            var b = Bot("B");
            yield return Seconds(0.6f);

            float before = Vector3.Distance(a.transform.position, b.transform.position);
            InputOf(a).MoveValue = Vector2.up;
            yield return Seconds(0.6f);
            InputOf(a).MoveValue = Vector2.zero;

            float after = Vector3.Distance(a.transform.position, b.transform.position);
            Assert.Greater(after, before + 1f, $"Se esperaba que A se alejara de B ({before:F2} → {after:F2})");
        }

        [UnityTest]
        public IEnumerator Saltar_SubeYVuelveAlPiso()
        {
            var a = Bot("A");
            yield return Seconds(0.5f);
            Assert.IsTrue(a.IsGrounded, "El jugador debería empezar en el piso");

            float y0 = a.transform.position.y;
            float peak = y0;
            InputOf(a).PressJump();
            float end = Time.time + 1.2f;
            while (Time.time < end)
            {
                peak = Mathf.Max(peak, a.transform.position.y);
                yield return null;
            }

            Assert.Greater(peak - y0, 1.1f, "El salto debería superar 1.1 m (necesario para subir a la caja)");
            Assert.Less(peak - y0, 2.1f, "El salto no debería alcanzar la cornisa (2.1 m) sin la caja");
            Assert.IsTrue(a.IsGrounded, "Debería haber vuelto al piso");
        }

        [UnityTest]
        public IEnumerator PlacaAbrePuerta_YSeCierraAlSalir()
        {
            var a = Bot("A");
            Bot("B");
            var plate = Object.FindObjectsByType<PressurePlate>(FindObjectsSortMode.None).Single(p => p.name == "Placa A");
            var door = Object.FindObjectsByType<Door>(FindObjectsSortMode.None).Single(d => d.name == "Puerta 1");
            yield return Seconds(0.3f);
            Assert.IsFalse(door.IsOpen);

            a.Respawn(plate.transform.position + Vector3.up * 0.9f, Quaternion.identity);
            yield return Seconds(1.2f);
            Assert.IsTrue(plate.IsPressed, "La placa debería estar presionada");
            Assert.IsTrue(door.IsOpen, "La puerta debería estar abierta");

            gm.Respawn(a);
            yield return Seconds(2.5f);
            Assert.IsFalse(plate.IsPressed);
            Assert.Less(door.OpenAmount, 0.05f, "La puerta debería haberse cerrado");
        }

        [UnityTest]
        public IEnumerator Agarrar_ArrastraLaCaja()
        {
            var a = Bot("A");
            Bot("B");
            var box = GameObject.Find("Caja").GetComponent<Rigidbody>();
            yield return Seconds(0.3f);

            // Parado detrás de la caja mirando hacia +Z (alejándose de B, que quedó en el inicio).
            a.Respawn(box.position + new Vector3(0f, 0.4f, -1.0f), Quaternion.identity);
            InputOf(a).Grab = true;
            yield return Seconds(0.3f);
            Assert.IsTrue(a.IsGrabbing, "Debería haber agarrado la caja");
            Assert.AreSame(box, a.GrabbedBody);

            Vector3 boxStart = box.position;
            InputOf(a).MoveValue = Vector2.up;
            yield return Seconds(1f);
            InputOf(a).MoveValue = Vector2.zero;
            Assert.Greater(Vector3.Distance(box.position, boxStart), 1.5f, "La caja debería haberse movido con el jugador");

            InputOf(a).Grab = false;
            yield return null;
            yield return null;
            Assert.IsFalse(a.IsGrabbing);
        }

        [UnityTest]
        public IEnumerator CaerAlVacio_Reaparece()
        {
            var a = Bot("A");
            yield return Seconds(0.3f);
            a.Respawn(new Vector3(-2f, -7f, 20f), Quaternion.identity);
            yield return Seconds(0.3f);
            Assert.Greater(a.transform.position.y, 0f, "Debería haber reaparecido en el checkpoint");
        }

        [UnityTest]
        public IEnumerator Captura_PantallaDividida()
        {
            var p1 = Bot("1");
            var p2 = Bot("2");
            yield return Seconds(0.8f);
            Capture("captura_2_jugadores.png");

            Bot("3");
            yield return Seconds(0.8f);
            Capture("captura_3_jugadores.png");

            // J1 cruzando las piedras, mirado por J2 desde el pasillo (ventana).
            Bot("4");
            yield return Seconds(0.3f);
            var players = gm.Players;
            players[3].Respawn(new Vector3(-3.5f, 0.9f, 19.6f), Quaternion.identity);   // J4 en la piedra 2
            players[2].Respawn(new Vector3(3.5f, 0.9f, 19.0f), Quaternion.Euler(0, -90, 0)); // J3 (observador de J4) en el pasillo frente a la ventana
            yield return Seconds(0.8f);
            Capture("captura_4_jugadores_ventana.png");
            Assert.Pass();
        }

        /// <summary>
        /// Renderiza todas las cámaras activas a un PNG (no incluye el HUD).
        /// En batchmode las cámaras no dibujan solas: se hace un render de calentamiento sobre una
        /// textura limpia y recién después el definitivo.
        /// </summary>
        static void Capture(string fileName)
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
            rt.Release();
            Object.Destroy(rt);

            string dir = Path.Combine(Application.dataPath, "..", "TestResults");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, fileName), tex.EncodeToPNG());
            Object.Destroy(tex);
        }
    }
}
