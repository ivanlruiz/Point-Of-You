using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PointOfYou.Tests
{
    /// <summary>Sensación física: empujones y bamboleo.</summary>
    public class PhysicsTests
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

        static float HorizontalSpeed(PlayerCharacter p)
        {
            var v = p.Body.linearVelocity;
            return new Vector2(v.x, v.z).magnitude;
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        [UnityTest]
        public IEnumerator Empujon_SeDeslizaMasYDespuesVuelveElControl()
        {
            var a = Bot("A");
            var b = Bot("B");
            a.Respawn(new Vector3(-3f, 0.9f, 2f), Quaternion.identity);
            b.Respawn(new Vector3(3f, 0.9f, 2f), Quaternion.identity);
            yield return Seconds(0.5f);

            yield return new WaitForFixedUpdate();
            b.Shove(new Vector3(0f, 0f, 3.5f));
            Assert.Less(b.ControlFactor, 0.2f, "Un empujón de 3,5 m/s quita casi todo el control");
            float start = Time.time, alive = 0f;
            while (Time.time - start < 1.5f)
            {
                yield return new WaitForFixedUpdate();
                if (HorizontalSpeed(b) > 0.7f) alive = Time.time - start;
            }
            Debug.Log($"[Fisica] empujón directo de 3,5 m/s: velocidad > 20 % durante {alive:F3} s (antes 0,080 s)");
            Assert.Greater(alive, 0.2f, "El empujón tiene que sentirse: antes el control lo frenaba en 0,08 s");
            Assert.AreEqual(1f, b.ControlFactor, 1e-4f, "El control vuelve solo");

            InputOf(b).MoveValue = Vector2.up;
            yield return Seconds(0.4f);
            Assert.Greater(HorizontalSpeed(b), 4f, "Con el control de vuelta, se mueve normal");
        }

        [UnityTest]
        public IEnumerator Choque_TambaleaAlChocadoYNoAlQueCorre()
        {
            var a = Bot("A");
            var b = Bot("B");
            a.Respawn(new Vector3(0f, 0.9f, 1f), Quaternion.identity);
            b.Respawn(new Vector3(0f, 0.9f, 4f), Quaternion.identity);
            yield return Seconds(0.8f);

            // Con 2 jugadores, la cámara de A está en la cabeza de B: "abajo" lleva a A directo hacia B.
            Vector3 bStart = b.transform.position;
            InputOf(a).MoveValue = Vector2.down;
            float start = Time.time, minA = 1f, minB = 1f, peak = 0f;
            while (Time.time - start < 0.8f)
            {
                yield return new WaitForFixedUpdate();
                minA = Mathf.Min(minA, a.ControlFactor);
                minB = Mathf.Min(minB, b.ControlFactor);
                peak = Mathf.Max(peak, HorizontalSpeed(b));
            }
            InputOf(a).MoveValue = Vector2.zero;
            float moved = Vector3.Distance(Flat(bStart), Flat(b.transform.position));
            Debug.Log($"[Fisica] choque corriendo: pico de B {peak:F2} m/s (antes 2,25), B se movió {moved:F2} m, control mínimo A {minA:F2} / B {minB:F2}");
            Assert.Less(minB, 0.5f, "Al que chocan corriendo se tambalea");
            Assert.Greater(minA, 0.95f, "El que corre y choca no pierde el control: frenar contra algo no es un empujón");
            Assert.Greater(peak, 2.6f, "El golpe empuja más que el choque físico solo (2,25 m/s)");
        }

        [UnityTest]
        public IEnumerator Bamboleo_MueveLaCabezaPeroNoElOjo()
        {
            var a = Bot("A");
            var b = Bot("B");
            a.Respawn(new Vector3(0f, 0.9f, 1f), Quaternion.identity);
            b.Respawn(new Vector3(0f, 0.9f, 5f), Quaternion.identity);
            yield return Seconds(0.6f);

            var wobble = a.GetComponent<PlayerWobble>();
            Assert.IsNotNull(wobble);
            Vector3 eyeLocal = a.transform.InverseTransformPoint(a.EyePosition);
            float maxLean = 0f, maxHeadOffset = 0f, maxEyeDrift = 0f;

            // Arrancar, correr y frenar de golpe: el cuerpo se tiene que bambolear.
            InputOf(a).MoveValue = Vector2.down;
            for (float t = 0f; t < 1.2f; t += Time.deltaTime)
            {
                if (t > 0.45f) InputOf(a).MoveValue = Vector2.zero;
                yield return null;
                maxLean = Mathf.Max(maxLean, wobble.LeanDegrees);
                maxHeadOffset = Mathf.Max(maxHeadOffset, Vector3.Distance(a.Head.position, a.EyePosition));
                maxEyeDrift = Mathf.Max(maxEyeDrift, Vector3.Distance(a.transform.InverseTransformPoint(a.EyePosition), eyeLocal));
            }
            Debug.Log($"[Fisica] bamboleo: inclinación máx {maxLean:F1}°, cabeza visible a {maxHeadOffset:F3} m del ojo, ojo corrido {maxEyeDrift:F4} m");
            Assert.Greater(maxLean, 3f, "Arrancar y frenar inclina el cuerpo");
            Assert.LessOrEqual(maxLean, 12.5f, "Con tope: el pecho no se acerca al ojo");
            Assert.Greater(maxHeadOffset, 0.02f, "La cabeza que ven los demás se mueve con el bamboleo");
            Assert.Less(maxEyeDrift, 1e-3f, "El ojo (la cámara del otro) no se mueve con el bamboleo");
            Assert.Less(Vector3.Distance(b.View.transform.position, a.EyePosition), 0.15f, "La cámara de B sigue en el ojo de A");
        }

        [UnityTest]
        public IEnumerator Aterrizaje_AplastaElCuerpo()
        {
            var a = Bot("A");
            var b = Bot("B");
            a.Respawn(new Vector3(0f, 0.9f, 1f), Quaternion.identity);
            b.Respawn(new Vector3(0f, 0.9f, 5f), Quaternion.identity);
            yield return Seconds(0.6f);

            var wobble = a.GetComponent<PlayerWobble>();
            InputOf(a).PressJump();
            float minSquash = 0f, maxSquash = 0f;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                yield return null;
                minSquash = Mathf.Min(minSquash, wobble.Squash);
                maxSquash = Mathf.Max(maxSquash, wobble.Squash);
            }
            Debug.Log($"[Fisica] salto: estirado máx {maxSquash:F3}, aplastado máx {minSquash:F3}");
            Assert.Greater(maxSquash, 0.02f, "Al saltar se estira");
            Assert.Less(minSquash, -0.03f, "Al caer se aplasta");
            Assert.LessOrEqual(maxSquash, 0.0625f + 1e-4f, "Estirado con tope: el cuerpo no llega al ojo");
        }
    }
}
