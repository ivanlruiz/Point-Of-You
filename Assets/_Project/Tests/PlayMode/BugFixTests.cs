using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PointOfYou.Tests
{
    /// <summary>Bugs del informe de los 10 agentes (Docs/Sugerencias-agentes-2026-10-20.md) y la cámara antimareo.</summary>
    public class BugFixTests
    {
        GameManager gm;

        [UnitySetUp]
        public IEnumerator LoadPrototype()
        {
            if (MapRuntime.Instance) MapRuntime.Instance.Select(null);
            yield return SceneManager.LoadSceneAsync("Prototype", LoadSceneMode.Single);
            yield return null;
            gm = GameManager.Instance;
            Assert.IsNotNull(gm);
        }

        PlayerCharacter Bot(string name) => gm.Join(new ScriptedInputSource(name), remember: false);
        static ScriptedInputSource InputOf(PlayerCharacter p) => (ScriptedInputSource)p.InputSource;

        static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
        }

        [UnityTest]
        public IEnumerator Bug1_CheckpointPorJugador_RNoTeLlevaAlDelOtro()
        {
            var a = Bot("A");
            var b = Bot("B");
            yield return Seconds(0.3f);

            a.Respawn(new Vector3(0f, 0.9f, 14.25f), Quaternion.identity); // A ya cruzó la puerta 1
            yield return Seconds(0.3f);
            Assert.AreEqual(1, gm.CheckpointOf(a).Order, "A tocó el checkpoint 1");
            Assert.AreEqual(0, gm.CheckpointOf(b).Order, "B no: sigue en el inicio");

            InputOf(b).PressReset();
            yield return Seconds(0.3f);
            Assert.Less(b.transform.position.z, 12f, "Con R, B vuelve al inicio, no aparece del otro lado de la puerta");

            InputOf(a).PressReset();
            yield return Seconds(0.3f);
            Assert.Greater(a.transform.position.z, 12.4f, "A reaparece en su propio checkpoint");
        }

        [UnityTest]
        public IEnumerator Bug1_LaPuerta1EsAngosta()
        {
            var door = Object.FindObjectsByType<Door>(FindObjectsSortMode.None).Single(d => d.name == "Puerta 1");
            float width = door.GetComponent<BoxCollider>().bounds.size.x;
            Assert.Less(width, 1.3f, "En el vano entra un solo personaje (0,7 m): nadie traba la puerta y pasa otro al costado");
            Assert.Greater(width, 0.8f);
            yield break;
        }

        [UnityTest]
        public IEnumerator Bug2_SiElObservadorSeTeletransporta_LaCamaraCortaEnVezDeVolar()
        {
            var a = Bot("A");
            var b = Bot("B");
            yield return Seconds(0.5f);
            Assert.AreSame(b, a.View.Observer);

            b.Respawn(new Vector3(0f, 0.9f, 34f), Quaternion.identity); // 28 m de golpe
            yield return null;
            yield return null;
            Assert.Less(Vector3.Distance(a.View.transform.position, b.EyePosition), 0.2f,
                "La cámara de A debería estar ya en la cabeza nueva de B, no viajando a través de las paredes");
        }

        [UnityTest]
        public IEnumerator Bug3_ReaparecerSueltaAQuienTeTieneAgarrado()
        {
            var a = Bot("A");
            var b = Bot("B");
            yield return Seconds(0.3f);
            a.Respawn(new Vector3(-3f, 0.9f, 4f), Quaternion.identity);
            b.Respawn(new Vector3(-3f, 0.9f, 4.7f), Quaternion.identity);
            InputOf(a).Grab = true;
            yield return Seconds(0.3f);
            Assert.AreSame(b.Body, a.GrabbedBody, "A debería tener agarrado a B");

            gm.Respawn(b);
            yield return null;
            Assert.IsFalse(a.IsGrabbing, "Si B reaparece, A lo suelta");

            b.Respawn(new Vector3(-3f, 0.9f, 4.7f), Quaternion.identity);
            yield return Seconds(0.3f);
            Assert.IsFalse(a.IsGrabbing, "Sin soltar el botón, A no vuelve a agarrar solo");

            InputOf(a).Grab = false;
            yield return null;
            InputOf(a).Grab = true;
            yield return Seconds(0.2f);
            Assert.IsTrue(a.IsGrabbing, "Soltando y volviendo a apretar, sí");
        }

        [UnityTest]
        public IEnumerator Bug4_SacarAUnJugador_ElAnilloSeRearma()
        {
            var a = Bot("A");
            var b = Bot("B");
            var c = Bot("C");
            yield return Seconds(0.3f);
            gm.RemoveLocalPlayer(b);
            yield return null;
            Assert.AreEqual(2, gm.Players.Count);
            Assert.IsFalse(b, "El personaje sacado se destruye");
            Assert.AreSame(c, a.View.Observer, "Sin B, la ronda queda A↔C");
            Assert.AreSame(a, c.View.Observer);
        }

        [UnityTest]
        public IEnumerator Antimareo_AdelanteConMemoria()
        {
            var memory = new DirectionMemory();
            Vector3 north = Vector3.forward, east = Vector3.right;
            Assert.AreEqual(north, memory.Apply(north, Vector2.up, 0f));
            Assert.AreEqual(north, memory.Apply(east, Vector2.up, 0.1f), "Mientras mantenés la dirección, la cámara puede girar y adelante no cambia");
            Assert.AreEqual(north, memory.Apply(east, Vector2.zero, 0.2f), "Un instante sin input no lo suelta");
            Assert.AreEqual(east, memory.Apply(east, Vector2.zero, 0.5f), "Al soltar un rato, vuelve a seguir a la cámara");
            Assert.AreEqual(east, memory.Apply(east, Vector2.up, 0.6f));
            Assert.AreEqual(north, memory.Apply(north, Vector2.right, 0.7f), "Girar el stick más de 40° vuelve a tomar la cámara");
            yield break;
        }

        [UnityTest]
        public IEnumerator Antimareo_ElSaltoDelObservadorNoSacudeLaCamaraIgual()
        {
            var a = Bot("A");
            var b = Bot("B");
            yield return Seconds(0.8f);
            float camRest = a.View.transform.position.y, headRest = b.EyePosition.y;
            float camPeak = camRest, headPeak = headRest;

            InputOf(b).PressJump();
            float end = Time.time + 1.4f;
            while (Time.time < end)
            {
                camPeak = Mathf.Max(camPeak, a.View.transform.position.y);
                headPeak = Mathf.Max(headPeak, b.EyePosition.y);
                yield return null;
            }
            Assert.Greater(headPeak - headRest, 1f, "B saltó");
            Assert.Less(camPeak - camRest, headPeak - headRest - 0.05f, "La cámara de A sube menos que la cabeza de B");
            Assert.Less(Mathf.Abs(a.View.transform.position.y - b.EyePosition.y), 0.1f, "Y vuelve a la cabeza al terminar");
        }
    }
}
