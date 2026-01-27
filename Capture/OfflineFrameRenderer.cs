using UnityEngine;

namespace CinematicRecorder.Capture
{
    public sealed class OfflineFrameRenderer
    {
        private readonly Camera camera;
        private readonly RenderTexture target;

        public OfflineFrameRenderer(Camera camera, RenderTexture target)
        {
            this.camera = camera;
            this.target = target;
        }

        public void Render()
        {
            var prev = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = prev;
        }
    }
}
