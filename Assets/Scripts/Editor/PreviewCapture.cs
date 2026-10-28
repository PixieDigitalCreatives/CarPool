using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RacingPrototype.EditorTools
{
    /// <summary>
    /// Renders review stills of every route (from racing-camera height and above) and of the five
    /// cars in the garage, through URP with post-processing. Output: RACING_CAPTURE_DIR or Logs/Screenshots.
    /// Batch: -executeMethod RacingPrototype.EditorTools.PreviewCapture.CaptureBatch
    /// </summary>
    public static class PreviewCapture
    {
        static string outDir;

        [MenuItem("Racing Prototype/Capture Preview Images", priority = 20)]
        public static void CaptureMenu()
        {
            Capture();
            EditorUtility.RevealInFinder(outDir);
        }

        public static void CaptureBatch()
        {
            try { Capture(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
        }

        static void Capture()
        {
            outDir = Environment.GetEnvironmentVariable("RACING_CAPTURE_DIR");
            if (string.IsNullOrEmpty(outDir)) outDir = Path.GetFullPath("Logs/Screenshots");
            Directory.CreateDirectory(outDir);
            var catalog = AssetDatabase.LoadAssetAtPath<CarCatalog>("Assets/Resources/CarCatalog.asset");
            var routes = AssetDatabase.LoadAssetAtPath<RouteCatalog>("Assets/Resources/RouteCatalog.asset");

            foreach (var route in routes.routes)
            {
                EditorSceneManager.OpenScene($"Assets/Scenes/{route.sceneName}.unity", OpenSceneMode.Single);
                var track = UnityEngine.Object.FindFirstObjectByType<TrackPath>();
                var grid = GameObject.Find("Starting Grid").transform;
                for (int i = 0; i < grid.childCount; i++)
                {
                    var slot = grid.GetChild(i);
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(catalog.cars[i % catalog.Count].prefab);
                    car.transform.SetPositionAndRotation(slot.position - Vector3.up * 0.02f, slot.rotation);
                }
                var cam = MakeCamera(out var camGo);
                string prefix = $"R{route.number}_";
                var last = grid.GetChild(grid.childCount - 1);
                var pole = grid.GetChild(0);
                Shot(cam, prefix + "01_grid_chase", last.position - last.forward * 9f + Vector3.up * 3.1f, last.position + last.forward * 6f + Vector3.up * 1.1f, 60f);
                Shot(cam, prefix + "02_grid_high", pole.position + pole.forward * 30f + pole.right * 14f + Vector3.up * 16f, (pole.position + last.position) * 0.5f, 55f);
                Shot(cam, prefix + "03_start_gantry", track.GetPoint(-55f) + Vector3.up * 3f, track.GetPoint(0f) + Vector3.up * 4f, 62f);
                for (int k = 0; k < 4; k++)
                {
                    float d = (0.12f + k * 0.22f) * track.Length;
                    Vector3 p = track.GetPoint(d), t = track.GetTangent(d);
                    Shot(cam, prefix + $"1{k}_racing_view", p - t * 8f + Vector3.up * 3f, p + t * 10f + Vector3.up * 1.2f, 62f);
                }
                var cp = GameObject.Find("Checkpoint 2").transform;
                Shot(cam, prefix + "20_checkpoint", cp.position - cp.forward * 26f + Vector3.up * 3f, cp.position + Vector3.up * 3.5f, 60f);
                Shot(cam, prefix + "30_overview", track.GetPoint(0.5f * track.Length) * 0.15f + new Vector3(0, 520f, -260f), Vector3.zero, 55f, 1600, 900);
                UnityEngine.Object.DestroyImmediate(camGo);
            }

            // Cars in the garage showroom (front 3/4 and rear with the unisolutions.in plate).
            EditorSceneManager.OpenScene(ProjectBuilder.MenuScenePath, OpenSceneMode.Single);
            var turntable = GameObject.Find("Turntable").transform;
            var cam2 = MakeCamera(out var cam2Go);
            for (int i = 0; i < catalog.Count; i++)
            {
                var car = (GameObject)PrefabUtility.InstantiatePrefab(catalog.cars[i].prefab, turntable);
                car.transform.localPosition = Vector3.zero;
                Vector3 c = car.transform.position + Vector3.up * 0.7f;
                Shot(cam2, $"CAR{i + 1}_{catalog.cars[i].id}_front", c + new Vector3(-3.6f, 0.9f, 4.6f), c, 38f, 1200, 800);
                Shot(cam2, $"CAR{i + 1}_{catalog.cars[i].id}_rear", c + new Vector3(1.8f, 0.8f, -5.2f), c, 38f, 1200, 800);
                UnityEngine.Object.DestroyImmediate(car);
            }
            UnityEngine.Object.DestroyImmediate(cam2Go);
            Debug.Log($"[PreviewCapture] Images written to {outDir}");
        }

        static Camera MakeCamera(out GameObject go)
        {
            go = new GameObject("Capture Camera");
            var cam = go.AddComponent<Camera>();
            cam.farClipPlane = 2600f;
            cam.nearClipPlane = 0.1f;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            data.renderPostProcessing = true;
            data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            Canvas.ForceUpdateCanvases();
            return cam;
        }

        public static void Shot(Camera cam, string name, Vector3 position, Vector3 lookAt, float fov, int width = 1600, int height = 900)
        {
            cam.transform.position = position;
            cam.transform.LookAt(lookAt);
            cam.fieldOfView = fov;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
