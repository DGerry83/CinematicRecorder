using CinematicRecorder.Capture;
using CinematicRecorder.Integration;
using CinematicRecorder.UI;
using FFmpeg.AutoGen;
using KSP.UI.Screens;
using System;
using System.IO;
using UnityEngine;

namespace CinematicRecorder.Core
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class CinematicRecorderAddon : MonoBehaviour
    {
        public static CinematicRecorderAddon Instance { get; private set; }
        public static FrameCapture FrameCaptureInstance { get; private set; }

        private ApplicationLauncherButton toolbarButton;
        private Texture2D toolbarIcon;

        /// <summary>
        /// Locates FFmpeg binaries and initializes AutoGen bindings.
        /// </summary>
        void Awake()
        {
            Instance = this;

            // Set FFmpeg path immediately and verify
            string pluginPath = Path.GetDirectoryName(typeof(CinematicRecorderAddon).Assembly.Location);
            string ffmpegPath = Path.Combine(pluginPath, "..", "PluginData", "FFmpeg");
            ffmpegPath = Path.GetFullPath(ffmpegPath); // Resolve the ..

            UnityEngine.Debug.Log($"[CinematicRecorder] Plugin location: {pluginPath}");
            UnityEngine.Debug.Log($"[CinematicRecorder] FFmpeg path: {ffmpegPath}");

            if (!Directory.Exists(ffmpegPath))
            {
                UnityEngine.Debug.LogError($"[CinematicRecorder] FFmpeg directory NOT FOUND: {ffmpegPath}");
                return;
            }

            string[] requiredDlls = new[] { "avcodec-59.dll", "avformat-59.dll", "avutil-57.dll", "swresample-4.dll", "swscale-6.dll" };
            foreach (var dll in requiredDlls)
            {
                string dllPath = Path.Combine(ffmpegPath, dll);
                if (File.Exists(dllPath))
                    UnityEngine.Debug.Log($"[CinematicRecorder] Found {dll}");
                else
                    UnityEngine.Debug.LogError($"[CinematicRecorder] MISSING {dll} at {dllPath}");
            }

            ffmpeg.RootPath = ffmpegPath;
            UnityEngine.Debug.Log($"[CinematicRecorder] FFmpeg.RootPath set to: {ffmpeg.RootPath}");

            // Test FFmpeg load
            try
            {
                var version = ffmpeg.av_version_info();
                UnityEngine.Debug.Log($"[CinematicRecorder] FFmpeg version: {version}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[CinematicRecorder] FFmpeg init failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
        /// <summary>
        /// Creates core GameObjects and hooks into ApplicationLauncher toolbar.
        /// </summary>
        void Start()
        {
            // Initialize core systems
            GameObject coreObject = new GameObject("CinematicRecorder_Core");
            DontDestroyOnLoad(coreObject);

            FrameCaptureInstance = coreObject.AddComponent<FrameCapture>();

            GameObject safetyMonitorObj = new GameObject("CinematicRecorder_SafetyMonitor");
            DontDestroyOnLoad(safetyMonitorObj);
            safetyMonitorObj.AddComponent<SafetyMonitor>();
            UnityEngine.Debug.Log("[CinematicRecorder] SafetyMonitor initialized");

            // Hook into ApplicationLauncher (toolbar)
            GameEvents.onGUIApplicationLauncherReady.Add(OnGUIApplicationLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(OnGUIApplicationLauncherDestroyed);

            // Load icon
            toolbarIcon = GameDatabase.Instance.GetTexture("CinematicRecorder/Icons/CinematicIcon", false);
            if (toolbarIcon == null)
            {
                UnityEngine.Debug.LogWarning("[CinematicRecorder] Icon not found, using white texture");
                toolbarIcon = Texture2D.whiteTexture;
            }
            // Init Camera Panel Config
            GameObject configObj = new GameObject("CameraPanelConfig");
            DontDestroyOnLoad(configObj);
            configObj.AddComponent<CameraPanelConfig>();

            // Create DearImGui-KSP UI host (draws nothing until window ports land)
            GameObject uiHostObj = new GameObject("CinematicRecorder_UiHost");
            DontDestroyOnLoad(uiHostObj);
            uiHostObj.AddComponent<CinematicUiHost>();

            // Settings dialog dismissal resets the toolbar button (HOST-2)
            if (CinematicUiHost.Instance != null)
            {
                CinematicUiHost.Instance.Settings.OnDialogDismissed += OnDialogClosed;
            }
        }
        /// <summary>
        /// Removes toolbar button and destroys UI windows.
        /// </summary>
        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIApplicationLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnGUIApplicationLauncherDestroyed);

            if (toolbarButton != null)
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);

            // Cleanup windows
            if (CinematicUiHost.Instance != null)
            {
                CinematicUiHost.Instance.Settings.OnDialogDismissed -= OnDialogClosed;
            }

            // Destroy the UI host (its OnDestroy unregisters from DearImGui-KSP and
            // shuts down the recording controls view)
            if (CinematicUiHost.Instance != null)
                Destroy(CinematicUiHost.Instance.gameObject);
        }
        private void OnGUIApplicationLauncherReady()
        {
            if (toolbarButton == null)
            {
                toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                    OnToolbarButtonOn,    
                    OnToolbarButtonOff,   
                    null, null, null, null,
                    ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW,
                    toolbarIcon
                );
            }
        }
        private void OnGUIApplicationLauncherDestroyed()
        {
            if (toolbarButton != null)
            {
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);
                toolbarButton = null;
            }
        }
        private void OnToolbarButtonOn()
        {
            if (CinematicUiHost.Instance != null)
            {
                CinematicUiHost.Instance.Settings.Show();
                CinematicUiHost.Instance.RecordingControls.Show();
            }
        }
        private void OnToolbarButtonOff()
        {
            // Button pressed to turn OFF - hide both windows
            if (CinematicUiHost.Instance != null)
            {
                CinematicUiHost.Instance.Settings.Hide();
                CinematicUiHost.Instance.RecordingControls.Hide();
            }
        }
        private void OnDialogClosed()
        {
            if (toolbarButton != null)
                toolbarButton.SetFalse(false); 
        }
    }
}