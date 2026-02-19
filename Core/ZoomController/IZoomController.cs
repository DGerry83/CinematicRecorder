namespace CinematicRecorder.Core
{
    public interface IZoomController
    {
        bool UseConsistentAutoZoom { get; set; }
        float ConsistentZoomPadding { get; set; }
        bool HasActiveStrategy { get; }
        float CurrentFoV { get; }

        void SetRateInput(float input);
        void DecayZoomIntent(float deltaTime);
        void Update(float deltaTime);
        void ApplyConsistentFraming();
        void QueueTargetZoom(float targetFOV, float duration, ZoomCurve curve);
        void QueueConsistentTransition(float duration, ZoomCurve curve);
        void Interrupt(IZoomStrategy strategy);
        void CancelActiveZoom();
        void ResetZoom(float maxFov);
    }
}