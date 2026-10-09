using UnityEngine;

namespace OverTheHorizon.LocalSimulation
{
    /// <summary>Independent fallback while a destroyed session's cleanup is retained.</summary>
    [DisallowMultipleComponent]
    internal sealed class LocalCleanupRecoveryHost : MonoBehaviour
    {
        private void LateUpdate() { LocalCleanupRecovery.LateRecovery(); }
    }
}
