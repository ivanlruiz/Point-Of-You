using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace PointOfYou.EditorTools
{
    /// <summary>
    /// Instala los paquetes del modo online con la API de Package Manager (sin tocar manifest.json a mano).
    /// Correr con el Editor en batchmode SIN -quit (la instalación es asíncrona y este script sale solo):
    /// Unity.exe -batchmode -projectPath . -executeMethod PointOfYou.EditorTools.PackageInstaller.Install -logFile -
    /// </summary>
    public static class PackageInstaller
    {
        static readonly string[] PackagesToAdd =
        {
            "com.unity.netcode.gameobjects@2.13.3", // 3.x pide Unity 6000.7
            "com.unity.services.multiplayer@2.4.0",
            "com.unity.multiplayer.playmode@2.0.2",
        };

        const double TimeoutSeconds = 900;

        static AddAndRemoveRequest request;
        static double deadline;

        public static void Install()
        {
            Debug.Log($"[PackageInstaller] Agregando: {string.Join(", ", PackagesToAdd)}");
            request = Client.AddAndRemove(packagesToAdd: PackagesToAdd);
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (request == null) return;
            if (!request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup <= deadline) return;
                EditorApplication.update -= Poll;
                Debug.LogError("[PackageInstaller] Timeout esperando a Package Manager.");
                EditorApplication.Exit(2);
                return;
            }

            EditorApplication.update -= Poll;
            if (request.Status == StatusCode.Success)
            {
                Debug.Log($"[PackageInstaller] Resuelto: {string.Join(", ", request.Result.Select(p => $"{p.name}@{p.version}"))}");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[PackageInstaller] Falló: {request.Error?.message}");
                EditorApplication.Exit(1);
            }
        }
    }
}
