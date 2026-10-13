using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PointOfYou.EditorTools
{
    /// <summary>La primera vez que se abre el proyecto en esta PC, abre la escena del prototipo.</summary>
    [InitializeOnLoad]
    static class OpenPrototypeOnFirstLoad
    {
        static OpenPrototypeOnFirstLoad() => EditorApplication.delayCall += Check;

        static void Check()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;

            string key = "PointOfYou.OpenedPrototype." + Application.dataPath.GetHashCode();
            if (EditorPrefs.GetBool(key)) return;
            EditorPrefs.SetBool(key, true);

            var active = SceneManager.GetActiveScene();
            if (active.path == PrototypeBuilder.ScenePath || active.isDirty) return;
            if (File.Exists(PrototypeBuilder.ScenePath)) EditorSceneManager.OpenScene(PrototypeBuilder.ScenePath);
        }
    }
}
