using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TA.ArtTools.Editor
{
    internal static class TaPreIntegratedLutBaker
    {
        const string ShaderGuid = "f30e8a58bdef4fb18f2a462cbeff7519";

        internal static ComputeShader LoadShader()
        {
            return AssetDatabase.LoadAssetAtPath<ComputeShader>(AssetDatabase.GUIDToAssetPath(ShaderGuid));
        }

        internal static RenderTexture Bake(ComputeShader shader, Color falloff, bool keepDirectBounce, int resolution)
        {
            if (shader == null)
                throw new InvalidOperationException("未找到 LUT 积分 Compute Shader，请检查工具文件是否完整。");
            if (!SystemInfo.supportsComputeShaders ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf))
                throw new NotSupportedException("当前图形设备不支持 Compute Shader 或 ARGBHalf 随机写入。");
            if (resolution < 8 || resolution > 512 || resolution > SystemInfo.maxTextureSize)
                throw new ArgumentOutOfRangeException(nameof(resolution), "分辨率必须位于 8–512 且不超过设备限制。");
            if (!ValidChannel(falloff.r) || !ValidChannel(falloff.g) || !ValidChannel(falloff.b))
                throw new ArgumentException("Falloff RGB 必须为 0–1 的有限数值。", nameof(falloff));

            var descriptor = new RenderTextureDescriptor(resolution, resolution, RenderTextureFormat.ARGBHalf, 0)
            {
                enableRandomWrite = true,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false,
                msaaSamples = 1
            };
            var target = new RenderTexture(descriptor)
            {
                name = "TA PreIntegrated LUT Preview",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            try
            {
                if (!target.Create())
                    throw new InvalidOperationException("无法创建 LUT 烘焙纹理。");
                int kernel = shader.FindKernel("CSMain");
                bool previousKeyword = shader.IsKeywordEnabled("KEEP_DIRECT_BOUNCE");
                try
                {
                    if (keepDirectBounce) shader.EnableKeyword("KEEP_DIRECT_BOUNCE");
                    else shader.DisableKeyword("KEEP_DIRECT_BOUNCE");
                    shader.SetTexture(kernel, "_IntegratedLUT", target);
                    shader.SetVector("_FalloffColor", falloff);
                    shader.SetInt("_Resolution", resolution);
                    shader.GetKernelThreadGroupSizes(kernel, out uint x, out uint y, out uint z);
                    shader.Dispatch(kernel, Mathf.CeilToInt(resolution / (float)x), Mathf.CeilToInt(resolution / (float)y), 1);
                }
                finally
                {
                    if (previousKeyword) shader.EnableKeyword("KEEP_DIRECT_BOUNCE");
                    else shader.DisableKeyword("KEEP_DIRECT_BOUNCE");
                }
                return target;
            }
            catch
            {
                Release(target);
                throw;
            }
        }

        internal static Texture2D SaveTga(RenderTexture lut, string assetPath)
        {
            if (lut == null || !lut.IsCreated())
                throw new InvalidOperationException("请先烘焙 LUT。");
            assetPath = (assetPath ?? "").Replace('\\', '/');
            string absolutePath = Path.GetFullPath(assetPath);
            string assetsRoot = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !absolutePath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(assetPath), ".tga", StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(Path.GetDirectoryName(absolutePath)))
                throw new ArgumentException("请选择 Assets 内已有文件夹中的 .tga 文件路径。", nameof(assetPath));

            RenderTexture previousActive = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderTexture.active = lut;
                pixels = new Texture2D(lut.width, lut.height, TextureFormat.RGB24, false, true);
                pixels.ReadPixels(new Rect(0, 0, lut.width, lut.height), 0, 0, false);
                pixels.Apply(false, false);
                File.WriteAllBytes(absolutePath, pixels.EncodeToTGA());
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("无法导入生成的 LUT：" + assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(32, lut.width, lut.height));
            // A rebaked data LUT must not inherit lossy overrides from an older texture.
            foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL", "Windows Store Apps", "PS4", "PS5", "XboxOne", "Nintendo Switch", "tvOS" })
                importer.ClearPlatformTextureSettings(platform);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        internal static void Release(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }

        static bool ValidChannel(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
    }
}
