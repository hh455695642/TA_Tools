using System;
using System.Collections.Generic;
using System.Linq;
using AssetCloneIsolation.Editor;

namespace TA.ArtTools.Editor
{
    /// <summary>
    /// Shared presentation helpers for the asset clone isolation relationship preview.
    /// </summary>
    internal static class TaAssetCloneIsolationPreviewView
    {
        /// <summary>
        /// Default maximum number of shared dependency reference rows shown before filtering.
        /// </summary>
        public const int SharedDependencyPreviewLimit = 20;

        /// <summary>
        /// Builds the top-level plan summary displayed above the relationship preview.
        /// </summary>
        public static string BuildPlanSummary(AssetCloneIsolationPlan plan)
        {
            AssetCloneIsolationPlanSummary summary = AssetCloneIsolationPlanSummary.FromPlan(plan);
            int rootCount = plan == null ? 0 : plan.RootPlans.Count;
            int assetCount = plan == null ? 0 : plan.Assets.Count;
            int guidMapCount = plan == null ? 0 : plan.GuidMap.Count;
            return $"Root {rootCount} | 克隆 {assetCount} | 新建 {summary.NewTargetAssetCount} | 覆盖已有 {summary.ExistingTargetAssetCount} | 外部共享 {summary.ExternalSharedDependencyCount} | 外部迁移 {summary.ExternalCloneDependencyCount} | GUID 映射 {guidMapCount} | 显式共享 {summary.ExplicitSharedDependencyCount} | TargetRoot 修复 {summary.TargetRewriteFileCount} | 错误 {summary.BlockingErrorCount} | 警告 {summary.WarningCount}";
        }

        /// <summary>
        /// Builds a one-line summary for a selected root foldout.
        /// </summary>
        public static string BuildRootSummary(AssetCloneIsolationRootPlan rootPlan, AssetCloneIsolationPlan plan)
        {
            AssetCloneIsolationRootSummary summary = AssetCloneIsolationRootSummary.FromRootPlan(rootPlan, plan);
            return $"新建目标 {summary.NewTargetAssetCount} | 覆盖已有目标 {summary.ExistingTargetAssetCount} | 外部共享 {summary.ExternalSharedDependencyCount} | 外部迁移 {summary.ExternalCloneDependencyCount} | TargetRoot 修复 {summary.TargetRewriteFileCount} | 显式共享 {summary.ExplicitSharedDependencyCount} | 阻断 {summary.BlockingIssueCount} | 警告 {summary.WarningCount}";
        }

        /// <summary>
        /// Builds a grouped type summary for shared dependency references.
        /// </summary>
        public static string BuildSharedDependencyTypeSummary(IReadOnlyList<AssetCloneIsolationRelationNode> nodes)
        {
            if (nodes == null || nodes.Count == 0)
            {
                return "类型统计：无";
            }

            string[] preferredOrder = { "Shader", "Texture", "Material", "Prefab", "Scene", "Other" };
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (AssetCloneIsolationRelationNode node in nodes)
            {
                string bucket = NormalizeAssetTypeBucket(node == null ? string.Empty : node.AssetType);
                counts[bucket] = counts.TryGetValue(bucket, out int count) ? count + 1 : 1;
            }

            return "类型统计：" + string.Join(" / ", preferredOrder
                .Where(bucket => counts.ContainsKey(bucket))
                .Select(bucket => bucket + " " + counts[bucket]));
        }

        /// <summary>
        /// Returns true when the shared dependency list should show every matched row.
        /// </summary>
        public static bool ShouldShowAllSharedDependencyRows(string pathFilter, string decisionFilter)
        {
            return !string.IsNullOrEmpty(pathFilter)
                   || string.Equals(decisionFilter, "共享依赖引用", StringComparison.Ordinal);
        }

        /// <summary>
        /// Maps Unity asset type labels into stable summary buckets.
        /// </summary>
        static string NormalizeAssetTypeBucket(string assetType)
        {
            if (string.IsNullOrEmpty(assetType))
            {
                return "Other";
            }

            if (assetType.IndexOf("Shader", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Shader";
            }

            if (assetType.IndexOf("Texture", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Texture";
            }

            if (assetType.IndexOf("Material", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Material";
            }

            if (assetType.IndexOf("Prefab", StringComparison.OrdinalIgnoreCase) >= 0
                || assetType.IndexOf("GameObject", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Prefab";
            }

            if (assetType.IndexOf("Scene", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Scene";
            }

            return "Other";
        }
    }
}
