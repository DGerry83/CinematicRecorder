using DearImGuiKSP;
using UnityEngine;

[assembly: KSPAssemblyDependencyEqualMajor("DearImGuiKSP", 1, 3)]

namespace CinematicRecorder.UI
{
    /// <summary>
    /// DearImGui-KSP host for CinematicRecorder. Owns the per-frame UI callback
    /// registration and dispatches drawing to the window views as they are ported.
    /// The remaining IMGUI windows run alongside this host until their port chunks.
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

        /// <summary>Main settings dialog view (ported in chunk C2).</summary>
        public SettingsDialog Settings { get; private set; }

        /// <summary>Advanced settings view (ported in chunk C3).</summary>
        public AdvancedSettingsWindow AdvancedSettings { get; private set; }

        /// <summary>
        /// Records the singleton for this host instance and creates the ported views.
        /// </summary>
        void Awake()
        {
            Instance = this;
            Settings = new SettingsDialog();
            AdvancedSettings = new AdvancedSettingsWindow();
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
        /// window views; each port chunk adds its own dispatch block between the
        /// markers below.
        /// </summary>
        private void OnFrame()
        {
            // ------------------------------------------------------------------
            // Settings dispatch (added by chunk C2)
            // ------------------------------------------------------------------
            if (Settings != null && Settings.IsVisible)
            {
                using (var window = ImGuiEx.Window(CinematicUIStrings.Settings.WindowTitle, autoResize: true))
                {
                    if (window.Visible)
                    {
                        Settings.Draw();
                    }
                }
            }

            // ------------------------------------------------------------------
            // AdvancedSettings dispatch (added by chunk C3)
            // ------------------------------------------------------------------
            if (AdvancedSettings != null && AdvancedSettings.IsVisible)
            {
                using (var window = ImGuiEx.Window(CinematicUIStrings.AdvancedSettings.WindowTitle, autoResize: true))
                {
                    if (window.Visible)
                    {
                        AdvancedSettings.Draw();
                    }
                }
            }

            // ------------------------------------------------------------------
            // FinalReport dispatch (added by chunk C6)
            // ------------------------------------------------------------------

            // ------------------------------------------------------------------
            // RecordingControls dispatch (added by chunk C5)
            // ------------------------------------------------------------------
        }
    }
}
