using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Pre-computed Halton sequence for Temporal Accumulation Blur jitter.
    /// Halton(2,3) sequence provides low-discrepancy sub-pixel sampling.
    /// </summary>
    public static class HaltonSequence
    {
        /// <summary>
        /// Pre-computed Halton(2,3) sequence for indices 0-7.
        /// Values normalized to [0, 1).
        /// </summary>
        public static readonly Vector2[] Sequence23 = new Vector2[]
        {
            new Vector2(0.0000f, 0.0000f),   // index 0: 0/2, 0/3
            new Vector2(0.5000f, 0.3333f),   // index 1: 1/2, 1/3
            new Vector2(0.2500f, 0.6667f),   // index 2: 1/4, 2/3
            new Vector2(0.7500f, 0.1111f),   // index 3: 3/4, 1/9
            new Vector2(0.1250f, 0.4444f),   // index 4: 1/8, 4/9
            new Vector2(0.6250f, 0.7778f),   // index 5: 5/8, 7/9
            new Vector2(0.3750f, 0.2222f),   // index 6: 3/8, 2/9
            new Vector2(0.8750f, 0.5556f)    // index 7: 7/8, 5/9
        };
    }
}
