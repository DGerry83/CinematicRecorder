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
        private bool _lastReportVisible;

        /// <summary>Main settings dialog view (ported in chunk C2).</summary>
        public SettingsDialog Settings { get; private set; }

        /// <summary>Advanced settings view (ported in chunk C3).</summary>
        public AdvancedSettingsWindow AdvancedSettings { get; private set; }

        /// <summary>Post-capture report view (ported in chunk C6).</summary>
        public FinalReportWindow FinalReport { get; private set; }

        /// <summary>Recording controls view (ported in chunk C5).</summary>
        public RecordingControlsWindow RecordingControls { get; private set; }

        /// <summary>
        /// Fade overlay controller (added in chunk C7): owns the fade clock driver and
        /// the capture-visible fade quad, plus the single CameraTransitionCoordinator
        /// instance the chunk C8 camera panel will consume.
        /// </summary>
        public FadeOverlayController FadeOverlay { get; private set; }

        /// <summary>
        /// Records the singleton for this host instance and creates the ported views.
        /// </summary>
        void Awake()
        {
            Instance = this;
            Settings = new SettingsDialog();
            AdvancedSettings = new AdvancedSettingsWindow();
            FinalReport = new FinalReportWindow();
            RecordingControls = new RecordingControlsWindow();
            FadeOverlay = new FadeOverlayController();
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
        /// Drives the report view's 30s session-end watchdog. The view is a plain
        /// class with no Unity event methods, so the host forwards its own Update.
        /// </summary>
        void Update()
        {
            if (FinalReport != null)
            {
                FinalReport.Tick();
            }

            // L6: when the final report appears, auto-hide the recording controls
            // window. Edge-triggered on the rising edge only — a manual re-open while
            // the report is still up is not fought.
            bool reportVisible = FinalReport != null && FinalReport.IsVisible;
            if (reportVisible && !_lastReportVisible && RecordingControls != null)
            {
                RecordingControls.Hide();
            }
            _lastReportVisible = reportVisible;
        }

        /// <summary>
        /// Drives the fade overlay controller once per rendered frame: the fade clock
        /// (deterministic while recording, real-time otherwise — the pre-C5 LateUpdate
        /// cadence) and the capture-visible fade quad. Deliberately not per physics
        /// step, which would over-advance the fade under TAB's sub-steps.
        /// </summary>
        void LateUpdate()
        {
            FadeOverlay?.Tick();
        }

        /// <summary>
        /// Unregisters the per-frame callback if registered, tears down the recording
        /// controls view (event unsubscribe + CameraTools shutdown) and the fade
        /// overlay (quad/mesh/material destruction), and clears the singleton.
        /// </summary>
        void OnDestroy()
        {
            if (_registered)
            {
                DearImGuiKSP.DearImGuiKSP.Unregister(ConsumerId);
                _registered = false;
            }

            RecordingControls?.Shutdown();
            FadeOverlay?.Shutdown();

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
            if (FinalReport != null && FinalReport.IsVisible)
            {
                using (var window = ImGuiEx.Window(CinematicUIStrings.Report.WindowTitle, autoResize: true))
                {
                    if (window.Visible)
                    {
                        FinalReport.Draw();
                    }
                }
            }

            // ------------------------------------------------------------------
            // RecordingControls dispatch (added by chunk C5)
            // ------------------------------------------------------------------
            if (RecordingControls != null && RecordingControls.IsVisible)
            {
                using (var window = ImGuiEx.Window(CinematicUIStrings.Recording.WindowTitle, autoResize: true))
                {
                    if (window.Visible)
                    {
                        RecordingControls.Draw();
                    }
                }
            }
        }
    }
}
