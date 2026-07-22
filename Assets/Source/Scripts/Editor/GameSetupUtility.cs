using System;
using System.Reflection;
using Fusion;
using Fusion.Editor;
using RumOverboard.Core;
using RumOverboard.Gameplay;
using RumOverboard.Networking;
using UnityEditor.Animations;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Source.Scripts.Editor
{
    /// <summary>
    /// One-shot setup utility for gameplay bootstrap objects that cannot be fully wired from plain text files.
    /// </summary>
    public static class GameSetupUtility
    {
        private const string ConfigAssetPath = "Assets/Source/Configs/GameConfig.asset";
        private const string PlayerPrefabPath = "Assets/Source/Prefabs/NetworkPlayer.prefab";
        private const string PlayerAnimatorControllerPath = "Assets/Source/Animation/NetworkPlayer.controller";
        private const string UpperBodyMaskPath = "Assets/Source/Animation/Masks/UpperBody.mask";
        private const string StickmanPrefabPath = "Assets/PolyOne/Free Stickman/Prefabs/Free Pack - Stick Man.prefab";
        private const string DeckMaterialPath = "Assets/PolyOne/Free Stickman/Materials/Plane_M.mat";
        private const string GameplayScenePath = "Assets/Scenes/Gameplay.unity";

        private const string IdleClipPath = "Assets/PolyOne/Free Stickman/Animation/Idle.anim";
        private const string WalkClipPath = "Assets/PolyOne/Free Stickman/Animation/Walk.anim";
        private const string RunClipPath = "Assets/PolyOne/Free Stickman/Animation/Run.anim";
        private const string RunFastClipPath = "Assets/PolyOne/Free Stickman/Animation/Run Fast.anim";
        private const string JumpClipPath = "Assets/PolyOne/Free Stickman/Animation/Jumping Up.anim";
        private const string SwimClipPath = "Assets/PolyOne/Free Stickman/Animation/Swimming.anim";
        private const string YellClipPath = "Assets/PolyOne/Free Stickman/Animation/Yelling.anim";

        private const string ParamSpeed = "Speed";
        private const string ParamGrounded = "Grounded";
        private const string ParamClimbing = "Climbing";
        private const string ParamSwimming = "Swimming";
        private const string ParamDrinking = "Drinking";

        [MenuItem("RumOverboard/Setup/Complete GameManager Setup")]
        public static void RunSetup()
        {
            EnsureFolder("Assets/Source/Configs");
            EnsureFolder("Assets/Source/Prefabs");
            EnsureFolder("Assets/Source/Animation");
            EnsureFolder("Assets/Source/Animation/Masks");

            GameConfig config = EnsureGameConfig();
            GameObject playerPrefab = EnsurePlayerPrefab(config);

            NetworkProjectConfigUtilities.RebuildPrefabTable();
            AssignSceneReferences(config, playerPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[GameSetupUtility] Setup complete: config created/updated, player prefab created/updated, GameManager references assigned.");
        }

        // Entry point for Unity batch mode: -executeMethod Source.Scripts.Editor.GameSetupUtility.RunSetupFromCommandLine
        public static void RunSetupFromCommandLine()
        {
            try
            {
                RunSetup();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameSetupUtility] Setup failed: {ex}");
                throw;
            }
        }

        private static GameConfig EnsureGameConfig()
        {
            GameConfig config = AssetDatabase.LoadAssetAtPath<GameConfig>(ConfigAssetPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GameConfig>();
                AssetDatabase.CreateAsset(config, ConfigAssetPath);
            }

            // Fill explicit values so the asset is fully self-documented in Inspector.
            config.PhotonAppId = GameConfig.DefaultAppId;
            config.FixedRegion = string.Empty;
            config.MaxCrew = 4;
            config.DefaultSessionName = "rum-overboard";

            config.MoveSpeed = 42.5f;
            config.GroundAcceleration = 70.0f;
            config.GroundDeceleration = 80.0f;
            config.ClimbSpeed = 12.5f;
            config.TurnSpeedDeg = 2700f;
            config.JumpImpulse = 20f;
            config.JumpBufferTime = 0.18f;
            config.CoyoteTime = 0.12f;
            config.ExtraRiseGravity = 90f;
            config.ExtraFallGravity = 180f;
            config.AnimatorSpeedRiseRate = 55f;
            config.AnimatorSpeedFallRate = 45f;

            config.DrunkControlLoss = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            config.DrunkMaxWobbleDeg = 45f;
            config.RumSipDuration = 2f;
            config.RumSipIntoxication = 0.15f;
            config.DrunkRagdollInfluence = 0.5f;
            config.RagdollRecoverRate = 0.4f;

            EditorUtility.SetDirty(config);
            return config;
        }

        private static GameObject EnsurePlayerPrefab(GameConfig config)
        {
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            GameObject root;

            if (prefabRoot != null)
            {
                root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            }
            else
            {
                root = new GameObject("NetworkPlayer");
            }

            root.name = "NetworkPlayer";

            var networkObject = GetOrAddComponent<NetworkObject>(root);
            var networkTransform = GetOrAddComponent<NetworkTransform>(root);
            var networkPlayer = GetOrAddComponent<NetworkPlayer>(root);
            var rigidbody = GetOrAddComponent<Rigidbody>(root);
            var capsule = GetOrAddComponent<CapsuleCollider>(root);

            rigidbody.mass = 70f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.constraints = RigidbodyConstraints.FreezeRotation;

            capsule.height = 1.8f;
            capsule.radius = 0.28f;
            capsule.center = new Vector3(0f, 0.9f, 0f);

            EnsureVisualModel(root.transform);
            AnimatorController gameplayController = EnsureGameplayAnimatorController(config.MoveSpeed);

            Transform cameraAnchor = root.transform.Find("CameraAnchor");
            if (cameraAnchor == null)
            {
                var anchorGo = new GameObject("CameraAnchor");
                anchorGo.transform.SetParent(root.transform, false);
                cameraAnchor = anchorGo.transform;
            }
            cameraAnchor.localPosition = new Vector3(0f, 1.65f, 0f);
            cameraAnchor.localRotation = Quaternion.identity;

            var animator = root.GetComponentInChildren<Animator>(true);
            var ragdoll = root.GetComponentInChildren<RagdollController>(true);

            if (animator == null)
            {
                throw new InvalidOperationException("Player visual model has no Animator component.");
            }

            animator.runtimeAnimatorController = gameplayController;
            animator.applyRootMotion = false;

            var playerSo = new SerializedObject(networkPlayer);
            playerSo.FindProperty("config").objectReferenceValue = config;
            playerSo.FindProperty("animator").objectReferenceValue = animator;
            playerSo.FindProperty("ragdoll").objectReferenceValue = ragdoll;
            playerSo.FindProperty("cameraAnchor").objectReferenceValue = cameraAnchor;

            playerSo.ApplyModifiedPropertiesWithoutUndo();

            // Touch components so Unity persists changes in prefab contents.
            EditorUtility.SetDirty(networkObject);
            EditorUtility.SetDirty(networkTransform);
            EditorUtility.SetDirty(networkPlayer);
            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(rigidbody);
            EditorUtility.SetDirty(capsule);

            GameObject savedPrefab;
            if (prefabRoot != null)
            {
                savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                PrefabUtility.UnloadPrefabContents(root);
            }
            else
            {
                savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                UnityEngine.Object.DestroyImmediate(root);
            }

            return savedPrefab;
        }

        private static void EnsureVisualModel(Transform root)
        {
            var existingAnimator = root.GetComponentInChildren<Animator>(true);
            if (existingAnimator != null)
            {
                return;
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(StickmanPrefabPath);
            if (source == null)
            {
                throw new InvalidOperationException($"Stickman source prefab not found at '{StickmanPrefabPath}'.");
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = "StickmanModel";
            instance.transform.SetParent(root, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }

        private static void AssignSceneReferences(GameConfig config, GameObject playerPrefab)
        {
            if (playerPrefab == null)
            {
                throw new InvalidOperationException("Player prefab asset is null and cannot be assigned.");
            }

            var scene = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
            var manager = UnityEngine.Object.FindAnyObjectByType<ConnectionManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                throw new InvalidOperationException("ConnectionManager was not found in Gameplay scene.");
            }

            var managerSo = new SerializedObject(manager);
            managerSo.FindProperty("config").objectReferenceValue = config;

            var playerPrefabProperty = managerSo.FindProperty("playerPrefab");
            var networkObject = playerPrefab.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                throw new InvalidOperationException("Player prefab has no NetworkObject component.");
            }

            SetNetworkPrefabRef(playerPrefabProperty, networkObject);
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            AssignDeckMaterial();

            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static AnimatorController EnsureGameplayAnimatorController(float moveSpeed)
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerAnimatorControllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(PlayerAnimatorControllerPath);
            }

            EnsureParameter(controller, ParamSpeed, AnimatorControllerParameterType.Float);
            EnsureParameter(controller, ParamGrounded, AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, ParamClimbing, AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, ParamSwimming, AnimatorControllerParameterType.Bool);
            EnsureParameter(controller, ParamDrinking, AnimatorControllerParameterType.Bool);

            AnimationClip idle = LoadClip(IdleClipPath);
            AnimationClip walk = LoadClip(WalkClipPath);
            AnimationClip run = LoadClip(RunClipPath);
            AnimationClip runFast = LoadClip(RunFastClipPath);
            AnimationClip jump = LoadClip(JumpClipPath);
            AnimationClip swim = LoadClip(SwimClipPath);
            AnimationClip yell = LoadClip(YellClipPath);

            RemoveControllerSubAssets<BlendTree>(controller);

            AnimatorControllerLayer[] layers = controller.layers;

            AnimatorControllerLayer baseLayer = layers.Length > 0
                ? layers[0]
                : new AnimatorControllerLayer();

            if (baseLayer.stateMachine == null)
                baseLayer.stateMachine = CreateStateMachineAsset(controller, "BaseLocomotion");

            baseLayer.name = "Base Locomotion";
            baseLayer.defaultWeight = 1f;
            baseLayer.blendingMode = AnimatorLayerBlendingMode.Override;
            baseLayer.avatarMask = null;

            BuildBaseLocomotionStateMachine(baseLayer.stateMachine, controller, idle, walk, run, runFast, jump, swim, moveSpeed);

            AvatarMask upperBodyMask = EnsureUpperBodyMask();

            AnimatorControllerLayer upperLayer = layers.Length > 1
                ? layers[1]
                : new AnimatorControllerLayer();

            if (upperLayer.stateMachine == null)
                upperLayer.stateMachine = CreateStateMachineAsset(controller, "UpperBodyActions");

            upperLayer.name = "UpperBody Actions";
            upperLayer.defaultWeight = 1f;
            upperLayer.blendingMode = AnimatorLayerBlendingMode.Override;
            upperLayer.avatarMask = upperBodyMask;
            upperLayer.iKPass = false;

            BuildUpperBodyStateMachine(upperLayer.stateMachine, walk, swim, yell);

            controller.layers = new[] { baseLayer, upperLayer };

            EditorUtility.SetDirty(baseLayer.stateMachine);
            EditorUtility.SetDirty(upperLayer.stateMachine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        private static void BuildBaseLocomotionStateMachine(
            AnimatorStateMachine stateMachine,
            AnimatorController controller,
            AnimationClip idle,
            AnimationClip walk,
            AnimationClip run,
            AnimationClip runFast,
            AnimationClip jump,
            AnimationClip swim,
            float moveSpeed)
        {
            ResetStateMachine(stateMachine);

            var grounded = stateMachine.AddState("Grounded", new Vector3(250f, 80f, 0f));
            var inAir = stateMachine.AddState("InAir", new Vector3(520f, 80f, 0f));
            var swimming = stateMachine.AddState("Swimming", new Vector3(520f, 200f, 0f));
            var climbing = stateMachine.AddState("Climbing", new Vector3(520f, -40f, 0f));

            var groundedBlend = new BlendTree
            {
                name = "GroundedBlend",
                blendType = BlendTreeType.Simple1D,
                blendParameter = ParamSpeed,
                useAutomaticThresholds = false,
            };
            AssetDatabase.AddObjectToAsset(groundedBlend, controller);
            float walkThreshold = Mathf.Max(1f, moveSpeed * 0.16f);
            float runThreshold = Mathf.Max(walkThreshold + 1f, moveSpeed * 0.45f);
            float fastThreshold = Mathf.Max(runThreshold + 1f, moveSpeed * 0.85f);

            groundedBlend.AddChild(idle, 0f);
            groundedBlend.AddChild(walk, walkThreshold);
            groundedBlend.AddChild(run, runThreshold);
            groundedBlend.AddChild(runFast, fastThreshold);

            grounded.motion = groundedBlend;
            inAir.motion = jump;
            swimming.motion = swim;
            climbing.motion = walk;
            climbing.speed = 0.8f;

            stateMachine.defaultState = grounded;

            AddTransition(grounded, inAir,
                new Condition(AnimatorConditionMode.IfNot, ParamGrounded),
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.IfNot, ParamClimbing));
            AddTransition(grounded, swimming, new Condition(AnimatorConditionMode.If, ParamSwimming));
            AddTransition(grounded, climbing, new Condition(AnimatorConditionMode.If, ParamClimbing));

            AddTransition(inAir, grounded,
                new Condition(AnimatorConditionMode.If, ParamGrounded),
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.IfNot, ParamClimbing));
            AddTransition(inAir, swimming, new Condition(AnimatorConditionMode.If, ParamSwimming));
            AddTransition(inAir, climbing, new Condition(AnimatorConditionMode.If, ParamClimbing));

            AddTransition(swimming, grounded,
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.If, ParamGrounded),
                new Condition(AnimatorConditionMode.IfNot, ParamClimbing));
            AddTransition(swimming, inAir,
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.IfNot, ParamGrounded),
                new Condition(AnimatorConditionMode.IfNot, ParamClimbing));
            AddTransition(swimming, climbing, new Condition(AnimatorConditionMode.If, ParamClimbing));

            AddTransition(climbing, grounded,
                new Condition(AnimatorConditionMode.IfNot, ParamClimbing),
                new Condition(AnimatorConditionMode.If, ParamGrounded),
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming));
            AddTransition(climbing, inAir,
                new Condition(AnimatorConditionMode.IfNot, ParamClimbing),
                new Condition(AnimatorConditionMode.IfNot, ParamGrounded),
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming));
            AddTransition(climbing, swimming, new Condition(AnimatorConditionMode.If, ParamSwimming));
        }

        private static void BuildUpperBodyStateMachine(
            AnimatorStateMachine stateMachine,
            AnimationClip walk,
            AnimationClip swim,
            AnimationClip yell)
        {
            ResetStateMachine(stateMachine);

            var upperIdle = stateMachine.AddState("UpperIdle", new Vector3(220f, 110f, 0f));
            var upperDrink = stateMachine.AddState("UpperDrink", new Vector3(540f, 220f, 0f));
            var upperClimb = stateMachine.AddState("UpperClimb", new Vector3(540f, 110f, 0f));
            var upperSwim = stateMachine.AddState("UpperSwim", new Vector3(540f, 0f, 0f));

            upperIdle.motion = null; // Keep base locomotion untouched when no overlay action is active.
            upperDrink.motion = yell;
            upperClimb.motion = walk;
            upperClimb.speed = 0.8f;
            upperSwim.motion = swim;

            stateMachine.defaultState = upperIdle;

            AddAnyTransition(stateMachine, upperSwim,
                new Condition(AnimatorConditionMode.If, ParamSwimming));
            AddAnyTransition(stateMachine, upperClimb,
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.If, ParamClimbing));
            AddAnyTransition(stateMachine, upperDrink,
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.If, ParamDrinking));

            AddTransition(upperSwim, upperClimb,
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.If, ParamClimbing));
            AddTransition(upperSwim, upperDrink,
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.If, ParamDrinking));
            AddTransition(upperSwim, upperIdle,
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.IfNot, ParamClimbing),
                new Condition(AnimatorConditionMode.IfNot, ParamDrinking));

            AddTransition(upperClimb, upperDrink,
                new Condition(AnimatorConditionMode.If, ParamDrinking));
            AddTransition(upperClimb, upperSwim,
                new Condition(AnimatorConditionMode.If, ParamSwimming));
            AddTransition(upperClimb, upperIdle,
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.IfNot, ParamClimbing),
                new Condition(AnimatorConditionMode.IfNot, ParamDrinking));

            AddTransition(upperDrink, upperSwim,
                new Condition(AnimatorConditionMode.If, ParamSwimming));
            AddTransition(upperDrink, upperClimb,
                new Condition(AnimatorConditionMode.IfNot, ParamDrinking),
                new Condition(AnimatorConditionMode.If, ParamClimbing));
            AddTransition(upperDrink, upperIdle,
                new Condition(AnimatorConditionMode.IfNot, ParamSwimming),
                new Condition(AnimatorConditionMode.IfNot, ParamClimbing),
                new Condition(AnimatorConditionMode.IfNot, ParamDrinking));
        }

        private static AvatarMask EnsureUpperBodyMask()
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            }

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);

            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static AnimatorStateMachine CreateStateMachineAsset(AnimatorController controller, string name)
        {
            var stateMachine = new AnimatorStateMachine { name = name };
            AssetDatabase.AddObjectToAsset(stateMachine, controller);
            return stateMachine;
        }

        private static void RemoveControllerSubAssets<T>(AnimatorController controller) where T : UnityEngine.Object
        {
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            var assets = AssetDatabase.LoadAllAssetsAtPath(controllerPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is T)
                    UnityEngine.Object.DestroyImmediate(assets[i], true);
            }
        }

        private static AnimationClip LoadClip(string path)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                throw new InvalidOperationException($"Animation clip not found at '{path}'.");
            }

            return clip;
        }

        private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            foreach (var parameter in controller.parameters)
            {
                if (parameter.name == name)
                {
                    if (parameter.type != type)
                    {
                        controller.RemoveParameter(parameter);
                        break;
                    }

                    return;
                }
            }

            controller.AddParameter(name, type);
        }

        private static void ResetStateMachine(AnimatorStateMachine stateMachine)
        {
            var childStates = stateMachine.states;
            for (int i = childStates.Length - 1; i >= 0; i--)
            {
                stateMachine.RemoveState(childStates[i].state);
            }

            var childStateMachines = stateMachine.stateMachines;
            for (int i = childStateMachines.Length - 1; i >= 0; i--)
            {
                stateMachine.RemoveStateMachine(childStateMachines[i].stateMachine);
            }

            var anyTransitions = stateMachine.anyStateTransitions;
            for (int i = anyTransitions.Length - 1; i >= 0; i--)
            {
                stateMachine.RemoveAnyStateTransition(anyTransitions[i]);
            }

            var entryTransitions = stateMachine.entryTransitions;
            for (int i = entryTransitions.Length - 1; i >= 0; i--)
            {
                stateMachine.RemoveEntryTransition(entryTransitions[i]);
            }
        }

        private readonly struct Condition
        {
            public readonly AnimatorConditionMode Mode;
            public readonly string Parameter;
            public readonly float Threshold;

            public Condition(AnimatorConditionMode mode, string parameter, float threshold = 0f)
            {
                Mode = mode;
                Parameter = parameter;
                Threshold = threshold;
            }
        }

        private static AnimatorStateTransition AddTransition(AnimatorState from, AnimatorState to, params Condition[] conditions)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.08f;
            transition.offset = 0f;
            transition.interruptionSource = TransitionInterruptionSource.None;
            transition.orderedInterruption = true;

            foreach (var condition in conditions)
            {
                transition.AddCondition(condition.Mode, condition.Threshold, condition.Parameter);
            }

            return transition;
        }

        private static AnimatorStateTransition AddAnyTransition(AnimatorStateMachine machine, AnimatorState to, params Condition[] conditions)
        {
            var transition = machine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.08f;
            transition.offset = 0f;
            transition.interruptionSource = TransitionInterruptionSource.None;
            transition.orderedInterruption = true;
            transition.canTransitionToSelf = false;

            foreach (var condition in conditions)
            {
                transition.AddCondition(condition.Mode, condition.Threshold, condition.Parameter);
            }

            return transition;
        }

        private static void AssignDeckMaterial()
        {
            Material deckMaterial = AssetDatabase.LoadAssetAtPath<Material>(DeckMaterialPath);
            if (deckMaterial == null)
            {
                Debug.LogWarning($"[GameSetupUtility] Deck material not found at '{DeckMaterialPath}'.");
                return;
            }

            GameObject deck = GameObject.Find("Deck (placeholder)");
            if (deck == null)
            {
                Debug.LogWarning("[GameSetupUtility] Deck (placeholder) object not found in Gameplay scene.");
                return;
            }

            MeshRenderer renderer = deck.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Debug.LogWarning("[GameSetupUtility] Deck (placeholder) has no MeshRenderer.");
                return;
            }

            if (renderer.sharedMaterial != deckMaterial)
            {
                renderer.sharedMaterial = deckMaterial;
                EditorUtility.SetDirty(renderer);
            }
        }

        private static void SetNetworkPrefabRef(SerializedProperty prefabRefProperty, NetworkObject prefab)
        {
            if (prefabRefProperty == null)
            {
                throw new ArgumentNullException(nameof(prefabRefProperty));
            }

            var prefabGuid = NetworkObjectEditor.GetPrefabGuid(prefab);
            var fusionEditorAssembly = typeof(NetworkObjectEditor).Assembly;
            var guidDrawerType = fusionEditorAssembly.GetType("Fusion.Editor.NetworkObjectGuidDrawer");
            var setValueMethod = guidDrawerType?.GetMethod(
                "SetValue",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (setValueMethod == null)
            {
                throw new MissingMethodException("Fusion.Editor.NetworkObjectGuidDrawer.SetValue was not found.");
            }

            setValueMethod.Invoke(null, new object[] { prefabRefProperty, prefabGuid });
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            if (component == null)
            {
                component = go.AddComponent<T>();
            }
            return component;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string[] parts = path.Split('/');
            if (parts.Length < 2)
            {
                return;
            }

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}

