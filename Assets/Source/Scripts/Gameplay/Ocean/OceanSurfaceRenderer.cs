using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    /// <summary>
    /// Draws the ocean as ONE radial mesh centred on the ship/camera and displaced entirely on the
    /// GPU (see the vertex stage of OceanStylizedURP). The mesh is built once: vertices are packed
    /// densely near the centre and sparsely toward the rim, so detail is highest around the ship
    /// (dynamic LOD) and the far field is cheap — and it fades to a flat, single-colour surface via
    /// the shader's distance falloff. Each frame we only reposition the mesh to follow the ship;
    /// the CPU never touches vertices, which is what makes it cheap (the old per-vertex CPU
    /// displacement was the framerate sink).
    ///
    /// Physics still samples waves on the CPU (OceanWaveField.Sample) — but only at the handful of
    /// buoyancy points, which is negligible.
    /// </summary>
    public class OceanSurfaceRenderer : MonoBehaviour
    {
        private const int MaxShaderWaves = 12;

        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private Transform followTarget;
        [SerializeField] private Camera followCamera;
        [SerializeField] private Material oceanMaterial;
        [SerializeField] private bool regenerateOnQualityChange = true;

        [Header("Radial mesh (dynamic LOD around the ship)")]
        [Tooltip("Radial subdivisions (centre→rim). More = finer LOD. 0 = derive from quality preset.")]
        [SerializeField] private int radialRings = 0;
        [Tooltip("Angular subdivisions. 0 = radialRings * 2.")]
        [SerializeField] private int radialSectors = 0;
        [Tooltip(">1 packs vertices toward the centre (near the ship). 2 = quadratic.")]
        [SerializeField] private float radialExponent = 2f;
        [Tooltip("Mesh radius (metres). 0 = use the quality preset's far size.")]
        [SerializeField] private float meshRadius = 0f;

        [Header("Detail falloff (big ocean, calm distance)")]
        [Tooltip("Within this distance from the camera the waves are full height.")]
        [SerializeField] private float detailFadeStart = 110f;
        [Tooltip("Beyond this the surface is flat and single-colour; between the two it eases off.")]
        [SerializeField] private float detailFadeEnd = 230f;

        private readonly int _propOceanTime = Shader.PropertyToID("_OceanTime");
        private readonly int _propFoamIntensity = Shader.PropertyToID("_FoamIntensity");
        private readonly int _propWaveCount = Shader.PropertyToID("_WaveCount");
        private readonly int _propWaveDirAmp = Shader.PropertyToID("_WaveDirAmp");
        private readonly int _propWaveParams = Shader.PropertyToID("_WaveParams");
        private readonly int _propSeaLevel = Shader.PropertyToID("_SeaLevel");
        private readonly int _propShallowDepthMax = Shader.PropertyToID("_ShallowDepthMax");
        private readonly int _propShallowColorBoost = Shader.PropertyToID("_ShallowColorBoost");
        private readonly int _propCrestFoamThreshold = Shader.PropertyToID("_CrestFoamThreshold");
        private readonly int _propDetailFadeStart = Shader.PropertyToID("_DetailFadeStart");
        private readonly int _propDetailFadeEnd = Shader.PropertyToID("_DetailFadeEnd");
        private MaterialPropertyBlock _props;

        private Vector4[] _waveDirAmpBuffer;
        private Vector4[] _waveParamsBuffer;

        private GameObject _surfaceGo;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private float _builtRadius;
        private OceanQualityPreset _lastQuality;
        private bool _warnedMissingQuality;

        private void Awake()
        {
            EnsureRuntimeBuffers();

            if (waveField == null)
                waveField = FindAnyObjectByType<OceanWaveField>();
            if (followCamera == null)
                followCamera = Camera.main;
            if (followTarget == null && followCamera != null)
                followTarget = followCamera.transform;

            BuildMesh();
        }

        private void OnEnable()
        {
            EnsureRuntimeBuffers();
            if (_mesh == null)
                BuildMesh();
        }

        private void LateUpdate()
        {
            EnsureRuntimeBuffers();

            if (waveField == null)
                return;

            if (regenerateOnQualityChange && waveField.Quality != _lastQuality)
                BuildMesh();

            FollowReferenceTarget();
            UpdateMaterialProperties();
        }

        private void BuildMesh()
        {
            if (waveField == null)
                return;

            OceanQualityPreset q = waveField.Quality;
            if (q == null)
            {
                if (!_warnedMissingQuality)
                {
                    _warnedMissingQuality = true;
                    Debug.LogWarning("[OceanSurfaceRenderer] Missing quality preset from OceanWaveField. Skipping mesh build for this frame.");
                }
                return;
            }

            _lastQuality = q;
            _warnedMissingQuality = false;

            int rings = radialRings > 0 ? radialRings : Mathf.Clamp(q.nearResolution, 24, 160);
            int sectors = radialSectors > 0 ? radialSectors : Mathf.Clamp(rings * 2, 48, 400);
            float radius = meshRadius > 0f ? meshRadius : Mathf.Max(64f, q.farSize);
            _builtRadius = radius;

            EnsureSurfaceObject();

            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
            }

            _mesh = GenerateRadialMesh(rings, sectors, radius, Mathf.Max(1f, radialExponent));
            _filter.sharedMesh = _mesh;
        }

        private void EnsureSurfaceObject()
        {
            if (_surfaceGo == null)
            {
                _surfaceGo = new GameObject("OceanSurface");
                _surfaceGo.transform.SetParent(transform, false);
                _filter = _surfaceGo.AddComponent<MeshFilter>();
                _renderer = _surfaceGo.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
            }

            if (oceanMaterial != null)
                _renderer.sharedMaterial = oceanMaterial;
        }

        private void FollowReferenceTarget()
        {
            Transform target = followTarget;
            if (target == null && followCamera != null)
                target = followCamera.transform;
            if (target == null)
                return;

            // No snapping needed: displacement is a world-space function of XZ (GPU), so translating
            // the mesh doesn't make waves swim. Just centre it on the ship at sea level.
            float seaLevel = waveField != null && waveField.Config != null ? waveField.Config.seaLevel : 0f;
            transform.position = new Vector3(target.position.x, seaLevel, target.position.z);
        }

        private void UpdateMaterialProperties()
        {
            if (oceanMaterial == null || _renderer == null)
                return;

            EnsureRuntimeBuffers();

            var profile = waveField.ActiveProfile;
            int waveCount = BuildShaderWaveBuffers(profile);
            float shallowDepth = ResolveShallowDepthMax();

            _props.Clear();
            _props.SetFloat(_propOceanTime, waveField.OceanTimeNow);
            _props.SetFloat(_propFoamIntensity, profile != null ? profile.foamIntensity : 0.35f);
            _props.SetFloat(_propCrestFoamThreshold, profile != null ? profile.crestFoamThreshold : 0.65f);
            _props.SetFloat(_propSeaLevel, waveField.Config != null ? waveField.Config.seaLevel : 0f);
            _props.SetFloat(_propShallowDepthMax, shallowDepth);
            _props.SetFloat(_propShallowColorBoost, profile != null ? profile.shallowColorBoost : 0.5f);
            _props.SetInt(_propWaveCount, waveCount);
            _props.SetFloat(_propDetailFadeStart, Mathf.Max(0f, detailFadeStart));
            _props.SetFloat(_propDetailFadeEnd, Mathf.Max(detailFadeStart + 0.01f, detailFadeEnd));
            _props.SetVectorArray(_propWaveDirAmp, _waveDirAmpBuffer);
            _props.SetVectorArray(_propWaveParams, _waveParamsBuffer);

            _renderer.SetPropertyBlock(_props);
        }

        private void EnsureRuntimeBuffers()
        {
            if (_props == null)
                _props = new MaterialPropertyBlock();

            if (_waveDirAmpBuffer == null || _waveDirAmpBuffer.Length != MaxShaderWaves)
                _waveDirAmpBuffer = new Vector4[MaxShaderWaves];

            if (_waveParamsBuffer == null || _waveParamsBuffer.Length != MaxShaderWaves)
                _waveParamsBuffer = new Vector4[MaxShaderWaves];
        }

        private int BuildShaderWaveBuffers(OceanSeaStateProfile profile)
        {
            for (int i = 0; i < MaxShaderWaves; i++)
            {
                _waveDirAmpBuffer[i] = Vector4.zero;
                _waveParamsBuffer[i] = Vector4.zero;
            }

            if (profile == null)
                return 0;

            // Bake the SAME CPU-side modifiers the physics path uses into the shader uniforms, so the
            // GPU-displaced surface and its normals/foam match the buoyancy surface:
            //   directional jitter, choppiness, runtime amplitude/speed, and the dominant swell.
            float choppiness = Mathf.Clamp(profile.choppiness, 0f, 2f);
            float ampMul = waveField != null ? waveField.RuntimeWaveAmplitudeMultiplier : 1f;
            float speedMul = waveField != null ? waveField.RuntimeWaveSpeedMultiplier : 1f;
            float jitterDeg = profile.directionJitterDegrees;
            uint seed = waveField != null && waveField.Config != null ? waveField.Config.sharedSeed : 1337u;

            int count = 0;

            if (profile.HasSwell)
                PackWave(ref count, profile.BuildSwellWave(), choppiness, ampMul, speedMul, jitterDeg, seed, OceanWaveField.SwellWaveIndex);

            if (profile.waves != null)
            {
                for (int i = 0; i < profile.waves.Length && count < MaxShaderWaves; i++)
                {
                    var wave = profile.waves[i];
                    if (!wave.IsValid || wave.visualWeight <= 0.0001f)
                        continue;

                    PackWave(ref count, wave, choppiness, ampMul, speedMul, jitterDeg, seed, i);
                }
            }

            return count;
        }

        private void PackWave(ref int count, in OceanWaveDefinition wave, float choppiness,
            float ampMul, float speedMul, float jitterDeg, uint seed, int index)
        {
            if (count >= MaxShaderWaves)
                return;

            Vector2 dir = OceanWaveMath.JitteredDirection(wave.DirectionNormalized, jitterDeg, seed, index);
            float k = (2f * Mathf.PI / Mathf.Max(0.01f, wave.wavelength)) * Mathf.Max(0.001f, wave.frequency);
            float steep = Mathf.Clamp01(wave.steepness * choppiness);

            _waveDirAmpBuffer[count] = new Vector4(dir.x, dir.y, wave.amplitude * ampMul, steep);
            _waveParamsBuffer[count] = new Vector4(k, wave.speed * speedMul, wave.phase, wave.visualWeight);
            count++;
        }

        private float ResolveShallowDepthMax()
        {
            if (waveField == null || waveField.Config == null || waveField.Config.depthProfile == null)
                return 5f;

            return Mathf.Max(0.5f, waveField.Config.depthProfile.shallowDepth);
        }

        // A single disk: one centre vertex plus concentric rings whose radius grows as
        // (ring / rings)^exponent, so vertices bunch up near the ship and thin out to the rim.
        private static Mesh GenerateRadialMesh(int rings, int sectors, float maxRadius, float exponent)
        {
            int vertCount = 1 + rings * sectors;
            var vertices = new Vector3[vertCount];
            var uv = new Vector2[vertCount];
            var normals = new Vector3[vertCount];

            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            normals[0] = Vector3.up;

            for (int r = 1; r <= rings; r++)
            {
                float radius = maxRadius * Mathf.Pow(r / (float)rings, exponent);
                for (int s = 0; s < sectors; s++)
                {
                    float angle = (s / (float)sectors) * Mathf.PI * 2f;
                    float cx = Mathf.Cos(angle);
                    float sz = Mathf.Sin(angle);
                    int idx = 1 + (r - 1) * sectors + s;
                    vertices[idx] = new Vector3(cx * radius, 0f, sz * radius);
                    uv[idx] = new Vector2(0.5f + cx * 0.5f * (r / (float)rings), 0.5f + sz * 0.5f * (r / (float)rings));
                    normals[idx] = Vector3.up;
                }
            }

            // Triangles: centre fan + quads between successive rings. Winding chosen so the top
            // face (normal +Y) is front-facing under Cull Back.
            int triCount = sectors + (rings - 1) * sectors * 2;
            var triangles = new int[triCount * 3];
            int t = 0;

            // Centre fan (centre → first ring).
            for (int s = 0; s < sectors; s++)
            {
                int s1 = (s + 1) % sectors;
                triangles[t++] = 0;
                triangles[t++] = RingVert(1, s1, sectors);
                triangles[t++] = RingVert(1, s, sectors);
            }

            // Rings.
            for (int r = 1; r < rings; r++)
            {
                for (int s = 0; s < sectors; s++)
                {
                    int s1 = (s + 1) % sectors;
                    int innerS = RingVert(r, s, sectors);
                    int innerS1 = RingVert(r, s1, sectors);
                    int outerS = RingVert(r + 1, s, sectors);
                    int outerS1 = RingVert(r + 1, s1, sectors);

                    triangles[t++] = innerS;
                    triangles[t++] = innerS1;
                    triangles[t++] = outerS;

                    triangles[t++] = innerS1;
                    triangles[t++] = outerS1;
                    triangles[t++] = outerS;
                }
            }

            var mesh = new Mesh { name = "OceanRadialSurface" };
            mesh.indexFormat = vertCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.triangles = triangles;
            // Big fixed bounds: the GPU displaces vertices, so let it never frustum-cull the disk.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(maxRadius * 2f, 80f, maxRadius * 2f));
            return mesh;
        }

        private static int RingVert(int ring, int sector, int sectors) => 1 + (ring - 1) * sectors + sector;

        private void OnDisable()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) Destroy(_mesh);
                else DestroyImmediate(_mesh);
                _mesh = null;
            }

            if (_surfaceGo != null)
            {
                if (Application.isPlaying) Destroy(_surfaceGo);
                else DestroyImmediate(_surfaceGo);
                _surfaceGo = null;
                _filter = null;
                _renderer = null;
            }
        }
    }
}
