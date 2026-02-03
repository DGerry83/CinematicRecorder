using UnityEngine;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Centralized UI resources, constants, and style factories for CinematicRecorder.
    /// All dimensions, colors, and spacing values previously scattered across dialog files.
    /// </summary>
    public static class CinematicUIResources
    {
        #region Internal Helpers
        private static Texture2D CreateTexture(Color color)
        {
            Color[] pixels = new Color[4];
            for (int i = 0; i < 4; i++) pixels[i] = color;
            Texture2D result = new Texture2D(2, 2);
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }
        #endregion

        #region Window Definitions
        public static class Windows
        {
            public static class IDs
            {
                public const int Settings = 12345;
                public const int FinalReport = 12346;
                public const int RecordingControls = 12347;
                public const int AdvancedOptions = 12348;
                public const int DialogDelete = 99999;
                public const int DialogUnassign = 99998;
            }

            public static class Settings
            {
                public const float DEFAULT_X = 300f;
                public const float DEFAULT_Y = 60f;
                public const float COLLAPSED_HEIGHT = 380f;
                public const float EXPANDED_HEIGHT = 620f;
            }

            public static class Recording
            {
                public const float DEFAULT_X = 300f;
                public const float DEFAULT_Y = 480f;
                public const float WIDTH = 355f;
                public const float HEIGHT_INITIAL = 220f;
                public const float HEIGHT_BASE = 140f;
            }

            public static class FinalReport
            {
                public const float DEFAULT_X = 320f;
                public const float DEFAULT_Y = 480f;
                public const float WIDTH = 400f;
                public const float HEIGHT = 240f;
            }

            public static class AdvancedOptions
            {
                public const float DEFAULT_X = 610f;
                public const float DEFAULT_Y = 480f;
                public const float WIDTH = 280f;
                public const float HEIGHT = 120f;
            }
        }
        #endregion

        #region Layout Constants
        public static class Layout
        {
            public const float SEPARATOR_LINE_WIDTH = 2f;

            public static class Settings
            {
                public const float MAIN_PANEL_WIDTH = 320f;
                public const float ADVANCED_PANEL_WIDTH = 260f;
                public const float ADVANCED_MARGIN = 20f;
                public const float TEXT_COLUMN_PADDING = 10f;
                public const float ADVANCED_TOGGLE_WIDTH = 110f;
                public const float ADVANCED_TOGGLE_HEIGHT = 28f;
            }

            public static class Encoder
            {
                public const float BTN_WIDTH_AMD = 55f;
                public const float BTN_WIDTH_NVIDIA = 70f;
                public const float BTN_WIDTH_CPU = 55f;
                public const float RATECONTROL_WIDTH_QUALITY = 110f;
                public const float RATECONTROL_WIDTH_VBR = 55f;
                public const float SPEED_WIDTH_SPEED = 80f;
                public const float SPEED_WIDTH_BALANCED = 100f;
                public const float SPEED_WIDTH_QUALITY = 90f;
            }

            public static class Duration
            {
                public const float BTN_WIDTH = 50f;
                public const float FIELD_WIDTH = 80f;
                public const float STEP = 5f;
            }

            public static class FPS
            {
                public const float PLAYBACK_LABEL_WIDTH = 90f;
                public const float LOCK_TOGGLE_WIDTH = 60f;
                public const float SELECTOR_WIDTH = 25f;
                public const float LABEL_WIDTH = 75f;
            }

            public const float BTN_HEIGHT_RECORD = 40f;

            public static class SpeedControl
            {
                public const float BUTTON_WIDTH = 80f;
                public const float BUTTON_HEIGHT = 30f;
            }

            public static class Camera
            {
                public const int GRID_ROWS = 4;
                public const int GRID_COLS = 4;
                public const int TOTAL_SLOTS = 16;
                public const float BUTTON_SIZE = 32f;
                public const float BUTTON_HEIGHT = 30f;
                public const float GRID_COLUMN_WIDTH = 140f;
                public const float GRID_TEXT_COLUMN_WIDTH = 160f;
            }

            public static class Progress
            {
                public const float BAR_WIDTH = 200f;
                public const float BAR_HEIGHT = 16f;
                public const float SEGMENT_WIDTH = 60f;
                public const float PULSE_SPEED = 2f;
            }

            public static class Zoom
            {
                public const float SMOOTH_TIME = 0.15f;
                public const float MAX_SPEED = 40f;
                public const float RETURN_SPEED = 8f;
                public const float INTENT_THRESHOLD = 0.05f;
                public const float LABEL_WIDTH = 30f;
                public const float RESET_BUTTON_WIDTH = 90f;
            }

            public static class Dialog
            {
                public const float WIDTH = 200f;
                public const float HEIGHT = 100f;
                public const float OFFSET_X = 60f;
                public const float OFFSET_Y = 80f;
                public const float BUTTON_HEIGHT = 30f;
            }

            public static class Crossfade
            {
                public const float DURATION_MIN = 0.05f;
                public const float DURATION_MAX = 2.0f;
                public const float SLIDER_MAX = 1f;
            }

            public static class Ramp
            {
                public const float DURATION_MIN = 0.1f;
                public const float DURATION_MAX = 3.0f;
            }
        }
        #endregion

        #region Spacing
        public static class Spacing
        {
            public const float MINIMAL = 2f;
            public const float TIGHT = 4f;      
            public const float INNER = 5f;
            public const float SECTION = 8f;    
            public const float NORMAL = 10f;    
            public const float LARGE = 15f;
            public const float STATUS_TOP = 10f;
        }
        #endregion

        #region Typography
        public static class Typography
        {
            public const int HEADER = 14;
            public const int INFO = 11;
            public const int HELP = 10;
        }
        #endregion

        #region Colors
        public static class Colors
        {
            public static readonly Color INFO_ORANGE = new Color(1f, 0.5490196f, 0f);
            public static readonly Color PROGRESS_BLUE = new Color(0.2f, 0.6f, 0.9f);
            public static readonly Color GLOW_GREEN = new Color(0.2f, 1f, 0.2f);
            public static readonly Color TOGGLE_ACTIVE_GREEN = new Color(0.2f, 0.9f, 0.2f);
            public static readonly Color AUTO_TRACK_BLUE = new Color(0.2f, 0.8f, 1f);
            public static readonly Color TEXT_DIM = Color.gray;
            public static readonly Color SEPARATOR_GRAY = new Color(0.9f, 0.9f, 0.9f);

            public static class Camera
            {
                public static readonly Color ACTIVE = new Color(0.2f, 0.8f, 0.2f);      // Green
                public static readonly Color ASSIGNED = new Color(1f, 0.9f, 0.2f);      // Yellow  
                public static readonly Color UNAVAILABLE = new Color(0.8f, 0.2f, 0.2f); // Red
                public static readonly Color UNASSIGNED = new Color(0.3f, 0.3f, 0.3f);  // Gray
                public static readonly Color REMOTE = new Color(0.0f, 0.8f, 0.8f);      // Aqua
                public static readonly Color CT_ACTIVE = new Color(1.0f, 0.6f, 0.1f);   // Orange (CameraTools)
                public static readonly Color CT_INACTIVE = new Color(0.8f, 0.4f, 0.1f); // Dark Orange
            }

            public static class Status
            {
                public static readonly Color RECORDING = Color.yellow;
                public static readonly Color STOPPING = Color.red;
                public static readonly Color READY = Color.green;
                public static readonly Color UNLIMITED = Color.yellow;
            }
        }
        #endregion

        #region Style Factories
        public static class Styles
        {
            /// <summary>
            /// Base window style from HighLogic.Skin
            /// </summary>
            public static GUIStyle Window()
            {
                return new GUIStyle(HighLogic.Skin.window);
            }

            /// <summary>
            /// Standard header label - bold, 14pt
            /// </summary>
            public static GUIStyle Header(bool centered = false)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.fontStyle = FontStyle.Bold;
                style.fontSize = Typography.HEADER;
                if (centered)
                    style.alignment = TextAnchor.MiddleCenter;
                return style;
            }

            /// <summary>
            /// Info text style - orange, wordwrapped
            /// </summary>
            public static GUIStyle Info(bool small = false)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.normal.textColor = Colors.INFO_ORANGE;
                style.wordWrap = true;
                style.fontSize = small ? Typography.HELP : Typography.INFO;
                return style;
            }

            /// <summary>
            /// Small helper text (10pt orange)
            /// </summary>
            public static GUIStyle Help()
            {
                return Info(small: true);
            }

            /// <summary>
            /// Generic label with specific color and alignment
            /// </summary>
            public static GUIStyle Label(Color color, FontStyle fontStyle = FontStyle.Normal,
                int fontSize = 0, TextAnchor alignment = TextAnchor.UpperLeft)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.normal.textColor = color;
                style.fontStyle = fontStyle;
                if (fontSize > 0)
                    style.fontSize = fontSize;
                style.alignment = alignment;
                return style;
            }

            /// <summary>
            /// Centered label with optional color override
            /// </summary>
            public static GUIStyle Centered(Color? color = null)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.alignment = TextAnchor.MiddleCenter;
                if (color.HasValue)
                    style.normal.textColor = color.Value;
                return style;
            }

            /// <summary>
            /// Status indicator - bold colored text
            /// </summary>
            public static GUIStyle Status(Color color)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.label);
                style.normal.textColor = color;
                style.fontStyle = FontStyle.Bold;
                return style;
            }

            /// <summary>
            /// Active/Selected button style (green bold text)
            /// </summary>
            public static GUIStyle ActiveButton()
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.button);
                style.normal.textColor = Color.green;
                style.fontStyle = FontStyle.Bold;
                return style;
            }

            /// <summary>
            /// Toggle button that appears colored when active
            /// Apply color to normal.textColor after calling this if state is active
            /// </summary>
            public static GUIStyle Toggle()
            {
                return new GUIStyle(HighLogic.Skin.toggle);
            }

            /// <summary>
            /// Creates a solid colored button style
            /// </summary>
            public static GUIStyle ColoredButton(Color background, Color text, FontStyle font = FontStyle.Bold)
            {
                GUIStyle style = new GUIStyle(HighLogic.Skin.button);
                style.fontStyle = font;
                style.normal.textColor = text;
                style.hover.textColor = text;
                style.active.textColor = text;

                Texture2D bg = CreateTexture(background);
                style.normal.background = bg;
                style.hover.background = bg;
                style.active.background = bg;
                return style;
            }

            /// <summary>
            /// Camera grid button by status index:
            /// 0=Active(Green), 1=Assigned(Yellow), 2=Unavailable(Red), 3=Unassigned(Gray), 4=Remote(Aqua),
            /// 5=CT_Active(Orange), 6=CT_Inactive(DarkOrange)
            /// </summary>
            public static GUIStyle CameraButton(int statusIndex)
            {
                switch (statusIndex)
                {
                    case 0: return ColoredButton(Colors.Camera.ACTIVE, Color.white, FontStyle.Bold);
                    case 1: return ColoredButton(Colors.Camera.ASSIGNED, Color.black, FontStyle.Bold);
                    case 2: return ColoredButton(Colors.Camera.UNAVAILABLE, Color.white, FontStyle.Bold);
                    case 3: return ColoredButton(Colors.Camera.UNASSIGNED, Colors.TEXT_DIM, FontStyle.Bold);
                    case 4: return ColoredButton(Colors.Camera.REMOTE, Color.white, FontStyle.Bold);
                    case 5: return ColoredButton(Colors.Camera.CT_ACTIVE, Color.white, FontStyle.Bold);
                    case 6: return ColoredButton(Colors.Camera.CT_INACTIVE, Color.white, FontStyle.Bold);
                    default: return ColoredButton(Colors.Camera.UNASSIGNED, Colors.TEXT_DIM, FontStyle.Bold);
                }
            }

            /// <summary>
            /// Progress bar background (empty box)
            /// </summary>
            public static GUIStyle ProgressBackground()
            {
                return new GUIStyle(GUI.skin.box);
            }

            /// <summary>
            /// Progress bar fill texture
            /// </summary>
            public static GUIStyle ProgressFill()
            {
                GUIStyle style = new GUIStyle(GUI.skin.box);
                style.normal.background = CreateTexture(Colors.PROGRESS_BLUE);
                return style;
            }

            /// <summary>
            /// Standard button style wrapper for consistency
            /// </summary>
            public static GUIStyle Button()
            {
                return new GUIStyle(HighLogic.Skin.button);
            }
        }
        #endregion
    }
}