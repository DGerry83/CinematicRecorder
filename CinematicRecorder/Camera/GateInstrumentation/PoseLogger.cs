using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace CinematicRecorder.Camera.GateInstrumentation
{
    /// <summary>
    /// Owner of the locked pose-log file format (build-program chunk P1-C7;
    /// INTEGRATION_CONTRACT "Pose-log format"; consumed by every G-P* gate run).
    /// One structured file per capture run, written from the capture path only
    /// (never in preview): one row per captured OUTPUT frame with the frame
    /// index, playback seconds, camera position, rotation, FOV, the frame
    /// encoding used, and the camera driver. The run header carries the mod
    /// version, capture rate, sim FPS, timestamp, hardware leg, the active
    /// camera, and its anchor descriptor.
    ///
    /// Format v1: UTF-8 CSV, `#`-prefixed header lines, then a column header
    /// row, then one row per captured frame. All floating-point values are
    /// doubles formatted "G17" in invariant culture (round-trip safe on
    /// .NET Framework 4.8 — the same discipline as the camera library file).
    /// The logged position is the physical <c>FlightCamera</c> world pose, so
    /// the logger is driver-agnostic: native-camera rows and CT-interop
    /// comparison rows (G-P1b) land in the same format; the per-row driver
    /// column discriminates them.
    ///
    /// Writing discipline: buffered StreamWriter, flushed and closed by
    /// <see cref="Close"/> at capture end (idempotent; safe on the
    /// emergency-reset path). No engine clock API is read anywhere in this
    /// file — playback seconds arrive as a parameter from the capture
    /// session's frame-counter clock — and no per-frame logging goes anywhere
    /// but this file (plan invariant 4).
    /// </summary>
    public sealed class PoseLogger : IDisposable
    {
        /// <summary>Per-row encoding label of the logged coordinates: the physical world pose.</summary>
        public const string WorldEncodingLabel = "world";

        private const string FileNamePrefix = "PoseLog";
        private const string DoubleFormat = "G17";

        private readonly string _filePath;
        private readonly string _leg;
        private StreamWriter _writer;

        /// <summary>
        /// Creates the pose log for one capture run and writes the header.
        /// The file name carries the hardware leg, the playback rate, and the
        /// run timestamp: <c>PoseLog_&lt;leg&gt;_&lt;rate&gt;fps_&lt;yyyy-MM-dd_HH-mm-ss&gt;.csv</c>.
        /// </summary>
        /// <param name="directory">Directory the file is written to (created if missing).</param>
        /// <param name="leg">Hardware leg of this run (e.g. "AMD", "NVIDIA"); sanitized into the file name.</param>
        /// <param name="playbackFps">Playback rate of the capture run, frames per second.</param>
        /// <param name="simulationFps">Simulation rate of the capture run, frames per second.</param>
        /// <param name="cameraLabel">Active camera at recording start ("native:&lt;name&gt;", "ct", or "none").</param>
        /// <param name="anchorDescriptor">Human-readable anchor descriptor of the active camera, or "-".</param>
        /// <exception cref="ArgumentNullException"><paramref name="directory"/> or <paramref name="leg"/> is null.</exception>
        /// <exception cref="IOException">The directory or file could not be created/opened.</exception>
        public PoseLogger(
            string directory,
            string leg,
            int playbackFps,
            int simulationFps,
            string cameraLabel,
            string anchorDescriptor)
        {
            if (directory == null) throw new ArgumentNullException(nameof(directory));
            if (leg == null) throw new ArgumentNullException(nameof(leg));

            Directory.CreateDirectory(directory);

            string safeLeg = leg.ToUpperInvariant();
            foreach (char c in Path.GetInvalidFileNameChars())
                safeLeg = safeLeg.Replace(c, '_');
            _leg = safeLeg;

            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
            _filePath = Path.Combine(directory, $"{FileNamePrefix}_{safeLeg}_{playbackFps}fps_{stamp}.csv");

            _writer = new StreamWriter(_filePath, false, System.Text.Encoding.UTF8);
            WriteHeader(playbackFps, simulationFps, cameraLabel ?? "-", anchorDescriptor ?? "-");
        }

        /// <summary>Absolute path of the log file being written.</summary>
        public string FilePath
        {
            get { return _filePath; }
        }

        /// <summary>
        /// Default pose-log directory: <c>PoseLogs/</c> under the mod's
        /// PluginData folder, resolved with the same idiom as the camera
        /// library file.
        /// </summary>
        public static string DefaultDirectory
        {
            get
            {
                string pluginData = Path.Combine(
                    KSPUtil.ApplicationRootPath,
                    "GameData",
                    "CinematicRecorder",
                    "PluginData");
                return Path.Combine(pluginData, "PoseLogs");
            }
        }

        /// <summary>
        /// Writes one pose row for the frame about to render. No-op after
        /// <see cref="Close"/>. Never throws: a failed write disables the
        /// logger instead of killing a capture.
        /// </summary>
        /// <param name="frameIndex">Zero-based captured-frame index.</param>
        /// <param name="playbackSeconds">Playback time of this frame from the capture session's frame-counter clock.</param>
        /// <param name="posX">Camera world position X, meters.</param>
        /// <param name="posY">Camera world position Y, meters.</param>
        /// <param name="posZ">Camera world position Z, meters.</param>
        /// <param name="rotX">Camera world rotation quaternion X.</param>
        /// <param name="rotY">Camera world rotation quaternion Y.</param>
        /// <param name="rotZ">Camera world rotation quaternion Z.</param>
        /// <param name="rotW">Camera world rotation quaternion W.</param>
        /// <param name="fovDegrees">Camera field of view, degrees.</param>
        /// <param name="driver">Camera driver of this frame ("native:&lt;name&gt;", "ct", or "none").</param>
        public void LogFrame(
            int frameIndex,
            double playbackSeconds,
            double posX,
            double posY,
            double posZ,
            double rotX,
            double rotY,
            double rotZ,
            double rotW,
            double fovDegrees,
            string driver)
        {
            StreamWriter writer = _writer;
            if (writer == null) return;

            try
            {
                writer.Write(frameIndex.ToString(CultureInfo.InvariantCulture));
                writer.Write(',');
                writer.Write(Format(playbackSeconds));
                writer.Write(',');
                writer.Write(Format(posX));
                writer.Write(',');
                writer.Write(Format(posY));
                writer.Write(',');
                writer.Write(Format(posZ));
                writer.Write(',');
                writer.Write(Format(rotX));
                writer.Write(',');
                writer.Write(Format(rotY));
                writer.Write(',');
                writer.Write(Format(rotZ));
                writer.Write(',');
                writer.Write(Format(rotW));
                writer.Write(',');
                writer.Write(Format(fovDegrees));
                writer.Write(',');
                writer.Write(WorldEncodingLabel);
                writer.Write(',');
                writer.WriteLine(driver ?? "none");
            }
            catch (Exception)
            {
                // A gate-instrumentation failure must never abort a capture:
                // disable the logger and keep capturing footage.
                try { writer.Dispose(); } catch (Exception) { }
                _writer = null;
            }
        }

        /// <summary>
        /// Flushes and closes the file. Idempotent; safe to call multiple
        /// times and from the emergency-reset path.
        /// </summary>
        public void Close()
        {
            StreamWriter writer = _writer;
            _writer = null;
            if (writer == null) return;

            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch (Exception)
            {
                // Closing is best-effort; nothing actionable remains.
            }
        }

        /// <summary>Closes the log file (see <see cref="Close"/>).</summary>
        public void Dispose()
        {
            Close();
        }

        private void WriteHeader(int playbackFps, int simulationFps, string cameraLabel, string anchorDescriptor)
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            _writer.WriteLine("# CinematicRecorder pose log v1");
            _writer.WriteLine("# mod_version: " + version);
            _writer.WriteLine("# started: " + stamp);
            _writer.WriteLine("# leg: " + _leg);
            _writer.WriteLine("# playback_fps: " + playbackFps.ToString(CultureInfo.InvariantCulture));
            _writer.WriteLine("# sim_fps: " + simulationFps.ToString(CultureInfo.InvariantCulture));
            _writer.WriteLine("# camera: " + cameraLabel);
            _writer.WriteLine("# anchor: " + anchorDescriptor);
            _writer.WriteLine("# encoding: " + WorldEncodingLabel + " (physical FlightCamera world pose; G17 invariant doubles)");
            _writer.WriteLine("# driver: native:<name> | ct | none");
            _writer.WriteLine("frame,playback_seconds,pos_x_m,pos_y_m,pos_z_m,rot_x,rot_y,rot_z,rot_w,fov_deg,encoding,driver");
        }

        private static string Format(double value)
        {
            return value.ToString(DoubleFormat, CultureInfo.InvariantCulture);
        }
    }
}
