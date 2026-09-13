using CinematicRecorder.Capture;
using CinematicRecorder.Core;
using CinematicRecorder.Integration;
using DearImGuiKSP;
using DearImGuiKSP.Application;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Camera assignment panel view — 16-slot grid, fade-on-swap controls, auto-zoom
    /// and target zoom, and camera preset persistence. DearImGui-KSP VIEW-1 port
    /// (chunk C8) of the IMGUI controller, drawn inside the "Recording Controls"
    /// window's "Camera Panel" collapsing header (PANEL-1). Camera switches route
    /// through the shared <see cref="CameraTransitionCoordinator"/> owned by
    /// <see cref="FadeOverlayController"/> (C7; D-U9 withdrawn in the C8 fade
    /// rework — the fade is a screen overlay again, pre-0.2.4 behavior).
    /// </summary>
    public class CameraPanelController
    {
        #region Constants & Static State
        // Slot button colors: the old 7 IMGUI CameraButton styles become byte-exact
        // ImGuiCol.Button pushes (index mapping preserved in GetStyleIndexForStatus).
        private static readonly Color32[] SlotButtonColors =
        {
            new Color32(51, 204, 51, 255),   // 0 Active      — old Colors.Camera.ACTIVE (0.2, 0.8, 0.2)
            new Color32(255, 229, 51, 255),  // 1 Assigned    — old (1.0, 0.9, 0.2)
            new Color32(204, 51, 51, 255),   // 2 Unavailable — old (0.8, 0.2, 0.2)
            new Color32(76, 76, 76, 255),    // 3 Unassigned  — old (0.3, 0.3, 0.3)
            new Color32(0, 204, 204, 255),   // 4 Remote      — old (0.0, 0.8, 0.8)
            new Color32(255, 153, 26, 255),  // 5 CT_Active   — old (1.0, 0.6, 0.1)
            new Color32(204, 102, 26, 255),  // 6 CT_Inactive — old (0.8, 0.4, 0.1)
        };

        private static readonly Color32[] SlotButtonTextColors =
        {
            new Color32(255, 255, 255, 255), // 0 Active      — old Color.white
            new Color32(0, 0, 0, 255),       // 1 Assigned    — old Color.black (on yellow)
            new Color32(255, 255, 255, 255), // 2 Unavailable — old Color.white
            new Color32(128, 128, 128, 255), // 3 Unassigned  — old Colors.TEXT_DIM
            new Color32(255, 255, 255, 255), // 4 Remote      — old Color.white
            new Color32(255, 255, 255, 255), // 5 CT_Active   — old Color.white
            new Color32(255, 255, 255, 255), // 6 CT_Inactive — old Color.white
        };

        // Grid button labels built once — no per-frame string building (VIEW-1).
        private static readonly string[] SlotButtonLabels = BuildSlotButtonLabels();

        // Zoom-curve combo items (replaces the old 2x2 SelectionGrid).
        private static readonly string[] CurveComboItems =
        {
            CameraController.CurveLinear,
            CameraController.CurveEaseIn,
            CameraController.CurveEaseOut,
            CameraController.CurveEaseInOut
        };

        // Go button: the old flat green ColoredButton becomes a gradient button with
        // the same green identity (explicit-color overload renders in every theme).
        private static readonly Color32 GoButtonTop = new Color32(51, 255, 51, 255);    // old Colors.GLOW_GREEN (0.2, 1, 0.2)
        private static readonly Color32 GoButtonBottom = new Color32(26, 178, 26, 255);

        // Grey for read-only stand-ins (no disabled-widget state — C2 D-U8 #1) and
        // orange for the info lines (old Colors.INFO_ORANGE).
        private static readonly Color32 GreyedColor = KspPalette.TextLightGrey;
        private static readonly Color32 InfoOrangeColor = new Color32(255, 140, 0, 255);

        // Layout constants relocated from CinematicUIResources.Layout (file deleted in C9).
        private const int GridRows = 4;
        private const int GridCols = 4;
        private const int TotalSlots = 16;
        private const float ZoomMaxSpeed = 40f;
        private const float ZoomReturnSpeed = 8f;
        private const float FadeDurationMin = 0.05f;
        private const float FadeDurationMax = 2.0f;
        private const float FadeSliderMax = 1f;

        // Note 15: one explicit size for all 16 slot cells — uniform 4x4 grid (labels
        // auto-sized before, so "1" and "16" rendered different widths). 40px fits the
        // two-digit label with padding (demo grid precedent). Explicit px sizes do not
        // follow the library UI scale (documented limitation).
        private static readonly Vector2 SlotButtonSize = new Vector2(40f, 26f);

        // Stock PopupDialog identities (G-U1 exception); names double as dedupe keys.
        private const string DeleteDialogName = "CinematicRecorderDeletePreset";
        private const string UnassignDialogName = "CinematicRecorderUnassignSlot";
        private const string OverwriteDialogName = "CinematicRecorderOverwritePreset";
        #endregion

        #region Services
        private readonly CameraSlotManager slotManager;
        private readonly CinematicCameraManager cameraManager;
        private readonly CameraToolsCameraController ctController;
        private readonly HullCamZoomController _hullCamZoom;
        private readonly CameraToolsZoomController _cameraToolsZoom;
        private DeterministicZoomController _deterministicZoom; // Set when recording starts

        private float _zoomIntent;
        #endregion

        #region UI State
        private string presetNameBuffer = "";
        private int _selectedSlotIndex = -1; // L5: last clicked slot (the × Unassign target)
        private int _lastCTSlotIndex = -1;

        private enum ZoomMode { Rate, Target }
        private ZoomMode currentZoomMode = ZoomMode.Rate;
        private float targetDuration = 0f;
        private ZoomCurve targetZoomCurve = ZoomCurve.Linear;
        private bool targetIsConsistentFraming = false;
        private float targetFOVValue = 60f;
        private string _targetFovText = "60.0";

        // Note 16: on-bar fade-seconds cache (FR-3 valueText) — recomputed only when
        // the coordinator's slider value changes; never per frame. Seeded to -1 so the
        // first frame always computes (the slider range is 0..1).
        private float _lastFadeSliderValue = -1f;
        private string _fadeDurationText;
        #endregion

        #region Preset Name Cache
        // Rebuilt only when CameraPanelConfig fires OnPresetsChanged (or lazily on
        // first sight of the config) — no per-frame list/array allocation.
        private string[] _presetNamesCache;
        #endregion

        #region Constructor
        /// <summary>
        /// Creates the panel services and subscribes to game and preset events.
        /// The old MonoBehaviour host parameter is gone — the field it fed was
        /// write-only (nothing in the panel used coroutines).
        /// </summary>
        internal CameraPanelController()
        {
            slotManager = new CameraSlotManager();
            ctController = new CameraToolsCameraController();
            cameraManager = CinematicCameraManager.Instance;
            _hullCamZoom = new HullCamZoomController();
            _cameraToolsZoom = new CameraToolsZoomController();

            SubscribeToEvents();
        }
        #endregion

        #region Initialization & Cleanup
        private void SubscribeToEvents()
        {
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
            GameEvents.onVesselChange.Add(OnVesselChange);

            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded += OnPresetLoaded;
                CameraPanelConfig.Instance.OnPresetsChanged += OnPresetsChanged;
            }
        }

        private void UnsubscribeFromEvents()
        {
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
            GameEvents.onVesselChange.Remove(OnVesselChange);

            if (CameraPanelConfig.Instance != null)
            {
                CameraPanelConfig.Instance.OnPresetLoaded -= OnPresetLoaded;
                CameraPanelConfig.Instance.OnPresetsChanged -= OnPresetsChanged;
            }
        }

        /// <summary>
        /// Cleans up event subscriptions. Called from RecordingControlsWindow.Shutdown.
        /// </summary>
        public void Shutdown()
        {
            UnsubscribeFromEvents();
        }
        #endregion

        #region Event Handlers
        private void OnVesselWillDestroy(Vessel v)
        {
            if (v == FlightGlobals.ActiveVessel)
            {
                HullCamBridge.ClearHullCamStaticState();
                cameraManager.ClearActiveSlot();
            }
        }

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            HullCamBridge.ClearHullCamStaticState();
            slotManager.HandleSceneChange();
        }

        private void OnVesselChange(Vessel v)
        {
            slotManager.HandleVesselChange();
        }

        private void OnPresetLoaded(CameraPanelPreset preset)
        {
            slotManager.LoadPreset(preset);
            presetNameBuffer = preset?.presetName ?? "";
        }

        private void OnPresetsChanged()
        {
            _presetNamesCache = null;
        }
        #endregion

        #region Main Rendering
        /// <summary>
        /// Per-frame widget declarations for the camera panel body. Called only from
        /// RecordingControlsWindow, inside the "Camera Panel" collapsing header.
        /// Layout per LAYOUT_PROPOSAL §4: fade row → slot grid → Return/Assign row →
        /// slot/context section (L4/L5) → Zoom collapsing header → presets.
        /// </summary>
        internal void Draw()
        {
            if (!HullCamBridge.IsAvailable)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, CameraController.RequiresHullCam);
                return;
            }

            DrawFadeRow();

            Vessel currentVessel = FlightGlobals.ActiveVessel;
            DrawSlotGrid(currentVessel);

            using (ImGuiEx.Row())
            {
                DrawReturnToMainButton();
                DrawAssignCurrentButton();
            }

            DrawContextSection();
            DrawSelectedSlotRow(currentVessel);

            if (cameraManager.HasActiveCamera)
            {
                DrawZoomSection();
            }

            DrawPresetsSection();
        }
        #endregion

        #region Per-Frame Processing
        /// <summary>
        /// Per-frame game logic (no ImGui calls), forwarded from
        /// RecordingControlsWindow.Tick while the recording-controls window is
        /// visible — the old pre-port cadence of the fade overlay (window-visible,
        /// not foldout). Applies the midpoint auto-zoom rehomed from the deleted
        /// fade-overlay draw method, then the zoom processing of the old
        /// ProcessZoomLateUpdate, gated on the cached "Camera Panel" header state
        /// (the old foldout gate). The fade clock itself is driven
        /// unconditionally by FadeOverlayController.Tick (C7).
        /// </summary>
        /// <param name="panelOpen">The last drawn "Camera Panel" collapsing-header state.</param>
        internal void ProcessFrame(bool panelOpen)
        {
            ApplyMidpointAutoZoom();

            if (!panelOpen) return;

            slotManager.CheckExternalDeactivation();
            var activeSlot = slotManager.ActiveSlot;

            // Get the appropriate controller for current mode and camera type
            IZoomController zoomController = GetCurrentZoomController();
            if (zoomController == null) return;

            // Sync slot settings to controller before processing
            SyncZoomSettingsFromSlot(zoomController, activeSlot);

            if (currentZoomMode == ZoomMode.Rate)
            {
                HandleRateMode(zoomController, activeSlot);
            }
            else // Target mode
            {
                HandleTargetMode(zoomController, activeSlot);
            }
        }

        // Rehomed verbatim from the old fade-overlay draw method (:219-239): at the
        // fade midpoint the active CameraTools slot's auto-zoom re-applies
        // (consistent framing, or the native distance heuristic). Old cadence was
        // "recording-controls window visible" — preserved by the caller's IsVisible
        // gate, not the header gate.
        private void ApplyMidpointAutoZoom()
        {
            CameraTransitionCoordinator coordinator = CinematicUiHost.Instance?.FadeOverlay?.Coordinator;
            if (coordinator == null || !coordinator.IsCompletingSwitch) return;

            var activeSlot = slotManager.ActiveSlot;
            if (activeSlot == null || !activeSlot.isCameraToolsSlot || activeSlot.ctSettings == null)
                return;

            var ctCam = cameraManager.ActiveCamera as CameraToolsCamera;
            if (ctCam == null) return;

            if (activeSlot.ctSettings.UseConsistentAutoZoom)
            {
                _cameraToolsZoom.UseConsistentAutoZoom = true;
                _cameraToolsZoom.ConsistentZoomPadding = activeSlot.ctSettings.ZoomPadding;
                _cameraToolsZoom.ApplyConsistentFraming();
            }
            else if (activeSlot.ctSettings.AutoZoom)
            {
                ApplyNativeAutoZoom(activeSlot);
            }
        }

        private IZoomController GetCurrentZoomController()
        {
            if (DeterministicCaptureSession.IsRunning)
            {
                // Cache deterministic controller reference
                if (_deterministicZoom == null && DeterministicCaptureSession.ActiveZoomController != null)
                {
                    _deterministicZoom = DeterministicCaptureSession.ActiveZoomController;
                }
                return _deterministicZoom;
            }

            // Real-time mode: return appropriate controller for active camera
            if (cameraManager.ActiveCamera is CameraToolsCamera)
                return _cameraToolsZoom;
            else if (cameraManager.ActiveCamera is HullCamController)
                return _hullCamZoom;

            return null;
        }

        private void SyncZoomSettingsFromSlot(IZoomController zoom, CameraSlot slot)
        {
            if (slot == null) return;

            zoom.UseConsistentAutoZoom = slot.GetUseConsistentAutoZoom();
            zoom.ConsistentZoomPadding = slot.GetZoomPadding();
        }

        private void PersistZoomSettingsToSlot(IZoomController zoom, CameraSlot slot)
        {
            if (slot == null) return;

            slot.SetUseConsistentAutoZoom(zoom.UseConsistentAutoZoom);
            slot.SetZoomPadding(zoom.ConsistentZoomPadding);
        }

        private void HandleRateMode(IZoomController zoom, CameraSlot activeSlot)
        {
            // Apply rate input from UI
            zoom.SetRateInput(_zoomIntent);

            // Update based on mode
            if (zoom.UseConsistentAutoZoom)
            {
                zoom.ApplyConsistentFraming();
            }
            else
            {
                zoom.Update(Time.deltaTime);
            }

            // Persist any state changes (e.g., auto-activation from transitions)
            PersistZoomSettingsToSlot(zoom, activeSlot);

            // Decay input for elastic slider behavior (UI-side only)
            if (!Input.GetMouseButton(0))
            {
                _zoomIntent = Mathf.MoveTowards(_zoomIntent, 0f,
                    Time.deltaTime * ZoomReturnSpeed);
            }
        }

        private void HandleTargetMode(IZoomController zoom, CameraSlot activeSlot)
        {
            // Interrupt with rate input if slider moved significantly
            if (Mathf.Abs(_zoomIntent) > 0.1f)
            {
                zoom.Interrupt(new RateBasedZoomStrategy(ZoomMaxSpeed));
                zoom.SetRateInput(_zoomIntent);

                // Decay and return (rate mode takes precedence when slider active)
                if (!Input.GetMouseButton(0))
                {
                    _zoomIntent = Mathf.MoveTowards(_zoomIntent, 0f,
                        Time.deltaTime * ZoomReturnSpeed);
                }
                return;
            }

            // Normal target mode update
            if (zoom.UseConsistentAutoZoom)
            {
                zoom.ApplyConsistentFraming();
            }
            else
            {
                bool hadStrategy = zoom.HasActiveStrategy;
                zoom.Update(Time.deltaTime);

                // Handoff: When consistent transition completes, persist to slot
                if (!DeterministicCaptureSession.IsRunning && hadStrategy && !zoom.HasActiveStrategy
                    && zoom.UseConsistentAutoZoom)
                {
                    PersistZoomSettingsToSlot(zoom, activeSlot);
                }
            }
        }
        #endregion

        #region Fade Controls
        // Fade row (LAYOUT_PROPOSAL §4 item 1): one Row — toggle + duration slider with
        // the lerped seconds on the bar (note 16, FR-3 valueText). Both widgets write
        // the shared coordinator (C7 accessor); the slider is now always visible (the
        // locked single-row layout), where the old UI hid it while the toggle was off.
        private void DrawFadeRow()
        {
            CameraTransitionCoordinator coordinator = CinematicUiHost.Instance?.FadeOverlay?.Coordinator;
            if (coordinator == null) return;

            using (ImGuiEx.Row())
            {
                bool useFade = coordinator.UseFadeOnSwap;
                bool changed;
                if (useFade)
                {
                    // Active-state styling: the old green+bold becomes a green Text scope
                    // (the library has no per-widget bold).
                    using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                    {
                        changed = DearImGuiKSP.DearImGuiKSP.Toggle(CameraController.FadeOnSwapToggle, ref useFade);
                    }
                }
                else
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(CameraController.FadeOnSwapToggle, ref useFade);
                }

                if (changed)
                {
                    coordinator.UseFadeOnSwap = useFade;
                }

                // Note 16: refresh the cached on-bar string before drawing so the bar
                // text matches this frame's slider. The printf format cannot express the
                // Lerp, so the seconds string is a cached valueText (change-guarded).
                float slider = coordinator.FadeDurationSlider;
                if (slider != _lastFadeSliderValue)
                {
                    _lastFadeSliderValue = slider;
                    _fadeDurationText = string.Format(CameraController.FadeDurationValueFormat,
                        Mathf.Lerp(FadeDurationMin, FadeDurationMax, slider));
                }

                if (DearImGuiKSP.DearImGuiKSP.SliderFloat("##fadeDuration", ref slider, 0f,
                        FadeSliderMax, valueText: _fadeDurationText))
                {
                    coordinator.FadeDurationSlider = slider;
                }
            }
        }
        #endregion

        #region Slot Grid
        private void DrawSlotGrid(Vessel currentVessel)
        {
            for (int row = 0; row < GridRows; row++)
            {
                using (ImGuiEx.Row())
                {
                    for (int col = 0; col < GridCols; col++)
                    {
                        int index = row * GridCols + col;
                        DrawSlotButton(index, currentVessel);
                    }
                }
            }
        }

        private void DrawSlotButton(int index, Vessel currentVessel)
        {
            CameraSlot.SlotStatus status = slotManager.GetSlotStatus(index, currentVessel);
            var slot = slotManager.GetSlot(index);
            int styleIndex = GetStyleIndexForStatus(status, slot?.isCameraToolsSlot ?? false);

            using (ImGuiEx.StyleColor(ImGuiCol.Button, SlotButtonColors[styleIndex]))
            {
                using (ImGuiEx.StyleColor(ImGuiCol.Text, SlotButtonTextColors[styleIndex]))
                {
                    if (DearImGuiKSP.DearImGuiKSP.Button(SlotButtonLabels[index], SlotButtonSize))
                    {
                        OnButtonClicked(index);
                    }
                }
            }
        }
        #endregion

        #region Return / Assign Row
        // Old disabled-button conditions become greyed stand-ins (uniform idiom,
        // C3 ruling D-2).
        private void DrawReturnToMainButton()
        {
            bool hasActiveCam = ctController.IsActive || HullCamBridge.IsAnyCameraActive();
            if (!hasActiveCam)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, CameraController.ReturnToMain);
                return;
            }

            if (DearImGuiKSP.DearImGuiKSP.Button(CameraController.ReturnToMain))
            {
                CaptureCTStateIfActive();
                BeginCameraSwitch(() =>
                {
                    cameraManager.ReturnToMain(immediate: true);
                    slotManager.ClearActiveSlot();
                    _lastCTSlotIndex = -1;
                });
            }
        }

        private void DrawAssignCurrentButton()
        {
            if (HullCamBridge.GetCurrentCamera() == null)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, CameraController.AssignCurrent);
                return;
            }

            if (DearImGuiKSP.DearImGuiKSP.Button(CameraController.AssignCurrent))
            {
                AssignCurrentToFirstOpenSlot();
            }
        }
        #endregion

        #region Slot / Context Section
        // L4: the old right-hand column moves below the grid as one vertical section
        // (Rows do not nest, so grid + side column cannot share a line).
        private void DrawContextSection()
        {
            ICamera activeCam = cameraManager.ActiveCamera;
            var activeSlot = slotManager.ActiveSlot;

            if (activeCam is CameraToolsCamera && activeSlot != null)
            {
                // Consistent framing controls via the slot abstraction (CT path)
                DrawAutoZoomControls(
                    activeSlot,
                    _cameraToolsZoom.CurrentFoV,
                    (newVal) => {
                        activeSlot.SetUseConsistentAutoZoom(newVal);
                        _cameraToolsZoom.UseConsistentAutoZoom = newVal;
                        if (newVal) _cameraToolsZoom.ApplyConsistentFraming();
                    },
                    (newVal) => {
                        activeSlot.SetZoomPadding(newVal);
                        _cameraToolsZoom.ConsistentZoomPadding = newVal;
                        if (activeSlot.GetUseConsistentAutoZoom()) _cameraToolsZoom.ApplyConsistentFraming();
                    });
            }
            else if (activeCam is HullCamController && activeSlot != null)
            {
                // Supports per-slot settings for HullCam too via slot abstraction
                DrawAutoZoomControls(
                    activeSlot,
                    _hullCamZoom.CurrentFoV,
                    (newVal) => {
                        activeSlot.SetUseConsistentAutoZoom(newVal);
                        _hullCamZoom.UseConsistentAutoZoom = newVal;
                        if (newVal) _hullCamZoom.ApplyConsistentFraming();
                    },
                    (newVal) => {
                        activeSlot.SetZoomPadding(newVal);
                        _hullCamZoom.ConsistentZoomPadding = newVal;
                        if (activeSlot.GetUseConsistentAutoZoom()) _hullCamZoom.ApplyConsistentFraming();
                    });
            }
            else
            {
                DrawInstructions();
            }
        }

        // L5: the explicit unassign affordance replacing right-click (raw
        // mouse-event handling died with IMGUI). Shows for the selected slot while
        // it stays assigned.
        private void DrawSelectedSlotRow(Vessel currentVessel)
        {
            if (_selectedSlotIndex < 0) return;

            var slot = slotManager.GetSlot(_selectedSlotIndex);
            if (slot == null) return;

            CameraSlot.SlotStatus status = slotManager.GetSlotStatus(_selectedSlotIndex, currentVessel);
            if (status == CameraSlot.SlotStatus.Unassigned) return;

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(
                    string.Format(CameraController.SlotLabelFormat, _selectedSlotIndex + 1, slot.GetDisplayName()));

                if (DearImGuiKSP.DearImGuiKSP.Button(CameraController.UnassignButton))
                {
                    ShowUnassignDialog(_selectedSlotIndex);
                }
            }
        }

        private void DrawAutoZoomControls(
            CameraSlot activeSlot,
            float currentFOV,
            Action<bool> onToggleChanged,
            Action<float> onPaddingChanged)
        {
            DearImGuiKSP.DearImGuiKSP.Text(CameraController.AutoZoomHeader);

            bool useConsistent = activeSlot.GetUseConsistentAutoZoom();
            bool newUseConsistent = useConsistent;
            bool changed;
            if (useConsistent)
            {
                // Active-state styling: the old green+bold becomes a green Text scope.
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(
                        CameraController.ConsistentFramingToggle, ref newUseConsistent);
                }
            }
            else
            {
                changed = DearImGuiKSP.DearImGuiKSP.Toggle(
                    CameraController.ConsistentFramingToggle, ref newUseConsistent);
            }

            if (changed)
            {
                onToggleChanged(newUseConsistent);
            }

            if (useConsistent)
            {
                using (ImGuiEx.Row())
                {
                    DearImGuiKSP.DearImGuiKSP.Text(CameraController.PaddingRowLabel);

                    float padding = activeSlot.GetZoomPadding();
                    if (DearImGuiKSP.DearImGuiKSP.SliderFloat("##zoomPadding", ref padding, 0.5f, 3.0f))
                    {
                        onPaddingChanged(padding);
                    }
                    // The old permanent help label, now a tooltip on the slider.
                    DearImGuiKSP.DearImGuiKSP.Tooltip(CameraController.PaddingTooltip);

                    DearImGuiKSP.DearImGuiKSP.Text(string.Format(CameraController.PaddingValueFormat, padding));
                }
            }

            DearImGuiKSP.DearImGuiKSP.TextColored(InfoOrangeColor,
                string.Format(CameraController.CurrentFOVFormat, currentFOV));
        }

        // The old 3-bullet help, condensed to the two still-relevant lines — the
        // right-click bullet died with IMGUI (L5 replaces that affordance).
        private static void DrawInstructions()
        {
            DearImGuiKSP.DearImGuiKSP.Text(CameraController.ControlsHeader);
            DearImGuiKSP.DearImGuiKSP.Text(CameraController.ControlLeftClick);
            DearImGuiKSP.DearImGuiKSP.Text(CameraController.ControlAssignCurrent);
        }
        #endregion

        #region Zoom Section
        // LAYOUT_PROPOSAL §4 item 4: default closed (open item §8.3 — sanity-check in
        // game); only drawn while a camera is active, as today.
        private void DrawZoomSection()
        {
            if (!DearImGuiKSP.DearImGuiKSP.CollapsingHeader(CameraController.ZoomHeader))
            {
                return;
            }

            if (CaptureCameraResolver.IsIvaMode())
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(InfoOrangeColor, CameraController.IvaZoomDisabledNotice);
                return;
            }

            using (ImGuiEx.Row())
            {
                int mode = (int)currentZoomMode;
                DearImGuiKSP.DearImGuiKSP.RadioButton(CameraController.RateModeToggle, ref mode, (int)ZoomMode.Rate);
                DearImGuiKSP.DearImGuiKSP.RadioButton(CameraController.TargetModeToggle, ref mode, (int)ZoomMode.Target);

                if (mode != (int)currentZoomMode)
                {
                    // Cancel zoom on all controllers (same as the old mode toggles)
                    _hullCamZoom.CancelActiveZoom();
                    _cameraToolsZoom.CancelActiveZoom();
                    if (DeterministicCaptureSession.IsRunning)
                    {
                        DeterministicCaptureSession.ActiveZoomController?.Clear();
                    }

                    if (mode == (int)ZoomMode.Target)
                    {
                        _zoomIntent = 0f;
                    }

                    currentZoomMode = (ZoomMode)mode;
                }
            }

            if (currentZoomMode == ZoomMode.Rate)
            {
                DrawRateModeControls();
            }
            else
            {
                DrawTargetModeControls();
            }
        }

        private void DrawRateModeControls()
        {
            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(CameraController.ZoomOut);

                float intent = _zoomIntent;
                if (DearImGuiKSP.DearImGuiKSP.SliderFloat("##zoomIntent", ref intent, -1f, 1f))
                {
                    _zoomIntent = intent;
                }

                DearImGuiKSP.DearImGuiKSP.Text(CameraController.ZoomIn);
            }

            using (ImGuiEx.Row())
            {
                float maxFov = cameraManager.GetMaxFOV();

                // Get current FOV from active zoom controller
                IZoomController currentZoom = GetCurrentZoomController();
                float currentFOV = currentZoom?.CurrentFoV ?? 60f;

                DearImGuiKSP.DearImGuiKSP.Text(string.Format(CameraController.FOVFormat, currentFOV, maxFov));

                if (DearImGuiKSP.DearImGuiKSP.Button(CameraController.ResetZoom))
                {
                    if (DeterministicCaptureSession.IsRunning)
                    {
                        var detZoom = DeterministicCaptureSession.ActiveZoomController;
                        if (detZoom != null)
                            detZoom.Interrupt(new InstantZoomStrategy(maxFov));
                    }
                    else
                    {
                        // Reset both controllers (active one will apply, inactive is harmless)
                        _hullCamZoom.ResetZoom(maxFov);
                        _cameraToolsZoom.ResetZoom(maxFov);
                    }
                }
            }
        }

        private void DrawTargetModeControls()
        {
            bool newConsistentTarget = targetIsConsistentFraming;
            bool changed;
            if (targetIsConsistentFraming)
            {
                // Active-state styling: the old green+bold becomes a green Text scope.
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(
                        CameraController.TargetConsistentFramingToggle, ref newConsistentTarget);
                }
            }
            else
            {
                changed = DearImGuiKSP.DearImGuiKSP.Toggle(
                    CameraController.TargetConsistentFramingToggle, ref newConsistentTarget);
            }

            if (changed)
            {
                targetIsConsistentFraming = newConsistentTarget;
            }

            if (!targetIsConsistentFraming)
            {
                // The field reseeds from state every frame, exactly like the old
                // TextField: unparsable text snaps back on the next frame.
                _targetFovText = targetFOVValue.ToString("F1");

                using (ImGuiEx.Row())
                {
                    DearImGuiKSP.DearImGuiKSP.Text(CameraController.TargetFOVLabel);

                    if (DearImGuiKSP.DearImGuiKSP.InputText("##targetFov", ref _targetFovText, 16)
                        && float.TryParse(_targetFovText, out float parsedTarget))
                    {
                        targetFOVValue = Mathf.Clamp(parsedTarget, 2f, 120f);
                    }

                    DearImGuiKSP.DearImGuiKSP.Text("°");
                }
            }

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(CameraController.ZoomDurationLabel);

                float duration = targetDuration;
                if (DearImGuiKSP.DearImGuiKSP.SliderFloat("##targetDuration", ref duration, 0f, 5f))
                {
                    targetDuration = duration;
                }
                // The old "0.0" / "5.0" slider-end labels, now a tooltip (L8 idiom).
                DearImGuiKSP.DearImGuiKSP.Tooltip(CameraController.ZoomDurationRangeTooltip);

                DearImGuiKSP.DearImGuiKSP.Text(string.Format(CameraController.DurationValueFormat, targetDuration));
            }

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(CameraController.CurveLabel);

                int curve = (int)targetZoomCurve;
                if (DearImGuiKSP.DearImGuiKSP.Combo("##zoomCurve", ref curve, CurveComboItems))
                {
                    targetZoomCurve = (ZoomCurve)curve;
                }
            }

            if (ImGuiGradients.GradientButton(CameraController.GoButton,
                    GoButtonTop, GoButtonBottom, new Vector2(0f, 30f)))
            {
                ExecuteTargetZoom();
            }
        }

        private void ExecuteTargetZoom()
        {
            if (!targetIsConsistentFraming && targetFOVValue <= 0)
                targetFOVValue = cameraManager.GetCurrentFOV();

            // Get unified controller
            IZoomController zoom = GetCurrentZoomController();
            if (zoom == null) return;

            if (DeterministicCaptureSession.IsRunning)
            {
                // Deterministic path
                if (targetIsConsistentFraming)
                {
                    zoom.QueueConsistentTransition(targetDuration, targetZoomCurve);
                }
                else
                {
                    if (targetDuration < 0.001f)
                        zoom.Interrupt(new InstantZoomStrategy(targetFOVValue));
                    else
                        zoom.Interrupt(new TargetBasedZoomStrategy(targetFOVValue, targetDuration, targetZoomCurve));
                }
            }
            else
            {
                // Real-time path
                if (targetIsConsistentFraming)
                {
                    zoom.QueueConsistentTransition(targetDuration, targetZoomCurve);

                    // For instant, update slot immediately so UI reflects it next frame
                    if (targetDuration < 0.001f && slotManager.ActiveSlot != null)
                    {
                        slotManager.ActiveSlot.SetUseConsistentAutoZoom(true);
                    }
                }
                else
                {
                    zoom.QueueTargetZoom(targetFOVValue, targetDuration, targetZoomCurve);
                }
            }
        }
        #endregion

        #region Presets
        // LAYOUT_PROPOSAL §4 item 5: name field + Save/Delete in one Row, Load label
        // + Combo in the next. The custom nested-box dropdown and its height-overflow
        // hack die — the Combo popup scrolls natively.
        private void DrawPresetsSection()
        {
            CameraPanelConfig scenario = CameraPanelConfig.Instance;
            CameraPanelPreset activePreset = scenario?.GetActivePreset();
            EnsurePresetNameBuffer();

            // Note 18: the presets block is always its own section, regardless of
            // camera-active state — the separator splits it from the instruction/
            // context content that immediately precedes it when no camera is active.
            DearImGuiKSP.DearImGuiKSP.Separator();

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.InputText("##presetName", ref presetNameBuffer, 64);

                if (DearImGuiKSP.DearImGuiKSP.Button(CameraController.SavePreset))
                {
                    SavePreset(scenario);
                }

                // Old behavior: Delete only enabled with an active preset — the
                // greyed stand-in covers the disabled state.
                if (activePreset == null)
                {
                    DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, CameraController.DeletePreset);
                }
                else if (DearImGuiKSP.DearImGuiKSP.Button(CameraController.DeletePreset))
                {
                    // L7: Delete actually routes through its confirmation now (the old
                    // IMGUI confirm dialog was a dead path).
                    ShowDeleteDialog();
                }
            }

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(CameraController.LoadPreset);

                EnsurePresetNames(scenario);

                // Preview tracks the active preset; picking an item loads it.
                int selected = IndexOfActivePreset(scenario, activePreset);
                if (DearImGuiKSP.DearImGuiKSP.Combo("##presetLoad", ref selected, _presetNamesCache)
                    && selected >= 0 && selected < _presetNamesCache.Length)
                {
                    scenario?.LoadPreset(_presetNamesCache[selected]);
                }
            }
        }

        private void EnsurePresetNames(CameraPanelConfig scenario)
        {
            if (_presetNamesCache != null) return;

            _presetNamesCache = scenario != null
                ? scenario.GetPresetNames().ToArray()
                : new string[0];
        }

        private int IndexOfActivePreset(CameraPanelConfig scenario, CameraPanelPreset activePreset)
        {
            if (scenario == null || activePreset == null) return -1;

            for (int i = 0; i < _presetNamesCache.Length; i++)
            {
                if (_presetNamesCache[i] == activePreset.presetName) return i;
            }
            return -1;
        }
        #endregion

        #region Confirmation Dialogs
        // Stock PopupDialog replaces the three IMGUI ModalWindows (G-U1 exception),
        // with identical text. SpawnPopupDialog dedupes by dialog name, so a click
        // while a dialog is open returns the existing popup instead of stacking.
        // Dialogs spawn on click frames only — never per frame.
        private void ShowDeleteDialog()
        {
            CameraPanelConfig scenario = CameraPanelConfig.Instance;
            if (scenario == null) return;

            string name = presetNameBuffer;
            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    DeleteDialogName,
                    string.Format(CameraController.DeleteConfirmFormat, name),
                    CameraController.ConfirmDeleteTitle,
                    HighLogic.UISkin,
                    new DialogGUIBase[]
                    {
                        new DialogGUIHorizontalLayout(
                            new DialogGUIButton(Common.Yes, () => ConfirmDeletePreset(scenario, name), true),
                            new DialogGUIButton(Common.No, Dismiss, true))
                    }),
                false, HighLogic.UISkin);
        }

        private void ConfirmDeletePreset(CameraPanelConfig scenario, string name)
        {
            scenario.DeletePreset(name);
            presetNameBuffer = GetDefaultPresetName();
        }

        private void ShowUnassignDialog(int slotIndex)
        {
            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    UnassignDialogName,
                    string.Format(CameraController.UnassignConfirmFormat, slotIndex + 1),
                    CameraController.ConfirmUnassignTitle,
                    HighLogic.UISkin,
                    new DialogGUIBase[]
                    {
                        new DialogGUIHorizontalLayout(
                            new DialogGUIButton(Common.Yes, () => ConfirmUnassignSlot(slotIndex), true),
                            new DialogGUIButton(Common.No, Dismiss, true))
                    }),
                false, HighLogic.UISkin);
        }

        private void ConfirmUnassignSlot(int slotIndex)
        {
            slotManager.ClearSlot(slotIndex);
            if (_selectedSlotIndex == slotIndex)
            {
                _selectedSlotIndex = -1;
            }
        }

        private void ShowOverwriteDialog(string presetName, CameraPanelConfig scenario)
        {
            PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new MultiOptionDialog(
                    OverwriteDialogName,
                    string.Format(CameraController.OverwriteConfirm, presetName),
                    CameraController.ConfirmOverwriteTitle,
                    HighLogic.UISkin,
                    new DialogGUIBase[]
                    {
                        new DialogGUIHorizontalLayout(
                            new DialogGUIButton(CameraController.OverwriteYes,
                                () => OverwriteExisting(presetName, scenario), true),
                            new DialogGUIButton(CameraController.OverwriteNo,
                                () => OverwriteCreateNew(presetName, scenario), true),
                            new DialogGUIButton(Common.Cancel, Dismiss, true))
                    }),
                false, HighLogic.UISkin);
        }

        private void OverwriteExisting(string presetName, CameraPanelConfig scenario)
        {
            SavePresetWithName(scenario, presetName);
            presetNameBuffer = presetName;
        }

        private void OverwriteCreateNew(string presetName, CameraPanelConfig scenario)
        {
            var existingNames = scenario.GetPresetNames();
            string newName = GetUniquePresetName(presetName, existingNames);
            SavePresetWithName(scenario, newName);
            presetNameBuffer = newName;
        }

        private static void Dismiss() { }
        #endregion

        #region Camera Interaction
        private void OnButtonClicked(int index)
        {
            // L5: every click selects the slot (activation/viewing behaves as today);
            // the × Unassign row in the context section acts on the selection.
            _selectedSlotIndex = index;

            var slot = slotManager.GetSlot(index);

            if (slot?.isCameraToolsSlot == true)
            {
                if (slot.ctSettings == null) return;

                if (slot.ctSettings.Mode == ToolModes.Pathing)
                {
                    if (slot.ctSettings.SelectedPathIndex < 0)
                    {
                        ScreenMessages.PostScreenMessage(CameraController.InvalidPathIndexMessage, 2f);
                        return;
                    }
                    if (!ctController.PathExists(slot.ctSettings.SelectedPathIndex))
                    {
                        ScreenMessages.PostScreenMessage(CameraController.SavedPathNoLongerExistsMessage, 2f);
                        return;
                    }
                }
                if (_lastCTSlotIndex >= 0 && _lastCTSlotIndex != index)
                {
                    CaptureCTStateIfActive();
                }

                BeginCameraSwitch(() =>
                {
                    // Sync slot settings to CameraToolsZoomController (zoom moved to separate controller)
                    _cameraToolsZoom.UseConsistentAutoZoom = slot.ctSettings.UseConsistentAutoZoom;
                    _cameraToolsZoom.ConsistentZoomPadding = slot.ctSettings.ZoomPadding;
                    _cameraToolsZoom.CancelActiveZoom();

                    slotManager.SetActiveSlot(index);
                    _lastCTSlotIndex = index;

                    cameraManager.SwitchToCamera(slot, immediate: true);

                    if (slot.ctSettings.UseConsistentAutoZoom)
                    {
                        _cameraToolsZoom.ApplyConsistentFraming();
                    }
                    else if (slot.ctSettings.AutoZoom)
                    {
                        ApplyNativeAutoZoom(slot);
                    }

                    var ctCam = cameraManager.ActiveCamera as CameraToolsCamera;
                    if (ctCam != null && slot.ctSettings.UseGeographicPosition)
                    {
                        if (ctController.HasPendingGeographicRestoration())
                        {
                            ctController.PostActivationPositionFixup();
                        }
                    }
                });
                return;
            }

            _lastCTSlotIndex = -1;

            Vessel vessel = FlightGlobals.ActiveVessel;
            CameraSlot.SlotStatus status = slot.GetStatus(vessel);

            switch (status)
            {
                case CameraSlot.SlotStatus.Unassigned:
                    AssignCurrentToSlot(index);
                    break;
                case CameraSlot.SlotStatus.Active:
                    return;
                case CameraSlot.SlotStatus.Assigned:
                case CameraSlot.SlotStatus.Remote:
                    BeginCameraSwitch(() =>
                    {
                        slotManager.SetActiveSlot(index);

                        if (slot != null && !slot.isCameraToolsSlot)
                        {
                            _hullCamZoom.UseConsistentAutoZoom = slot.GetUseConsistentAutoZoom();
                            _hullCamZoom.ConsistentZoomPadding = slot.GetZoomPadding();
                        }

                        cameraManager.SwitchToCamera(slot, immediate: true);

                        if (slot != null && !slot.isCameraToolsSlot && slot.GetUseConsistentAutoZoom())
                        {
                            _hullCamZoom.ApplyConsistentFraming();
                        }
                    });
                    break;
                case CameraSlot.SlotStatus.Unavailable:
                    ScreenMessages.PostScreenMessage(CameraController.CameraUnavailable, 2f);
                    break;
            }
        }

        private void BeginCameraSwitch(Action cameraAction)
        {
            bool useDeterministic = DeterministicCaptureSession.IsRunning;

            // FADE-1: the single coordinator lives on FadeOverlayController (C7).
            CameraTransitionCoordinator coordinator = CinematicUiHost.Instance?.FadeOverlay?.Coordinator;
            if (coordinator == null)
            {
                // No fade owner (host torn down) — run the switch without a fade.
                cameraAction?.Invoke();
                return;
            }

            coordinator.BeginTransition(cameraAction, useDeterministic);
        }

        private void CaptureCTStateIfActive()
        {
            if (_lastCTSlotIndex >= 0)
            {
                var slot = slotManager.GetSlot(_lastCTSlotIndex);
                if (slot?.isCameraToolsSlot == true && ctController.IsAvailable && ctController.IsActive)
                {
                    // Use API to get current FOV
                    float currentFOV = CameraToolsAPIManager.GetActualFOV();

                    if (currentFOV > 0)
                    {
                        // Clone then modify to avoid mutating stored reference
                        var newSettings = slot.ctSettings.Clone();
                        newSettings.ManualFOV = currentFOV;

                        // Read from zoom controller instead of ctController (zoom moved to separate controller)
                        newSettings.UseConsistentAutoZoom = _cameraToolsZoom.UseConsistentAutoZoom;
                        newSettings.ZoomPadding = _cameraToolsZoom.ConsistentZoomPadding;

                        // Re-assign to trigger property setter cloning
                        slot.ctSettings = newSettings;

                        UnityEngine.Debug.Log($"[FOV Capture] Slot {_lastCTSlotIndex}: Updated ManualFOV to {currentFOV:F1}, Consistent: {newSettings.UseConsistentAutoZoom}");
                    }
                }
            }
        }

        private void AssignCurrentToSlot(int index)
        {
            if (ctController.IsAvailable && ctController.IsActive)
            {
                var settings = ctController.CaptureCurrentSettings();
                if (settings != null)
                {
                    settings.LockPathingToPlaybackRate = SessionState.CameraPathPlaybackTiming;
                    settings.UseDeterministicControl = DeterministicCaptureSession.IsRunning;

                    UnityEngine.Debug.Log($"[AssignCurrentToSlot] Slot {index}: Capturing CT {settings.Mode} " +
                        $"(PathIndex: {settings.SelectedPathIndex}, UsePlaybackTiming: {settings.LockPathingToPlaybackRate})");

                    if (slotManager.AssignCameraToolsToSlot(index, settings))
                    {
                        ScreenMessages.PostScreenMessage(string.Format(CameraController.SavedCameraToolsFormat, settings.GetDisplayName()), 2f);
                    }
                    return;
                }
                else
                {
                    UnityEngine.Debug.LogError($"[AssignCurrentToSlot] Failed to capture CT settings for slot {index}");
                }
            }

            if (!ValidateAssignmentPrerequisites(out object currentCam, out Vessel vessel)) return;
            if (slotManager.AssignHullCamToSlot(index, currentCam, vessel))
            {
                string camName = HullCamBridge.GetCameraName(currentCam) ?? "";
                ScreenMessages.PostScreenMessage(string.Format(CameraController.SavedHullCamFormat, camName), 2f);
            }
        }

        private void AssignCurrentToFirstOpenSlot()
        {
            int openSlot = slotManager.FindFirstOpenSlot();
            if (openSlot >= 0)
            {
                AssignCurrentToSlot(openSlot);
            }
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

        private void ApplyNativeAutoZoom(CameraSlot slot)
        {
            if (CaptureCameraResolver.IsIvaMode()) return;

            Vessel currentVessel = FlightGlobals.ActiveVessel;
            if (currentVessel == null || FlightCamera.fetch == null) return;

            Vector3 targetPos = (slot.ctSettings.HasTarget && !slot.ctSettings.TargetSelf)
                ? ctController.CamTarget?.transform.position ?? currentVessel.CoM
                : currentVessel.CoM;

            float distance = Vector3.Distance(FlightCamera.fetch.transform.position, targetPos);
            float margin = 30f;
            float nativeFOV = (7000f / (distance + 100f)) - 14f + margin;
            nativeFOV = Mathf.Clamp(nativeFOV, 2f, 60f);

            _cameraToolsZoom.ResetZoom(nativeFOV);
            FlightCamera.fetch.SetFoV(nativeFOV);
        }
        #endregion

        #region Preset Management
        private void SavePreset(CameraPanelConfig scenario)
        {
            if (scenario == null) return;

            string nameToSave = string.IsNullOrWhiteSpace(presetNameBuffer)
                ? GetDefaultPresetName()
                : presetNameBuffer;

            // Check if this exact name exists
            var existingNames = scenario.GetPresetNames();

            if (existingNames.Contains(nameToSave))
            {
                // Prompt for overwrite
                ShowOverwriteDialog(nameToSave, scenario);
                return;
            }

            string uniqueName = GetUniquePresetName(nameToSave, existingNames);

            if (uniqueName != nameToSave)
            {
                SavePresetWithName(scenario, uniqueName);
                presetNameBuffer = uniqueName;
            }
            else
            {
                SavePresetWithName(scenario, nameToSave);
                presetNameBuffer = nameToSave;
            }
        }

        private void SavePresetWithName(CameraPanelConfig scenario, string name)
        {
            // Camera-panel position persistence in presets is spec-locked (§7), but
            // the ported panel has no rect — positions were never read back for
            // layout, so 0/0 is written (recorded in IMPEDIMENTS for the Lead).
            scenario.SavePreset(name, false, new List<CameraSlot>(slotManager.Slots), 0f, 0f);
        }

        private string GetUniquePresetName(string baseName, List<string> existingNames)
        {
            // If base name doesn't exist, use it as-is
            if (!existingNames.Contains(baseName))
                return baseName;

            string rootName = baseName;
            int existingIndex = 0;

            int bracketStart = baseName.LastIndexOf('[');
            int bracketEnd = baseName.LastIndexOf(']');

            if (bracketStart > 0 && bracketEnd > bracketStart && bracketEnd == baseName.Length - 1)
            {
                string numStr = baseName.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                if (int.TryParse(numStr, out int parsedNum))
                {
                    rootName = baseName.Substring(0, bracketStart);
                    existingIndex = parsedNum;
                }
            }

            int counter = existingIndex + 1;
            string candidate;

            do
            {
                candidate = $"{rootName}[{counter}]";
                counter++;
            } while (existingNames.Contains(candidate));

            return candidate;
        }

        private string GetDefaultPresetName()
        {
            // Note 17: GetDisplayName() returns the localized name — raw vesselName can
            // carry a "#autoLOC_" tag for vessels named via localization keys.
            return FlightGlobals.ActiveVessel?.GetDisplayName() ?? CameraController.Preset;
        }

        private void EnsurePresetNameBuffer()
        {
            if (string.IsNullOrEmpty(presetNameBuffer))
            {
                presetNameBuffer = GetDefaultPresetName();
            }
        }
        #endregion

        #region Helpers
        private static string[] BuildSlotButtonLabels()
        {
            int total = TotalSlots;
            var labels = new string[total];
            for (int i = 0; i < total; i++)
            {
                labels[i] = (i + 1) + "##camSlot" + i;
            }
            return labels;
        }

        // Index mapping carried over verbatim from the IMGUI version:
        // 0=Active(Green), 1=Assigned(Yellow), 2=Unavailable(Red), 3=Unassigned(Gray),
        // 4=Remote(Aqua), 5=CT_Active(Orange), 6=CT_Inactive(DarkOrange)
        private int GetStyleIndexForStatus(CameraSlot.SlotStatus status, bool isCameraTools)
        {
            if (isCameraTools)
            {
                switch (status)
                {
                    case CameraSlot.SlotStatus.Active: return 5;
                    case CameraSlot.SlotStatus.Assigned: return 6;
                    default: return 3;
                }
            }

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
    }
}
