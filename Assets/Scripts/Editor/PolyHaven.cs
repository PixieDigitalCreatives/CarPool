using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RacingPrototype.EditorTools
{
    /// <summary>
    /// Real-world CC0 assets from Poly Haven (polyhaven.com, see Assets/ThirdParty/PolyHaven/LICENSE.md):
    /// photo-scanned PBR surfaces, a night-sky HDRI and scanned props and trees. Builds URP Lit
    /// materials from the source maps (albedo, OpenGL normal, AO/roughness/metal) and prefabs from
    /// the models. Every method returns null when an asset is missing so the generator can fall back
    /// to its procedural version.
    /// </summary>
    public static class PolyHaven
    {
        public const string Root = "Assets/ThirdParty/PolyHaven";
        const string MatFolder = "Assets/Materials/PolyHaven";
        const string PrefabFolder = "Assets/Prefabs/PolyHaven";

        /// <summary>Real-world width in metres of one texture repeat (Poly Haven metadata).</summary>
        static readonly Dictionary<string, float> SurfaceSize = new Dictionary<string, float>
        {
            { "clean_asphalt", 2.1f }, { "concrete_pavement_03", 2.1f }, { "patterned_concrete_pavers", 1.8f }, { "rough_concrete", 1.23f },
            { "beige_wall_001", 3f }, { "white_stucco", 2f }, { "painted_brick", 1.8f }, { "concrete_tile_facade", 2.09f },
            { "factory_wall", 3f }, { "corrugated_iron", 1.12f }, { "clay_roof_tiles_02", 2.5f },
        };

        public class Surface { public Texture2D albedo, normal, mask; public float size; }

        static readonly Dictionary<string, Surface> surfaces = new Dictionary<string, Surface>();

        // ================================================================== Surfaces

        public static Surface LoadSurface(string id)
        {
            if (surfaces.TryGetValue(id, out var cached)) return cached;
            string dir = $"{Root}/Textures/{id}";
            var albedo = Import($"{dir}/{id}_diff_2k.jpg", Kind.Colour);
            if (albedo == null) return null;
            var s = new Surface
            {
                albedo = albedo,
                normal = Import($"{dir}/{id}_nor_gl_2k.jpg", Kind.Normal),
                mask = PackMask($"{dir}/{id}_mask.png", $"{dir}/{id}_arm_2k.jpg", null, null),
                size = SurfaceSize.TryGetValue(id, out float size) ? size : 2f
            };
            surfaces[id] = s;
            return s;
        }

        /// <summary>
        /// URP Lit material from a scanned surface. <paramref name="metresPerUv"/> is the size of one UV
        /// unit on the target mesh. <paramref name="wetness"/> (0-1) gives the surface a rain-damp sheen
        /// with glossier puddle patches, slightly darker and flatter, so lights reflect in it.
        /// </summary>
        public static Material SurfaceMaterial(string name, string id, Color tint, float metresPerUv, float smoothness = 1f, float normalScale = 1f, bool doubleSided = false, float wetness = 0f)
        {
            var s = LoadSurface(id);
            if (s == null) return null;
            var mat = new Material(ProceduralAssets.Lit) { name = name };
            float darken = 1f - 0.3f * wetness;
            mat.SetColor("_BaseColor", new Color(tint.r * darken, tint.g * darken, tint.b * darken, tint.a));
            mat.SetTexture("_BaseMap", s.albedo);
            float tile = metresPerUv / s.size;
            mat.SetTextureScale("_BaseMap", new Vector2(tile, tile));
            var mask = wetness > 0f && s.mask != null ? WetMask(id, s.mask, wetness) : s.mask;
            ApplyMaps(mat, s.normal, mask, normalScale * (1f - 0.35f * wetness), smoothness);
            if (doubleSided) { mat.SetFloat("_Cull", 0f); mat.doubleSidedGI = true; }
            mat.enableInstancing = true;
            ProceduralAssets.EnsureFolder(MatFolder);
            return ProceduralAssets.SaveOrReplace(mat, $"{MatFolder}/{name}.mat");
        }

        /// <summary>
        /// Layers a real surface onto a material that keeps its own albedo (generated facades with
        /// windows, painted barriers) as URP Lit detail maps: x2 albedo variation and normal relief,
        /// with independent tiling. <paramref name="mask"/> (alpha) limits it, e.g. to wall areas so
        /// windows stay glass; null applies it everywhere.
        /// </summary>
        public static void AddDetail(Material mat, string id, float metresPerUv, Texture2D mask = null, float normalScale = 0.9f)
        {
            var s = LoadSurface(id);
            if (s == null) return;
            float tile = metresPerUv / s.size;
            mat.SetTexture("_DetailAlbedoMap", DetailAlbedo(id, s));
            mat.SetTextureScale("_DetailAlbedoMap", new Vector2(tile, tile));
            mat.SetFloat("_DetailAlbedoMapScale", 1f);
            mat.SetTexture("_DetailNormalMap", s.normal);
            mat.SetFloat("_DetailNormalMapScale", normalScale);
            if (mask != null) mat.SetTexture("_DetailMask", mask);
            mat.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(mat);
        }

        static void ApplyMaps(Material mat, Texture2D normal, Texture2D mask, float normalScale, float smoothness)
        {
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetFloat("_BumpScale", normalScale);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (mask != null)
            {
                // One packed map serves both slots: URP Lit reads metallic from R, smoothness from A, occlusion from G.
                mat.SetTexture("_MetallicGlossMap", mask);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetTexture("_OcclusionMap", mask);
                mat.SetFloat("_OcclusionStrength", 1f);
                mat.EnableKeyword("_OCCLUSIONMAP");
            }
            mat.SetFloat("_Smoothness", smoothness);
        }

        /// <summary>The surface mask with its smoothness (alpha) raised: a damp film everywhere, near-mirror puddles in patches.</summary>
        static Texture2D WetMask(string id, Texture2D mask, float wetness)
        {
            string path = $"{Root}/Textures/{id}/{id}_mask_wet.png";
            if (!File.Exists(Path.GetFullPath(path)))
            {
                var px = ReadPixels(AssetDatabase.GetAssetPath(mask), out int w, out int h);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = x / (float)w, v = y / (float)h;
                        float puddle = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.68f, TileNoise(u, v, 3f)));
                        int i = y * w + x;
                        float target = Mathf.Lerp(0.8f, 0.96f, puddle) * 255f;
                        px[i].a = (byte)Mathf.RoundToInt(Mathf.Lerp(px[i].a, target, wetness * (0.6f + 0.4f * puddle)));
                    }
                WritePng(path, px, w, h);
            }
            return Import(path, Kind.Linear);
        }

        /// <summary>Perlin noise that wraps at the texture edges (blend of four offset samples).</summary>
        static float TileNoise(float u, float v, float frequency)
        {
            float a = Mathf.PerlinNoise(u * frequency, v * frequency), b = Mathf.PerlinNoise((u - 1f) * frequency, v * frequency);
            float c = Mathf.PerlinNoise(u * frequency, (v - 1f) * frequency), d = Mathf.PerlinNoise((u - 1f) * frequency, (v - 1f) * frequency);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>Neutral-grey (0.5 = no change) luminance map of the surface for the x2 detail albedo slot.</summary>
        static Texture2D DetailAlbedo(string id, Surface s)
        {
            string path = $"{Root}/Textures/{id}/{id}_detail.png";
            if (!File.Exists(path))
            {
                var src = ReadPixels(AssetDatabase.GetAssetPath(s.albedo), out int w, out int h);
                int dw = w / 2, dh = h / 2;
                var lum = new float[dw * dh];
                double mean = 0;
                for (int y = 0; y < dh; y++)
                    for (int x = 0; x < dw; x++)
                    {
                        var c = src[(y * 2) * w + x * 2];
                        float l = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
                        lum[y * dw + x] = l;
                        mean += l;
                    }
                mean /= lum.Length;
                var px = new Color32[dw * dh];
                for (int i = 0; i < px.Length; i++)
                {
                    byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(127.5f * Mathf.Clamp(lum[i] / (float)mean, 0.35f, 1.65f)), 0, 255);
                    px[i] = new Color32(v, v, v, 255);
                }
                WritePng(path, px, dw, dh);
            }
            return Import(path, Kind.Linear);
        }

        // ================================================================== HDRI sky

        /// <summary>Panoramic skybox from the downloaded night HDRI.</summary>
        public static Material NightSky(float exposure, Color tint)
        {
            string path = Directory.Exists(Path.GetFullPath($"{Root}/HDRI"))
                ? Directory.GetFiles(Path.GetFullPath($"{Root}/HDRI"), "*.hdr").Select(ToAssetPath).FirstOrDefault() : null;
            if (path == null) return null;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureShape != TextureImporterShape.Texture2D || importer.mipmapEnabled || importer.wrapModeU != TextureWrapMode.Repeat)
            {
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.mipmapEnabled = false;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            var hdr = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var mat = new Material(Shader.Find("Skybox/Panoramic")) { name = "Sky_Night_HDRI" };
            mat.SetTexture("_MainTex", hdr);
            mat.SetFloat("_Mapping", 1f);      // latitude-longitude layout
            mat.SetFloat("_ImageType", 0f);    // 360 degrees
            mat.SetFloat("_Exposure", exposure);
            mat.SetColor("_Tint", tint);   // 0.5 grey = neutral
            return ProceduralAssets.SaveOrReplace(mat, $"{ProceduralAssets.MaterialFolder}/Sky_Night_HDRI.mat");
        }

        // ================================================================== Models

        /// <summary>
        /// Prefab of a scanned model with URP Lit materials. Uses the finest LOD within
        /// <paramref name="maxTriangles"/> (the scans ship millions of triangles at LOD0) and culls it
        /// below <paramref name="cullScreenHeight"/>. Returns null when the model is not downloaded.
        /// </summary>
        public static GameObject Prefab(string id, int maxTriangles, float cullScreenHeight = 0.012f, System.Action<GameObject, Bounds> extra = null)
        {
            string dir = $"{Root}/Models/{id}";
            string fbx = $"{dir}/{id}.fbx";
            if (!(AssetImporter.GetAtPath(fbx) is ModelImporter importer)) return null;
            if (importer.importAnimation || importer.importCameras || importer.importLights || importer.animationType != ModelImporterAnimationType.None ||
                importer.materialImportMode != ModelImporterMaterialImportMode.ImportViaMaterialDescription)
            {
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.animationType = ModelImporterAnimationType.None;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                importer.SaveAndReimport();
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var root = new GameObject(id);
            var inst = Object.Instantiate(model, root.transform);
            inst.name = "Model";

            KeepLodWithin(inst, maxTriangles);

            // Poly Haven FBX files are authored in metres; guard against a centimetre export.
            var b = WorldBounds(root);
            if (b.size.y > 60f) { inst.transform.localScale *= 0.01f; b = WorldBounds(root); }
            // Origin at the base so placement code can drop it on the ground.
            inst.transform.localPosition -= new Vector3(0f, b.min.y, 0f);
            b = WorldBounds(root);

            var cache = new Dictionary<string, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = r.sharedMaterials.Select(src => ModelMaterial(id, dir, src, cache)).ToArray();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            // One LOD + cull so distant instances cost nothing.
            var oldGroup = inst.GetComponentInChildren<LODGroup>();
            if (oldGroup != null) Object.DestroyImmediate(oldGroup);
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(cullScreenHeight, root.GetComponentsInChildren<Renderer>(true)) });
            group.RecalculateBounds();

            extra?.Invoke(root, b);
            ProceduralAssets.EnsureFolder(PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{id}.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Billboard impostor of a scanned plant. The Poly Haven tree scans ship only their full
        /// LOD0 (millions of triangles) and trees line every route by the dozen, so each scan is
        /// rendered once (orthographic, evenly lit) from three directions into an atlas and shown on
        /// three crossed alpha-tested quads: photographic look, 6 triangles per tree.
        /// </summary>
        public static GameObject Impostor(string id, int resolution = 512)
        {
            var source = Prefab(id, int.MaxValue, 0.01f);
            if (source == null) return null;
            const int layer = 31, views = 3;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(source);
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            inst.GetComponent<LODGroup>().enabled = false;
            var b = WorldBounds(inst);
            float size = Mathf.Max(b.size.x, b.size.z, b.size.y) * 1.02f;
            Vector3 centre = new Vector3(b.center.x, b.min.y + size / 2f, b.center.z);

            var camGo = new GameObject("Impostor Camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = size / 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << layer;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = size * 4f;
            var lightGo = new GameObject("Impostor Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << layer;
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f);

            var rt = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            var atlas = new Color32[resolution * views * resolution];
            var read = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            for (int k = 0; k < views; k++)
            {
                float a = k * 180f / views * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                cam.transform.SetPositionAndRotation(centre - dir * size * 2f, Quaternion.LookRotation(dir, Vector3.up));
                lightGo.transform.rotation = Quaternion.LookRotation((dir + Vector3.down * 0.7f).normalized, Vector3.up);
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else cam.Render();
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
                RenderTexture.active = null;
                var px = read.GetPixels32();
                for (int y = 0; y < resolution; y++)
                    for (int x = 0; x < resolution; x++)
                        atlas[y * resolution * views + k * resolution + x] = px[y * resolution + x];
            }
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambient;
            Object.DestroyImmediate(read);
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(inst);

            Dilate(atlas, resolution * views, resolution, 6);   // bleed leaf colour into the gaps so mips keep clean edges
            string texPath = $"{Root}/Models/{id}/{id}_impostor.png";
            WritePng(texPath, atlas, resolution * views, resolution);
            var albedo = Import(texPath, Kind.Colour);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            if (!importer.alphaIsTransparency || !importer.mipMapsPreserveCoverage || importer.wrapMode != TextureWrapMode.Clamp)
            {
                importer.alphaIsTransparency = true;
                importer.mipMapsPreserveCoverage = true;
                importer.alphaTestReferenceValue = 0.5f;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }

            var mat = new Material(ProceduralAssets.Lit) { name = $"PH_{id}_Impostor" };
            mat.SetTexture("_BaseMap", albedo);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.05f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0f);
            mat.doubleSidedGI = true;
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.enableInstancing = true;
            ProceduralAssets.EnsureFolder(MatFolder);
            mat = ProceduralAssets.SaveOrReplace(mat, $"{MatFolder}/PH_{id}_Impostor.mat");

            var kit = new MeshKit();
            Vector3 c0 = new Vector3(centre.x, 0f, centre.z);
            for (int k = 0; k < views; k++)
            {
                float a = k * 180f / views * Mathf.Deg2Rad;
                Vector3 right = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)) * (size / 2f);
                float u0 = k / (float)views, u1 = (k + 1) / (float)views;
                // Up-facing normals: the night lights (moon, lamps overhead) light every plane evenly.
                kit.QuadUV(c0 - right, c0 + right, c0 + right + Vector3.up * size, c0 - right + Vector3.up * size,
                    new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 1), new Vector2(u0, 1), Vector3.up);
            }
            ProceduralAssets.EnsureFolder("Assets/Models/Environment");
            var mesh = ProceduralAssets.SaveOrReplace(kit.Build(id + "_Impostor"), $"Assets/Models/Environment/{id}_Impostor.asset");
            var root = new GameObject(id + "_impostor");
            var quad = new GameObject("Billboard");
            quad.transform.SetParent(root.transform, false);
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = quad.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            var group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.006f, new Renderer[] { mr }) });
            group.RecalculateBounds();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{id}_impostor.prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[PolyHaven] {id}: {size:0.0} m impostor from a {Triangles(source.GetComponentsInChildren<Renderer>(true))}-triangle scan");
            return prefab;
        }

        /// <summary>Copies colour from opaque neighbours into transparent pixels (alpha untouched).</summary>
        static void Dilate(Color32[] px, int w, int h, int passes)
        {
            var src = (Color32[])px.Clone();
            for (int p = 0; p < passes; p++)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (src[i].a > 0 || src[i].r > 0 || src[i].g > 0 || src[i].b > 0) continue;
                        int r = 0, g = 0, bl = 0, n = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                                var c = src[yy * w + xx];
                                if (c.a == 0 && c.r == 0 && c.g == 0 && c.b == 0) continue;
                                r += c.r; g += c.g; bl += c.b; n++;
                            }
                        if (n > 0) px[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(bl / n), 0);
                    }
                System.Array.Copy(px, src, px.Length);
            }
        }

        /// <summary>Destroys LOD levels above the triangle budget (keeps the finest one that fits).</summary>
        static void KeepLodWithin(GameObject inst, int maxTriangles)
        {
            var group = inst.GetComponentInChildren<LODGroup>();
            if (group == null) return;
            var lods = group.GetLODs();
            int keep = lods.Length - 1;
            for (int i = 0; i < lods.Length; i++)
                if (Triangles(lods[i].renderers) <= maxTriangles) { keep = i; break; }
            for (int i = 0; i < lods.Length; i++)
            {
                if (i == keep) continue;
                foreach (var r in lods[i].renderers) if (r != null) Object.DestroyImmediate(r.gameObject);
            }
            Debug.Log($"[PolyHaven] {inst.transform.parent.name}: kept LOD{keep} ({Triangles(lods[keep].renderers)} tris) of {lods.Length}");
        }

        static int Triangles(IEnumerable<Renderer> renderers)
        {
            int t = 0;
            foreach (var r in renderers)
            {
                var mf = r != null ? r.GetComponent<MeshFilter>() : null;
                if (mf != null && mf.sharedMesh != null) t += mf.sharedMesh.triangles.Length / 3;
            }
            return t;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>URP Lit material for one FBX material slot, from the textures named after it.</summary>
        static Material ModelMaterial(string id, string dir, Material source, Dictionary<string, Material> cache)
        {
            string slot = source != null ? source.name : id;
            if (cache.TryGetValue(slot, out var done)) return done;
            string texDir = $"{dir}/textures";
            var files = Directory.Exists(Path.GetFullPath(texDir)) ? Directory.GetFiles(Path.GetFullPath(texDir)).Select(ToAssetPath).ToArray() : new string[0];
            // Texture prefix: the slot name when textures carry it, else the asset id.
            string prefix = files.Any(f => Path.GetFileName(f).StartsWith(slot + "_")) ? slot : id;
            string Find(string map) => files.FirstOrDefault(f =>
            {
                string n = Path.GetFileNameWithoutExtension(f);
                return n.StartsWith(prefix + "_" + map + "_") && !n.EndsWith("_mask") && !n.EndsWith("_rgba");
            });

            string diff = Find("diff"), alpha = Find("alpha") ?? Find("opacity");
            Texture2D albedo = diff != null ? (alpha != null ? CombineAlpha(diff, alpha) : Import(diff, Kind.Colour)) : null;
            var normal = Find("nor_gl") is string n ? Import(n, Kind.Normal) : null;
            var mask = PackMask($"{texDir}/{prefix}_mask.png", Find("arm"), Find("rough"), Find("metal"));

            var mat = new Material(ProceduralAssets.Lit) { name = "PH_" + slot };
            mat.SetColor("_BaseColor", Color.white);
            if (albedo != null) mat.SetTexture("_BaseMap", albedo);
            ApplyMaps(mat, normal, mask, 1f, 1f);
            if (alpha != null)
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.45f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 0f);
                mat.doubleSidedGI = true;
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                mat.SetOverrideTag("RenderType", "TransparentCutout");
            }
            mat.enableInstancing = true;
            ProceduralAssets.EnsureFolder(MatFolder);
            var saved = ProceduralAssets.SaveOrReplace(mat, $"{MatFolder}/PH_{slot}.mat");
            cache[slot] = saved;
            return saved;
        }

        // ================================================================== Texture helpers

        enum Kind { Colour, Linear, Normal }

        static Texture2D Import(string path, Kind kind)
        {
            if (path == null || !File.Exists(Path.GetFullPath(path))) return null;
            if (AssetImporter.GetAtPath(path) == null) AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var type = kind == Kind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            bool srgb = kind == Kind.Colour;
            if (importer.textureType != type || importer.sRGBTexture != srgb || importer.anisoLevel != 8 || importer.maxTextureSize != 2048)
            {
                importer.textureType = type;
                importer.sRGBTexture = srgb;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 8;
                importer.maxTextureSize = 2048;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Packs AO / roughness / metal sources into one URP Lit mask (R metal, G AO, A smoothness).</summary>
        static Texture2D PackMask(string outPath, string armPath, string roughPath, string metalPath)
        {
            if (!File.Exists(Path.GetFullPath(outPath)))
            {
                Color32[] arm = null, rough = null, metal = null;
                int w = 0, h = 0;
                if (armPath != null) arm = ReadPixels(armPath, out w, out h);
                else if (roughPath != null)
                {
                    rough = ReadPixels(roughPath, out w, out h);
                    if (metalPath != null)
                    {
                        metal = ReadPixels(metalPath, out int mw, out int mh);
                        if (mw != w || mh != h) metal = null;
                    }
                }
                else return null;
                var px = new Color32[w * h];
                for (int i = 0; i < px.Length; i++)
                {
                    byte ao = arm != null ? arm[i].r : (byte)255;
                    byte r = arm != null ? arm[i].g : rough[i].r;
                    byte mt = arm != null ? arm[i].b : metal != null ? metal[i].r : (byte)0;
                    px[i] = new Color32(mt, ao, 0, (byte)(255 - r));
                }
                WritePng(outPath, px, w, h);
            }
            return Import(outPath, Kind.Linear);
        }

        /// <summary>Albedo with the separate alpha/opacity map in its alpha channel (leaves, glass).</summary>
        static Texture2D CombineAlpha(string diffPath, string alphaPath)
        {
            string outPath = Path.ChangeExtension(diffPath, null) + "_rgba.png";
            if (!File.Exists(Path.GetFullPath(outPath)))
            {
                var c = ReadPixels(diffPath, out int w, out int h);
                var a = ReadPixels(alphaPath, out int aw, out int ah);
                // The separate maps are 16-bit greyscale, which LoadImage can't decode (it leaves a 2x2
                // texture); the foliage albedo PNGs carry the same cutout in their own alpha channel.
                bool ownAlpha = false;
                for (int i = 0; i < c.Length && !ownAlpha; i += 97) ownAlpha = c[i].a < 250;
                if (!ownAlpha && aw == w && ah == h)
                    for (int i = 0; i < c.Length; i++) c[i].a = a[i].r;
                WritePng(outPath, c, w, h);
            }
            var tex = Import(outPath, Kind.Colour);
            var importer = (TextureImporter)AssetImporter.GetAtPath(outPath);
            if (!importer.alphaIsTransparency || importer.mipMapsPreserveCoverage == false)
            {
                importer.alphaIsTransparency = true;
                importer.mipMapsPreserveCoverage = true;   // foliage keeps its density in the distance
                importer.alphaTestReferenceValue = 0.45f;
                importer.SaveAndReimport();
            }
            return tex;
        }

        static Color32[] ReadPixels(string assetPath, out int w, out int h)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(Path.GetFullPath(assetPath)));
            w = tex.width; h = tex.height;
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        static void WritePng(string assetPath, Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(px);
            File.WriteAllBytes(Path.GetFullPath(assetPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        static string ToAssetPath(string full)
        {
            string project = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
            return full.Replace('\\', '/').Replace(project, "");
        }
    }
}
