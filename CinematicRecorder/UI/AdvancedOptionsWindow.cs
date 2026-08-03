using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Advanced recording options popout - currently reserved for future use.
    /// Speed ramps have moved to RecordingControlsWindow.
    /// </summary>
    public class AdvancedOptionsWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(
            CinematicUIResources.Windows.AdvancedOptions.DEFAULT_X,
            CinematicUIResources.Windows.AdvancedOptions.DEFAULT_Y,
            CinematicUIResources.Windows.AdvancedOptions.WIDTH,
            CinematicUIResources.Windows.AdvancedOptions.HEIGHT
        );

        private GUIStyle windowStyle;
        private bool stylesInitialized = false;
        private bool shouldShow = false;

        void Start()
        {
            InitStyles();
        }

        private void InitStyles()
        {
            if (stylesInitialized) return;
            windowStyle = CinematicUIResources.Styles.Window();
            stylesInitialized = true;
        }

        void OnGUI()
        {
            if (!shouldShow) return;

            windowRect = GUILayout.Window(
                CinematicUIResources.Windows.IDs.AdvancedOptions,
                windowRect,
                DrawWindow,
                Settings.AdvancedOptionsHeader,
                windowStyle);
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            GUIStyle labelStyle = CinematicUIResources.Styles.Label(Color.white, alignment: TextAnchor.MiddleCenter);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Advanced recording options\nwill appear here in future versions.", labelStyle);
            GUILayout.FlexibleSpace();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        public void Show()
        {
            shouldShow = true;
        }

        public void Hide()
        {
            shouldShow = false;
        }

        public void Toggle()
        {
            if (shouldShow) Hide();
            else Show();
        }
    }
}