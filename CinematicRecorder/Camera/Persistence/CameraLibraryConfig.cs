using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CinematicRecorder.Camera.Frames;
using CinematicRecorder.Camera.Model;

namespace CinematicRecorder.Camera.Persistence
{
    /// <summary>
    /// Outcome of loading the camera library file: the parsed cameras (in file order)
    /// and how many <c>CAMERA</c> nodes were skipped as malformed. Constructed only by
    /// <see cref="CameraLibraryConfig"/>.
    /// </summary>
    public sealed class CameraLibraryLoadResult
    {
        /// <summary>Cameras that parsed successfully, in file order.</summary>
        public IReadOnlyList<CameraDefinition> Cameras { get; }

        /// <summary>Number of CAMERA nodes skipped because they were malformed or duplicated a name.</summary>
        public int SkippedCount { get; }

        /// <summary>Number of cameras that loaded successfully.</summary>
        public int LoadedCount
        {
            get { return Cameras.Count; }
        }

        internal CameraLibraryLoadResult(IReadOnlyList<CameraDefinition> cameras, int skippedCount)
        {
            Cameras = cameras;
            SkippedCount = skippedCount;
        }
    }

    /// <summary>
    /// ConfigNode file I/O for the camera library
    /// (<c>GameData/CinematicRecorder/PluginData/CameraLibrary.cfg</c>, parent spec §4.4).
    /// One <c>CAMERA</c> node per camera under a root <c>CAMERA_LIBRARY</c> node carrying a
    /// <c>version</c> key (1 for the P1 schema). Anchors and targets are written with an
    /// explicit <c>kind</c> discriminant exactly as the DTO discriminates them; positions are
    /// stored as doubles in their named frame encodings (body-fixed geodetic or anchor-local
    /// offsets) and never converted through world space (plan invariant 2).
    ///
    /// Malformed CAMERA nodes never throw: a node that cannot be parsed is skipped and
    /// counted in <see cref="CameraLibraryLoadResult.SkippedCount"/>. A missing file loads
    /// as an empty library (not an error).
    ///
    /// P2-C2 extension point: spline path keys (playback-second times, anchor-local
    /// positions/rotations, per-key FOV, duplicate-timestamp +0.001 s guard) will serialize
    /// as a <c>PATH</c> child node of each <c>CAMERA</c> node; see the marked site in
    /// <see cref="WriteCameraNode"/>.
    /// </summary>
    public sealed class CameraLibraryConfig
    {
        /// <summary>Schema version written to the root node's <c>version</c> key.</summary>
        public const int CurrentFileVersion = 1;

        private const string NodeNameCameraLibrary = "CAMERA_LIBRARY";
        private const string NodeNameCamera = "CAMERA";
        private const string NodeNameAnchor = "ANCHOR";
        private const string NodeNameTarget = "TARGET";
        private const string NodeNameVelocity = "VELOCITY";
        private const string NodeNameManualOffset = "MANUAL_OFFSET";
        private const string NodeNameZoom = "ZOOM";

        private const string AnchorKindBody = "BODY";
        private const string AnchorKindVessel = "VESSEL";
        private const string TargetKindNone = "NONE";
        private const string TargetKindVesselCom = "VESSEL_COM";
        private const string TargetKindPart = "PART";
        private const string TargetKindGeoPoint = "GEO_POINT";
        private const string TargetKindPathDirection = "PATH_DIRECTION";

        /// <summary>
        /// Absolute path of the library file used when no explicit path is supplied:
        /// <c>CameraLibrary.cfg</c> under the mod's PluginData folder, resolved exactly
        /// like <c>Core/CameraPanelConfig</c> resolves its presets file.
        /// </summary>
        public static string DefaultFilePath
        {
            get
            {
                string pluginData = Path.Combine(
                    KSPUtil.ApplicationRootPath,
                    "GameData",
                    "CinematicRecorder",
                    "PluginData");
                return Path.Combine(pluginData, "CameraLibrary.cfg");
            }
        }

        /// <summary>Path of the file this instance reads and writes.</summary>
        public string FilePath { get; }

        /// <summary>
        /// Creates an instance bound to <see cref="DefaultFilePath"/> (the in-game
        /// PluginData location).
        /// </summary>
        public CameraLibraryConfig() : this(DefaultFilePath)
        {
        }

        /// <summary>
        /// Creates an instance bound to an explicit file path. Used by verification
        /// harnesses; in-game code uses the parameterless constructor.
        /// </summary>
        /// <param name="filePath">Absolute or relative path of the library file.</param>
        public CameraLibraryConfig(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            FilePath = filePath;
        }

        /// <summary>
        /// Reads the library file. Never throws: a missing or unreadable file yields an
        /// empty result; a malformed <c>CAMERA</c> node (missing/empty name, missing or
        /// unknown anchor/target discriminant, any present-but-unparseable value, or a
        /// duplicate name) is skipped and counted. Cameras keep file order.
        /// </summary>
        /// <returns>The parsed cameras plus the number of skipped nodes.</returns>
        public CameraLibraryLoadResult Load()
        {
            var cameras = new List<CameraDefinition>();

            if (!File.Exists(FilePath))
                return new CameraLibraryLoadResult(cameras, 0);

            ConfigNode root;
            try
            {
                root = ConfigNode.Load(FilePath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[CameraLibraryConfig] Failed to load '" + FilePath + "': " + ex);
                return new CameraLibraryLoadResult(cameras, 0);
            }

            if (root == null)
                return new CameraLibraryLoadResult(cameras, 0);

            ConfigNode libraryNode = root.GetNode(NodeNameCameraLibrary);
            if (libraryNode == null)
                return new CameraLibraryLoadResult(cameras, 0);

            int skippedCount = 0;
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConfigNode cameraNode in libraryNode.GetNodes(NodeNameCamera))
            {
                CameraDefinition camera;
                if (!TryParseCameraNode(cameraNode, out camera) || !seenNames.Add(camera.Name))
                {
                    skippedCount++;
                    continue;
                }
                cameras.Add(camera);
            }

            return new CameraLibraryLoadResult(cameras, skippedCount);
        }

        /// <summary>
        /// Writes all cameras to the library file (root <c>CAMERA_LIBRARY</c>,
        /// <c>version = 1</c>). Creates the containing directory if needed. Cameras with a
        /// null or empty name, or a null anchor, are omitted. Failures are logged, not
        /// thrown.
        /// </summary>
        /// <param name="cameras">Cameras in their current order.</param>
        public void Save(IReadOnlyList<CameraDefinition> cameras)
        {
            if (cameras == null) throw new ArgumentNullException(nameof(cameras));

            try
            {
                ConfigNode root = new ConfigNode();
                ConfigNode libraryNode = root.AddNode(NodeNameCameraLibrary);
                libraryNode.AddValue("version", CurrentFileVersion.ToString(CultureInfo.InvariantCulture));

                foreach (CameraDefinition camera in cameras)
                {
                    if (camera == null || string.IsNullOrEmpty(camera.Name) || camera.Anchor == null)
                        continue;

                    WriteCameraNode(libraryNode, camera);
                }

                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                root.Save(FilePath);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[CameraLibraryConfig] Failed to save '" + FilePath + "': " + ex);
            }
        }

        #region Serialization

        /// <summary>
        /// Doubles are formatted with the round-trip "G17" specifier in invariant culture:
        /// on .NET Framework 4.8 neither the default "G15" nor "R" guarantees a
        /// bit-exact round-trip, and typed ConfigNode.AddValue(name, object) formats with
        /// the current culture.
        /// </summary>
        private static string FormatDouble(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        private static string FormatUint(uint value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void WriteGeoCoordinate(ConfigNode node, GeoCoordinate coordinate)
        {
            node.AddValue("bodyName", coordinate.BodyName);
            node.AddValue("latitude", FormatDouble(coordinate.Latitude));
            node.AddValue("longitude", FormatDouble(coordinate.Longitude));
            node.AddValue("altitude", FormatDouble(coordinate.Altitude));
        }

        private static void WriteCameraNode(ConfigNode libraryNode, CameraDefinition camera)
        {
            ConfigNode node = libraryNode.AddNode(NodeNameCamera);
            node.AddValue("name", camera.Name);

            ConfigNode anchorNode = node.AddNode(NodeNameAnchor);
            CameraAnchor.Body bodyAnchor = camera.Anchor as CameraAnchor.Body;
            if (bodyAnchor != null)
            {
                anchorNode.AddValue("kind", AnchorKindBody);
                WriteGeoCoordinate(anchorNode, bodyAnchor.Coordinate);
            }
            else
            {
                CameraAnchor.Vessel vesselAnchor = (CameraAnchor.Vessel)camera.Anchor;
                anchorNode.AddValue("kind", AnchorKindVessel);
                anchorNode.AddValue("anchorPartPersistentId", FormatUint(vesselAnchor.AnchorPartPersistentId));
                anchorNode.AddValue("fallbackPolicy", vesselAnchor.FallbackPolicy.ToString());
                anchorNode.AddValue("distance", FormatDouble(vesselAnchor.Distance));
                anchorNode.AddValue("forward", FormatDouble(vesselAnchor.Forward));
                anchorNode.AddValue("right", FormatDouble(vesselAnchor.Right));
                anchorNode.AddValue("up", FormatDouble(vesselAnchor.Up));
            }

            ConfigNode targetNode = node.AddNode(NodeNameTarget);
            if (camera.Target == null || camera.Target is CameraTarget.NoTarget)
            {
                targetNode.AddValue("kind", TargetKindNone);
            }
            else if (camera.Target is CameraTarget.VesselComTarget)
            {
                targetNode.AddValue("kind", TargetKindVesselCom);
            }
            else if (camera.Target is CameraTarget.PathDirectionTarget)
            {
                targetNode.AddValue("kind", TargetKindPathDirection);
            }
            else if (camera.Target is CameraTarget.PartTarget)
            {
                var partTarget = (CameraTarget.PartTarget)camera.Target;
                targetNode.AddValue("kind", TargetKindPart);
                targetNode.AddValue("partPersistentId", FormatUint(partTarget.PersistentId));
            }
            else
            {
                var geoPointTarget = (CameraTarget.GeoPointTarget)camera.Target;
                targetNode.AddValue("kind", TargetKindGeoPoint);
                WriteGeoCoordinate(targetNode, geoPointTarget.Coordinate);
            }

            VelocityModelSettings velocity = camera.Velocity ?? new VelocityModelSettings();
            ConfigNode velocityNode = node.AddNode(NodeNameVelocity);
            velocityNode.AddValue("rigidity", FormatDouble(velocity.Rigidity));
            velocityNode.AddValue("matchOnStart", velocity.MatchOnStart.ToString());
            velocityNode.AddValue("gravity", velocity.Gravity.ToString());

            node.AddValue("frameMode", camera.FrameMode.ToString());

            AnchorLocalOffset manualOffset = camera.ManualOffset;
            ConfigNode offsetNode = node.AddNode(NodeNameManualOffset);
            offsetNode.AddValue("forward", FormatDouble(manualOffset.Forward));
            offsetNode.AddValue("right", FormatDouble(manualOffset.Right));
            offsetNode.AddValue("up", FormatDouble(manualOffset.Up));

            node.AddValue("rollDegrees", FormatDouble(camera.RollDegrees));
            node.AddValue("pivotMode", camera.PivotMode.ToString());

            ZoomSettings zoom = camera.Zoom ?? new ZoomSettings();
            ConfigNode zoomNode = node.AddNode(NodeNameZoom);
            zoomNode.AddValue("manualFov", FormatDouble(zoom.ManualFov));
            zoomNode.AddValue("autoZoomEnabled", zoom.AutoZoomEnabled.ToString());
            zoomNode.AddValue("paddingMultiplier", FormatDouble(zoom.PaddingMultiplier));
            zoomNode.AddValue("transitionDuration", FormatDouble(zoom.TransitionDurationSeconds));

            // P2-C2 extension point: serialize the camera's spline path keys here as a
            // PATH child node (playback-second times, anchor-local positions/rotations,
            // per-key FOV; the duplicate-timestamp +0.001 s guard applies to those keys).
        }

        #endregion

        #region Deserialization

        /// <summary>
        /// Present-but-unparseable or non-finite ⇒ false (the CAMERA node is skipped);
        /// absent ⇒ true and <paramref name="value"/> keeps its default.
        /// </summary>
        private static bool TryReadDouble(ConfigNode node, string key, ref double value)
        {
            string raw = node.GetValue(key);
            if (raw == null) return true;

            double parsed;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return false;
            if (double.IsNaN(parsed) || double.IsInfinity(parsed)) return false;

            value = parsed;
            return true;
        }

        /// <summary>Same discipline as <see cref="TryReadDouble"/>: absent keeps the default.</summary>
        private static bool TryReadUint(ConfigNode node, string key, ref uint value)
        {
            string raw = node.GetValue(key);
            if (raw == null) return true;

            uint parsed;
            if (!uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return false;

            value = parsed;
            return true;
        }

        /// <summary>Same discipline: absent keeps the default.</summary>
        private static bool TryReadBool(ConfigNode node, string key, ref bool value)
        {
            string raw = node.GetValue(key);
            if (raw == null) return true;

            bool parsed;
            if (!bool.TryParse(raw, out parsed)) return false;

            value = parsed;
            return true;
        }

        /// <summary>Same discipline: absent keeps the default; numeric strings that do not
        /// name a defined enum member are rejected.</summary>
        private static bool TryReadEnum<TEnum>(ConfigNode node, string key, ref TEnum value) where TEnum : struct
        {
            string raw = node.GetValue(key);
            if (raw == null) return true;

            TEnum parsed;
            if (!Enum.TryParse(raw, out parsed)) return false;
            if (!Enum.IsDefined(typeof(TEnum), parsed)) return false;

            value = parsed;
            return true;
        }

        private static bool TryParseGeoCoordinate(ConfigNode node, out GeoCoordinate coordinate)
        {
            coordinate = default(GeoCoordinate);

            string bodyName = node.GetValue("bodyName");
            if (string.IsNullOrEmpty(bodyName)) return false;

            double latitude = 0.0;
            double longitude = 0.0;
            double altitude = 0.0;
            if (!TryReadDouble(node, "latitude", ref latitude)) return false;
            if (!TryReadDouble(node, "longitude", ref longitude)) return false;
            if (!TryReadDouble(node, "altitude", ref altitude)) return false;

            coordinate = new GeoCoordinate(bodyName, latitude, longitude, altitude);
            return true;
        }

        private static bool TryParseAnchorNode(ConfigNode node, out CameraAnchor anchor)
        {
            anchor = null;

            string kind = node.GetValue("kind");
            if (kind == AnchorKindBody)
            {
                GeoCoordinate coordinate;
                if (!TryParseGeoCoordinate(node, out coordinate)) return false;

                anchor = CameraAnchor.AtBody(coordinate);
                return true;
            }

            if (kind == AnchorKindVessel)
            {
                uint anchorPartPersistentId = 0;
                if (!TryReadUint(node, "anchorPartPersistentId", ref anchorPartPersistentId)) return false;

                AnchorFallbackPolicy fallbackPolicy = AnchorFallbackPolicy.RootPart;
                if (!TryReadEnum(node, "fallbackPolicy", ref fallbackPolicy)) return false;

                double distance = 0.0;
                double forward = 0.0;
                double right = 0.0;
                double up = 0.0;
                if (!TryReadDouble(node, "distance", ref distance)) return false;
                if (!TryReadDouble(node, "forward", ref forward)) return false;
                if (!TryReadDouble(node, "right", ref right)) return false;
                if (!TryReadDouble(node, "up", ref up)) return false;
                if (distance < 0.0) return false;

                anchor = CameraAnchor.OnVessel(anchorPartPersistentId, fallbackPolicy, distance, forward, right, up);
                return true;
            }

            return false;
        }

        private static bool TryParseTargetNode(ConfigNode node, out CameraTarget target)
        {
            target = null;

            string kind = node.GetValue("kind");
            switch (kind)
            {
                case TargetKindNone:
                    target = CameraTarget.None;
                    return true;

                case TargetKindVesselCom:
                    target = CameraTarget.ForVesselCenterOfMass();
                    return true;

                case TargetKindPathDirection:
                    target = CameraTarget.ForPathDirection();
                    return true;

                case TargetKindPart:
                {
                    uint persistentId = 0;
                    if (!TryReadUint(node, "partPersistentId", ref persistentId)) return false;

                    target = CameraTarget.ForPart(persistentId);
                    return true;
                }

                case TargetKindGeoPoint:
                {
                    GeoCoordinate coordinate;
                    if (!TryParseGeoCoordinate(node, out coordinate)) return false;

                    target = CameraTarget.ForGeoPoint(coordinate);
                    return true;
                }

                default:
                    return false;
            }
        }

        private static bool TryParseCameraNode(ConfigNode node, out CameraDefinition camera)
        {
            camera = null;

            string name = node.GetValue("name");
            if (string.IsNullOrEmpty(name)) return false;

            ConfigNode anchorNode = node.GetNode(NodeNameAnchor);
            if (anchorNode == null) return false;

            CameraAnchor anchor;
            if (!TryParseAnchorNode(anchorNode, out anchor)) return false;

            // A missing TARGET node means the DTO default (no target); a present but
            // malformed one rejects the whole camera.
            CameraTarget target;
            ConfigNode targetNode = node.GetNode(NodeNameTarget);
            if (targetNode == null)
            {
                target = CameraTarget.None;
            }
            else if (!TryParseTargetNode(targetNode, out target))
            {
                return false;
            }

            VelocityModelSettings velocity = new VelocityModelSettings();
            ConfigNode velocityNode = node.GetNode(NodeNameVelocity);
            if (velocityNode != null)
            {
                double rigidity = velocity.Rigidity;
                bool matchOnStart = velocity.MatchOnStart;
                bool gravity = velocity.Gravity;
                if (!TryReadDouble(velocityNode, "rigidity", ref rigidity)) return false;
                if (!TryReadBool(velocityNode, "matchOnStart", ref matchOnStart)) return false;
                if (!TryReadBool(velocityNode, "gravity", ref gravity)) return false;
                velocity.Rigidity = rigidity;
                velocity.MatchOnStart = matchOnStart;
                velocity.Gravity = gravity;
            }

            ReferenceFrameMode frameMode = ReferenceFrameMode.Surface;
            if (!TryReadEnum(node, "frameMode", ref frameMode)) return false;

            AnchorLocalOffset manualOffset = new AnchorLocalOffset(0.0, 0.0, 0.0);
            ConfigNode offsetNode = node.GetNode(NodeNameManualOffset);
            if (offsetNode != null)
            {
                double forward = 0.0;
                double right = 0.0;
                double up = 0.0;
                if (!TryReadDouble(offsetNode, "forward", ref forward)) return false;
                if (!TryReadDouble(offsetNode, "right", ref right)) return false;
                if (!TryReadDouble(offsetNode, "up", ref up)) return false;
                manualOffset = new AnchorLocalOffset(forward, right, up);
            }

            double rollDegrees = 0.0;
            if (!TryReadDouble(node, "rollDegrees", ref rollDegrees)) return false;

            CameraPivotMode pivotMode = CameraPivotMode.Camera;
            if (!TryReadEnum(node, "pivotMode", ref pivotMode)) return false;

            ZoomSettings zoom = new ZoomSettings();
            ConfigNode zoomNode = node.GetNode(NodeNameZoom);
            if (zoomNode != null)
            {
                double manualFov = zoom.ManualFov;
                bool autoZoomEnabled = zoom.AutoZoomEnabled;
                double paddingMultiplier = zoom.PaddingMultiplier;
                double transitionDuration = zoom.TransitionDurationSeconds;
                if (!TryReadDouble(zoomNode, "manualFov", ref manualFov)) return false;
                if (!TryReadBool(zoomNode, "autoZoomEnabled", ref autoZoomEnabled)) return false;
                if (!TryReadDouble(zoomNode, "paddingMultiplier", ref paddingMultiplier)) return false;
                if (!TryReadDouble(zoomNode, "transitionDuration", ref transitionDuration)) return false;
                zoom.ManualFov = manualFov;
                zoom.AutoZoomEnabled = autoZoomEnabled;
                zoom.PaddingMultiplier = paddingMultiplier;
                zoom.TransitionDurationSeconds = transitionDuration;
            }

            camera = new CameraDefinition
            {
                Name = name,
                Anchor = anchor,
                Target = target,
                Velocity = velocity,
                FrameMode = frameMode,
                ManualOffset = manualOffset,
                RollDegrees = rollDegrees,
                PivotMode = pivotMode,
                Zoom = zoom,
            };
            return true;
        }

        #endregion
    }
}
