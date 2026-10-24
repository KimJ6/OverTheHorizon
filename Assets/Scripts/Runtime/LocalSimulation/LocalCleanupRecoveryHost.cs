using UnityEngine;

namespace OverTheHorizon.LocalSimulation
{
    /// <summary>Persistent cleanup owner after a session root is destroyed or time is paused.</summary>
    [DisallowMultipleComponent]
    internal sealed class LocalCleanupRecoveryHost : MonoBehaviour
    {
        // LateUpdate remains eligible while Time.timeScale is zero.
        private void LateUpdate() { LocalCleanupRecovery.LateRecovery(); }
    }
}
