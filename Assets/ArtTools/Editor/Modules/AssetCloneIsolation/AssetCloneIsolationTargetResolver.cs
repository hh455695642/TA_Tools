using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetCloneIsolation.Editor
{
    /// <summary>
    /// Resolves Project objects, folders, and Hierarchy prefab instances into clone input asset paths.
    /// </summary>
    public static class AssetCloneIsolationTargetResolver
    {
        /// <summary>
        /// Adds a resolved asset path to a target path list when it is valid and not already present.
        /// </summary>
        public static bool AddResolvedTarget(UnityEngine.Object rawTarget, IList<string> selectedAssetPaths)
        {
            if (selectedAssetPaths == null)
            {
                throw new ArgumentNullException("selectedAssetPaths");
            }

            string assetPath = ResolveToAssetPath(rawTarget);
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (selectedAssetPaths.Any(existingPath => string.Equals(existingPath, assetPath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            selectedAssetPaths.Add(assetPath);
            return true;
        }

        /// <summary>
        /// Builds a sorted, de-duplicated asset path list from raw Unity objects.
        /// </summary>
        public static List<string> BuildSelectedAssetPaths(IEnumerable<UnityEngine.Object> rawTargets)
        {
            var selectedAssetPaths = new List<string>();
            if (rawTargets == null)
            {
                return selectedAssetPaths;
            }

            foreach (UnityEngine.Object rawTarget in rawTargets)
            {
                AddResolvedTarget(rawTarget, selectedAssetPaths);
            }

            selectedAssetPaths.Sort(StringComparer.OrdinalIgnoreCase);
            return selectedAssetPaths;
        }

        /// <summary>
        /// Resolves a raw Unity object to a Project asset or folder object when possible.
        /// </summary>
        public static UnityEngine.Object ResolveToProjectObject(UnityEngine.Object rawTarget)
        {
            string assetPath = ResolveToAssetPath(rawTarget);
            return string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadMainAssetAtPath(assetPath);
        }

        /// <summary>
        /// Resolves a raw Unity object to a normalized Project asset path.
        /// </summary>
        public static string ResolveToAssetPath(UnityEngine.Object rawTarget)
        {
            if (rawTarget == null)
            {
                return string.Empty;
            }

            string assetPath = AssetCloneIsolationUtility.NormalizeAssetPath(AssetDatabase.GetAssetPath(rawTarget));
            if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                return assetPath;
            }

            if (rawTarget is GameObject gameObject)
            {
                GameObject prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(gameObject)
                                        ?? PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
                string prefabPath = prefabRoot != null
                    ? AssetCloneIsolationUtility.NormalizeAssetPath(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabRoot))
                    : string.Empty;
                if (!string.IsNullOrEmpty(prefabPath) && prefabPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    return prefabPath;
                }
            }

            return string.Empty;
        }
    }
}
