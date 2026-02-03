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
                        ManualPosition = s.ctSettings.ManualPosition,
                        AutoZoom = s.ctSettings.AutoZoom,
                        ManualFOV = s.ctSettings.ManualFOV,
                        AutoFlybyPosition = s.ctSettings.AutoFlybyPosition,
                        ManualOffset = s.ctSettings.ManualOffset,
                        ManualOffsetForward = s.ctSettings.ManualOffsetForward,
                        ManualOffsetRight = s.ctSettings.ManualOffsetRight,
                        ManualOffsetUp = s.ctSettings.ManualOffsetUp,
                        AutoLandingPosition = s.ctSettings.AutoLandingPosition,
                        UsePresetOffset = s.ctSettings.UsePresetOffset,
                        PresetOffset = s.ctSettings.PresetOffset,
                        HasTarget = s.ctSettings.HasTarget,
                        TargetPartPersistentId = s.ctSettings.TargetPartPersistentId,
                        TargetCoM = s.ctSettings.TargetCoM,
                        MaintainInitialVelocity = s.ctSettings.MaintainInitialVelocity,
                        UseOrbital = s.ctSettings.UseOrbital,
                        SelectedPathIndex = s.ctSettings.SelectedPathIndex,
                        PathTimeScale = s.ctSettings.PathTimeScale,
                        CurrentKeyframeIndex = s.ctSettings.CurrentKeyframeIndex,
                        IsPlayingPath = s.ctSettings.IsPlayingPath,
                        UseRealTime = s.ctSettings.UseRealTime,
                        PathStartTime = s.ctSettings.PathStartTime
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

                        // NEW: CameraTools support
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

                            // Stationary position (Vector3 serializes as "x,y,z")
                            ctNode.AddValue("manualPos", slot.ctSettings.ManualPosition);
                            ctNode.AddValue("autoFlyby", slot.ctSettings.AutoFlybyPosition);
                            ctNode.AddValue("manualOffset", slot.ctSettings.ManualOffset);
                            ctNode.AddValue("manualOffsetFwd", slot.ctSettings.ManualOffsetForward);
                            ctNode.AddValue("manualOffsetRight", slot.ctSettings.ManualOffsetRight);
                            ctNode.AddValue("manualOffsetUp", slot.ctSettings.ManualOffsetUp);
                            ctNode.AddValue("autoLanding", slot.ctSettings.AutoLandingPosition);
                            ctNode.AddValue("usePresetOffset", slot.ctSettings.UsePresetOffset);
                            ctNode.AddValue("presetOffset", slot.ctSettings.PresetOffset);

                            // Target tracking
                            ctNode.AddValue("hasTarget", slot.ctSettings.HasTarget);
                            ctNode.AddValue("targetSelf", slot.ctSettings.TargetSelf);
                            ctNode.AddValue("targetPartId", slot.ctSettings.TargetPartPersistentId);
                            ctNode.AddValue("targetCoM", slot.ctSettings.TargetCoM);

                            // Velocity/orbit settings
                            ctNode.AddValue("maintainVel", slot.ctSettings.MaintainInitialVelocity);
                            ctNode.AddValue("useOrbital", slot.ctSettings.UseOrbital);

                            // Zoom/FOV
                            ctNode.AddValue("autoZoom", slot.ctSettings.AutoZoom);
                            ctNode.AddValue("manualFOV", slot.ctSettings.ManualFOV);

                            // Pathing
                            ctNode.AddValue("pathIndex", slot.ctSettings.SelectedPathIndex);
                            ctNode.AddValue("pathTimeScale", slot.ctSettings.PathTimeScale);
                            ctNode.AddValue("keyframeIndex", slot.ctSettings.CurrentKeyframeIndex);
                            ctNode.AddValue("isPlaying", slot.ctSettings.IsPlayingPath);
                            ctNode.AddValue("useRealTime", slot.ctSettings.UseRealTime);
                            ctNode.AddValue("pathStartTime", slot.ctSettings.PathStartTime);

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

                        // NEW: Check for CameraTools flag (default false for legacy support)
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

                        // NEW: Load CameraTools settings if present
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

                                // Stationary positioning - World position offset from CoM
                                string posStr = ctNode.GetValue("manualPos");
                                if (!string.IsNullOrEmpty(posStr))
                                {
                                    string[] parts = posStr.Split(',');
                                    if (parts.Length == 3)
                                    {
                                        float x, y, z;
                                        if (float.TryParse(parts[0], out x) && float.TryParse(parts[1], out y) && float.TryParse(parts[2], out z))
                                            slot.ctSettings.ManualPosition = new Vector3(x, y, z);
                                    }
                                }

                                // Stationary positioning mode flags
                                bool.TryParse(ctNode.GetValue("autoFlyby") ?? "False", out slot.ctSettings.AutoFlybyPosition);
                                bool.TryParse(ctNode.GetValue("manualOffset") ?? "False", out slot.ctSettings.ManualOffset);
                                float.TryParse(ctNode.GetValue("manualOffsetFwd") ?? "500", out slot.ctSettings.ManualOffsetForward);
                                float.TryParse(ctNode.GetValue("manualOffsetRight") ?? "50", out slot.ctSettings.ManualOffsetRight);
                                float.TryParse(ctNode.GetValue("manualOffsetUp") ?? "5", out slot.ctSettings.ManualOffsetUp);
                                bool.TryParse(ctNode.GetValue("autoLanding") ?? "False", out slot.ctSettings.AutoLandingPosition);
                                bool.TryParse(ctNode.GetValue("usePresetOffset") ?? "False", out slot.ctSettings.UsePresetOffset);

                                // Preset offset world position (for "Click to Set Position" mode)
                                string presetPosStr = ctNode.GetValue("presetOffset");
                                if (!string.IsNullOrEmpty(presetPosStr))
                                {
                                    string[] parts = presetPosStr.Split(',');
                                    if (parts.Length == 3)
                                    {
                                        float x, y, z;
                                        if (float.TryParse(parts[0], out x) && float.TryParse(parts[1], out y) && float.TryParse(parts[2], out z))
                                            slot.ctSettings.PresetOffset = new Vector3(x, y, z);
                                    }
                                }

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

                                // Pathing parameters
                                int.TryParse(ctNode.GetValue("pathIndex") ?? "-1", out slot.ctSettings.SelectedPathIndex);
                                float.TryParse(ctNode.GetValue("pathTimeScale") ?? "1", out slot.ctSettings.PathTimeScale);
                                int.TryParse(ctNode.GetValue("keyframeIndex") ?? "-1", out slot.ctSettings.CurrentKeyframeIndex);
                                bool.TryParse(ctNode.GetValue("isPlaying") ?? "False", out slot.ctSettings.IsPlayingPath);
                                bool.TryParse(ctNode.GetValue("useRealTime") ?? "True", out slot.ctSettings.UseRealTime);
                                float.TryParse(ctNode.GetValue("pathStartTime") ?? "0", out slot.ctSettings.PathStartTime);
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