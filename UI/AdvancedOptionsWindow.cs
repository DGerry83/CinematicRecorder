using UnityEngine;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Advanced recording options popout - currently reserved for future use.
    /// Speed ramps have moved to RecordingControlsWindow.
    /// </summary>
    public class AdvancedOptionsWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(610, 480, 280, 120);
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
            windowStyle = new GUIStyle(HighLogic.Skin.window);
            stylesInitialized = true;
        }

        void OnGUI()
        {
            if (!shouldShow) return;

            windowRect = GUILayout.Window(
                12348,
                windowRect,
                DrawWindow,
                "Advanced Options",
                windowStyle);
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();

            GUIStyle labelStyle = new GUIStyle(HighLogic.Skin.label);
            labelStyle.alignment = TextAnchor.MiddleCenter;
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