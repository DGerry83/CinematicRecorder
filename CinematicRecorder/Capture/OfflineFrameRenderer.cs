using UnityEngine;

namespace CinematicRecorder.Capture
{
    /// <summary>
    /// Renders a Unity camera to a specific RenderTexture with proper state preservation.
    /// </summary>
    public sealed class OfflineFrameRenderer
    {
        private readonly Camera camera;
        private readonly RenderTexture target;

        public OfflineFrameRenderer(Camera camera, RenderTexture target)
        {
            this.camera = camera;
            this.target = target;
        }
        /// <summary>
        /// Renders the camera to the target texture, restoring the previous active render texture afterward.
        /// </summary>
        public void Render()
        {
            var prev = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = prev;
        }
    }
}
