using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AssetCloneIsolation.Editor
{
    /// <summary>
    /// Shared path, GUID, and text rewrite helpers for clone isolation.
    /// </summary>
    public static class AssetCloneIsolationUtility
    {
        /// <summary>
        /// Regex for Unity YAML object reference GUID fields.
        /// </summary>
        static readonly Regex AssetGuidReferenceRegex = new Regex(@"\bguid:\s*([0-9a-fA-F]{32})\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex for the asset's own GUID line inside a meta file.
        /// </summary>
        static readonly Regex MetaGuidRegex = new Regex(@"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex used to collapse duplicate path separators.
        /// </summary>
        static readonly Regex MultiSlashRegex = new Regex(@"/+", RegexOptions.Compiled);

        /// <summary>
        /// Extensions that Unity normally serializes as editable text when Force Text is enabled.
        /// </summary>
        static readonly HashSet<string> TextAssetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".anim",
            ".asmdef",
            ".asmref",
            ".asset",
            ".cginc",
            ".compute",
            ".controller",
            ".hlsl",
            ".mat",
            ".meta",
            ".mixer",
            ".overridecontroller",
            ".playable",
            ".prefab",
            ".rendertexture",
            ".shader",
            ".shadergraph",
            ".shadersubgraph",
            ".spriteatlas",
            ".terrainlayer",
            ".unity",
            ".uss",
            ".uxml",
            ".vfx"
        };

        /// <summary>
        /// Extensions that are allowed to remain shared across project art roots.
        /// </summary>
        static readonly HashSet<string> SharedCodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".asmdef",
            ".asmref",
            ".cs",
            ".dll"
        };

        /// <summary>
        /// Normalizes a Unity asset path to use forward slashes.
        /// </summary>
        public static string NormalizeAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return string.Empty;
            }

            string normalizedPath = assetPath.Trim().Replace('\\', '/').Trim('/');
            return MultiSlashRegex.Replace(normalizedPath, "/");
        }

        /// <summary>
        /// Returns true when an asset path is equal to or under a root path.
        /// </summary>
        public static bool IsUnderRoot(string assetPath, string rootPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            string normalizedRoot = NormalizeAssetPath(rootPath);
            return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the target path by replacing the source root prefix with the target root prefix.
        /// </summary>
        public static string BuildTargetPath(string sourceAssetPath, string sourceRoot, string targetRoot)
        {
            string normalizedSourcePath = NormalizeAssetPath(sourceAssetPath);
            string normalizedSourceRoot = NormalizeAssetPath(sourceRoot);
            string normalizedTargetRoot = NormalizeAssetPath(targetRoot);

            if (!IsUnderRoot(normalizedSourcePath, normalizedSourceRoot))
            {
                throw new ArgumentException("Source asset is not under SourceRoot: " + normalizedSourcePath);
            }

            if (normalizedSourcePath.Equals(normalizedSourceRoot, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedTargetRoot;
            }

            string relativePath = normalizedSourcePath.Substring(normalizedSourceRoot.Length).TrimStart('/');
            return NormalizeAssetPath(normalizedTargetRoot + "/" + relativePath);
        }

        /// <summary>
        /// Builds the target path for an external Assets dependency cloned into the target root.
        /// </summary>
        public static string BuildExternalTargetPath(string assetPath, string targetRoot)
        {
            string normalizedAssetPath = NormalizeAssetPath(assetPath);
            string normalizedTargetRoot = NormalizeAssetPath(targetRoot);
            if (!normalizedAssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("External clone asset must start with Assets/: " + normalizedAssetPath);
            }

            return NormalizeAssetPath(normalizedTargetRoot + "/_External/" + normalizedAssetPath);
        }

        /// <summary>
        /// Builds the clone target path for a source-root asset or an explicitly cloned external dependency.
        /// </summary>
        public static string BuildCloneTargetPath(string assetPath, AssetCloneIsolationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            string normalizedAssetPath = NormalizeAssetPath(assetPath);
            if (IsUnderRoot(normalizedAssetPath, options.SourceRoot))
            {
                return BuildTargetPath(normalizedAssetPath, options.SourceRoot, options.TargetRoot);
            }

            return BuildExternalTargetPath(normalizedAssetPath, options.TargetRoot);
        }

        /// <summary>
        /// Returns true when the asset is safe to rewrite as UTF-8 text.
        /// </summary>
        public static bool IsTextAssetPath(string assetPath)
        {
            string extension = Path.GetExtension(NormalizeAssetPath(assetPath));
            return TextAssetExtensions.Contains(extension);
        }

        /// <summary>
        /// Returns true when the dependency may stay shared instead of cloned.
        /// </summary>
        public static bool IsAllowedSharedDependencyPath(string dependencyPath, string sourceRoot, string targetRoot)
        {
            string normalizedPath = NormalizeAssetPath(dependencyPath);

            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            if (IsUnderRoot(normalizedPath, targetRoot))
            {
                return true;
            }

            if (normalizedPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedPath.StartsWith("Library/PackageCache/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedPath.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedPath.StartsWith("Resources/unity_builtin_extra", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string extension = Path.GetExtension(normalizedPath);
            return normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                   && SharedCodeExtensions.Contains(extension);
        }

        /// <summary>
        /// Returns true when a material shader path is valid for an isolated target root.
        /// </summary>
        public static bool IsAllowedShaderPath(string shaderPath, string targetRoot)
        {
            string normalizedPath = NormalizeAssetPath(shaderPath);

            if (string.IsNullOrEmpty(normalizedPath))
            {
                return false;
            }

            return IsUnderRoot(normalizedPath, targetRoot)
                   || normalizedPath.StartsWith("Packages/com.unity.render-pipelines.", StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.StartsWith("Packages/com.unity.shadergraph", StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.StartsWith("Library/PackageCache/com.unity.render-pipelines.", StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.StartsWith("Library/PackageCache/com.unity.shadergraph", StringComparison.OrdinalIgnoreCase)
                   || normalizedPath.StartsWith("Resources/unity_builtin_extra", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Rewrites Unity YAML GUID fields according to the supplied map.
        /// </summary>
        public static string RewriteGuidReferences(string sourceText, IReadOnlyDictionary<string, string> guidMap, out int replacementCount)
        {
            int localReplacementCount = 0;

            if (string.IsNullOrEmpty(sourceText) || guidMap == null || guidMap.Count == 0)
            {
                replacementCount = 0;
                return sourceText;
            }

            string rewrittenText = AssetGuidReferenceRegex.Replace(sourceText, match =>
            {
                Group guidGroup = match.Groups[1];
                string oldGuid = guidGroup.Value.ToLowerInvariant();
                string newGuid;
                if (!guidMap.TryGetValue(oldGuid, out newGuid))
                {
                    return match.Value;
                }

                localReplacementCount++;
                int guidOffset = guidGroup.Index - match.Index;
                return match.Value.Substring(0, guidOffset) + newGuid.ToLowerInvariant();
            });

            replacementCount = localReplacementCount;
            return rewrittenText;
        }

        /// <summary>
        /// Extracts Unity YAML GUID references from text.
        /// </summary>
        public static HashSet<string> ExtractGuidReferences(string sourceText)
        {
            var guidSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(sourceText))
            {
                return guidSet;
            }

            string[] lines = sourceText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex];
                MatchCollection matches = AssetGuidReferenceRegex.Matches(line);
                if (matches.Count == 0)
                {
                    continue;
                }

                if (!IsLikelyUnityObjectReferenceLine(line) && !IsLikelyUnityObjectReferenceBlock(lines, lineIndex))
                {
                    continue;
                }

                foreach (Match match in matches)
                {
                    guidSet.Add(match.Groups[1].Value.ToLowerInvariant());
                }
            }

            return guidSet;
        }

        /// <summary>
        /// Reads the own asset GUID from a meta file body.
        /// </summary>
        public static bool TryReadMetaGuid(string metaText, out string guid)
        {
            guid = string.Empty;

            if (string.IsNullOrEmpty(metaText))
            {
                return false;
            }

            Match match = MetaGuidRegex.Match(metaText);
            if (!match.Success)
            {
                return false;
            }

            guid = match.Groups[1].Value.ToLowerInvariant();
            return true;
        }

        /// <summary>
        /// Replaces or creates the own GUID line in a meta file.
        /// </summary>
        public static string ReplaceOrCreateMetaGuid(string metaText, string newGuid)
        {
            string normalizedGuid = (newGuid ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(metaText))
            {
                return "fileFormatVersion: 2\n" + "guid: " + normalizedGuid + "\n";
            }

            if (MetaGuidRegex.IsMatch(metaText))
            {
                return MetaGuidRegex.Replace(metaText, "guid: " + normalizedGuid, 1);
            }

            return "guid: " + normalizedGuid + "\n" + metaText;
        }

        /// <summary>
        /// Generates a Unity-compatible lower-case GUID.
        /// </summary>
        public static string GenerateUnityGuid()
        {
            return Guid.NewGuid().ToString("N").ToLowerInvariant();
        }

        /// <summary>
        /// Converts a Unity asset path into an absolute project file-system path.
        /// </summary>
        public static string ToProjectAbsolutePath(string assetPath)
        {
            string projectRoot = Directory.GetCurrentDirectory().Replace('\\', '/');
            return Path.Combine(projectRoot, NormalizeAssetPath(assetPath)).Replace('\\', '/');
        }

        /// <summary>
        /// Converts an absolute project file-system path into a Unity asset path when possible.
        /// </summary>
        public static string ToAssetPath(string absolutePath)
        {
            string projectRoot = Directory.GetCurrentDirectory().Replace('\\', '/').TrimEnd('/');
            string normalizedAbsolutePath = (absolutePath ?? string.Empty).Replace('\\', '/');
            if (!normalizedAbsolutePath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return NormalizeAssetPath(normalizedAbsolutePath.Substring(projectRoot.Length + 1));
        }

        /// <summary>
        /// Returns true when a GUID is empty or one of Unity's built-in placeholders.
        /// </summary>
        public static bool IsBuiltInOrEmptyGuid(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return true;
            }

            string normalizedGuid = guid.Trim().ToLowerInvariant();
            return normalizedGuid == "00000000000000000000000000000000"
                   || normalizedGuid == "0000000000000000e000000000000000"
                   || normalizedGuid == "0000000000000000f000000000000000";
        }

        /// <summary>
        /// Scans a binary file prefix for readable Unity YAML GUID markers.
        /// </summary>
        public static bool ContainsAsciiGuidMarker(string filePath)
        {
            const int maxBytesToScan = 1024 * 1024;
            byte[] markerBytes = Encoding.ASCII.GetBytes("guid:");
            FileInfo fileInfo = new FileInfo(filePath);
            byte[] buffer = new byte[Math.Min(maxBytesToScan, (int)Math.Min(fileInfo.Length, maxBytesToScan))];

            if (buffer.Length == 0)
            {
                return false;
            }

            using (FileStream fileStream = File.OpenRead(filePath))
            {
                int bytesRead = fileStream.Read(buffer, 0, buffer.Length);
                for (int index = 0; index <= bytesRead - markerBytes.Length; index++)
                {
                    if (MatchesAsciiMarker(buffer, markerBytes, index))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true when the dependency extension is configured as shared code.
        /// </summary>
        public static bool IsSharedCodeAssetPath(string assetPath)
        {
            string extension = Path.GetExtension(NormalizeAssetPath(assetPath));
            return SharedCodeExtensions.Contains(extension);
        }

        /// <summary>
        /// Checks whether a single line looks like an inline Unity object reference.
        /// </summary>
        static bool IsLikelyUnityObjectReferenceLine(string line)
        {
            return line.IndexOf("fileID:", StringComparison.OrdinalIgnoreCase) >= 0
                   && line.IndexOf("type:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Checks whether neighboring lines form a multi-line Unity object reference block.
        /// </summary>
        static bool IsLikelyUnityObjectReferenceBlock(IReadOnlyList<string> lines, int lineIndex)
        {
            bool hasFileId = false;
            bool hasType = false;
            int startIndex = Math.Max(0, lineIndex - 2);
            int endIndex = Math.Min(lines.Count - 1, lineIndex + 2);

            for (int index = startIndex; index <= endIndex; index++)
            {
                string line = lines[index];
                hasFileId |= line.IndexOf("fileID:", StringComparison.OrdinalIgnoreCase) >= 0;
                hasType |= line.IndexOf("type:", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return hasFileId && hasType;
        }

        /// <summary>
        /// Compares a byte buffer slice with an ASCII marker case-insensitively.
        /// </summary>
        static bool MatchesAsciiMarker(IReadOnlyList<byte> buffer, IReadOnlyList<byte> markerBytes, int startIndex)
        {
            for (int markerIndex = 0; markerIndex < markerBytes.Count; markerIndex++)
            {
                byte sourceByte = buffer[startIndex + markerIndex];
                byte markerByte = markerBytes[markerIndex];
                if (sourceByte != markerByte && sourceByte != markerByte - 32)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
