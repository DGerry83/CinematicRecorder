using CinematicRecorder.Integration;
using DearImGuiKSP;
using UnityEngine;

[assembly: KSPAssemblyDependencyEqualMajor("DearImGuiKSP", 1, 3)]

namespace CinematicRecorder.UI
{
    /// <summary>
    /// DearImGui-KSP host for CinematicRecorder. Owns the per-frame UI callback
    /// registration and dispatches drawing to the window views as they are ported.
    /// C10: three window views (Settings — with the Advanced content as its second
    /// tab — FinalReport, RecordingControls); the L6 auto-hide is superseded.
    /// </summary>
    public sealed class CinematicUiHost : MonoBehaviour
    {
        /// <summary>
        /// Singleton set in Awake and cleared in OnDestroy. Consumers reach their
        /// views through this instance (e.g. CinematicUiHost.Instance.Settings).
        /// </summary>
        public static CinematicUiHost Instance { get; private set; }

        private const string ConsumerId = "CinematicRecorder";

        /// <summary>
        /// Which host instance currently owns the DearImGui registration. Tracked
        /// separately from <see cref="Instance"/> because the two diverge during a
        /// Flight→Flight transition: the new host registers while the previous
        /// generation's host is still alive awaiting deferred destruction (#011).
        /// </summary>
        private static CinematicUiHost registeredHost;

        private bool _registered;

        /// <summary>Main settings dialog view (ported in chunk C2; hosts the Advanced tab since C10).</summary>
        public SettingsDialog Settings { get; private set; }

        /// <summary>Post-capture report view (ported in chunk C6).</summary>
        public FinalReportWindow FinalReport { get; private set; }

        /// <summary>Recording controls view (ported in chunk C5).</summary>
        public RecordingControlsWindow RecordingControls { get; private set; }

        /// <summary>
        /// Fade overlay controller (added in chunk C7, reworked in the C8 fade
        /// rework after D-U9 withdrawal): owns the fade clock driver, the IMGUI
        /// fullscreen overlay draw, and the single CameraTransitionCoordinator
        /// instance the chunk C8 camera panel consumes.
        /// </summary>
        public FadeOverlayController FadeOverlay { get; private set; }

        /// <summary>
        /// Records the singleton for this host instance and creates the ported views.
        /// </summary>
        void Awake()
        {
            Instance = this;
            Settings = new SettingsDialog();
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

            // #011: the library IGNORES duplicate-id registrations (no replacement),
            // and a previous scene generation's host may still hold the consumer ID
            // when this Start runs (its GameObject destruction is deferred). Take the
            // ID over explicitly so this host's callback becomes the live one.
            if (registeredHost != null && registeredHost != this)
            {
                DearImGuiKSP.DearImGuiKSP.Unregister(ConsumerId);
                registeredHost = null;
            }

            DearImGuiKSP.DearImGuiKSP.Register(ConsumerId, OnFrame);
            registeredHost = this;
            _registered = true;
        }

        /// <summary>
        /// Drives the report view's 30s session-end watchdog. The view is a plain
        /// class with no Unity event methods, so the host forwards its own Update.
        /// (L6 auto-hide was removed in C10 — the recording-controls window stays
        /// visible when the report appears.)
        /// </summary>
        void Update()
        {
            if (FinalReport != null)
            {
                FinalReport.Tick();
            }
        }

        /// <summary>
        /// Drives the fade overlay controller and the camera panel once per rendered
        /// frame. The fade clock (deterministic while recording, real-time otherwise
        /// — the pre-C5 LateUpdate cadence) lives in FadeOverlay.Tick; the panel tick
        /// forwards zoom processing and the fade-midpoint auto-zoom from
        /// RecordingControls.Tick. Deliberately not per physics step, which would
        /// over-advance the fade under TAB's sub-steps.
        /// </summary>
        void LateUpdate()
        {
            FadeOverlay?.Tick();
            RecordingControls?.Tick();
        }

        /// <summary>
        /// Draws the stock IMGUI fade overlay (D-U9 withdrawn — screen overlay,
        /// pre-0.2.4 behavior; appears in footage only when Capture UI is on).
        /// Second sanctioned G-U1 exception alongside stock PopupDialog. Renders
        /// only — the fade clock is driven by LateUpdate, never from here.
        /// </summary>
        void OnGUI()
        {
            FadeOverlay?.DrawOverlay();
        }

        /// <summary>
        /// Tears down the recording controls view (event unsubscribe + camera panel —
        /// instance-level, always runs). Shared resources are released only by their
        /// current owner: the CameraTools interop statics and the singleton gate on
        /// <see cref="Instance"/>, the DearImGui consumer-ID registration gates on
        /// <see cref="registeredHost"/> (#010/#011 — on a Flight→Flight transition the
        /// next scene's host may have taken over either or both before a previous
        /// generation's deferred destruction runs).
        /// </summary>
        void OnDestroy()
        {
            bool ownsSharedState = Instance == this;

            if (ownsSharedState)
            {
                CameraToolsAPIManager.Shutdown();
                Instance = null;
            }

            if (registeredHost == this)
            {
                if (_registered)
                {
                    DearImGuiKSP.DearImGuiKSP.Unregister(ConsumerId);
                    _registered = false;
                }
                registeredHost = null;
            }

            RecordingControls?.Shutdown();
        }

        /// <summary>
        /// Per-frame UI declaration, invoked by DearImGui-KSP. Dispatches to the
        /// window views; each port chunk adds its own dispatch block between the
        /// markers below.
        /// </summary>
        private void OnFrame()
        {
            // ------------------------------------------------------------------
            // Settings dispatch (added by chunk C2; hosts the Advanced tab since C10)
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
            // Note 7 (FR-6): open offset to the right of the main panel so both are
            // visible (main opens at the library default ~60,60 and is auto-sized).
            // FirstUseEver positions once per session and never fights user drags.
            if (RecordingControls != null && RecordingControls.IsVisible)
            {
                ImGuiEx.SetNextWindowPos(new Vector2(560f, 60f), ImGuiCond.FirstUseEver);
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
