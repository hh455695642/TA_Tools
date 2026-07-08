using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace AssetCloneIsolation.Editor
{
    /// <summary>
    /// Builds GUID reference indexes and direct dependency lists for relationship previews.
    /// </summary>
    internal static class AssetCloneIsolationReferenceIndex
    {
        /// <summary>
        /// Builds a map from asset path to GUID references found in SourceRoot and TargetRoot text assets.
        /// </summary>
        public static Dictionary<string, HashSet<string>> BuildReferenceIndex(string sourceRoot, string targetRoot)
        {
            var referenceIndex = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            string[] searchRoots =
            {
                AssetCloneIsolationUtility.NormalizeAssetPath(sourceRoot),
                AssetCloneIsolationUtility.NormalizeAssetPath(targetRoot)
            };

            var validSearchRoots = new List<string>();
            foreach (string searchRoot in searchRoots)
            {
                if (!string.IsNullOrEmpty(searchRoot)
                    && AssetDatabase.IsValidFolder(searchRoot)
                    && !ContainsPath(validSearchRoots, searchRoot))
                {
                    validSearchRoots.Add(searchRoot);
                }
            }

            if (validSearchRoots.Count == 0)
            {
                return referenceIndex;
            }

            foreach (string guid in AssetDatabase.FindAssets(string.Empty, validSearchRoots.ToArray()))
            {
                string assetPath = AssetCloneIsolationUtility.NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                if (string.IsNullOrEmpty(assetPath) || !AssetCloneIsolationUtility.IsTextAssetPath(assetPath))
                {
                    continue;
                }

                string absolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetPath);
                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                HashSet<string> references = AssetCloneIsolationUtility.ExtractGuidReferences(File.ReadAllText(absolutePath));
                if (references.Count > 0)
                {
                    referenceIndex[assetPath] = references;
                }
            }

            return referenceIndex;
        }

        /// <summary>
        /// Checks whether a path list already contains the path using Unity asset path casing rules.
        /// </summary>
        static bool ContainsPath(List<string> paths, string candidatePath)
        {
            foreach (string path in paths)
            {
                if (path.Equals(candidatePath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets direct dependencies reported by Unity plus explicit text GUID dependencies.
        /// </summary>
        public static List<string> GetDirectDependencyPaths(string assetPath)
        {
            var dependencyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
            {
                return new List<string>();
            }

            foreach (string rawDependencyPath in AssetDatabase.GetDependencies(assetPath, false))
            {
                string dependencyPath = AssetCloneIsolationUtility.NormalizeAssetPath(rawDependencyPath);
                if (!string.IsNullOrEmpty(dependencyPath) && !dependencyPath.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    dependencyPaths.Add(dependencyPath);
                }
            }

            if (AssetCloneIsolationUtility.IsTextAssetPath(assetPath))
            {
                string absolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetPath);
                if (File.Exists(absolutePath))
                {
                    foreach (string guid in AssetCloneIsolationUtility.ExtractGuidReferences(File.ReadAllText(absolutePath)))
                    {
                        if (AssetCloneIsolationUtility.IsBuiltInOrEmptyGuid(guid))
                        {
                            continue;
                        }

                        string guidPath = AssetCloneIsolationUtility.NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                        if (!string.IsNullOrEmpty(guidPath) && !guidPath.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            dependencyPaths.Add(guidPath);
                        }
                    }
                }
            }

            return new List<string>(dependencyPaths);
        }
    }
}
