using System;
using System.Collections.Generic;
using CinematicRecorder.Camera.Model;
using CinematicRecorder.Integration;
using UnityEngine;

namespace CinematicRecorder.Camera.Control
{
    /// <summary>
    /// Which fallback a camera target engaged after the requested target part
    /// could not be resolved on the reference vessel (same chain and spirit as
    /// the vessel anchor frame of P1-C2: root part, then vessel center of mass).
    /// </summary>
    public enum TargetFallbackKind
    {
        /// <summary>The stored target part resolved; no fallback engaged.</summary>
        None,

        /// <summary>The stored target part was gone; the vessel's root part is targeted instead.</summary>
        RootPart,

        /// <summary>No part could be targeted; the vessel's center of mass is targeted instead.</summary>
        VesselCenterOfMass
    }

    /// <summary>
    /// Arguments for the one-time target-fallback warning of
    /// <see cref="NativeTargetResolver"/>: which fallback took over after the
    /// requested target part could not be resolved. Carries no UI text
    /// (strings arrive with the library UI in P3); consumers map the reason to
    /// a message.
    /// </summary>
    public sealed class TargetFallbackEventArgs : EventArgs
    {
        /// <summary>The vessel the target belongs to.</summary>
        public Vessel Vessel { get; }

        /// <summary>The stored target part persistent id that could not be resolved.</summary>
        public uint RequestedPartPersistentId { get; }

        /// <summary>The fallback the target engaged: root part or vessel center of mass.</summary>
        public TargetFallbackKind Fallback { get; }

        internal TargetFallbackEventArgs(Vessel vessel, uint requestedPartPersistentId, TargetFallbackKind fallback)
        {
            Vessel = vessel;
            RequestedPartPersistentId = requestedPartPersistentId;
            Fallback = fallback;
        }
    }

    /// <summary>
    /// Outcome of resolving a camera target for the current evaluation: either
    /// a world position to look at, an explicit "no target" (fixed orientation
    /// in the anchor frame), or an unresolvable target (the camera controller
    /// surfaces the camera as Unavailable with the reason — never throws,
    /// parent spec §5.4).
    /// </summary>
    public struct TargetResolution
    {
        /// <summary>True when <see cref="TargetPosition"/> carries a world position to look at.</summary>
        public bool HasTargetPosition { get; private set; }

        /// <summary>True when the target resolved (or is explicitly none); false when unresolvable.</summary>
        public bool IsResolvable { get; private set; }

        /// <summary>Why the target is unresolvable; null when <see cref="IsResolvable"/> is true.</summary>
        public string UnavailableReason { get; private set; }

        /// <summary>World position of the resolved target.</summary>
        public Vector3d TargetPosition { get; private set; }

        private TargetResolution(bool hasTargetPosition, bool isResolvable, string unavailableReason, Vector3d targetPosition)
        {
            HasTargetPosition = hasTargetPosition;
            IsResolvable = isResolvable;
            UnavailableReason = unavailableReason;
            TargetPosition = targetPosition;
        }

        /// <summary>Resolution for targets that hold a fixed local orientation.</summary>
        public static TargetResolution FixedOrientation()
        {
            return new TargetResolution(false, true, null, Vector3d.zero);
        }

        /// <summary>Resolution for a target that resolved to a world position.</summary>
        public static TargetResolution Resolved(Vector3d targetPosition)
        {
            return new TargetResolution(true, true, null, targetPosition);
        }

        /// <summary>Resolution for an unresolvable target, carrying the reason.</summary>
        public static TargetResolution Unavailable(string reason)
        {
            return new TargetResolution(false, false, reason, Vector3d.zero);
        }
    }

    /// <summary>
    /// Resolves a camera definition's target to a world position each
    /// evaluation and composes the camera orientation from it (build-program
    /// chunk P1-C5; parent spec §5.3 "Targeting"). Target kinds:
    /// None (fixed orientation in the anchor frame), vessel center of mass,
    /// specific part (persistent id, with the same fallback spirit as the
    /// anchor: root part, then vessel CoM, one-time warning event), geo point
    /// (body cameras; resolved through the body's full-precision surface
    /// position), and PathDirection — a documented P2 extension point: with no
    /// path playback in P1 such a camera reports Unavailable with a reason
    /// instead of silently holding a wrong orientation.
    ///
    /// The orientation is the look-at rotation toward the resolved target
    /// (or the anchor frame orientation for fixed targets), composed with the
    /// manual rotation and the definition's roll around the camera forward
    /// axis. Pivot mode selects the rotation center per the parent spec: with
    /// Camera pivot the hold position stands and the manual rotation applies
    /// in place; with Target pivot the manual rotation orbits the camera
    /// position about the target (<see cref="OrbitPositionAboutTarget"/>).
    /// P1 feeds an identity manual rotation (no orbit input exists yet), so
    /// both pivots evaluate identically until P3 wires orbit input — the seam
    /// is honored in the API now so the distinction becomes observable without
    /// reshaping this class.
    ///
    /// Read-only KSP access: writes nothing to the scene. No engine clock API
    /// is read anywhere in this class (build-program invariant 1); every value
    /// is re-derived per call and nothing world-space persists across steps
    /// (build-program invariant 2).
    /// </summary>
    public sealed class NativeTargetResolver
    {
        // Squared-below this, the camera-to-target direction is degenerate and
        // the anchor frame orientation is used instead (never throws, §5.4).
        private const double DegenerateDirectionSqr = 1e-12;

        private readonly CameraDefinition _definition;
        private bool _fallbackWarningRaised;

        /// <summary>
        /// Raised at most once per fallback engagement when the requested
        /// target part cannot be resolved and a fallback (root part or vessel
        /// center of mass) takes over. The warning latch resets when the
        /// stored part resolves again. UI strings are wired in P3; the event
        /// carries only the reason.
        /// </summary>
        public event EventHandler<TargetFallbackEventArgs> TargetFallbackEngaged;

        /// <summary>
        /// Creates the resolver for a camera definition. The definition is
        /// read live on each call: target, roll, and pivot edits written
        /// through while the camera is active take effect on the next
        /// evaluation.
        /// </summary>
        /// <param name="definition">The camera definition supplying the target,
        /// roll, and pivot mode. Must not be null.</param>
        public NativeTargetResolver(CameraDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            _definition = definition;
        }

        /// <summary>
        /// Resolves the definition's target to a world position for the current
        /// evaluation. Vessel-relative targets (center of mass, part) resolve
        /// against the supplied reference vessel — the anchor vessel for
        /// vessel-anchored cameras, otherwise the active vessel (P1 forced
        /// reading: the DTO carries no vessel identity for its targets).
        /// </summary>
        /// <param name="referenceVessel">The vessel VESSEL_COM and PART targets
        /// resolve against, or null when there is none.</param>
        public TargetResolution ResolveTarget(Vessel referenceVessel)
        {
            CameraTarget target = _definition.Target ?? CameraTarget.None;

            CameraTarget.NoTarget noTarget = target as CameraTarget.NoTarget;
            if (noTarget != null) return TargetResolution.FixedOrientation();

            CameraTarget.VesselComTarget vesselCom = target as CameraTarget.VesselComTarget;
            if (vesselCom != null)
            {
                if (referenceVessel == null)
                    return TargetResolution.Unavailable("target vessel is unavailable");
                return TargetResolution.Resolved(referenceVessel.CoMD);
            }

            CameraTarget.PartTarget partTarget = target as CameraTarget.PartTarget;
            if (partTarget != null) return ResolvePartTarget(referenceVessel, partTarget.PersistentId);

            CameraTarget.GeoPointTarget geoTarget = target as CameraTarget.GeoPointTarget;
            if (geoTarget != null)
            {
                // ResolveBody never returns null (unknown names fall back to the
                // current main body), so a geo target always resolves in P1.
                CelestialBody body = GeographicCoordinateSystem.ResolveBody(geoTarget.Coordinate.BodyName);
                Vector3d position = body.GetWorldSurfacePosition(
                    geoTarget.Coordinate.Latitude, geoTarget.Coordinate.Longitude, geoTarget.Coordinate.Altitude);
                return TargetResolution.Resolved(position);
            }

            CameraTarget.PathDirectionTarget pathDirection = target as CameraTarget.PathDirectionTarget;
            if (pathDirection != null)
            {
                // P2 extension point: path-tangent resolution plugs in here once
                // path playback exists. Until then the camera is surfaced as
                // Unavailable rather than silently holding a wrong orientation.
                return TargetResolution.Unavailable("path direction targets require path playback (Phase 2)");
            }

            return TargetResolution.Unavailable("unknown camera target kind");
        }

        /// <summary>
        /// Composes the camera orientation for this evaluation: the look-at
        /// rotation toward the resolved target (or the anchor frame
        /// orientation for fixed-orientation targets), composed with the
        /// manual rotation and the definition's roll around the camera forward
        /// axis. A degenerate camera-to-target direction (target on the camera)
        /// falls back to the anchor frame orientation instead of throwing.
        /// </summary>
        /// <param name="cameraWorldPosition">World position of the camera this evaluation.</param>
        /// <param name="anchorFrameOrientation">Canonical orientation of the
        /// anchor frame: the fixed orientation for no-target cameras and the
        /// fall-back for degenerate look directions.</param>
        /// <param name="manualRotation">Manual orientation offset; identity in
        /// P1 (orbit input arrives with the library UI).</param>
        /// <param name="resolution">The target resolution of this evaluation.</param>
        public Quaternion ComputeOrientation(Vector3d cameraWorldPosition, Quaternion anchorFrameOrientation, Quaternion manualRotation, TargetResolution resolution)
        {
            Quaternion baseOrientation;
            if (resolution.HasTargetPosition)
            {
                Vector3d toTarget = resolution.TargetPosition - cameraWorldPosition;
                if (toTarget.sqrMagnitude < DegenerateDirectionSqr)
                {
                    baseOrientation = anchorFrameOrientation;
                }
                else
                {
                    Vector3 direction = new Vector3((float)toTarget.x, (float)toTarget.y, (float)toTarget.z).normalized;
                    Vector3 upHint = anchorFrameOrientation * Vector3.up;
                    baseOrientation = Quaternion.LookRotation(direction, upHint);
                }
            }
            else
            {
                baseOrientation = anchorFrameOrientation;
            }

            Quaternion withManual = baseOrientation * manualRotation;
            return withManual * Quaternion.AngleAxis((float)_definition.RollDegrees, Vector3.forward);
        }

        /// <summary>
        /// Applies the pivot mode to the camera position: Camera pivot leaves
        /// the hold position untouched (the manual rotation applies in place
        /// in <see cref="ComputeOrientation"/>); Target pivot rotates the
        /// camera-to-target offset vector about the target by the manual
        /// rotation, so the camera orbits its subject. With an identity manual
        /// rotation — the only input P1 can produce — both pivots return the
        /// hold position unchanged; the math exists now so P3 orbit input
        /// needs no reshaping of this class.
        /// </summary>
        /// <param name="cameraWorldPosition">Hold-composed camera position.</param>
        /// <param name="resolution">The target resolution of this evaluation.</param>
        /// <param name="manualRotation">Manual orientation offset; identity in P1.</param>
        /// <param name="pivotMode">The definition's pivot mode.</param>
        public Vector3d OrbitPositionAboutTarget(Vector3d cameraWorldPosition, TargetResolution resolution, Quaternion manualRotation, CameraPivotMode pivotMode)
        {
            if (pivotMode != CameraPivotMode.Target) return cameraWorldPosition;
            if (!resolution.HasTargetPosition) return cameraWorldPosition;
            if (manualRotation == Quaternion.identity) return cameraWorldPosition;

            Vector3d offset = cameraWorldPosition - resolution.TargetPosition;
            Vector3 rotated = manualRotation * new Vector3((float)offset.x, (float)offset.y, (float)offset.z);
            return resolution.TargetPosition + new Vector3d(rotated.x, rotated.y, rotated.z);
        }

        private TargetResolution ResolvePartTarget(Vessel referenceVessel, uint persistentId)
        {
            if (referenceVessel == null)
                return TargetResolution.Unavailable("target vessel is unavailable");

            Part storedPart = FindPartByPersistentId(referenceVessel, persistentId);
            if (storedPart != null)
            {
                _fallbackWarningRaised = false;
                return TargetResolution.Resolved(storedPart.GetReferenceTransform().position);
            }

            if (referenceVessel.rootPart != null)
            {
                RaiseFallbackWarningOnce(referenceVessel, persistentId, TargetFallbackKind.RootPart);
                return TargetResolution.Resolved(referenceVessel.rootPart.GetReferenceTransform().position);
            }

            RaiseFallbackWarningOnce(referenceVessel, persistentId, TargetFallbackKind.VesselCenterOfMass);
            return TargetResolution.Resolved(referenceVessel.CoMD);
        }

        private void RaiseFallbackWarningOnce(Vessel vessel, uint requestedPartPersistentId, TargetFallbackKind fallback)
        {
            if (_fallbackWarningRaised) return;
            _fallbackWarningRaised = true;

            EventHandler<TargetFallbackEventArgs> handler = TargetFallbackEngaged;
            if (handler != null)
                handler(this, new TargetFallbackEventArgs(vessel, requestedPartPersistentId, fallback));
        }

        private static Part FindPartByPersistentId(Vessel vessel, uint persistentId)
        {
            List<Part> parts = vessel.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part == null) continue;
                if (part.persistentId == persistentId) return part;
            }
            return null;
        }
    }
}
