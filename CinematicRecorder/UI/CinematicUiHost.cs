using DearImGuiKSP;
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

        /// <summary>Main settings dialog view (ported in chunk C2).</summary>
        public SettingsDialog Settings { get; private set; }

        // SCAFFOLDING (C2): replaced by C3 port — the legacy IMGUI advanced settings
        // window stays alive (floating) until then; this host owns its GameObject.
        private AdvancedSettingsWindow _legacyAdvancedSettings;
        private GameObject _legacyAdvancedSettingsObject;

        /// <summary>
        /// True while the legacy advanced settings window is visible. The settings view
        /// highlights its Advanced button from this. SCAFFOLDING (C2): replaced by C3 port.
        /// </summary>
        public bool LegacyAdvancedSettingsVisible =>
            _legacyAdvancedSettings != null && _legacyAdvancedSettings.IsVisible;

        /// <summary>
        /// Records the singleton for this host instance and creates the ported views.
        /// </summary>
        void Awake()
        {
            Instance = this;
            Settings = new SettingsDialog();
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

            // SCAFFOLDING (C2): replaced by C3 port — destroy the legacy advanced
            // window this host created.
            if (_legacyAdvancedSettingsObject != null)
            {
                Destroy(_legacyAdvancedSettingsObject);
                _legacyAdvancedSettingsObject = null;
                _legacyAdvancedSettings = null;
            }

            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// Toggles the legacy IMGUI advanced settings window, creating it on first use.
        /// SCAFFOLDING (C2): replaced by C3 port — the settings view calls this because
        /// a plain view class cannot own components.
        /// </summary>
        public void ToggleLegacyAdvancedSettings()
        {
            if (_legacyAdvancedSettings == null)
            {
                _legacyAdvancedSettingsObject = new GameObject("AdvancedSettingsWindow");
                DontDestroyOnLoad(_legacyAdvancedSettingsObject);
                _legacyAdvancedSettings = _legacyAdvancedSettingsObject.AddComponent<AdvancedSettingsWindow>();
                _legacyAdvancedSettings.Initialize();
                _legacyAdvancedSettings.Show();
                return;
            }

            if (_legacyAdvancedSettings.IsVisible)
            {
                _legacyAdvancedSettings.Hide();
            }
            else
            {
                _legacyAdvancedSettings.Show();
            }
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

            // ------------------------------------------------------------------
            // FinalReport dispatch (added by chunk C6)
            // ------------------------------------------------------------------

            // ------------------------------------------------------------------
            // RecordingControls dispatch (added by chunk C5)
            // ------------------------------------------------------------------
        }
    }
}
