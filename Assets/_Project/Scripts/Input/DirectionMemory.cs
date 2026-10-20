using UnityEngine;

namespace PointOfYou
{
    /// <summary>
    /// "Adelante con memoria": mientras mantenés una dirección, el eje de movimiento queda fijo aunque la cámara
    /// (que está en la cabeza de otro y te sigue) gire. Si no, pasar al lado de tu observador te da vuelta
    /// "adelante" 180° y caminar recto se vuelve una curva. Se recalcula al soltar o al girar mucho el stick.
    /// </summary>
    public sealed class DirectionMemory
    {
        const float ReleaseDelay = 0.15f;
        const float RelockAngle = 40f;

        bool locked;
        Vector3 lockedForward;
        Vector2 lockedStick;
        float releasedAt = -1f;

        public bool IsLocked => locked;

        public Vector3 Apply(Vector3 cameraForward, Vector2 move, float time)
        {
            if (move.sqrMagnitude < 0.04f)
            {
                if (locked)
                {
                    if (releasedAt < 0f) releasedAt = time;
                    else if (time - releasedAt > ReleaseDelay) locked = false;
                }
                return locked ? lockedForward : cameraForward;
            }

            releasedAt = -1f;
            if (!locked || Vector2.Angle(move, lockedStick) > RelockAngle)
            {
                locked = true;
                lockedForward = cameraForward;
                lockedStick = move;
            }
            return lockedForward;
        }

        public void Reset()
        {
            locked = false;
            releasedAt = -1f;
        }
    }
}
