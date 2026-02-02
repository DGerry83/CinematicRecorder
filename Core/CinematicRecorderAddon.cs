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
        private SettingsDialog settingsDialog;
        private RecordingControlsWindow recordingControlsWindow;
        private Texture2D toolbarIcon;

        void Awake()
        {
            Instance = this;

            // CRITICAL: Set FFmpeg path immediately and verify
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

        void Start()
        {
            // Initialize core systems
            GameObject coreObject = new GameObject("CinematicRecorder_Core");
            DontDestroyOnLoad(coreObject);

            FrameCaptureInstance = coreObject.AddComponent<FrameCapture>();

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
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnGUIApplicationLauncherReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(OnGUIApplicationLauncherDestroyed);

            if (toolbarButton != null)
                ApplicationLauncher.Instance.RemoveModApplication(toolbarButton);

            // Cleanup windows
            if (settingsDialog != null)
            {
                settingsDialog.OnDialogDismissed -= OnDialogClosed;
                if (settingsDialog.gameObject != null)
                    Destroy(settingsDialog.gameObject);
            }
            if (recordingControlsWindow != null && recordingControlsWindow.gameObject != null)
                Destroy(recordingControlsWindow.gameObject);
        }

        private void OnGUIApplicationLauncherReady()
        {
            if (toolbarButton == null)
            {
                toolbarButton = ApplicationLauncher.Instance.AddModApplication(
                    OnToolbarButtonOn,    // Called when button pressed (turns on)
                    OnToolbarButtonOff,   // Called when button pressed again (turns off)
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
            // Button pressed to turn ON - show both windows

            // Ensure SettingsDialog exists (DontDestroyOnLoad)
            if (settingsDialog == null)
            {
                GameObject settingsGo = new GameObject("SettingsDialog");
                DontDestroyOnLoad(settingsGo);
                settingsDialog = settingsGo.AddComponent<SettingsDialog>();
                // Position: (300, 60) - defined in SettingsDialog, but we can ensure it here if needed
                settingsDialog.OnDialogDismissed += OnDialogClosed;
            }
            settingsDialog.Show();

            // Ensure RecordingControlsWindow exists (DontDestroyOnLoad)
            if (recordingControlsWindow == null)
            {
                GameObject controlsGo = new GameObject("RecordingControlsWindow");
                DontDestroyOnLoad(controlsGo);
                recordingControlsWindow = controlsGo.AddComponent<RecordingControlsWindow>();
                // Position: (300, 480) - defined in RecordingControlsWindow
            }
            recordingControlsWindow.Show();
        }

        private void OnToolbarButtonOff()
        {
            // Button pressed to turn OFF - hide both windows
            if (settingsDialog != null)
            {
                settingsDialog.Hide();
            }
            if (recordingControlsWindow != null)
            {
                recordingControlsWindow.Hide();
            }
        }

        // Called when dialog closes via escape or other means
        private void OnDialogClosed()
        {
            if (toolbarButton != null)
                toolbarButton.SetFalse(false); 
        }
    }
}