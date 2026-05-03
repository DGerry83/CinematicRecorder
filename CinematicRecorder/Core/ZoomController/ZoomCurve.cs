using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Interpolation curves for target-based zoom transitions.
    /// </summary>
    public enum ZoomCurve
    {
        Linear,
        EaseIn,
        EaseOut,
        EaseInOut
    }

    /// <summary>
    /// Static evaluator for zoom interpolation curves.
    /// </summary>
    public static class ZoomCurveEvaluator
    {
        public static float Evaluate(float t, ZoomCurve curve)
        {
            switch (curve)
            {
                case ZoomCurve.Linear:
                    return t;

                case ZoomCurve.EaseIn:
                    // Quadratic ease in: t^2
                    return t * t;

                case ZoomCurve.EaseOut:
                    // Quadratic ease out: 1 - (1-t)^2
                    float tOut = 1f - t;
                    return 1f - (tOut * tOut);

                case ZoomCurve.EaseInOut:
                    // Quadratic ease in-out
                    if (t < 0.5f)
                    {
                        return 2f * t * t;
                    }
                    else
                    {
                        float tInOut = -2f * t + 2f;
                        return 1f - (tInOut * tInOut) / 2f;
                    }

                default:
                    return t;
            }
        }
    }
}