using UnityEditor;
using UnityEngine;

namespace RumOverboard.EditorTools
{
    /// <summary>
    /// Builds a standard 11-body humanoid ragdoll (Rigidbodies + colliders +
    /// CharacterJoints) on the selected humanoid, then adds a RagdollController.
    /// Sizes are derived from the rig's bone lengths, so it works on the PolyOne
    /// Stickman as well as any other Humanoid avatar. Re-running is safe: bones
    /// that already have a Rigidbody are skipped.
    ///
    /// Menu: RumOverboard ▸ Ragdoll ▸ Build On Selected
    /// </summary>
    public static class RagdollBuilder
    {
        private const string MenuBuild = "RumOverboard/Ragdoll/Build On Selected";
        private const string MenuClear = "RumOverboard/Ragdoll/Clear From Selected";

        [MenuItem(MenuBuild, true)]
        [MenuItem(MenuClear, true)]
        private static bool Validate() =>
            Selection.activeGameObject != null &&
            Selection.activeGameObject.GetComponentInChildren<Animator>() != null;

        [MenuItem(MenuBuild)]
        private static void Build()
        {
            var root = Selection.activeGameObject;
            var animator = root.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                EditorUtility.DisplayDialog("Ragdoll Builder",
                    "Selected object has no Humanoid Animator. Set the rig to 'Humanoid' on the model import settings.",
                    "OK");
                return;
            }

            Transform Bone(HumanBodyBones b) => animator.GetBoneTransform(b);

            var hips = Bone(HumanBodyBones.Hips);
            var chest = Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine);
            var head = Bone(HumanBodyBones.Head);
            if (hips == null || chest == null || head == null)
            {
                Debug.LogError("[RagdollBuilder] Missing core humanoid bones (hips/chest/head).");
                return;
            }

            Undo.SetCurrentGroupName("Build Ragdoll");
            int group = Undo.GetCurrentGroup();

            // --- Torso chain ---
            MakeBody(hips, 2.5f);
            AddBoxCollider(hips, chest, 0.28f);

            MakeBody(chest, 2.5f);
            AddBoxCollider(chest, head, 0.30f);
            Join(chest, hips, twist: 20, swing: 25);

            MakeBody(head, 1.0f);
            AddSphereCollider(head, 0.14f);
            Join(head, chest, twist: 25, swing: 20);

            // --- Arms ---
            BuildLimb(animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, chest, 1.0f, 0.7f);
            BuildLimb(animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, chest, 1.0f, 0.7f);

            // --- Legs ---
            BuildLimb(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, hips, 1.5f, 1.0f);
            BuildLimb(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, hips, 1.5f, 1.0f);

            // --- Controller ---
            var controller = root.GetComponent<Gameplay.RagdollController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<Gameplay.RagdollController>(root);
                Debug.Log("[RagdollBuilder] Added RagdollController — assign its Root Body to the character's main Rigidbody.");
            }

            Undo.CollapseUndoOperations(group);
            Debug.Log("[RagdollBuilder] Ragdoll built. Bone bodies start kinematic; RagdollController toggles them.");
        }

        [MenuItem(MenuClear)]
        private static void Clear()
        {
            var root = Selection.activeGameObject;
            var animator = root.GetComponentInChildren<Animator>();
            if (animator == null) return;

            Undo.SetCurrentGroupName("Clear Ragdoll");
            int group = Undo.GetCurrentGroup();

            foreach (var joint in root.GetComponentsInChildren<CharacterJoint>(true))
                Undo.DestroyObjectImmediate(joint);
            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                // Leave the character's own root body (if any) alone — only clear bone bodies.
                if (rb.transform == root.transform) continue;
                foreach (var col in rb.GetComponents<Collider>())
                    Undo.DestroyObjectImmediate(col);
                Undo.DestroyObjectImmediate(rb);
            }
            Undo.CollapseUndoOperations(group);
            Debug.Log("[RagdollBuilder] Cleared ragdoll bodies/joints/colliders.");
        }

        // ---- helpers ------------------------------------------------------------
        private static void BuildLimb(Animator anim, HumanBodyBones upper, HumanBodyBones lower,
            HumanBodyBones end, Transform parent, float upperMass, float lowerMass)
        {
            var u = anim.GetBoneTransform(upper);
            var l = anim.GetBoneTransform(lower);
            var e = anim.GetBoneTransform(end);
            if (u == null || l == null) return;

            MakeBody(u, upperMass);
            AddCapsuleCollider(u, l, 0.22f);
            Join(u, parent, twist: 20, swing: 45);

            MakeBody(l, lowerMass);
            AddCapsuleCollider(l, e != null ? e : l, 0.20f);
            Join(l, u, twist: 10, swing: 60);
        }

        private static Rigidbody MakeBody(Transform t, float mass)
        {
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(t.gameObject);
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true; // RagdollController flips this on activation
            return rb;
        }

        private static void Join(Transform child, Transform parentBone, float twist, float swing)
        {
            var parentRb = parentBone.GetComponent<Rigidbody>();
            if (parentRb == null) return;

            var joint = child.GetComponent<CharacterJoint>();
            if (joint == null) joint = Undo.AddComponent<CharacterJoint>(child.gameObject);
            joint.connectedBody = parentRb;
            joint.anchor = Vector3.zero; // bone origin = the anatomical pivot
            joint.enablePreprocessing = false;
            joint.lowTwistLimit = new SoftJointLimit { limit = -twist };
            joint.highTwistLimit = new SoftJointLimit { limit = twist };
            joint.swing1Limit = new SoftJointLimit { limit = swing };
            joint.swing2Limit = new SoftJointLimit { limit = swing * 0.5f };
        }

        private static void AddCapsuleCollider(Transform bone, Transform child, float radiusFactor)
        {
            var cap = bone.GetComponent<CapsuleCollider>();
            if (cap == null) cap = Undo.AddComponent<CapsuleCollider>(bone.gameObject);

            Vector3 delta = child.position - bone.position;
            float length = Mathf.Max(delta.magnitude, 0.05f);
            Vector3 localDir = bone.InverseTransformDirection(delta.normalized);
            cap.direction = DominantAxis(localDir);
            cap.height = length;
            cap.radius = length * radiusFactor;
            cap.center = bone.InverseTransformPoint(bone.position + delta * 0.5f);
            cap.enabled = false; // RagdollController enables on activation
        }

        private static void AddBoxCollider(Transform bone, Transform child, float widthFactor)
        {
            var box = bone.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(bone.gameObject);

            Vector3 delta = child.position - bone.position;
            float length = Mathf.Max(delta.magnitude, 0.08f);
            float width = length * widthFactor / 0.3f * 0.3f; // ~ proportional torso width
            box.size = new Vector3(width, length, width * 0.7f);
            box.center = bone.InverseTransformPoint(bone.position + delta * 0.5f);
            box.enabled = false;
        }

        private static void AddSphereCollider(Transform bone, float radius)
        {
            var sph = bone.GetComponent<SphereCollider>();
            if (sph == null) sph = Undo.AddComponent<SphereCollider>(bone.gameObject);
            sph.radius = radius;
            sph.center = new Vector3(0f, radius * 0.6f, 0f);
            sph.enabled = false;
        }

        private static int DominantAxis(Vector3 v)
        {
            v = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            if (v.x >= v.y && v.x >= v.z) return 0; // X
            if (v.y >= v.z) return 1;               // Y
            return 2;                               // Z
        }
    }
}
