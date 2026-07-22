using UnityEngine;

namespace RumOverboard.Gameplay.Ocean
{
    public class OceanSurfaceRenderer : MonoBehaviour
    {
        private const int MaxShaderWaves = 12;

        [SerializeField] private OceanWaveField waveField;
        [SerializeField] private Transform followTarget;
        [SerializeField] private Camera followCamera;
        [SerializeField] private Material oceanMaterial;
        [SerializeField] private bool regenerateOnQualityChange = true;

        private struct LodData
        {
            public string name;
            public float innerSize;
            public float outerSize;
            public int resolution;
            public int updateModulo;

            public Mesh mesh;
            public Transform transform;
            public MeshFilter filter;
            public MeshRenderer renderer;

            public Vector3[] baseVertices;
            public Vector3[] deformedVertices;
            public Vector3[] deformedNormals;
        }

        private readonly LodData[] _lods = new LodData[3];
        private readonly int _propOceanTime = Shader.PropertyToID("_OceanTime");
        private readonly int _propFoamIntensity = Shader.PropertyToID("_FoamIntensity");
        private readonly int _propWaveCount = Shader.PropertyToID("_WaveCount");
        private readonly int _propWaveDirAmp = Shader.PropertyToID("_WaveDirAmp");
        private readonly int _propWaveParams = Shader.PropertyToID("_WaveParams");
        private readonly int _propSeaLevel = Shader.PropertyToID("_SeaLevel");
        private readonly int _propShallowDepthMax = Shader.PropertyToID("_ShallowDepthMax");
        private readonly int _propShallowColorBoost = Shader.PropertyToID("_ShallowColorBoost");
        private readonly int _propCrestFoamThreshold = Shader.PropertyToID("_CrestFoamThreshold");
        private MaterialPropertyBlock _props;

        private Vector4[] _waveDirAmpBuffer;
        private Vector4[] _waveParamsBuffer;

        private OceanQualityPreset _lastQuality;
        private bool _warnedMissingQuality;

        private void Awake()
        {
            EnsureRuntimeBuffers();

            if (waveField == null)
                waveField = FindFirstObjectByType<OceanWaveField>();
            if (followCamera == null)
                followCamera = Camera.main;
            if (followTarget == null && followCamera != null)
                followTarget = followCamera.transform;

            BuildMeshes();
        }

        private void OnEnable()
        {
            EnsureRuntimeBuffers();

            if (_lods[0].mesh == null)
                BuildMeshes();
        }

        private void LateUpdate()
        {
            EnsureRuntimeBuffers();

            if (waveField == null)
                return;

            if (regenerateOnQualityChange && waveField.Quality != _lastQuality)
                BuildMeshes();

            FollowReferenceTarget();
            UpdateVisualSurface(Time.frameCount);
            UpdateMaterialProperties();
        }

        private void BuildMeshes()
        {
            if (waveField == null)
                return;

            CleanupMeshes();

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

            SetupLod(0, "Near", 0f, q.nearSize, q.nearResolution, 1);
            SetupLod(1, "Mid", q.nearSize * 0.92f, q.midSize, q.midResolution, 2);
            SetupLod(2, "Far", q.midSize * 0.94f, q.farSize, q.farResolution, 4);
        }

        private void SetupLod(int index, string lodName, float innerSize, float outerSize, int resolution, int updateModulo)
        {
            var go = new GameObject("OceanLOD_" + lodName);
            go.transform.SetParent(transform, false);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            if (oceanMaterial != null)
                renderer.sharedMaterial = oceanMaterial;

            Mesh mesh = index == 0
                ? GeneratePlaneMesh(resolution, outerSize)
                : GenerateRingMesh(resolution, innerSize, outerSize);

            filter.sharedMesh = mesh;

            LodData lod = new LodData
            {
                name = lodName,
                innerSize = innerSize,
                outerSize = outerSize,
                resolution = resolution,
                updateModulo = updateModulo,
                mesh = mesh,
                transform = go.transform,
                filter = filter,
                renderer = renderer,
                baseVertices = mesh.vertices,
                deformedVertices = (Vector3[])mesh.vertices.Clone(),
                deformedNormals = new Vector3[mesh.vertexCount],
            };

            _lods[index] = lod;
        }

        private void FollowReferenceTarget()
        {
            Transform target = followTarget;
            if (target == null && followCamera != null)
                target = followCamera.transform;
            if (target == null)
                return;

            OceanQualityPreset q = waveField != null ? waveField.Quality : null;
            float snap = q != null ? Mathf.Max(0.5f, q.nearSize / Mathf.Max(8f, q.nearResolution)) : 1f;

            float x = Mathf.Round(target.position.x / snap) * snap;
            float z = Mathf.Round(target.position.z / snap) * snap;
            transform.position = new Vector3(x, 0f, z);
        }

        private void UpdateVisualSurface(int frame)
        {
            float time = waveField.OceanTimeNow;

            for (int lodIndex = 0; lodIndex < _lods.Length; lodIndex++)
            {
                LodData lod = _lods[lodIndex];
                if (lod.mesh == null)
                    continue;

                if (lod.updateModulo > 1 && frame % lod.updateModulo != 0)
                    continue;

                for (int i = 0; i < lod.baseVertices.Length; i++)
                {
                    Vector3 baseLocal = lod.baseVertices[i];
                    Vector3 worldBase = lod.transform.TransformPoint(baseLocal.x, 0f, baseLocal.z);

                    waveField.SampleVisualSurface(worldBase, time, out Vector3 disp, out Vector3 normal);

                    lod.deformedVertices[i] = new Vector3(baseLocal.x + disp.x, disp.y, baseLocal.z + disp.z);
                    lod.deformedNormals[i] = normal;
                }

                lod.mesh.vertices = lod.deformedVertices;
                lod.mesh.normals = lod.deformedNormals;
                lod.mesh.RecalculateBounds();

                _lods[lodIndex] = lod;
            }
        }

        private void UpdateMaterialProperties()
        {
            if (oceanMaterial == null)
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
            _props.SetVectorArray(_propWaveDirAmp, _waveDirAmpBuffer);
            _props.SetVectorArray(_propWaveParams, _waveParamsBuffer);

            for (int i = 0; i < _lods.Length; i++)
            {
                if (_lods[i].renderer != null)
                    _lods[i].renderer.SetPropertyBlock(_props);
            }
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

            if (profile == null || profile.waves == null)
                return 0;

            int count = 0;
            for (int i = 0; i < profile.waves.Length && count < MaxShaderWaves; i++)
            {
                var wave = profile.waves[i];
                if (!wave.IsValid || wave.visualWeight <= 0.0001f)
                    continue;

                Vector2 dir = wave.DirectionNormalized;
                float k = (2f * Mathf.PI / Mathf.Max(0.01f, wave.wavelength)) * Mathf.Max(0.001f, wave.frequency);

                _waveDirAmpBuffer[count] = new Vector4(dir.x, dir.y, wave.amplitude, wave.steepness);
                _waveParamsBuffer[count] = new Vector4(k, wave.speed, wave.phase, wave.visualWeight);
                count++;
            }

            return count;
        }

        private float ResolveShallowDepthMax()
        {
            if (waveField == null || waveField.Config == null || waveField.Config.depthProfile == null)
                return 5f;

            return Mathf.Max(0.5f, waveField.Config.depthProfile.shallowDepth);
        }

        private static Mesh GeneratePlaneMesh(int resolution, float size)
        {
            int vertsPerAxis = resolution + 1;
            int vertCount = vertsPerAxis * vertsPerAxis;
            int triCount = resolution * resolution * 6;

            var vertices = new Vector3[vertCount];
            var uv = new Vector2[vertCount];
            var triangles = new int[triCount];

            float half = size * 0.5f;
            float step = size / resolution;
            int v = 0;

            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    vertices[v] = new Vector3(-half + x * step, 0f, -half + z * step);
                    uv[v] = new Vector2(x / (float)resolution, z / (float)resolution);
                    v++;
                }
            }

            int t = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i0 = z * vertsPerAxis + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + vertsPerAxis;
                    int i3 = i2 + 1;

                    triangles[t++] = i0;
                    triangles[t++] = i2;
                    triangles[t++] = i1;
                    triangles[t++] = i1;
                    triangles[t++] = i2;
                    triangles[t++] = i3;
                }
            }

            var mesh = new Mesh { name = "OceanPlane" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh GenerateRingMesh(int resolution, float innerSize, float outerSize)
        {
            int vertsPerAxis = resolution + 1;
            int vertCount = vertsPerAxis * vertsPerAxis;

            var vertices = new Vector3[vertCount];
            var uv = new Vector2[vertCount];
            int[] triangles = new int[resolution * resolution * 6];

            float half = outerSize * 0.5f;
            float innerHalf = innerSize * 0.5f;
            float step = outerSize / resolution;
            int v = 0;

            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    vertices[v] = new Vector3(-half + x * step, 0f, -half + z * step);
                    uv[v] = new Vector2(x / (float)resolution, z / (float)resolution);
                    v++;
                }
            }

            int t = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i0 = z * vertsPerAxis + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + vertsPerAxis;
                    int i3 = i2 + 1;

                    Vector3 c0 = vertices[i0];
                    Vector3 c1 = vertices[i1];
                    Vector3 c2 = vertices[i2];
                    Vector3 c3 = vertices[i3];

                    bool inside0 = Mathf.Abs(c0.x) < innerHalf && Mathf.Abs(c0.z) < innerHalf;
                    bool inside1 = Mathf.Abs(c1.x) < innerHalf && Mathf.Abs(c1.z) < innerHalf;
                    bool inside2 = Mathf.Abs(c2.x) < innerHalf && Mathf.Abs(c2.z) < innerHalf;
                    bool inside3 = Mathf.Abs(c3.x) < innerHalf && Mathf.Abs(c3.z) < innerHalf;

                    if (inside0 && inside1 && inside2 && inside3)
                        continue;

                    triangles[t++] = i0;
                    triangles[t++] = i2;
                    triangles[t++] = i1;
                    triangles[t++] = i1;
                    triangles[t++] = i2;
                    triangles[t++] = i3;
                }
            }

            if (t < triangles.Length)
            {
                int[] compact = new int[t];
                System.Array.Copy(triangles, compact, t);
                triangles = compact;
            }

            var mesh = new Mesh { name = "OceanRing" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void CleanupMeshes()
        {
            for (int i = 0; i < _lods.Length; i++)
            {
                if (_lods[i].mesh != null)
                {
                    if (Application.isPlaying)
                        Destroy(_lods[i].mesh);
                    else
                        DestroyImmediate(_lods[i].mesh);
                }

                if (_lods[i].transform != null)
                {
                    if (Application.isPlaying)
                        Destroy(_lods[i].transform.gameObject);
                    else
                        DestroyImmediate(_lods[i].transform.gameObject);
                }
            }
        }

        private void OnDisable()
        {
            CleanupMeshes();
        }
    }
}

