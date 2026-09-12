using UnityEngine;

[assembly: KSPAssemblyDependencyEqualMajor("DearImGuiKSP", 1, 3)]

namespace CinematicRecorder.UI
{
    /// <summary>
    /// DearImGui-KSP host for CinematicRecorder. Owns the per-frame UI callback
    /// registration and dispatches drawing to the window views as they are ported.
    /// The existing IMGUI windows run alongside this host until their port chunks.
    /// </summary>
    public sealed class CinematicUiHost : MonoBehaviour
    {
        /// <summary>
        /// Singleton set in Awake and cleared in OnDestroy. Consumers reach their
        /// views through this instance (e.g. CinematicUiHost.Instance.Settings).
        /// </summary>
        public static CinematicUiHost Instance { get; private set; }

        private const string ConsumerId = "CinematicRecorder";

        private bool _registered;

        /// <summary>
        /// Records the singleton for this host instance.
        /// </summary>
        void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Registers the per-frame callback with DearImGui-KSP when the library is
        /// available; otherwise logs a dependency notice and stays inert.
        /// </summary>
        void Start()
        {
            if (!DearImGuiKSP.DearImGuiKSP.IsAvailable)
            {
                Debug.Log(CinematicUIStrings.Common.DearImGuiKspUnavailableLog);
                return;
            }

            DearImGuiKSP.DearImGuiKSP.Register(ConsumerId, OnFrame);
            _registered = true;
        }

        /// <summary>
        /// Unregisters the per-frame callback if registered and clears the singleton.
        /// </summary>
        void OnDestroy()
        {
            if (_registered)
            {
                DearImGuiKSP.DearImGuiKSP.Unregister(ConsumerId);
                _registered = false;
            }

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Per-frame UI declaration, invoked by DearImGui-KSP. Dispatches to the
        /// window views; this chunk ships zero views, so nothing is drawn yet.
        /// Future view properties are named after their windows: Settings,
        /// AdvancedSettings, RecordingControls, FinalReport — each port chunk adds
        /// its own dispatch block between the markers below.
        /// </summary>
        private void OnFrame()
        {
            // ------------------------------------------------------------------
            // Settings dispatch (added by chunk C2)
            // ------------------------------------------------------------------

            // ------------------------------------------------------------------
            // AdvancedSettings dispatch (added by chunk C3)
            // ------------------------------------------------------------------

            // ------------------------------------------------------------------
            // FinalReport dispatch (added by chunk C6)
            // ------------------------------------------------------------------

            // ------------------------------------------------------------------
            // RecordingControls dispatch (added by chunk C5)
            // ------------------------------------------------------------------
        }
    }
}
