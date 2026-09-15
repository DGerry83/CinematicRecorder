using System;

namespace CinematicRecorder.Camera.Time
{
    /// <summary>
    /// The single-owner playback clock: a frame-counter clock whose time is
    /// always <see cref="FrameIndex"/> divided by the current playback FPS.
    /// This is the only clock that may drive capture-visible camera evaluation
    /// (plan invariant 1): it never reads any wall-clock, scaled, or delta time
    /// from the engine — no engine time API appears anywhere in this file.
    ///
    /// Ownership rule: the camera controller constructs exactly one instance and
    /// is its sole owner. The clock is not a global singleton, is not shared
    /// between owners, and is not thread-safe; it is driven from the main thread
    /// by its owner, which calls <see cref="AdvanceOneFrame"/> once per captured
    /// frame and <see cref="Reset"/> at recording start.
    /// </summary>
    public sealed class PlaybackClock
    {
        private readonly Func<double> _playbackFpsSource;
        private int _frameIndex;

        /// <summary>
        /// Creates a clock at frame 0. The playback FPS is read lazily from the
        /// supplied source so a session setting change is picked up on the next
        /// <see cref="TimeSeconds"/> read without reconstructing the clock.
        /// </summary>
        /// <param name="playbackFpsSource">Source of the current playback FPS in frames per second; must return a positive value during capture.</param>
        public PlaybackClock(Func<double> playbackFpsSource)
        {
            if (playbackFpsSource == null) throw new ArgumentNullException(nameof(playbackFpsSource));
            _playbackFpsSource = playbackFpsSource;
            _frameIndex = 0;
        }

        /// <summary>
        /// Number of captured frames elapsed since the last <see cref="Reset"/>.
        /// Advances by one per <see cref="AdvanceOneFrame"/> call.
        /// </summary>
        public int FrameIndex
        {
            get { return _frameIndex; }
        }

        /// <summary>
        /// Playback time in seconds: <see cref="FrameIndex"/> divided by the
        /// playback FPS reported by the source. Returns 0 when the source
        /// reports a non-positive FPS (no capture session active), mirroring the
        /// existing defensive guard in the transition coordinator.
        /// </summary>
        public double TimeSeconds
        {
            get
            {
                double playbackFps = _playbackFpsSource();
                if (playbackFps <= 0.0) return 0.0;
                return _frameIndex / playbackFps;
            }
        }

        /// <summary>
        /// Resets the clock to frame 0. Called by the owner at recording start
        /// so playback time always begins at 0 for every capture run.
        /// </summary>
        public void Reset()
        {
            _frameIndex = 0;
        }

        /// <summary>
        /// Advances the clock by exactly one captured frame, adding
        /// 1/playbackFPS seconds of playback time. Called by the owner once per
        /// captured frame, never per rendered or simulated step.
        /// </summary>
        public void AdvanceOneFrame()
        {
            _frameIndex++;
        }
    }
}
