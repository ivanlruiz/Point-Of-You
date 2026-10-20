using Unity.Netcode;

namespace PointOfYou
{
    /// <summary>
    /// En qué rol corre esta PC. El host (o el modo local) simula la física y la lógica del nivel;
    /// los clientes ("réplicas") solo muestran lo que les llega por red.
    /// </summary>
    public static class Net
    {
        public static bool IsOnline => NetworkManager.Singleton && NetworkManager.Singleton.IsListening;
        public static bool IsReplica => IsOnline && !NetworkManager.Singleton.IsServer;
        public static bool IsHost => IsOnline && NetworkManager.Singleton.IsServer;
        /// <summary>Online y ya adentro (el cliente puede estar "escuchando" pero todavía conectándose).</summary>
        public static bool IsConnected => IsOnline && (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsConnectedClient);
    }
}
