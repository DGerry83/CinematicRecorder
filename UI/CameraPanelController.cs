using CinematicRecorder.Core;
using CinematicRecorder.Integration;
using static CinematicRecorder.UI.CinematicUIStrings;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Manages the camera assignment grid, zoom controls, and preset management.
    /// Designed to be hosted inside RecordingControlsWindow or operate standalone.
    /// </summary>
    public class CameraPanelController
    {
        #region Fields & State
        private readonly List<CameraSlot> cameraSlots = new List<CameraSlot>();
        private readonly GUIStyle[] cameraButtonStyles = new GUIStyle[7];

        private bool cameraPanelStylesInitialized = false;
        private bool showCameraPanel = false;
        private bool showPresetList = false;
        private bool showDeleteConfirm = false;
        private int pendingUnassignSlot = -1;
        private string presetNameBuffer = "";

        // Zoom State
        private float zoomIntentSlider = 0f;
        private float zoomSmoothVelocity = 0f;
        private bool autoDistanceTracking = false;
        private float autoZoomDistanceRef = 100f;
        private float targetFoV = 60f;
        private float currentFoV = 60f;
        private object zoomControlledCamera = null;

        // Crossfade State (coordinated with parent window)
        private float screenFadeAlpha = 0f;
        private bool isFading = false;
        private float fadeSpeed = 8f;
        private Action pendingCameraAction;
        private bool useFadeOnSwap = true;
        private float fadeDurationSlider = 0.5f;

        // External Dependencies
        private readonly MonoBehaviour host;
        private Rect parentWindowRect;
        #endregion

        #region Properties
        public bool IsVisible => showCameraPanel;
        public bool IsFading => isFading;
        public float FadeAlpha => screenFadeAlpha;
        public bool UseFadeOnSwap => useFadeOnSwap;
        #endregion

        #region Constructor
        public CameraPanelController(MonoBehaviour hostBehaviour)
        {
            host = hostBehaviour ?? throw new ArgumentNullException(nameof(hostBehaviour));
            InitializeSlots();
            SubscribeToEvents();
        }
        #endregion

        #region Initialization
        private void InitializeSlots()
        {
            cameraSlots.Clear();
            for (int i = 0; i < CinematicUIResources.Layout.Camera.TOTAL_SLOTS; i++)
            {
                cameraSlots.Add(new CameraSlot { buttonID = string.Format(CameraController.ButtonIdFormat, i) });
            }

            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded += OnPresetLoaded;
            }
        }

        public void InitializeStyles()
        {
            if (cameraPanelStylesInitialized) return;

            for (int i = 0; i < 7; i++)
            {
                cameraButtonStyles[i] = CinematicUIResources.Styles.CameraButton(i);
            }

            cameraPanelStylesInitialized = true;
        }

        public void Shutdown()
        {
            UnsubscribeFromEvents();
            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded -= OnPresetLoaded;
            }
        }
        #endregion

        #region Event Subscription
        private void SubscribeToEvents()
        {
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
        }

        private void UnsubscribeFromEvents()
        {
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
        }

        private void OnVesselWillDestroy(Vessel v)
        {
            if (v == FlightGlobals.ActiveVessel && HullCamBridge.IsAnyCameraActive())
            {
                HullCamBridge.ClearHullCamStaticState();
            }
        }

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            HullCamBridge.ClearHullCamStaticState();
        }

        private void OnPresetLoaded(CameraPanelPreset preset)
        {
            if (preset?.buttonAssignments != null && preset.buttonAssignments.Count == CinematicUIResources.Layout.Camera.TOTAL_SLOTS)
            {
                cameraSlots.Clear();
                cameraSlots.AddRange(preset.buttonAssignments);
                presetNameBuffer = preset.presetName;

                // Notify parent to update window position if needed
                // Parent can read preset.panelX/panelY from preset if desired
            }
        }
        #endregion

        #region Main Rendering Entry Point
        public void Draw(Rect parentWindowRect)
        {
            this.parentWindowRect = parentWindowRect;

            if (!HullCamBridge.IsAvailable)
            {
                DrawDisabledPanel();
                return;
            }

            InitializeStyles();

            GUILayout.Space(CinematicUIResources.Spacing.SECTION);
            DrawFoldoutButton();

            if (!showCameraPanel) return;

            DrawFadeControls();
            DrawGridContainer();
            DrawZoomControlsIfActive();
            GUILayout.Space(CinematicUIResources.Spacing.SECTION);
            DrawProfilesInterface();
            UpdateMonitoring();
        }

        public void DrawFadeOverlay()
        {
            if (!isFading) return;

            screenFadeAlpha += Time.unscaledDeltaTime * fadeSpeed;

            if (screenFadeAlpha >= 1f)
            {
                screenFadeAlpha = 1f;
                pendingCameraAction?.Invoke();
                pendingCameraAction = null;
                fadeSpeed = -Mathf.Abs(fadeSpeed);
            }
            else if (screenFadeAlpha <= 0f && fadeSpeed < 0)
            {
                screenFadeAlpha = 0f;
                isFading = false;
                fadeSpeed = Mathf.Abs(fadeSpeed);
            }

            GUI.color = new Color(0, 0, 0, screenFadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        #endregion

        #region UI Sections
        private void DrawDisabledPanel()
        {
            GUIStyle disabledStyle = CinematicUIResources.Styles.Label(
                CinematicUIResources.Colors.TEXT_DIM,
                alignment: TextAnchor.MiddleCenter
            );
            GUILayout.Label(CameraController.RequiresHullCam, disabledStyle);
        }

        private void DrawFoldoutButton()
        {
            string label = showCameraPanel ? CameraController.FoldoutCollapse : CameraController.FoldoutExpand;
            if (GUILayout.Button(label, HighLogic.Skin.button))
            {
                showCameraPanel = !showCameraPanel;
            }
        }

        private void DrawFadeControls()
        {
            GUILayout.BeginHorizontal();
            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (useFadeOnSwap)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
                toggleStyle.alignment = TextAnchor.MiddleLeft;
            }
            useFadeOnSwap = GUILayout.Toggle(useFadeOnSwap, CameraController.FadeOnSwapToggle, toggleStyle);
            GUILayout.EndHorizontal();

            if (useFadeOnSwap)
            {
                float duration = Mathf.Lerp(
                    CinematicUIResources.Layout.Crossfade.DURATION_MIN,
                    CinematicUIResources.Layout.Crossfade.DURATION_MAX,
                    fadeDurationSlider
                );
                GUILayout.Label(string.Format(CameraController.FadeDurationFormat, duration), HighLogic.Skin.label);
                fadeDurationSlider = GUILayout.HorizontalSlider(
                    fadeDurationSlider,
                    0f,
                    CinematicUIResources.Layout.Crossfade.SLIDER_MAX
                );
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
        }

        private void DrawGridContainer()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            // LEFT: Camera Grid + Return Button
            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Camera.GRID_COLUMN_WIDTH));
            DrawGrid();

            GUILayout.FlexibleSpace();

            bool hasActiveCam = HullCamBridge.IsAnyCameraActive() || CameraToolsBridge.IsActive();
            GUI.enabled = hasActiveCam;
            if (GUILayout.Button(CameraController.ReturnToMain, GUILayout.Height(CinematicUIResources.Layout.SpeedControl.BUTTON_HEIGHT)))
            {
                TriggerSwitchWithFade(() =>
                {
                    if (CameraToolsBridge.IsActive())
                    {
                        CameraToolsBridge.Revert();
                    }
                    else if (HullCamBridge.IsAnyCameraActive())
                    {
                        HullCamBridge.RestoreMain();
                    }
                });
            }
            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            // RIGHT: Instructions + Assign Current
            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Camera.GRID_TEXT_COLUMN_WIDTH));
            DrawInstructions();

            GUILayout.FlexibleSpace();

            GUI.enabled = HullCamBridge.GetCurrentCamera() != null;
            if (GUILayout.Button(CameraController.AssignCurrent, GUILayout.Height(CinematicUIResources.Layout.SpeedControl.BUTTON_HEIGHT)))
            {
                AssignCurrentToFirstOpenSlot();
            }
            GUI.enabled = true;
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawGrid()
        {
            Vessel currentVessel = FlightGlobals.ActiveVessel;

            for (int row = 0; row < CinematicUIResources.Layout.Camera.GRID_ROWS; row++)
            {
                GUILayout.BeginHorizontal();

                for (int col = 0; col < CinematicUIResources.Layout.Camera.GRID_COLS; col++)
                {
                    int index = row * 4 + col;
                    DrawCameraButton(index, currentVessel);
                }

                GUILayout.EndHorizontal();
            }
        }

        private void DrawCameraButton(int index, Vessel currentVessel)
        {
            CameraSlot slot = cameraSlots[index];
            CameraSlot.SlotStatus status = slot.GetStatus(currentVessel);
            int styleIndex = GetStyleIndexForStatus(status, slot.isCameraToolsSlot);
            string buttonLabel = (index + 1).ToString();

            // Reserve rect first so we can use it for hit detection
            Rect buttonRect = GUILayoutUtility.GetRect(
                CinematicUIResources.Layout.Camera.BUTTON_SIZE,
                CinematicUIResources.Layout.Camera.BUTTON_HEIGHT,
                cameraButtonStyles[styleIndex]);

            // Check right-click BEFORE drawing the button
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown &&
                evt.button == 1 &&
                buttonRect.Contains(evt.mousePosition))
            {
                if (status != CameraSlot.SlotStatus.Unassigned)
                {
                    pendingUnassignSlot = index;
                    evt.Use(); // Consume the event so button doesn't see it
                }
            }
            // Then draw the button and handle left-clicks
            else if (GUI.Button(buttonRect, buttonLabel, cameraButtonStyles[styleIndex]))
            {
                OnButtonClicked(index);
            }
        }

        private void DrawInstructions()
        {
            GUIStyle header = CinematicUIResources.Styles.Header();
            GUILayout.Label(CameraController.ControlsHeader, header);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            GUIStyle small = CinematicUIResources.Styles.Label(Color.white, fontSize: CinematicUIResources.Typography.INFO);
            small.wordWrap = true;
            GUILayout.Label(CameraController.ControlLeftClick, small);
            GUILayout.Label(CameraController.ControlRightClick, small);
            GUILayout.Label(CameraController.ControlAssignCurrent, small);
        }

        private void DrawZoomControlsIfActive()
        {
            if (!HullCamBridge.IsAnyCameraActive()) return;

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label(CameraController.ZoomControlLabel, HighLogic.Skin.label);

            GUILayout.BeginHorizontal();
            GUILayout.Label(CameraController.ZoomOut, GUILayout.Width(CinematicUIResources.Layout.Zoom.LABEL_WIDTH));

            GUIStyle intentStyle = new GUIStyle(HighLogic.Skin.horizontalSlider);
            GUIStyle thumbStyle = new GUIStyle(HighLogic.Skin.horizontalSliderThumb);
            zoomIntentSlider = GUILayout.HorizontalSlider(zoomIntentSlider, -1f, 1f, intentStyle, thumbStyle);

            GUILayout.Label(CameraController.ZoomIn, GUILayout.Width(CinematicUIResources.Layout.Zoom.LABEL_WIDTH));
            GUILayout.EndHorizontal();

            float maxFov = HullCamBridge.GetCameraFoVMax(HullCamBridge.GetCurrentCamera());
            GUILayout.Label(string.Format(CameraController.FOVFormat, currentFoV, maxFov), HighLogic.Skin.label);

            GUILayout.BeginHorizontal();
            DrawZoomResetButton(maxFov);
            GUILayout.FlexibleSpace();
            DrawAutoDistanceToggle();
            GUILayout.EndHorizontal();

            if (autoDistanceTracking)
            {
                GUIStyle distStyle = CinematicUIResources.Styles.Label(
                    CinematicUIResources.Colors.INFO_ORANGE,
                    fontSize: CinematicUIResources.Typography.HELP
                );
                GUILayout.Label(CameraController.AutoDistanceTooltip, distStyle);
            }

            GUILayout.EndVertical();
        }

        private void DrawZoomResetButton(float maxFov)
        {
            if (GUILayout.Button(CameraController.ResetZoom, GUILayout.Width(CinematicUIResources.Layout.Zoom.RESET_BUTTON_WIDTH)))
            {
                targetFoV = maxFov;
                zoomIntentSlider = 0f;
            }
        }

        private void DrawAutoDistanceToggle()
        {
            GUIStyle autoStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (autoDistanceTracking)
            {
                autoStyle.normal.textColor = CinematicUIResources.Colors.AUTO_TRACK_BLUE;
                autoStyle.fontStyle = FontStyle.Bold;
            }
            autoDistanceTracking = GUILayout.Toggle(autoDistanceTracking, CameraController.AutoDistanceToggle, autoStyle);
        }

        private void DrawProfilesInterface()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);

            GUILayout.BeginHorizontal();

            CameraPanelConfig scenario = CameraPanelConfig.Instance;
            bool hasPresets = scenario != null && scenario.GetPresetNames().Count > 0;
            CameraPanelPreset activePreset = scenario?.GetActivePreset();

            EnsurePresetNameBuffer();

            presetNameBuffer = GUILayout.TextField(presetNameBuffer, GUILayout.Width(150));

            if (GUILayout.Button(CameraController.SavePreset, GUILayout.Width(50)))
            {
                SavePreset(scenario);
            }

            GUI.enabled = activePreset != null;
            if (GUILayout.Button(CameraController.DeletePreset, GUILayout.Width(50)))
            {
                if (activePreset != null) showDeleteConfirm = true;
            }
            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            if (hasPresets && GUILayout.Button(CameraController.LoadPreset, GUILayout.Width(60)))
            {
                showPresetList = !showPresetList;
            }

            GUILayout.EndHorizontal();

            if (showPresetList && scenario != null)
            {
                DrawPresetDropdown(scenario);
            }

            GUILayout.EndVertical();
        }

        private void DrawPresetDropdown(CameraPanelConfig scenario)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            List<string> presetNames = scenario.GetPresetNames();
            foreach (string name in presetNames)
            {
                if (GUILayout.Button(name))
                {
                    scenario.LoadPreset(name);
                    showPresetList = false;
                }
            }
            GUILayout.EndVertical();
        }
        #endregion

        #region Dialog Rendering (Call from parent's OnGUI)
        public void DrawConfirmationDialogs()
        {
            DrawDeleteDialog();
            DrawUnassignDialog();
        }

        private void DrawDeleteDialog()
        {
            if (!showDeleteConfirm) return;

            Rect dialogRect = new Rect(
                parentWindowRect.x + CinematicUIResources.Layout.Dialog.OFFSET_X,
                parentWindowRect.y + CinematicUIResources.Layout.Dialog.OFFSET_Y,
                CinematicUIResources.Layout.Dialog.WIDTH,
                CinematicUIResources.Layout.Dialog.HEIGHT
            );

            GUI.ModalWindow(CinematicUIResources.Windows.IDs.DialogDelete, dialogRect, (id) =>
            {
                GUILayout.Label(string.Format(CameraController.DeleteConfirmFormat, presetNameBuffer));
                GUILayout.Space(CinematicUIResources.Spacing.SECTION);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Common.Yes, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    CameraPanelConfig.Instance?.DeletePreset(presetNameBuffer);
                    presetNameBuffer = GetDefaultPresetName();
                    showDeleteConfirm = false;
                }

                if (GUILayout.Button(Common.No, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    showDeleteConfirm = false;
                }
                GUILayout.EndHorizontal();
            }, CameraController.ConfirmDeleteTitle);
        }

        private void DrawUnassignDialog()
        {
            if (pendingUnassignSlot < 0) return;

            Rect dialogRect = new Rect(
                parentWindowRect.x + CinematicUIResources.Layout.Dialog.OFFSET_X,
                parentWindowRect.y + CinematicUIResources.Layout.Dialog.OFFSET_Y,
                CinematicUIResources.Layout.Dialog.WIDTH,
                CinematicUIResources.Layout.Dialog.HEIGHT
            );
            int slotIndex = pendingUnassignSlot;

            GUI.ModalWindow(CinematicUIResources.Windows.IDs.DialogUnassign, dialogRect, (id) =>
            {
                GUILayout.Label(string.Format(CameraController.UnassignConfirmFormat, slotIndex + 1));
                GUILayout.Space(CinematicUIResources.Spacing.SECTION);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Common.Yes, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    ClearSlot(slotIndex);
                    pendingUnassignSlot = -1;
                }

                if (GUILayout.Button(Common.No, GUILayout.Height(CinematicUIResources.Layout.Dialog.BUTTON_HEIGHT)))
                {
                    pendingUnassignSlot = -1;
                }
                GUILayout.EndHorizontal();
            }, CameraController.ConfirmUnassignTitle);
        }
        #endregion

        #region Camera Interaction Logic
        private void OnButtonClicked(int index)
        {
            CameraSlot slot = cameraSlots[index];

            if (slot.isCameraToolsSlot)
            {
                if (slot.ctSettings == null)
                {
                    Debug.LogWarning($"[CamPanel] Slot {index} is CT type but has null settings");
                    return;
                }

                if (slot.ctSettings.Mode == ToolModes.Pathing)
                {
                    if (slot.ctSettings.SelectedPathIndex < 0)
                    {
                        ScreenMessages.PostScreenMessage("Cannot activate - invalid path index", 2f);
                        return; // Exit here, don't fade
                    }
                    if (!CameraToolsBridge.PathExists(slot.ctSettings.SelectedPathIndex))
                    {
                        ScreenMessages.PostScreenMessage("Saved path no longer exists", 2f);
                        return; // Exit here, don't fade
                    }
                }

                Debug.Log($"[CamPanel] Activating CT slot {index}: Mode={slot.ctSettings.Mode}, Pos={slot.ctSettings.ManualPosition}");

                TriggerSwitchWithFade(() =>
                {
                    // CRITICAL: Force full revert to clear CameraTools internal state
                    if (CameraToolsBridge.IsActive())
                    {
                        CameraToolsBridge.Revert();
                    }

                    if (HullCamBridge.IsAnyCameraActive())
                    {
                        HullCamBridge.ClearHullCamStaticState();
                    }

                    CameraToolsBridge.ActivateMode(slot.ctSettings.Mode, slot.ctSettings);
                });
                return;
            }

            // Existing HullCam handling
            Vessel currentVessel = FlightGlobals.ActiveVessel;
            CameraSlot.SlotStatus status = slot.GetStatus(currentVessel);

            switch (status)
            {
                case CameraSlot.SlotStatus.Unassigned:
                    AssignCurrentToSlot(index);
                    break;
                case CameraSlot.SlotStatus.Active:
                    return;
                case CameraSlot.SlotStatus.Assigned:
                case CameraSlot.SlotStatus.Remote:
                    ActivateSlotCamera(slot, currentVessel);
                    break;
                case CameraSlot.SlotStatus.Unavailable:
                    ScreenMessages.PostScreenMessage(CameraController.CameraUnavailable, 2f);
                    break;
            }
        }

        private void ActivateSlotCamera(CameraSlot slot, Vessel currentVessel)
        {
            object cam = HullCamBridge.ResolveCameraSlot(slot, currentVessel);
            if (cam != null && cam != HullCamBridge.GetCurrentCamera())
            {
                zoomIntentSlider = 0f;
                TriggerSwitchWithFade(() =>
                {
                    // One-step switch: Release CT if active, don't revert to stock
                    if (CameraToolsBridge.IsActive())
                    {
                        CameraToolsBridge.ReleaseControlWithoutReverting();
                    }

                    HullCamBridge.Activate(cam);
                });
            }
        }

        private void AssignCurrentToFirstOpenSlot()
        {
            for (int i = 0; i < CinematicUIResources.Layout.Camera.TOTAL_SLOTS; i++)
            {
                if (cameraSlots[i].GetStatus() == CameraSlot.SlotStatus.Unassigned)
                {
                    AssignCurrentToSlot(i);
                    break;
                }
            }
        }

        private void AssignCurrentToSlot(int index)
        {
            // Check if CameraTools is currently active first
            if (CameraToolsBridge.IsAvailable && CameraToolsBridge.IsActive())
            {
                var settings = CameraToolsBridge.CaptureCurrentSettings();
                if (settings != null)
                {
                    // VALIDATION: Ensure Pathing mode has a valid path selected
                    if (settings.Mode == ToolModes.Pathing && settings.SelectedPathIndex < 0)
                    {
                        ScreenMessages.PostScreenMessage("Cannot save: No path selected in CameraTools", 2f);
                        return; // Don't save invalid path reference
                    }

                    cameraSlots[index] = new CameraSlot
                    {
                        buttonID = string.Format(CameraController.ButtonIdFormat, index),
                        isCameraToolsSlot = true,
                        ctSettings = settings,
                        cameraName = settings.GetDisplayName()
                    };

                    ScreenMessages.PostScreenMessage(string.Format(CameraController.SavedCameraToolsFormat, settings.GetDisplayName()), 2f);
                    return;
                }
            }

            // Existing HullCam assignment
            if (!ValidateAssignmentPrerequisites(out object currentCam, out Vessel vessel)) return;

            Part part = GetPartFromCamera(currentCam);
            string camName = HullCamBridge.GetCameraName(currentCam) ?? "";

            cameraSlots[index] = new CameraSlot
            {
                buttonID = string.Format(CameraController.ButtonIdFormat, index),
                cameraName = camName,
                partPersistentId = part != null ? part.persistentId : 0u,
                vesselId = vessel.id.ToString(),
                allowAnyVessel = false,
                isCameraToolsSlot = false,
                ctSettings = null
            };

            ScreenMessages.PostScreenMessage(string.Format(CameraController.SavedHullCamFormat, camName), 2f);
        }

        private bool ValidateAssignmentPrerequisites(out object currentCam, out Vessel vessel)
        {
            currentCam = null;
            vessel = null;

            if (!HullCamBridge.IsAvailable) return false;

            currentCam = HullCamBridge.GetCurrentCamera();
            if (currentCam == null)
            {
                ScreenMessages.PostScreenMessage(CameraController.NoCameraToAssign, 2f);
                return false;
            }

            vessel = FlightGlobals.ActiveVessel;
            return vessel != null;
        }

        private Part GetPartFromCamera(object cameraModule)
        {
            try
            {
                Component comp = cameraModule as Component;
                if (comp == null) return null;

                Transform current = comp.transform;
                while (current != null)
                {
                    Part part = current.GetComponent<Part>();
                    if (part != null) return part;
                    current = current.parent;
                }
            }
            catch (Exception ex)
            {
                Debug.Log(CameraController.GetPartFromCameraFail + ex.Message);
            }
            return null;
        }

        private void ClearSlot(int index)
        {
            cameraSlots[index] = new CameraSlot { buttonID = string.Format(CameraController.ButtonIdFormat, index) };
        }

        private void UpdateMonitoring()
        {
            if (!HullCamBridge.IsAvailable) return;
            // Tracking only - HullCam handles camera death internally
            _ = HullCamBridge.GetCurrentCamera();
        }

        private int GetStyleIndexForStatus(CameraSlot.SlotStatus status)
        {
            switch (status)
            {
                case CameraSlot.SlotStatus.Active: return 0;
                case CameraSlot.SlotStatus.Assigned: return 1;
                case CameraSlot.SlotStatus.Unavailable: return 2;
                case CameraSlot.SlotStatus.Remote: return 4;
                default: return 3;
            }
        }
        #endregion

        #region Zoom Logic
        public void ProcessZoomLateUpdate()
        {
            if (!showCameraPanel) return;
            if (!HullCamBridge.IsAvailable) return;

            var activeCam = HullCamBridge.GetCurrentCamera();
            if (activeCam == null)
            {
                zoomControlledCamera = null;
                return;
            }

            InitializeZoomForCamera(activeCam);
            ProcessZoomIntent(activeCam);
            ApplyZoom(activeCam);
            DecayZoomIntent();
        }

        private void InitializeZoomForCamera(object activeCam)
        {
            if (activeCam == zoomControlledCamera) return;

            zoomControlledCamera = activeCam;
            currentFoV = HullCamBridge.GetCameraFoV(activeCam);
            targetFoV = currentFoV;
            zoomIntentSlider = 0f;
        }

        private void ProcessZoomIntent(object activeCam)
        {
            float minFoV = HullCamBridge.GetCameraFoVMin(activeCam);
            float maxFoV = HullCamBridge.GetCameraFoVMax(activeCam);

            if (autoDistanceTracking && FlightGlobals.ActiveVessel != null)
            {
                ApplyAutoZoom(activeCam, minFoV, maxFoV);
            }
            else
            {
                ApplyManualZoom(minFoV, maxFoV);
            }

            targetFoV = Mathf.Clamp(targetFoV, minFoV, maxFoV);
            currentFoV = Mathf.SmoothDamp(currentFoV, targetFoV, ref zoomSmoothVelocity,
                CinematicUIResources.Layout.Zoom.SMOOTH_TIME, Mathf.Infinity, Time.deltaTime);
        }

        private void ApplyAutoZoom(object activeCam, float minFoV, float maxFoV)
        {
            var camTransform = HullCamBridge.GetCameraTransform(activeCam);
            if (camTransform == null || FlightGlobals.ActiveVessel == null) return;

            float distance = Vector3.Distance(camTransform.position, FlightGlobals.ActiveVessel.transform.position);
            float t = Mathf.Clamp01(Mathf.Log(distance / 10f + 1f) / Mathf.Log(autoZoomDistanceRef / 10f + 1f));
            float autoTarget = Mathf.Lerp(maxFoV, minFoV, t);

            targetFoV = Mathf.Lerp(autoTarget, targetFoV, Mathf.Abs(zoomIntentSlider));
        }

        private void ApplyManualZoom(float minFoV, float maxFoV)
        {
            if (Mathf.Abs(zoomIntentSlider) > CinematicUIResources.Layout.Zoom.INTENT_THRESHOLD)
            {
                float zoomDelta = -zoomIntentSlider * CinematicUIResources.Layout.Zoom.MAX_SPEED * Time.deltaTime;
                targetFoV += zoomDelta;
            }
            else
            {
                zoomIntentSlider = Mathf.MoveTowards(zoomIntentSlider, 0f, Time.deltaTime * 2f);
            }
        }

        private void ApplyZoom(object activeCam)
        {
            HullCamBridge.SetCameraFoV(activeCam, currentFoV);
        }

        private void DecayZoomIntent()
        {
            if (!Input.GetMouseButton(0))
            {
                zoomIntentSlider = Mathf.MoveTowards(zoomIntentSlider, 0f,
                    Time.deltaTime * CinematicUIResources.Layout.Zoom.RETURN_SPEED);
            }
        }
        #endregion

        #region Crossfade
        private void TriggerSwitchWithFade(Action cameraAction)
        {
            if (!useFadeOnSwap)
            {
                cameraAction?.Invoke();
                return;
            }

            if (isFading) return;
            isFading = true;
            screenFadeAlpha = 0f;

            float duration = CinematicUIResources.Layout.Crossfade.DURATION_MIN +
                fadeDurationSlider * (CinematicUIResources.Layout.Crossfade.DURATION_MAX - CinematicUIResources.Layout.Crossfade.DURATION_MIN);
            fadeSpeed = 1f / duration;

            pendingCameraAction = cameraAction;
        }
        #endregion

        #region Preset Management
        private void SavePreset(CameraPanelConfig scenario)
        {
            if (scenario == null) return;

            string nameToSave = string.IsNullOrWhiteSpace(presetNameBuffer)
                ? GetDefaultPresetName()
                : presetNameBuffer;

            nameToSave = GetUniquePresetName(nameToSave);

            scenario.SavePreset(nameToSave, false, cameraSlots, parentWindowRect.x, parentWindowRect.y);
            presetNameBuffer = nameToSave;
        }

        private string GetDefaultPresetName()
        {
            return FlightGlobals.ActiveVessel?.vesselName ?? CameraController.Preset;
        }

        private string GetUniquePresetName(string baseName)
        {
            var scenario = CameraPanelConfig.Instance;
            if (scenario == null) return baseName;

            var existing = scenario.GetPresetNames();
            if (!existing.Contains(baseName)) return baseName;

            int counter = 1;
            string candidate;
            do
            {
                candidate = string.Format(CameraController.PresetNameUniqueFormat, baseName, counter);
                counter++;
            } while (existing.Contains(candidate));

            return candidate;
        }

        private void EnsurePresetNameBuffer()
        {
            if (string.IsNullOrEmpty(presetNameBuffer))
            {
                presetNameBuffer = GetDefaultPresetName();
            }
        }
        #endregion

        #region Helper Methods

        private int GetStyleIndexForStatus(CameraSlot.SlotStatus status, bool isCameraTools)
        {
            if (isCameraTools)
            {
                switch (status)
                {
                    case CameraSlot.SlotStatus.Active: return 5; // Orange - CT Active
                    case CameraSlot.SlotStatus.Assigned: return 6; // Dark Orange - CT Inactive
                    default: return 3; // Gray
                }
            }

            switch (status)
            {
                case CameraSlot.SlotStatus.Active: return 0; // Green
                case CameraSlot.SlotStatus.Assigned: return 1; // Yellow
                case CameraSlot.SlotStatus.Unavailable: return 2; // Red
                case CameraSlot.SlotStatus.Remote: return 4; // Aqua
                default: return 3; // Gray
            }
        }

        #endregion
    }
}