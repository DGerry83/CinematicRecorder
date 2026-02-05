using CinematicRecorder.Integration;
using CinematicRecorder.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// File-based preset storage for Camera Panel.
    /// Saves to GameData/CinematicRecorder/PluginData/CameraPresets.cfg
    /// </summary>
    public class CameraPanelConfig : MonoBehaviour
    {
        public static CameraPanelConfig Instance { get; private set; }

        private List<CameraPanelPreset> presets = new List<CameraPanelPreset>();
        private CameraPanelPreset activePreset;
        private string configPath;

        public event Action OnPresetsChanged;
        public event Action<CameraPanelPreset> OnPresetLoaded;

        void Awake()
        {
            Instance = this;

            // Set up path in PluginData
            string pluginData = Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData",
                "CinematicRecorder",
                "PluginData");

            Directory.CreateDirectory(pluginData);
            configPath = Path.Combine(pluginData, "CameraPresets.cfg");

            LoadFromFile();
        }

        void OnDestroy()
        {
            Instance = null;
        }

        public void SavePreset(string name, bool vesselSpecific, List<CameraSlot> slots, float x, float y)
        {
            if (string.IsNullOrEmpty(name)) return;

            // Remove existing with same name
            presets.RemoveAll(p => p.presetName == name);

            CameraPanelPreset preset = new CameraPanelPreset
            {
                presetName = name,
                vesselId = vesselSpecific ? FlightGlobals.ActiveVessel?.id.ToString() : "GLOBAL",
                autoLoadForVessel = vesselSpecific,
                panelX = x,
                panelY = y
            };

            if (slots != null && slots.Count == 16)
            {
                preset.buttonAssignments = slots.Select(s => new CameraSlot
                {
                    buttonID = s.buttonID,
                    cameraName = s.cameraName,
                    partPersistentId = s.partPersistentId,
                    vesselId = s.vesselId,
                    allowAnyVessel = s.allowAnyVessel,
                    isCameraToolsSlot = s.isCameraToolsSlot,
                    ctSettings = s.ctSettings != null ? new CameraToolsSettings
                    {
                        Mode = s.ctSettings.Mode,
                        DogfightDistance = s.ctSettings.DogfightDistance,
                        DogfightOffsetX = s.ctSettings.DogfightOffsetX,
                        DogfightOffsetY = s.ctSettings.DogfightOffsetY,
                        DogfightChasePlaneMode = s.ctSettings.DogfightChasePlaneMode,
                        DogfightTargetId = s.ctSettings.DogfightTargetId,

                        // Geographic positioning (NEW)
                        UseGeographicPosition = s.ctSettings.UseGeographicPosition,
                        Latitude = s.ctSettings.Latitude,
                        Longitude = s.ctSettings.Longitude,
                        Altitude = s.ctSettings.Altitude,
                        BodyName = s.ctSettings.BodyName,

                        // Positioning modes
                        AutoFlybyPosition = s.ctSettings.AutoFlybyPosition,
                        AutoLandingPosition = s.ctSettings.AutoLandingPosition,
                        ManualOffset = s.ctSettings.ManualOffset,
                        ManualOffsetForward = s.ctSettings.ManualOffsetForward,
                        ManualOffsetRight = s.ctSettings.ManualOffsetRight,
                        ManualOffsetUp = s.ctSettings.ManualOffsetUp,

                        // Target tracking
                        HasTarget = s.ctSettings.HasTarget,
                        TargetSelf = s.ctSettings.TargetSelf,
                        TargetPartPersistentId = s.ctSettings.TargetPartPersistentId,
                        TargetCoM = s.ctSettings.TargetCoM,

                        // Camera settings
                        MaintainInitialVelocity = s.ctSettings.MaintainInitialVelocity,
                        UseOrbital = s.ctSettings.UseOrbital,
                        AutoZoom = s.ctSettings.AutoZoom,
                        ManualFOV = s.ctSettings.ManualFOV,
                        InitialVelocity = s.ctSettings.InitialVelocity,

                        // Additional settings
                        SaveRotation = s.ctSettings.SaveRotation,
                        FmPivotMode = s.ctSettings.FmPivotMode,
                        PathingSecondarySmoothing = s.ctSettings.PathingSecondarySmoothing,

                        // Pathing
                        SelectedPathIndex = s.ctSettings.SelectedPathIndex,
                        PathTimeScale = s.ctSettings.PathTimeScale,
                        CurrentKeyframeIndex = s.ctSettings.CurrentKeyframeIndex,
                        IsPlayingPath = s.ctSettings.IsPlayingPath,
                        UseRealTime = s.ctSettings.UseRealTime,
                        PathStartTime = s.ctSettings.PathStartTime,

                        // NEW: Consistent Auto-Zoom settings (Step 3)
                        UseConsistentAutoZoom = s.ctSettings.UseConsistentAutoZoom,
                        ZoomPadding = s.ctSettings.ZoomPadding
                    } : null
                }).ToList();
            }

            presets.Add(preset);
            activePreset = preset;

            SaveToFile();
            OnPresetsChanged?.Invoke();
        }

        public void LoadPreset(string name)
        {
            CameraPanelPreset preset = presets.FirstOrDefault(p => p.presetName == name);
            if (preset != null)
            {
                activePreset = preset;
                OnPresetLoaded?.Invoke(preset);
            }
        }

        public void DeletePreset(string name)
        {
            if (presets.RemoveAll(p => p.presetName == name) > 0)
            {
                if (activePreset?.presetName == name)
                    activePreset = null;

                SaveToFile();
                OnPresetsChanged?.Invoke();
            }
        }

        public List<string> GetPresetNames()
        {
            return presets.Select(p => p.presetName).ToList();
        }

        public CameraPanelPreset GetActivePreset() { return activePreset; }

        void SaveToFile()
        {
            try
            {
                ConfigNode root = new ConfigNode();
                ConfigNode presetsNode = root.AddNode("CAMERA_PANEL_PRESETS");

                foreach (CameraPanelPreset p in presets)
                {
                    if (p == null || string.IsNullOrEmpty(p.presetName))
                        continue;

                    ConfigNode n = presetsNode.AddNode("PRESET");
                    n.AddValue("name", p.presetName);
                    n.AddValue("vessel", p.vesselId ?? "GLOBAL");
                    n.AddValue("autoLoad", p.autoLoadForVessel);
                    n.AddValue("panelX", p.panelX);
                    n.AddValue("panelY", p.panelY);

                    if (p.buttonAssignments == null) continue;

                    foreach (CameraSlot slot in p.buttonAssignments)
                    {
                        // Skip completely empty slots
                        if (slot.partPersistentId == 0 && string.IsNullOrEmpty(slot.cameraName) && !slot.isCameraToolsSlot)
                            continue;

                        ConfigNode slotNode = n.AddNode("SLOT");
                        slotNode.AddValue("buttonID", slot.buttonID ?? "Cam_0");
                        slotNode.AddValue("partId", slot.partPersistentId);
                        slotNode.AddValue("camName", slot.cameraName ?? "");
                        slotNode.AddValue("vesselId", slot.vesselId ?? "");
                        slotNode.AddValue("allowAny", slot.allowAnyVessel);

                        // CameraTools support
                        slotNode.AddValue("isCameraTools", slot.isCameraToolsSlot);

                        if (slot.isCameraToolsSlot && slot.ctSettings != null)
                        {
                            ConfigNode ctNode = slotNode.AddNode("CT_SETTINGS");

                            // Mode
                            ctNode.AddValue("mode", slot.ctSettings.Mode.ToString());

                            // Dogfight parameters
                            ctNode.AddValue("dogfightDistance", slot.ctSettings.DogfightDistance);
                            ctNode.AddValue("dogfightOffsetX", slot.ctSettings.DogfightOffsetX);
                            ctNode.AddValue("dogfightOffsetY", slot.ctSettings.DogfightOffsetY);
                            ctNode.AddValue("dogfightChasePlane", slot.ctSettings.DogfightChasePlaneMode);
                            ctNode.AddValue("dogfightTargetId", slot.ctSettings.DogfightTargetId ?? "");

                            // Positioning mode flags
                            ctNode.AddValue("useGeographicPosition", slot.ctSettings.UseGeographicPosition);
                            ctNode.AddValue("autoFlyby", slot.ctSettings.AutoFlybyPosition);
                            ctNode.AddValue("autoLanding", slot.ctSettings.AutoLandingPosition);
                            ctNode.AddValue("manualOffset", slot.ctSettings.ManualOffset);
                            ctNode.AddValue("manualOffsetFwd", slot.ctSettings.ManualOffsetForward);
                            ctNode.AddValue("manualOffsetRight", slot.ctSettings.ManualOffsetRight);
                            ctNode.AddValue("manualOffsetUp", slot.ctSettings.ManualOffsetUp);

                            // Geographic coordinates (THE FIX)
                            ctNode.AddValue("latitude", slot.ctSettings.Latitude);
                            ctNode.AddValue("longitude", slot.ctSettings.Longitude);
                            ctNode.AddValue("altitude", slot.ctSettings.Altitude);
                            ctNode.AddValue("bodyName", slot.ctSettings.BodyName ?? "");

                            // Target tracking
                            ctNode.AddValue("hasTarget", slot.ctSettings.HasTarget);
                            ctNode.AddValue("targetSelf", slot.ctSettings.TargetSelf);
                            ctNode.AddValue("targetPartId", slot.ctSettings.TargetPartPersistentId);
                            ctNode.AddValue("targetCoM", slot.ctSettings.TargetCoM);

                            // Velocity/orbit tracking
                            ctNode.AddValue("maintainVel", slot.ctSettings.MaintainInitialVelocity);
                            ctNode.AddValue("useOrbital", slot.ctSettings.UseOrbital);

                            // Zoom/FOV settings
                            ctNode.AddValue("autoZoom", slot.ctSettings.AutoZoom);
                            ctNode.AddValue("manualFOV", slot.ctSettings.ManualFOV);

                            // Additional settings
                            ctNode.AddValue("saveRotation", slot.ctSettings.SaveRotation);
                            ctNode.AddValue("fmPivotMode", slot.ctSettings.FmPivotMode.ToString());
                            ctNode.AddValue("pathingSecondarySmoothing", slot.ctSettings.PathingSecondarySmoothing);

                            // Initial velocity (Vector3)
                            Vector3 initVel = slot.ctSettings.InitialVelocity;
                            ctNode.AddValue("initialVelocity", $"{initVel.x},{initVel.y},{initVel.z}");

                            // Pathing parameters
                            ctNode.AddValue("pathIndex", slot.ctSettings.SelectedPathIndex);
                            ctNode.AddValue("pathTimeScale", slot.ctSettings.PathTimeScale);
                            ctNode.AddValue("keyframeIndex", slot.ctSettings.CurrentKeyframeIndex);
                            ctNode.AddValue("isPlaying", slot.ctSettings.IsPlayingPath);
                            ctNode.AddValue("useRealTime", slot.ctSettings.UseRealTime);
                            ctNode.AddValue("pathStartTime", slot.ctSettings.PathStartTime);

                            // NEW: Consistent Auto-Zoom settings (Step 3)
                            ctNode.AddValue("useConsistentAutoZoom", slot.ctSettings.UseConsistentAutoZoom);
                            ctNode.AddValue("zoomPadding", slot.ctSettings.ZoomPadding);
                        }
                    }
                }

                root.Save(configPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[CameraPanelConfig] Failed to save: " + ex);
            }
        }

        void LoadFromFile()
        {
            try
            {
                if (!File.Exists(configPath))
                    return;

                ConfigNode root = ConfigNode.Load(configPath);
                if (root == null) return;

                ConfigNode presetsNode = root.GetNode("CAMERA_PANEL_PRESETS");
                if (presetsNode == null) return;

                presets.Clear();

                foreach (ConfigNode n in presetsNode.GetNodes("PRESET"))
                {
                    string name = n.GetValue("name");
                    if (string.IsNullOrEmpty(name))
                        continue;

                    CameraPanelPreset p = new CameraPanelPreset
                    {
                        presetName = name,
                        vesselId = n.GetValue("vessel") ?? "GLOBAL",
                        autoLoadForVessel = bool.Parse(n.GetValue("autoLoad") ?? "False"),
                        panelX = float.Parse(n.GetValue("panelX") ?? "0"),
                        panelY = float.Parse(n.GetValue("panelY") ?? "0")
                    };

                    p.buttonAssignments.Clear();

                    foreach (ConfigNode slotNode in n.GetNodes("SLOT"))
                    {
                        uint partId;
                        uint.TryParse(slotNode.GetValue("partId") ?? "0", out partId);

                        bool allowAny;
                        bool.TryParse(slotNode.GetValue("allowAny") ?? "False", out allowAny);

                        bool isCT;
                        bool.TryParse(slotNode.GetValue("isCameraTools") ?? "False", out isCT);

                        CameraSlot slot = new CameraSlot
                        {
                            buttonID = slotNode.GetValue("buttonID") ?? ("Cam_" + p.buttonAssignments.Count),
                            partPersistentId = partId,
                            cameraName = slotNode.GetValue("camName") ?? "",
                            vesselId = slotNode.GetValue("vesselId") ?? "",
                            allowAnyVessel = allowAny,
                            isCameraToolsSlot = isCT
                        };

                        if (isCT)
                        {
                            ConfigNode ctNode = slotNode.GetNode("CT_SETTINGS");
                            if (ctNode != null)
                            {
                                slot.ctSettings = new CameraToolsSettings();

                                // Parse Mode enum
                                string modeStr = ctNode.GetValue("mode") ?? "StationaryCamera";
                                ToolModes mode;
                                if (Enum.TryParse(modeStr, out mode))
                                    slot.ctSettings.Mode = mode;
                                else
                                    slot.ctSettings.Mode = ToolModes.StationaryCamera;

                                // Dogfight parameters
                                float.TryParse(ctNode.GetValue("dogfightDistance") ?? "50", out slot.ctSettings.DogfightDistance);
                                float.TryParse(ctNode.GetValue("dogfightOffsetX") ?? "0", out slot.ctSettings.DogfightOffsetX);
                                float.TryParse(ctNode.GetValue("dogfightOffsetY") ?? "5", out slot.ctSettings.DogfightOffsetY);
                                bool.TryParse(ctNode.GetValue("dogfightChasePlane") ?? "False", out slot.ctSettings.DogfightChasePlaneMode);
                                slot.ctSettings.DogfightTargetId = ctNode.GetValue("dogfightTargetId") ?? "";

                                // Positioning mode flags
                                bool.TryParse(ctNode.GetValue("useGeographicPosition") ?? "False", out slot.ctSettings.UseGeographicPosition);
                                bool.TryParse(ctNode.GetValue("autoFlyby") ?? "False", out slot.ctSettings.AutoFlybyPosition);
                                bool.TryParse(ctNode.GetValue("autoLanding") ?? "False", out slot.ctSettings.AutoLandingPosition);
                                bool.TryParse(ctNode.GetValue("manualOffset") ?? "False", out slot.ctSettings.ManualOffset);
                                float.TryParse(ctNode.GetValue("manualOffsetFwd") ?? "500", out slot.ctSettings.ManualOffsetForward);
                                float.TryParse(ctNode.GetValue("manualOffsetRight") ?? "50", out slot.ctSettings.ManualOffsetRight);
                                float.TryParse(ctNode.GetValue("manualOffsetUp") ?? "5", out slot.ctSettings.ManualOffsetUp);

                                // Geographic coordinates (THE FIX)
                                double.TryParse(ctNode.GetValue("latitude") ?? "0", out slot.ctSettings.Latitude);
                                double.TryParse(ctNode.GetValue("longitude") ?? "0", out slot.ctSettings.Longitude);
                                double.TryParse(ctNode.GetValue("altitude") ?? "0", out slot.ctSettings.Altitude);
                                slot.ctSettings.BodyName = ctNode.GetValue("bodyName") ?? "";

                                // Target tracking
                                bool.TryParse(ctNode.GetValue("hasTarget") ?? "False", out slot.ctSettings.HasTarget);
                                bool.TryParse(ctNode.GetValue("targetSelf") ?? "False", out slot.ctSettings.TargetSelf);
                                uint.TryParse(ctNode.GetValue("targetPartId") ?? "0", out slot.ctSettings.TargetPartPersistentId);
                                bool.TryParse(ctNode.GetValue("targetCoM") ?? "False", out slot.ctSettings.TargetCoM);

                                // Velocity/orbit tracking
                                bool.TryParse(ctNode.GetValue("maintainVel") ?? "False", out slot.ctSettings.MaintainInitialVelocity);
                                bool.TryParse(ctNode.GetValue("useOrbital") ?? "False", out slot.ctSettings.UseOrbital);

                                // Zoom/FOV settings
                                bool.TryParse(ctNode.GetValue("autoZoom") ?? "False", out slot.ctSettings.AutoZoom);
                                float.TryParse(ctNode.GetValue("manualFOV") ?? "60", out slot.ctSettings.ManualFOV);

                                // Additional settings
                                bool.TryParse(ctNode.GetValue("saveRotation") ?? "False", out slot.ctSettings.SaveRotation);

                                string pivotModeStr = ctNode.GetValue("fmPivotMode") ?? "Camera";
                                FMPivotMode pivotMode;
                                if (Enum.TryParse(pivotModeStr, out pivotMode))
                                    slot.ctSettings.FmPivotMode = pivotMode;
                                else
                                    slot.ctSettings.FmPivotMode = FMPivotMode.Camera;

                                float.TryParse(ctNode.GetValue("pathingSecondarySmoothing") ?? "0", out slot.ctSettings.PathingSecondarySmoothing);

                                // Parse InitialVelocity (Vector3)
                                string initialVelStr = ctNode.GetValue("initialVelocity");
                                if (!string.IsNullOrEmpty(initialVelStr))
                                {
                                    string[] parts = initialVelStr.Split(',');
                                    if (parts.Length == 3)
                                    {
                                        float x, y, z;
                                        if (float.TryParse(parts[0], out x) && float.TryParse(parts[1], out y) && float.TryParse(parts[2], out z))
                                            slot.ctSettings.InitialVelocity = new Vector3(x, y, z);
                                    }
                                }

                                // Pathing parameters
                                int.TryParse(ctNode.GetValue("pathIndex") ?? "-1", out slot.ctSettings.SelectedPathIndex);
                                float.TryParse(ctNode.GetValue("pathTimeScale") ?? "1", out slot.ctSettings.PathTimeScale);
                                int.TryParse(ctNode.GetValue("keyframeIndex") ?? "-1", out slot.ctSettings.CurrentKeyframeIndex);
                                bool.TryParse(ctNode.GetValue("isPlaying") ?? "False", out slot.ctSettings.IsPlayingPath);
                                bool.TryParse(ctNode.GetValue("useRealTime") ?? "True", out slot.ctSettings.UseRealTime);
                                float.TryParse(ctNode.GetValue("pathStartTime") ?? "0", out slot.ctSettings.PathStartTime);

                                // NEW: Consistent Auto-Zoom settings (Step 3)
                                bool.TryParse(ctNode.GetValue("useConsistentAutoZoom") ?? "False", out slot.ctSettings.UseConsistentAutoZoom);
                                float.TryParse(ctNode.GetValue("zoomPadding") ?? "1.5", out slot.ctSettings.ZoomPadding);
                            }
                        }

                        p.buttonAssignments.Add(slot);
                    }

                    while (p.buttonAssignments.Count < 16)
                    {
                        p.buttonAssignments.Add(new CameraSlot
                        {
                            buttonID = "Cam_" + p.buttonAssignments.Count
                        });
                    }

                    presets.Add(p);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CameraPanelConfig] Failed to load: " + ex);
                presets = new List<CameraPanelPreset>();
            }
        }
    }
}