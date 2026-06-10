using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TA.ArtTools.Editor
{
    public static class TaCsvUtility
    {
        public static string ToCsvLine(params string[] fields)
        {
            if (fields == null || fields.Length == 0)
                return string.Empty;

            var builder = new StringBuilder();
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0)
                    builder.Append(',');
                builder.Append(EscapeField(fields[i]));
            }
            return builder.ToString();
        }

        public static string EscapeField(string field)
        {
            field = field ?? string.Empty;
            bool quote = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            string escaped = field.Replace("\"", "\"\"");
            return quote ? "\"" + escaped + "\"" : escaped;
        }

        public static void ExportReport(string path, ArtToolReport report)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(ToCsvLine("工具", "级别", "写入", "标题", "详情", "资源路径"));
                if (report == null)
                    return;

                foreach (ArtToolChange change in report.Changes)
                {
                    if (change == null)
                        continue;

                    writer.WriteLine(ToCsvLine(
                        report.ToolName,
                        change.SeverityText,
                        change.IsWriteOperation ? "是" : "否",
                        change.Title,
                        change.Detail,
                        change.AssetPath));
                }
            }
        }
    }

    public static class TaAssetSearchUtility
    {
        public static List<string> FindPrefabAndMaterialPaths(IEnumerable<string> folders)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (folders == null)
                return result.ToList();

            foreach (string folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                    continue;

                AddFindAssets(result, "t:Prefab", folder);
                AddFindAssets(result, "t:Material", folder);
            }

            return result.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<string> FindAssetPaths(string filter, string folder)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                return result;

            string[] guids = AssetDatabase.FindAssets(filter, new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    result.Add(path);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            path = path.Replace("\\", "/");
            string dataPath = Application.dataPath.Replace("\\", "/");
            if (path.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return "Assets" + path.Substring(dataPath.Length);

            return path;
        }

        public static string ResolveSelectedFolderPath(UnityEngine.Object selected)
        {
            if (selected == null)
                return string.Empty;

            string path = NormalizeAssetPath(AssetDatabase.GetAssetPath(selected));
            return ResolveAssetPathToFolder(path);
        }

        public static string ResolveSelectedFolderPath(IEnumerable<string> assetGuids, IEnumerable<UnityEngine.Object> selectedObjects)
        {
            if (assetGuids != null)
            {
                foreach (string guid in assetGuids)
                {
                    if (string.IsNullOrEmpty(guid))
                        continue;

                    string resolved = ResolveAssetPathToFolder(AssetDatabase.GUIDToAssetPath(guid));
                    if (!string.IsNullOrEmpty(resolved))
                        return resolved;
                }
            }

            if (selectedObjects != null)
            {
                foreach (UnityEngine.Object selected in selectedObjects)
                {
                    string resolved = ResolveSelectedFolderPath(selected);
                    if (!string.IsNullOrEmpty(resolved))
                        return resolved;
                }
            }

            return ResolveSelectedFolderPath(Selection.activeObject);
        }

        public static string ResolveCurrentProjectSelectionFolderPath()
        {
            return ResolveSelectedFolderPath(Selection.assetGUIDs, Selection.objects);
        }

        static string ResolveAssetPathToFolder(string path)
        {
            path = NormalizeAssetPath(path);
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            if (AssetDatabase.IsValidFolder(path))
                return path;

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            string folder = NormalizeAssetPath(Path.GetDirectoryName(path));
            return AssetDatabase.IsValidFolder(folder) ? folder : string.Empty;
        }

        public static bool IsEditableAssetPath(string path, string extension)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && !string.Equals(path, "Assets", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(extension) && !Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        static void AddFindAssets(HashSet<string> result, string filter, string folder)
        {
            string[] guids = AssetDatabase.FindAssets(filter, new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    result.Add(path);
            }
        }
    }

    public static class TaTexturePlatformUtility
    {
        static readonly TextureImporterFormat[] AstcBatchTargetFormats =
        {
            TextureImporterFormat.ASTC_5x5,
            TextureImporterFormat.ASTC_6x6,
            TextureImporterFormat.ASTC_8x8
        };

        public static TextureImporterFormat[] GetAstcBatchTargetFormats()
        {
            return AstcBatchTargetFormats.ToArray();
        }

        public static bool IsAstcBatchTargetFormat(TextureImporterFormat format)
        {
            foreach (TextureImporterFormat supported in AstcBatchTargetFormats)
            {
                if (supported == format)
                    return true;
            }
            return false;
        }

        public static bool ShouldConvertAstc5To6(TextureImporterFormat format)
        {
            return format == TextureImporterFormat.ASTC_5x5;
        }

        public static bool ShouldChangeFormat(TextureImporterFormat currentFormat, TextureImporterFormat targetFormat)
        {
            return currentFormat != targetFormat;
        }

        public static TextureImporterFormat GetActualFormat(TextureImporter importer, string platform)
        {
            if (importer == null)
                return TextureImporterFormat.RGBA32;

            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            return settings.overridden ? settings.format : importer.GetAutomaticFormat(platform);
        }

        public static bool ConvertPlatformAstc5To6(TextureImporter importer, string platform)
        {
            if (importer == null)
                return false;

            TextureImporterFormat actualFormat = GetActualFormat(importer, platform);
            if (!ShouldConvertAstc5To6(actualFormat))
                return false;

            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true;
            settings.format = TextureImporterFormat.ASTC_6x6;
            if (settings.maxTextureSize <= 0)
                settings.maxTextureSize = importer.maxTextureSize;
            importer.SetPlatformTextureSettings(settings);
            return true;
        }

        public static bool SetPlatformSizeAndAstc(TextureImporter importer, string platform, int maxSize)
        {
            if (importer == null || maxSize <= 0)
                return false;

            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            bool changed = !settings.overridden ||
                           settings.maxTextureSize != maxSize ||
                           settings.format != TextureImporterFormat.ASTC_6x6;

            settings.overridden = true;
            settings.maxTextureSize = maxSize;
            settings.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(settings);
            return changed;
        }

        public static bool SetPlatformFormatPreserveSize(
            TextureImporter importer,
            string platform,
            TextureImporterFormat targetFormat,
            out TextureImporterFormat oldFormat,
            out TextureImporterFormat newFormat)
        {
            oldFormat = TextureImporterFormat.RGBA32;
            newFormat = targetFormat;

            if (importer == null || !IsAstcBatchTargetFormat(targetFormat))
                return false;

            oldFormat = GetActualFormat(importer, platform);
            if (!ShouldChangeFormat(oldFormat, targetFormat))
                return false;

            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            int maxTextureSize = settings.overridden && settings.maxTextureSize > 0
                ? settings.maxTextureSize
                : importer.maxTextureSize;

            settings.overridden = true;
            settings.maxTextureSize = maxTextureSize > 0 ? maxTextureSize : 2048;
            settings.format = targetFormat;
            importer.SetPlatformTextureSettings(settings);
            return true;
        }
    }

    public static class TaMaterialScanUtility
    {
        static readonly Dictionary<Shader, string[]> TexturePropertyCache = new Dictionary<Shader, string[]>();

        public static IEnumerable<Texture2D> EnumerateMaterialTextures(Material material)
        {
            if (material == null || material.shader == null)
                yield break;

            string[] properties = GetTextureProperties(material.shader);
            foreach (string property in properties)
            {
                Texture texture = material.GetTexture(property);
                if (texture is Texture2D texture2D)
                    yield return texture2D;
            }
        }

        public static string[] GetTextureProperties(Shader shader)
        {
            if (shader == null)
                return Array.Empty<string>();

            if (TexturePropertyCache.TryGetValue(shader, out string[] cached))
                return cached;

            var result = new List<string>();
            int count = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                    result.Add(ShaderUtil.GetPropertyName(shader, i));
            }

            cached = result.ToArray();
            TexturePropertyCache[shader] = cached;
            return cached;
        }
    }

    public static class TaVfxTextureOptimizerUtility
    {
        public const string CsvPath = "Assets/Art_TA/ArtTools/TAArtTools/Data/VFX_Texture_Optimize_Log.csv";

        static readonly int[] SizeOptions =
        {
            32,
            64,
            128,
            256,
            512
        };

        public static int[] GetSizeOptions()
        {
            return (int[])SizeOptions.Clone();
        }

        public static string MakeCsvRecordKey(string textureAssetPath, string textureName)
        {
            string dirPath = Path.GetDirectoryName(textureAssetPath);
            if (string.IsNullOrEmpty(dirPath))
                return textureName ?? string.Empty;

            return dirPath.Replace("\\", "/") + "/" + (textureName ?? string.Empty);
        }
    }

    public static class TaTextureUsageAnalyzerUtility
    {
        static readonly string[] SortLabels =
        {
            "引用数",
            "Android尺寸",
            "iOS尺寸",
            "宽度",
            "高度"
        };

        public static string[] GetSortLabels()
        {
            return (string[])SortLabels.Clone();
        }
    }
}
