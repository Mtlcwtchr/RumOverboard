using System;
using System.Collections.Generic;
using System.IO;
using RumOverboard.Gameplay.Crew;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace RumOverboard.EditorTools.CharacterArt
{
    /// <summary>
    /// Base sailor model: copies the rigged FBX from the art pipeline (Art/Models/Character, outside Assets)
    /// into the project, configures it as Humanoid and verifies the Avatar and its poses.
    ///   Menu:  RumOverboard ▸ Character ▸ Import Base Model / Run Model Self-Test
    ///   Batch: Unity -batchmode -projectPath . -executeMethod RumOverboard.EditorTools.CharacterArt.CharacterModelImport.RunBatch
    /// Self-test renders rest / zero-muscle reference / test pose shots and an eye-style sheet to
    /// Art/Models/Character/base_v1/unity_*.png.
    /// Face decal: the FBX material "M_Face" is remapped to M_SailorFace (URP Lit, alpha clip, eyes atlas);
    /// the eye drawing is picked by <see cref="SailorEyes"/>.
    /// </summary>
    public static class CharacterModelImport
    {
        public const string SourceFbx = "Art/Models/Character/base_v1/base_rigged.fbx";
        public const string SourceAtlas = "Art/Models/Character/base_v1/eyes_atlas.png";
        public const string AssetPath = "Assets/Source/Art/Character/SailorBase.fbx";
        public const string AtlasPath = "Assets/Source/Art/Character/SailorEyes.png";
        public const string FaceMaterialPath = "Assets/Source/Art/Character/" + SailorEyes.FaceMaterialName + ".mat";
        private const string FbxFaceMaterial = "M_Face";
        private const string ShotsDir = "Art/Models/Character/base_v1";

        private static int _failures;

        [MenuItem("RumOverboard/Character/Import Base Model", priority = 0)]
        public static void ImportMenu()
        {
            Import();
            Run();
        }

        [MenuItem("RumOverboard/Character/Run Model Self-Test", priority = 20)]
        public static void RunMenu() => Run();

        public static void RunBatch()
        {
            Import();
            EditorApplication.Exit(Run() == 0 ? 0 : 1);
        }

        public static void Import()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string src = Path.Combine(projectRoot, SourceFbx);
            if (!File.Exists(src))
            {
                Debug.LogError($"[CharacterModel] source FBX not found: {src}");
                return;
            }

            Directory.CreateDirectory(Path.Combine(projectRoot, Path.GetDirectoryName(AssetPath)));
            Material face = ImportFaceMaterial(projectRoot);
            File.Copy(src, Path.Combine(projectRoot, AssetPath), true);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

            var importer = (ModelImporter)AssetImporter.GetAtPath(AssetPath);
            if (face != null)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), FbxFaceMaterial), face);
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.SaveAndReimport();

            // Bone names already equal HumanBodyBones, but the auto-mapper sends "Chest" elsewhere —
            // map every bone explicitly, keeping the skeleton the auto-setup produced.
            HumanDescription desc = importer.humanDescription;
            var human = new List<HumanBone>();
            foreach (HumanBodyBones b in RigBones)
                human.Add(new HumanBone { humanName = HumanTrait.BoneName[(int)b], boneName = b.ToString(), limit = new HumanLimit { useDefaultValues = true } });
            desc.human = human.ToArray();
            desc.skeleton = EnforceTPose();
            importer.humanDescription = desc;
            importer.SaveAndReimport();
            Debug.Log($"[CharacterModel] imported {AssetPath} as Humanoid ({human.Count} bones mapped explicitly, T-pose enforced)");
        }

        /// <summary>
        /// Eyes atlas (3x2 cells, see <see cref="EyeStyle"/>) + the face decal material: URP Lit, alpha-clipped,
        /// base map tiled to one cell (Dots by default; <see cref="SailorEyes"/> switches the cell per renderer).
        /// </summary>
        private static Material ImportFaceMaterial(string projectRoot)
        {
            string src = Path.Combine(projectRoot, SourceAtlas);
            if (!File.Exists(src))
            {
                Debug.LogError($"[CharacterModel] eyes atlas not found: {src}");
                return null;
            }
            File.Copy(src, Path.Combine(projectRoot, AtlasPath), true);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();

            var mat = AssetDatabase.LoadAssetAtPath<Material>(FaceMaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, FaceMaterialPath);
            }
            Vector4 dots = SailorEyes.CellScaleOffset(EyeStyle.Dots);
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath));
            mat.SetTextureScale("_BaseMap", new Vector2(dots.x, dots.y));
            mat.SetTextureOffset("_BaseMap", new Vector2(dots.z, dots.w));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Smoothness", 0.15f);
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>
        /// The mesh is bound in an A-pose, but muscle space is measured from the avatar's skeleton pose,
        /// which must be a T-pose (same as "Enforce T-Pose" in the Avatar window): arms horizontal,
        /// legs straight down. Only the avatar's skeleton description changes — the mesh stays A-pose.
        /// The description is rebuilt from the freshly imported hierarchy: the one kept in the .meta
        /// still holds the previous FBX's bone positions, and Humanoid would put bones back there.
        /// </summary>
        private static SkeletonBone[] EnforceTPose()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath);
            var go = (GameObject)UnityEngine.Object.Instantiate(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var bones = new Dictionary<string, Transform>();
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
                void Align(string bone, string child, Vector3 dir)
                {
                    Transform t = bones[bone];
                    t.rotation = Quaternion.FromToRotation(bones[child].position - t.position, dir) * t.rotation;
                }

                foreach ((string side, float x) in new[] { ("Left", -1f), ("Right", 1f) })
                {
                    Align(side + "UpperArm", side + "LowerArm", new Vector3(x, 0f, 0f));
                    Align(side + "LowerArm", side + "Hand", new Vector3(x, 0f, 0f));
                    Align(side + "UpperLeg", side + "LowerLeg", Vector3.down);
                    Align(side + "LowerLeg", side + "Foot", Vector3.down);
                }

                var result = new List<SkeletonBone>();
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    result.Add(new SkeletonBone
                    {
                        name = t == go.transform ? model.name : t.name,
                        position = t.localPosition,
                        rotation = t.localRotation,
                        scale = t.localScale,
                    });
                return result.ToArray();
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static readonly HumanBodyBones[] RigBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck,
            HumanBodyBones.Head, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightShoulder,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.LeftToes, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
        };

        public static int Run()
        {
            _failures = 0;
            try { Execute(); }
            catch (Exception e) { _failures++; Debug.LogException(e); }
            if (_failures == 0) Debug.Log("[CharacterModel] ALL PASSED");
            else Debug.LogError($"[CharacterModel] {_failures} FAILED");
            return _failures;
        }

        private static void Execute()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetPath);
            if (!Check("model asset exists", model != null)) return;

            Avatar avatar = null;
            foreach (UnityEngine.Object o in AssetDatabase.LoadAllAssetsAtPath(AssetPath))
                if (o is Avatar a) avatar = a;
            if (!Check("avatar generated", avatar != null)) return;
            Check("avatar is valid", avatar.isValid);
            Check("avatar is human", avatar.isHuman);

            var mapped = new Dictionary<string, string>();
            foreach (HumanBone hb in avatar.humanDescription.human) mapped[hb.humanName] = hb.boneName;
            Check($"all {RigBones.Length} rig bones mapped (got {mapped.Count})", mapped.Count == RigBones.Length);
            foreach (HumanBodyBones b in RigBones)
            {
                string human = HumanTrait.BoneName[(int)b];
                Check($"{human} mapped to bone of the same name",
                    mapped.TryGetValue(human, out string bone) && bone == b.ToString());
            }

            Scene scene = EditorSceneManager.NewPreviewScene();
            try { CheckPoses(scene, model, avatar); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void CheckPoses(Scene scene, GameObject model, Avatar avatar)
        {
            var go = (GameObject)UnityEngine.Object.Instantiate(model);
            SceneManager.MoveGameObjectToScene(go, scene);
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.avatar = avatar;
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);

            // ── rest pose: size, ground, facing ──
            var renderer = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (!Check("skinned mesh present", renderer != null)) return;

            // Outside the player loop skinning isn't refreshed for Camera.Render, so measure and draw
            // a baked copy of the skinned mesh instead of the SkinnedMeshRenderer itself.
            var baked = new Mesh();
            var bakedGo = new GameObject("Baked", typeof(MeshFilter), typeof(MeshRenderer));
            SceneManager.MoveGameObjectToScene(bakedGo, scene);
            bakedGo.GetComponent<MeshFilter>().sharedMesh = baked;
            bakedGo.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            renderer.enabled = false;
            void Bake()
            {
                renderer.BakeMesh(baked, true);
                bakedGo.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                baked.RecalculateBounds();
            }

            Bake();
            Bounds bounds = bakedGo.GetComponent<MeshRenderer>().bounds;
            Near("height ~1.81 m", bounds.size.y, 1.81f, 0.03f);
            Near("feet on the ground", bounds.min.y, 0f, 0.02f);
            Check("faces +Z (toes ahead of the foot)", B(HumanBodyBones.LeftToes).position.z > B(HumanBodyBones.LeftFoot).position.z + 0.03f);
            Check("left side is -X in Unity (character's left on screen right when facing camera)",
                B(HumanBodyBones.LeftHand).position.x < 0f && B(HumanBodyBones.RightHand).position.x > 0f);
            Check($"materials: {renderer.sharedMaterials.Length} slots (skin, shirt, shorts, nose, face)", renderer.sharedMaterials.Length == 5);
            int faceIndex = SailorEyes.FaceMaterialIndex(renderer);
            if (Check("face decal material remapped to " + SailorEyes.FaceMaterialName, faceIndex >= 0))
                Check("face material samples the eyes atlas",
                    renderer.sharedMaterials[faceIndex].GetTexture("_BaseMap") == AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath));

            // SailorEyes writes the atlas cell into the face slot's property block.
            var eyes = go.AddComponent<SailorEyes>();
            eyes.Style = EyeStyle.Crosses;
            var probe = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(probe, Mathf.Max(0, faceIndex));
            Check("SailorEyes sets the Crosses cell", probe.GetVector("_BaseMap_ST") == SailorEyes.CellScaleOffset(EyeStyle.Crosses));

            var cam = MakeCamera(scene);
            Shoot(cam, "unity_warmup"); // first render can miss material variants still compiling
            File.Delete(ShotPath("unity_warmup_front"));
            File.Delete(ShotPath("unity_warmup_three_quarter"));
            Shoot(cam, "unity_rest");
            ShootEyeSheet(cam, bakedGo.GetComponent<MeshRenderer>(), faceIndex, B(HumanBodyBones.Head).position);

            var handler = new HumanPoseHandler(avatar, go.transform);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            float[] rest = (float[])pose.muscles.Clone();
            var restMuscles = new System.Text.StringBuilder("[CharacterModel] rest-pose muscles:");
            for (int i = 0; i < rest.Length; i++)
                if (Mathf.Abs(rest[i]) > 0.05f) restMuscles.Append($" {HumanTrait.MuscleName[i]}={rest[i]:0.00};");
            Debug.Log(restMuscles.ToString());

            // ── how Unity reads the A-pose: straight limbs, arms down, left == right ──
            // (Stretch muscles: +1 = straight, 0 = half-bent — so "all muscles 0" is Unity's crouched
            // reference pose, not a standing one.)
            foreach (string side in new[] { "Left", "Right" })
            {
                Check($"rest: {side} knee straight", Muscle(rest, $"{side} Lower Leg Stretch") > 0.8f);
                Check($"rest: {side} elbow straight", Muscle(rest, $"{side} Forearm Stretch") > 0.8f);
                Check($"rest: {side} arm down (A-pose)", Muscle(rest, $"{side} Arm Down-Up") < -0.2f);
            }
            for (int i = 0; i < rest.Length; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (!n.StartsWith("Left ")) continue;
                int j = Array.IndexOf(HumanTrait.MuscleName, "Right " + n.Substring(5));
                if (j >= 0 && Mathf.Abs(rest[i] - rest[j]) > 0.02f)
                    Check($"rest: '{n}' symmetric ({rest[i]:0.00} vs {rest[j]:0.00})", false);
            }

            // ── round trip: pose → muscles → pose returns the bones where they were ──
            var restPositions = new Dictionary<HumanBodyBones, Vector3>();
            foreach (HumanBodyBones b in RigBones) restPositions[b] = B(b).position;
            handler.SetHumanPose(ref pose);
            float drift = 0f;
            var drifts = new System.Text.StringBuilder("[CharacterModel] round-trip drift (cm):");
            foreach (HumanBodyBones b in RigBones)
            {
                float d = Vector3.Distance(restPositions[b], B(b).position);
                drift = Mathf.Max(drift, d);
                if (d > 0.005f) drifts.Append($" {b}={d * 100f:0.0} {restPositions[b]}→{B(b).position};");
            }
            Debug.Log(drifts.ToString());
            Check($"round trip: bones return within 1 cm (max {drift * 100f:0.0} cm)", drift < 0.01f);

            // ── reference only: Unity's zero-muscle pose, must still be left/right symmetric ──
            for (int i = 0; i < pose.muscles.Length; i++) pose.muscles[i] = 0f;
            handler.SetHumanPose(ref pose);
            CheckMirror("zero muscles");
            Bake();
            Shoot(cam, "unity_zero_muscles");

            // ── test pose from the rest pose: both arms raised, elbows bent (symmetric),
            //    left thigh forward with the knee bent, head turned ──
            Array.Copy(rest, pose.muscles, rest.Length);
            SetMuscle(ref pose, "Left Arm Down-Up", 0.8f);
            SetMuscle(ref pose, "Right Arm Down-Up", 0.8f);
            SetMuscle(ref pose, "Left Forearm Stretch", -0.3f);
            SetMuscle(ref pose, "Right Forearm Stretch", -0.3f);
            SetMuscle(ref pose, "Left Upper Leg Front-Back", -0.6f); // range -90° (flex forward) … +50°
            SetMuscle(ref pose, "Left Lower Leg Stretch", -0.8f);
            SetMuscle(ref pose, "Head Turn Left-Right", 0.5f);
            handler.SetHumanPose(ref pose);
            CheckMirror("test pose arms");
            Check("arms raised: hands above shoulders",
                B(HumanBodyBones.LeftHand).position.y > B(HumanBodyBones.LeftUpperArm).position.y &&
                B(HumanBodyBones.RightHand).position.y > B(HumanBodyBones.RightUpperArm).position.y);
            Check("left thigh forward: knee ahead of hip",
                B(HumanBodyBones.LeftLowerLeg).position.z > B(HumanBodyBones.LeftUpperLeg).position.z + 0.1f);
            Check("left knee bends backwards: ankle behind knee",
                B(HumanBodyBones.LeftFoot).position.z < B(HumanBodyBones.LeftLowerLeg).position.z);
            Bake();
            Shoot(cam, "unity_pose");

            handler.Dispose();
            UnityEngine.Object.DestroyImmediate(baked);

            void CheckMirror(string label)
            {
                foreach ((HumanBodyBones l, HumanBodyBones r) in new[]
                         {
                             (HumanBodyBones.LeftHand, HumanBodyBones.RightHand),
                             (HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm),
                         })
                {
                    Vector3 a = B(l).position, b = B(r).position;
                    Check($"{label}: {l} mirrors {r} ({a} vs {b})",
                        Mathf.Abs(a.x + b.x) < 0.03f && Mathf.Abs(a.y - b.y) < 0.03f && Mathf.Abs(a.z - b.z) < 0.03f);
                }
            }
        }

        private static float Muscle(float[] muscles, string name) => muscles[Array.IndexOf(HumanTrait.MuscleName, name)];

        private static void SetMuscle(ref HumanPose pose, string name, float value)
        {
            int i = Array.IndexOf(HumanTrait.MuscleName, name);
            if (Check($"muscle '{name}' exists", i >= 0)) pose.muscles[i] = value;
        }

        private static Camera MakeCamera(Scene scene)
        {
            var light = new GameObject("Key", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
            SceneManager.MoveGameObjectToScene(light.gameObject, scene);

            var cam = new GameObject("ShotCamera", typeof(Camera)).GetComponent<Camera>();
            SceneManager.MoveGameObjectToScene(cam.gameObject, scene);
            cam.scene = scene;
            cam.orthographic = true;
            cam.orthographicSize = 1.15f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.78f, 0.78f, 0.78f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20f;
            return cam;
        }

        private static void Shoot(Camera cam, string name)
        {
            // Front (camera on +Z looking back at the character) and 3/4.
            foreach ((string suffix, Vector3 dir) in new[] { ("front", Vector3.forward), ("three_quarter", new Vector3(-0.7f, 0.2f, 0.7f).normalized) })
            {
                cam.orthographicSize = 1.15f;
                cam.transform.position = new Vector3(0f, 0.95f, 0f) + dir * 5f;
                cam.transform.LookAt(new Vector3(0f, 0.95f, 0f));
                Texture2D tex = RenderShot(cam, 600);
                File.WriteAllBytes(ShotPath($"{name}_{suffix}"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>Face close-ups for every <see cref="EyeStyle"/>, 3x2 like the atlas → unity_eyes.png.</summary>
        private static void ShootEyeSheet(Camera cam, MeshRenderer target, int faceIndex, Vector3 head)
        {
            const int size = 300;
            var styles = (EyeStyle[])Enum.GetValues(typeof(EyeStyle));
            var sheet = new Texture2D(size * SailorEyes.Columns, size * SailorEyes.Rows, TextureFormat.RGB24, false);
            var block = new MaterialPropertyBlock();
            Vector3 face = new Vector3(0f, head.y + 0.12f, 0f);
            cam.orthographicSize = 0.2f;
            cam.transform.position = face + Vector3.forward * 5f;
            cam.transform.LookAt(face);
            foreach (EyeStyle s in styles)
            {
                block.Clear();
                block.SetVector("_BaseMap_ST", SailorEyes.CellScaleOffset(s));
                target.SetPropertyBlock(block, faceIndex);
                Texture2D shot = RenderShot(cam, size);
                int i = (int)s;
                sheet.SetPixels(i % SailorEyes.Columns * size, (SailorEyes.Rows - 1 - i / SailorEyes.Columns) * size, size, size, shot.GetPixels());
                UnityEngine.Object.DestroyImmediate(shot);
            }
            target.SetPropertyBlock(null, faceIndex);
            sheet.Apply();
            File.WriteAllBytes(ShotPath("unity_eyes"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        private static Texture2D RenderShot(Camera cam, int size)
        {
            var rt = RenderTexture.GetTemporary(size, size, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        private static string ShotPath(string name) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath), ShotsDir, name + ".png");

        private static bool Check(string label, bool ok)
        {
            if (ok) Debug.Log($"[CharacterModel] ok   {label}");
            else { _failures++; Debug.LogError($"[CharacterModel] FAIL {label}"); }
            return ok;
        }

        private static void Near(string label, float actual, float expected, float tolerance) =>
            Check($"{label}: {actual:0.000} (expected {expected:0.000} ± {tolerance})", Mathf.Abs(actual - expected) <= tolerance);
    }
}
