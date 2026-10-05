using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RumOverboard.EditorTools.ShipSandbox
{
    /// <summary>
    /// Renders the network ship (in the sandbox scene, at its spawn pose) from a few viewpoints to
    /// PNGs — a visual check that hull, deck, rig and colliders line up. Batch (with graphics):
    ///   Unity -batchmode -projectPath . -executeMethod ...ShipSnapshot.Run -snapOut &lt;dir&gt;
    /// Collider overlay: a second set of shots draws every solid collider as a translucent box.
    /// </summary>
    public static class ShipSnapshot
    {
        [MenuItem("RumOverboard/Ship Sandbox/Diagnostics/Render Ship Snapshots")]
        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-snapOut");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Temp/ShipSnapshots";
            Directory.CreateDirectory(dir);

            EditorSceneManager.OpenScene(ShipSandboxSetup.ScenePath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Source/Prefabs/NetworkShip.prefab");
            var spawn = GameObject.Find("ShipSpawn");
            var ship = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            ship.transform.SetPositionAndRotation(spawn != null ? spawn.transform.position : Vector3.zero, Quaternion.identity);
            Transform s = ship.transform;

            var camGo = new GameObject("SnapCam");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 2000f;
            Type urp = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urp != null) camGo.AddComponent(urp);

            void Shot(string name, Vector3 localPos, Vector3 localLookAt)
            {
                Vector3 p = s.TransformPoint(localPos);
                cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(s.TransformPoint(localLookAt) - p, Vector3.up));
                var rt = new RenderTexture(1280, 720, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }

            Shot("01_side_port", new Vector3(-22f, 6f, 1f), new Vector3(0f, 4f, 1f));
            Shot("02_bow_quarter", new Vector3(14f, 7f, 20f), new Vector3(0f, 3f, 0f));
            Shot("03_stern_quarter", new Vector3(-10f, 5f, -20f), new Vector3(0f, 2.5f, -2f));
            Shot("04_deck_eye_fwd", new Vector3(0.6f, 2.8f, -6.2f), new Vector3(0.3f, 2.3f, 6f));
            Shot("05_deck_eye_aft", new Vector3(-0.8f, 2.8f, 4f), new Vector3(0f, 2.2f, -8f));
            Shot("06_top_down", new Vector3(0.01f, 30f, 0.5f), new Vector3(0f, 0f, 0.5f));

            // Collider overlay: solid colliders as magenta boxes (bounds in ship space).
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.color = new Color(1f, 0f, 1f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 0f, 1f, 1f));
            foreach (Renderer r in ship.GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (BoxCollider b in ship.GetComponentsInChildren<BoxCollider>())
            {
                if (b.isTrigger) continue;
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.transform.SetParent(b.transform, false);
                cube.transform.localPosition = b.center;
                cube.transform.localScale = b.size;
                cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            Shot("07_colliders_side", new Vector3(-22f, 6f, 1f), new Vector3(0f, 4f, 1f));
            Shot("08_colliders_deck", new Vector3(0.6f, 2.8f, -6.2f), new Vector3(0.3f, 2.3f, 6f));
            Debug.Log($"[ShipSnapshot] wrote snapshots to {Path.GetFullPath(dir)}");
        }
    }
}
