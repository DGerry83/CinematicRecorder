using CinematicRecorder.Capture;
using CinematicRecorder.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Owns the fade-to-black overlay that is burned into the captured frame (D-U9)
    /// and doubles as the on-screen preview. A single unlit transparent quad, rendered
    /// by whichever camera <see cref="CaptureCameraResolver"/> resolves for the current
    /// mode, is filled with black at <see cref="CameraTransitionCoordinator"/>'s fade
    /// alpha and parked just beyond the camera's near clip plane so it fills the view.
    /// Every capture path samples what the capture camera rendered (the
    /// AfterImageEffects blits in OfflineCaptureController) or, with Capture UI on,
    /// photographs the screen that same camera rendered, so one mechanism covers the
    /// standard, zero-copy, TAB, and screenshot paths — and the player's screen shows
    /// the same quad outside recording. Owns the single
    /// <see cref="CameraTransitionCoordinator"/> instance going forward; chunk C8's
    /// camera panel consumes it through <see cref="Coordinator"/>.
    /// </summary>
    public sealed class FadeOverlayController
    {
        #region Constants & Static State
        // Runtime shader chain (D-U9 contract): built-in unlit-transparent shaders most
        // likely to survive in KSP's player build. KSP's stripped shader set cannot be
        // verified statically; resolution is lazy (first fade) and failures log once.
        private static readonly string[] FadeShaderNames =
        {
            "Unlit/Transparent",
            "Sprites/Default",
            "Legacy Shaders/Transparent/Diffuse"
        };

        private const float NearClipOffset = 0.01f;
        private const float CoverageMargin = 1.01f;
        #endregion

        #region Fields & State
        /// <summary>
        /// The single fade state machine. C8's camera panel drives BeginTransition and
        /// the fade toggle/duration through this instance.
        /// </summary>
        public CameraTransitionCoordinator Coordinator { get; } = new CameraTransitionCoordinator();

        private GameObject _quad;
        private MeshRenderer _quadRenderer;
        private Mesh _mesh;
        private Material _material;
        private Shader _shader;
        private bool _shaderResolved;
        private Camera _camera;
        #endregion

        #region Public API
        /// <summary>
        /// Per-rendered-frame driver, forwarded from CinematicUiHost.LateUpdate. Advances
        /// the fade clock once per rendered frame (deterministic while recording,
        /// real-time otherwise — the pre-C5 LateUpdate cadence, not per physics step)
        /// and syncs the quad to the resolved camera, the live FOV, and the fade alpha.
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

            if (!Coordinator.IsFading)
            {
                if (_quad != null && _quad.activeSelf)
                {
                    _quad.SetActive(false);
                    _camera = null; // re-validate the parent on the next fade
                }
                return;
            }

            EnsureQuad();
            if (_quad == null)
                return; // no runtime shader available; failure already logged once

            Camera resolved = CaptureCameraResolver.ResolveForCurrentMode();
            if (resolved == null)
            {
                if (_quad.activeSelf)
                    _quad.SetActive(false);
                return;
            }

            if (_quad.activeSelf == false)
                _quad.SetActive(true);

            if (resolved != _camera)
            {
                // Camera swap (mid-fade switches land at full black, so the re-aim is
                // invisible). Parent to the new camera and adopt a layer it already
                // renders — no camera mutation, so there is no mask to restore.
                _camera = resolved;
                _quad.layer = LowestRenderedLayer(resolved.cullingMask);
                _quad.transform.SetParent(resolved.transform, false);
            }

            float distance = resolved.nearClipPlane + NearClipOffset;
            float halfHeight = Mathf.Tan(resolved.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
            float halfWidth = halfHeight * resolved.aspect;

            Transform quadTransform = _quad.transform;
            quadTransform.localPosition = new Vector3(0f, 0f, distance);
            quadTransform.localScale = new Vector3(
                halfWidth * 2f * CoverageMargin,
                halfHeight * 2f * CoverageMargin,
                1f);

            _material.color = new Color(0f, 0f, 0f, Coordinator.FadeAlpha);
        }

        /// <summary>
        /// Destroys the quad, its mesh, and its material. Called from the host's
        /// OnDestroy; safe to call when nothing was created.
        /// </summary>
        internal void Shutdown()
        {
            if (_quad != null)
            {
                UnityEngine.Object.Destroy(_quad);
                _quad = null;
            }
            _quadRenderer = null;
            _camera = null;

            if (_material != null)
            {
                UnityEngine.Object.Destroy(_material);
                _material = null;
            }
            if (_mesh != null)
            {
                UnityEngine.Object.Destroy(_mesh);
                _mesh = null;
            }
        }
        #endregion

        #region Private Implementation
        private void EnsureQuad()
        {
            if (_quad != null)
                return;

            if (!TryResolveShader())
                return;

            // Explicit one-unit mesh facing -Z so the camera at the origin (looking
            // down +Z) always sees the front face; no reliance on a primitive's
            // orientation convention. Created once, cached — no per-frame allocation.
            _mesh = new Mesh { name = "CinematicFadeQuadMesh" };
            _mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f)
            };
            _mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            _mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            _mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 }; // clockwise from -Z: normals face the camera
            _mesh.RecalculateBounds();

            _material = new Material(_shader) { name = "CinematicFadeMaterial" };

            _quad = new GameObject("CinematicFadeQuad");
            MeshFilter filter = _quad.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;
            _quadRenderer = _quad.AddComponent<MeshRenderer>();
            _quadRenderer.sharedMaterial = _material;
            _quadRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _quadRenderer.receiveShadows = false;
            _quadRenderer.lightProbeUsage = LightProbeUsage.Off;
            _quadRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            _camera = null; // force the parent/layer sync on the next Tick
        }

        private bool TryResolveShader()
        {
            if (_shaderResolved)
                return _shader != null;

            _shaderResolved = true;
            for (int i = 0; i < FadeShaderNames.Length; i++)
            {
                Shader candidate = Shader.Find(FadeShaderNames[i]);
                if (candidate != null)
                {
                    _shader = candidate;
                    return true;
                }
            }

            Debug.LogError("[CinematicRecorder] Fade overlay: no runtime unlit-transparent shader found " +
                "(tried Unlit/Transparent, Sprites/Default, Legacy Shaders/Transparent/Diffuse); " +
                "camera-switch fades will not render into footage.");
            return false;
        }

        private static int LowestRenderedLayer(int cullingMask)
        {
            // The first layer bit the camera already renders — robust against the
            // restricted culling masks of KSP cameras (HullCam in particular).
            int layer = 0;
            while (layer < 32 && (cullingMask & (1 << layer)) == 0)
            {
                layer++;
            }
            return layer < 32 ? layer : 0;
        }
        #endregion
    }
}
