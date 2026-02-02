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

            // Set up path in PluginData (standard KSP mod practice)
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
                    allowAnyVessel = s.allowAnyVessel
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
                        if (slot.partPersistentId == 0 && string.IsNullOrEmpty(slot.cameraName))
                            continue;

                        ConfigNode slotNode = n.AddNode("SLOT");
                        slotNode.AddValue("buttonID", slot.buttonID ?? "Cam_0");
                        slotNode.AddValue("partId", slot.partPersistentId);
                        slotNode.AddValue("camName", slot.cameraName ?? "");
                        slotNode.AddValue("vesselId", slot.vesselId ?? "");
                        slotNode.AddValue("allowAny", slot.allowAnyVessel);
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

                        p.buttonAssignments.Add(new CameraSlot
                        {
                            buttonID = slotNode.GetValue("buttonID") ?? ("Cam_" + p.buttonAssignments.Count),
                            partPersistentId = partId,
                            cameraName = slotNode.GetValue("camName") ?? "",
                            vesselId = slotNode.GetValue("vesselId") ?? "",
                            allowAnyVessel = allowAny
                        });
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