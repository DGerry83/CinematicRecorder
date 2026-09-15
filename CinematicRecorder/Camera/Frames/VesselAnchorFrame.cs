using System;
using System.Collections.Generic;
using CinematicRecorder.Camera.Model;
using UnityEngine;

namespace CinematicRecorder.Camera.Frames
{
    /// <summary>
    /// Outcome of resolving a vessel anchor for the current step: which part
    /// (if any) the anchor resolved to, or that the anchor is unresolvable.
    /// </summary>
    public enum VesselAnchorResolution
    {
        /// <summary>The anchor does not currently resolve to a world transform.</summary>
        Unresolved,

        /// <summary>The stored anchor part persistent id resolved to a part.</summary>
        StoredPart,

        /// <summary>No part was stored (id 0); the vessel's active control point is the anchor.</summary>
        ActiveControlPoint,

        /// <summary>The stored part could not be resolved; fell back to the vessel's root part.</summary>
        RootPart,

        /// <summary>The anchor fell back to the vessel's center of mass.</summary>
        VesselCenterOfMass
    }

    /// <summary>
    /// Arguments for the one-time fallback warning of
    /// <see cref="VesselAnchorFrame"/>: which fallback the anchor engaged
    /// after the requested anchor part could not be resolved. Carries no UI
    /// text (strings arrive in P3); consumers map the reason to a message.
    /// </summary>
    public sealed class VesselAnchorFallbackEventArgs : EventArgs
    {
        /// <summary>The vessel the anchor belongs to.</summary>
        public Vessel Vessel { get; }

        /// <summary>The stored anchor part persistent id that could not be resolved (0 = none stored).</summary>
        public uint RequestedPartPersistentId { get; }

        /// <summary>The fallback the anchor engaged: root part or vessel center of mass.</summary>
        public VesselAnchorResolution Fallback { get; }

        internal VesselAnchorFallbackEventArgs(Vessel vessel, uint requestedPartPersistentId, VesselAnchorResolution fallback)
        {
            Vessel = vessel;
            RequestedPartPersistentId = requestedPartPersistentId;
            Fallback = fallback;
        }
    }

    /// <summary>
    /// Vessel anchor frame for a camera (parent spec §5.3 "Vessel anchor
    /// frame"; build-program chunk P1-C2). Resolves the anchor part from a
    /// vessel by stored persistent id on each access: stored part, or when no
    /// part is stored (id 0) the vessel's active control point; when the
    /// resolved part is unavailable, the fallback chain of
    /// <see cref="AnchorFallbackPolicy"/> (root part → vessel center of mass),
    /// engaging a fallback raises <see cref="FallbackEngaged"/> at most once
    /// per engagement. Exposes the anchor's world transform (read) and the
    /// reference frame's velocity per <see cref="ReferenceFrameMode"/>, which
    /// the velocity model of P1-C4 consumes.
    ///
    /// Surface/Orbit behavior: the reference velocity is the vessel's
    /// surface-frame velocity (relative to the rotating body) in Surface mode
    /// and its inertial orbit-frame velocity in Orbit mode; the anchor's world
    /// rotation is exposed as resolved in both modes. Horizon-lock versus
    /// star-lock orientation holding is hold-model behavior (P1-C4) built on
    /// this velocity, not a transform stored here (build-program invariant 2:
    /// no world-frame pose persists across steps — every world-space value is
    /// re-derived per access). Read-only KSP access: writes nothing to the
    /// scene.
    /// </summary>
    public sealed class VesselAnchorFrame
    {
        private readonly Vessel _vessel;
        private readonly uint _anchorPartPersistentId;
        private readonly AnchorFallbackPolicy _fallbackPolicy;
        private readonly ReferenceFrameMode _frameMode;
        private bool _fallbackWarningRaised;

        /// <summary>
        /// Raised at most once per fallback engagement when the requested
        /// anchor part cannot be resolved and a fallback (root part or vessel
        /// center of mass) takes over. Fired while the anchor is being
        /// resolved (from any of the resolution-triggering members). UI
        /// strings are wired in P3; the event carries only the reason.
        /// </summary>
        public event EventHandler<VesselAnchorFallbackEventArgs> FallbackEngaged;

        /// <summary>
        /// Creates the vessel anchor frame. A null vessel is permitted: the
        /// frame then reports <see cref="VesselAnchorResolution.Unresolved"/>
        /// so the camera controller can surface the camera as Unavailable
        /// (parent spec §5.4) instead of throwing.
        /// </summary>
        /// <param name="vessel">The vessel the camera is anchored to, or null.</param>
        /// <param name="anchorPartPersistentId">Persistent id of the anchor part, or 0 for the runtime default (the vessel's active control point).</param>
        /// <param name="fallbackPolicy">Fallback used when the requested anchor part cannot be resolved.</param>
        /// <param name="frameMode">Reference frame mode selecting the reference velocity: surface (rotating) or orbit (inertial).</param>
        public VesselAnchorFrame(Vessel vessel, uint anchorPartPersistentId, AnchorFallbackPolicy fallbackPolicy, ReferenceFrameMode frameMode)
        {
            _vessel = vessel;
            _anchorPartPersistentId = anchorPartPersistentId;
            _fallbackPolicy = fallbackPolicy;
            _frameMode = frameMode;
        }

        /// <summary>The vessel the camera is anchored to (may be null).</summary>
        public Vessel Vessel { get { return _vessel; } }

        /// <summary>Persistent id of the anchor part, or 0 for the runtime default.</summary>
        public uint AnchorPartPersistentId { get { return _anchorPartPersistentId; } }

        /// <summary>Fallback used when the requested anchor part cannot be resolved.</summary>
        public AnchorFallbackPolicy FallbackPolicy { get { return _fallbackPolicy; } }

        /// <summary>Reference frame mode selecting the reference velocity: surface (rotating) or orbit (inertial).</summary>
        public ReferenceFrameMode FrameMode { get { return _frameMode; } }

        /// <summary>
        /// World position of the resolved anchor point, re-derived on each
        /// access; zero when the anchor is unresolvable. Resolving may raise
        /// <see cref="FallbackEngaged"/> once per fallback engagement.
        /// </summary>
        public Vector3d Position
        {
            get
            {
                Vector3d position;
                Quaternion rotation;
                ResolveAnchor(out position, out rotation);
                return position;
            }
        }

        /// <summary>
        /// World rotation of the resolved anchor, re-derived on each access;
        /// identity when the anchor is unresolvable. Resolving may raise
        /// <see cref="FallbackEngaged"/> once per fallback engagement.
        /// </summary>
        public Quaternion Rotation
        {
            get
            {
                Vector3d position;
                Quaternion rotation;
                ResolveAnchor(out position, out rotation);
                return rotation;
            }
        }

        /// <summary>
        /// Whether the anchor currently resolves to a world transform. False
        /// when the vessel reference is gone; the camera controller surfaces
        /// the camera as Unavailable in that case (parent spec §5.4).
        /// Resolving may raise <see cref="FallbackEngaged"/> once per fallback
        /// engagement.
        /// </summary>
        public bool IsResolved
        {
            get
            {
                Vector3d position;
                Quaternion rotation;
                return ResolveAnchor(out position, out rotation) != VesselAnchorResolution.Unresolved;
            }
        }

        /// <summary>
        /// The outcome of the most recent anchor resolution: which part, if
        /// any, the anchor resolved to, and whether a fallback is in use.
        /// </summary>
        public VesselAnchorResolution CurrentResolution
        {
            get
            {
                Vector3d position;
                Quaternion rotation;
                return ResolveAnchor(out position, out rotation);
            }
        }

        /// <summary>
        /// Reference frame velocity of the anchor per <see cref="FrameMode"/>:
        /// the vessel's surface-frame velocity (relative to the rotating body)
        /// in Surface mode, or its inertial orbit-frame velocity in Orbit
        /// mode. Zero when the vessel reference is gone.
        /// </summary>
        public Vector3d Velocity
        {
            get
            {
                if (_vessel == null) return Vector3d.zero;
                return _frameMode == ReferenceFrameMode.Orbit ? _vessel.obt_velocity : _vessel.srf_velocity;
            }
        }

        private VesselAnchorResolution ResolveAnchor(out Vector3d position, out Quaternion rotation)
        {
            position = Vector3d.zero;
            rotation = Quaternion.identity;

            if (_vessel == null) return VesselAnchorResolution.Unresolved;

            VesselAnchorResolution resolution;
            if (_anchorPartPersistentId != 0)
            {
                Part storedPart = FindPartByPersistentId(_vessel, _anchorPartPersistentId);
                if (storedPart != null)
                {
                    SetPoseFromTransform(storedPart.GetReferenceTransform(), out position, out rotation);
                    resolution = VesselAnchorResolution.StoredPart;
                }
                else
                {
                    resolution = ResolveFallback(out position, out rotation);
                }
            }
            else
            {
                Part controlPointPart = _vessel.GetReferenceTransformPart();
                if (controlPointPart != null)
                {
                    SetPoseFromTransform(controlPointPart.GetReferenceTransform(), out position, out rotation);
                    resolution = VesselAnchorResolution.ActiveControlPoint;
                }
                else
                {
                    resolution = ResolveFallback(out position, out rotation);
                }
            }

            if (resolution != VesselAnchorResolution.RootPart && resolution != VesselAnchorResolution.VesselCenterOfMass)
                _fallbackWarningRaised = false;

            return resolution;
        }

        private VesselAnchorResolution ResolveFallback(out Vector3d position, out Quaternion rotation)
        {
            if (_fallbackPolicy == AnchorFallbackPolicy.RootPart && _vessel.rootPart != null)
            {
                SetPoseFromTransform(_vessel.rootPart.GetReferenceTransform(), out position, out rotation);
                RaiseFallbackWarningOnce(VesselAnchorResolution.RootPart);
                return VesselAnchorResolution.RootPart;
            }

            position = _vessel.CoMD;
            rotation = _vessel.ReferenceTransform.rotation;
            RaiseFallbackWarningOnce(VesselAnchorResolution.VesselCenterOfMass);
            return VesselAnchorResolution.VesselCenterOfMass;
        }

        private void RaiseFallbackWarningOnce(VesselAnchorResolution fallback)
        {
            if (_fallbackWarningRaised) return;
            _fallbackWarningRaised = true;
            EventHandler<VesselAnchorFallbackEventArgs> handler = FallbackEngaged;
            if (handler != null)
                handler(this, new VesselAnchorFallbackEventArgs(_vessel, _anchorPartPersistentId, fallback));
        }

        private static void SetPoseFromTransform(Transform transform, out Vector3d position, out Quaternion rotation)
        {
            position = transform.position;
            rotation = transform.rotation;
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
