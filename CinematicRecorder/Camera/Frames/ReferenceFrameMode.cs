namespace CinematicRecorder.Camera.Frames
{
    /// <summary>
    /// Reference frame mode for vessel-anchored camera evaluation.
    /// Selects whether the anchor's local axes follow the body's rotating
    /// surface frame or the non-rotating inertial (orbit) frame.
    /// </summary>
    public enum ReferenceFrameMode
    {
        /// <summary>
        /// Surface frame: the anchor frame rotates with the body, so the camera
        /// holds its orientation relative to the local terrain/horizon.
        /// </summary>
        Surface,

        /// <summary>
        /// Orbit frame: the anchor frame is fixed to the non-rotating inertial
        /// frame of the current sphere of influence, so the camera holds its
        /// orientation relative to the stars.
        /// </summary>
        Orbit
    }

    /// <summary>
    /// A displacement expressed in a camera's anchor-local frame (forward/right/up
    /// axes of the camera's resolved anchor). Frame-encoding marker type: the
    /// encoding is carried by the type itself, so a persisted offset can never be
    /// mistaken for a displacement in another frame (frame-discriminator rule).
    /// </summary>
    public struct AnchorLocalOffset
    {
        /// <summary>
        /// Offset along the anchor's forward axis, in meters.
        /// </summary>
        public double Forward { get; }

        /// <summary>
        /// Offset along the anchor's right axis, in meters.
        /// </summary>
        public double Right { get; }

        /// <summary>
        /// Offset along the anchor's up axis, in meters.
        /// </summary>
        public double Up { get; }

        /// <summary>
        /// Creates an anchor-local offset from its axis components in meters.
        /// </summary>
        /// <param name="forward">Offset along the anchor's forward axis, in meters.</param>
        /// <param name="right">Offset along the anchor's right axis, in meters.</param>
        /// <param name="up">Offset along the anchor's up axis, in meters.</param>
        public AnchorLocalOffset(double forward, double right, double up)
        {
            Forward = forward;
            Right = right;
            Up = up;
        }
    }
}
