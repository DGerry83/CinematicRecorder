using CinematicRecorder.Core;
using UnityEngine;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Owns the fade-to-black state machine for camera swaps and draws the
    /// fullscreen IMGUI overlay. D-U9 (fade burned into footage) was withdrawn by
    /// user decision on 2026-09-12 after the in-game G-U2 failure — the capture-side
    /// quad rendered white/instant-on and could never cover the HullCam VDS overlay
    /// layer (IMGUI draws above everything). The fade is therefore a screen overlay
    /// again, exactly as pre-0.2.4: it appears in footage only when Capture UI is
    /// on. Owns the single <see cref="CameraTransitionCoordinator"/> instance;
    /// chunk C8's camera panel consumes it through <see cref="Coordinator"/> and
    /// routes <c>BeginTransition</c> through it.
    /// </summary>
    public sealed class FadeOverlayController
    {
        #region Fields & State
        /// <summary>
        /// The single fade state machine. C8's camera panel drives BeginTransition and
        /// the fade toggle/duration through this instance.
        /// </summary>
        public CameraTransitionCoordinator Coordinator { get; } = new CameraTransitionCoordinator();
        #endregion

        #region Public API
        /// <summary>
        /// Per-rendered-frame driver, forwarded from CinematicUiHost.LateUpdate. Advances
        /// the fade clock once per rendered frame (deterministic while recording,
        /// real-time otherwise — the pre-C5 LateUpdate cadence, not per physics step)
        /// so both clocks are driven from one place; the overlay itself only renders.
        /// </summary>
        internal void Tick()
        {
            if (DeterministicCaptureSession.IsRunning)
            {
                Coordinator.UpdateDeterministicFade();
            }
            else
            {
                Coordinator.UpdateFade();
            }
        }

        /// <summary>
        /// Draws the fullscreen fade overlay. Call from OnGUI only (second sanctioned
        /// G-U1 exception, alongside stock PopupDialog). The real-time/deterministic
        /// clock is driven by <see cref="Tick"/> — this method only renders, exactly
        /// like the pre-migration overlay did.
        /// </summary>
        internal void DrawOverlay()
        {
            if (!Coordinator.IsFading) return;

            GUI.color = Coordinator.GetFadeColor();
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        #endregion
    }
}
