using System;
using System.Collections.Generic;
using UnityEngine;

namespace AssetCloneIsolation.Editor
{
    /// <summary>
    /// User-facing options for building an isolated clone plan.
    /// </summary>
    [Serializable]
    public sealed class AssetCloneIsolationOptions
    {
        /// <summary>
        /// Default source art root used by the NewWorld project.
        /// </summary>
        public const string DefaultSourceRoot = "Assets/NewWorld/ArtResources";

        /// <summary>
        /// Default target art root used by the Mountainbike project.
        /// </summary>
        public const string DefaultTargetRoot = "Assets/ArtResources_Mountainbike";

        /// <summary>
        /// Source root that selected assets must live under.
        /// </summary>
        public string SourceRoot = DefaultSourceRoot;

        /// <summary>
        /// Target root where cloned assets will be written.
        /// </summary>
        public string TargetRoot = DefaultTargetRoot;

        /// <summary>
        /// Project asset paths selected by the user.
        /// </summary>
        public List<string> SelectedAssetPaths = new List<string>();

        /// <summary>
        /// Dependency paths that the user intentionally keeps shared instead of cloned.
        /// </summary>
        public List<string> ExplicitSharedAssetPaths = new List<string>();

        /// <summary>
        /// External Assets dependency paths that the user explicitly chooses to clone into the target root.
        /// </summary>
        public List<string> ExplicitCloneExternalAssetPaths = new List<string>();

        /// <summary>
        /// True when an existing target asset file may be overwritten while preserving its GUID.
        /// </summary>
        public bool OverwriteExistingAssets = true;

        /// <summary>
        /// True when existing target-root text assets should be rewritten from source GUIDs to target GUIDs.
        /// </summary>
        public bool RewriteExistingTargetAssets = true;

        /// <summary>
        /// Creates a copy so callers can mutate options without affecting existing plans.
        /// </summary>
        public AssetCloneIsolationOptions Clone()
        {
            return new AssetCloneIsolationOptions
            {
                SourceRoot = SourceRoot,
                TargetRoot = TargetRoot,
                SelectedAssetPaths = new List<string>(SelectedAssetPaths),
                ExplicitSharedAssetPaths = new List<string>(ExplicitSharedAssetPaths),
                ExplicitCloneExternalAssetPaths = new List<string>(ExplicitCloneExternalAssetPaths),
                OverwriteExistingAssets = OverwriteExistingAssets,
                RewriteExistingTargetAssets = RewriteExistingTargetAssets
            };
        }
    }

    /// <summary>
    /// Describes how a relation node participates in the clone isolation plan.
    /// </summary>
    public enum AssetCloneIsolationRelationKind
    {
        /// <summary>
        /// Root asset selected by the user.
        /// </summary>
        Root,

        /// <summary>
        /// Asset referenced by the root or by another dependency.
        /// </summary>
        Dependency,

        /// <summary>
        /// Asset that directly references the selected root asset.
        /// </summary>
        UpstreamReference,

        /// <summary>
        /// Asset that references a downstream dependency of the selected root asset.
        /// </summary>
        SharedDependencyReference,

        /// <summary>
        /// Existing target-root asset that will have GUID references rewritten.
        /// </summary>
        TargetRewrite
    }

    /// <summary>
    /// Describes the clone, share, or block decision assigned to a relation node.
    /// </summary>
    public enum AssetCloneIsolationDecision
    {
        /// <summary>
        /// Asset will be cloned into the target root.
        /// </summary>
        Clone,

        /// <summary>
        /// Source-root dependency is intentionally kept shared by user choice.
        /// </summary>
        ExplicitShared,

        /// <summary>
        /// Asset is allowed to remain shared by built-in rules.
        /// </summary>
        SharedDependency,

        /// <summary>
        /// External Assets dependency is intentionally left shared by default.
        /// </summary>
        ExternalShared,

        /// <summary>
        /// External Assets dependency will be cloned into the target root external bucket.
        /// </summary>
        ExternalClone,

        /// <summary>
        /// Asset is an external art dependency that blocks applying the plan.
        /// </summary>
        BlockedExternal,

        /// <summary>
        /// Asset already lives under the target root.
        /// </summary>
        AlreadyInTarget,

        /// <summary>
        /// Asset path or GUID could not be resolved.
        /// </summary>
        MissingOrUnknown,

        /// <summary>
        /// Asset is displayed only because it references this root graph.
        /// </summary>
        ReferenceOnly
    }

    /// <summary>
    /// One dependency, upstream reference, or rewrite item shown in the relationship preview.
    /// </summary>
    [Serializable]
    public sealed class AssetCloneIsolationRelationNode
    {
        /// <summary>
        /// Asset path represented by this relation node.
        /// </summary>
        public string AssetPath = string.Empty;

        /// <summary>
        /// Target asset path when this node maps to a cloned target asset.
        /// </summary>
        public string TargetAssetPath = string.Empty;

        /// <summary>
        /// Source asset GUID or referencing asset GUID.
        /// </summary>
        public string Guid = string.Empty;

        /// <summary>
        /// Target GUID when the node participates in the clone GUID map.
        /// </summary>
        public string TargetGuid = string.Empty;

        /// <summary>
        /// Short asset type name used for grouping and filtering in the UI.
        /// </summary>
        public string AssetType = string.Empty;

        /// <summary>
        /// Relationship direction and purpose.
        /// </summary>
        public AssetCloneIsolationRelationKind RelationKind;

        /// <summary>
        /// Clone isolation decision assigned to this relation.
        /// </summary>
        public AssetCloneIsolationDecision Decision;

        /// <summary>
        /// Dependency depth where zero is the root asset.
        /// </summary>
        public int Depth;

        /// <summary>
        /// Additional human-readable reason or risk summary.
        /// </summary>
        public string Detail = string.Empty;
    }

    /// <summary>
    /// Relationship preview data for one user-selected clone target.
    /// </summary>
    [Serializable]
    public sealed class AssetCloneIsolationRootPlan
    {
        /// <summary>
        /// User-selected root asset or folder path.
        /// </summary>
        public string RootAssetPath = string.Empty;

        /// <summary>
        /// Target path for the root asset when it is cloned.
        /// </summary>
        public string TargetAssetPath = string.Empty;

        /// <summary>
        /// Source GUID for the selected root asset.
        /// </summary>
        public string RootGuid = string.Empty;

        /// <summary>
        /// Target GUID for the selected root asset when it is cloned.
        /// </summary>
        public string TargetGuid = string.Empty;

        /// <summary>
        /// True when the root input is a Project folder.
        /// </summary>
        public bool IsFolderRoot;

        /// <summary>
        /// Downstream dependencies reached from this root.
        /// </summary>
        public List<AssetCloneIsolationRelationNode> DownstreamDependencies = new List<AssetCloneIsolationRelationNode>();

        /// <summary>
        /// Assets in the configured scan scope that directly reference this root asset.
        /// </summary>
        public List<AssetCloneIsolationRelationNode> UpstreamReferences = new List<AssetCloneIsolationRelationNode>();

        /// <summary>
        /// Assets in the configured scan scope that reference this root's downstream dependencies.
        /// </summary>
        public List<AssetCloneIsolationRelationNode> SharedDependencyReferences = new List<AssetCloneIsolationRelationNode>();

        /// <summary>
        /// Existing target-root files related to this root that need GUID rewrites.
        /// </summary>
        public List<AssetCloneIsolationRewriteRecord> TargetRewriteRecords = new List<AssetCloneIsolationRewriteRecord>();

        /// <summary>
        /// Root-local warnings displayed near this root in the relationship preview.
        /// </summary>
        public List<string> Warnings = new List<string>();

        /// <summary>
        /// Root-local errors displayed near this root in the relationship preview.
        /// </summary>
        public List<string> Errors = new List<string>();
    }

    /// <summary>
    /// Read-only counter summary for a complete clone isolation plan.
    /// </summary>
    [Serializable]
    public sealed class AssetCloneIsolationPlanSummary
    {
        /// <summary>
        /// Number of target asset files that will be created.
        /// </summary>
        public int NewTargetAssetCount;

        /// <summary>
        /// Number of existing target asset files whose content will be overwritten while keeping target GUIDs.
        /// </summary>
        public int ExistingTargetAssetCount;

        /// <summary>
        /// Number of existing target-root text files that will have GUID references rewritten.
        /// </summary>
        public int TargetRewriteFileCount;

        /// <summary>
        /// Number of source-root dependencies intentionally kept shared.
        /// </summary>
        public int ExplicitSharedDependencyCount;

        /// <summary>
        /// Number of external Assets dependencies left shared as visible risks.
        /// </summary>
        public int ExternalSharedDependencyCount;

        /// <summary>
        /// Number of external Assets dependencies explicitly cloned into the target root.
        /// </summary>
        public int ExternalCloneDependencyCount;

        /// <summary>
        /// Number of blocking errors in the plan.
        /// </summary>
        public int BlockingErrorCount;

        /// <summary>
        /// Number of non-blocking warnings in the plan.
        /// </summary>
        public int WarningCount;

        /// <summary>
        /// Builds a summary from a full plan.
        /// </summary>
        public static AssetCloneIsolationPlanSummary FromPlan(AssetCloneIsolationPlan plan)
        {
            var summary = new AssetCloneIsolationPlanSummary();
            if (plan == null)
            {
                return summary;
            }

            foreach (AssetCloneIsolationAssetRecord assetRecord in plan.Assets)
            {
                if (assetRecord.TargetAlreadyExists)
                {
                    summary.ExistingTargetAssetCount++;
                }
                else
                {
                    summary.NewTargetAssetCount++;
                }
            }

            summary.TargetRewriteFileCount = plan.TargetRewriteRecords.Count;
            summary.ExplicitSharedDependencyCount = plan.ExplicitSharedDependencies.Count;
            summary.ExternalSharedDependencyCount = plan.ExternalSharedDependencies.Count;
            summary.ExternalCloneDependencyCount = plan.ExplicitCloneExternalDependencies.Count;
            summary.BlockingErrorCount = plan.Errors.Count;
            summary.WarningCount = plan.Warnings.Count;
            return summary;
        }
    }

    /// <summary>
    /// Read-only counter summary for one selected root in a clone isolation plan.
    /// </summary>
    [Serializable]
    public sealed class AssetCloneIsolationRootSummary
    {
        /// <summary>
        /// Number of target asset files that will be created for this root graph.
        /// </summary>
        public int NewTargetAssetCount;

        /// <summary>
        /// Number of existing target asset files that will be overwritten for this root graph.
        /// </summary>
        public int ExistingTargetAssetCount;

        /// <summary>
        /// Number of target-root text files that will be rewritten for this root graph.
        /// </summary>
        public int TargetRewriteFileCount;

        /// <summary>
        /// Number of source-root dependencies intentionally kept shared for this root graph.
        /// </summary>
        public int ExplicitSharedDependencyCount;

        /// <summary>
        /// Number of external Assets dependencies left shared for this root graph.
        /// </summary>
        public int ExternalSharedDependencyCount;

        /// <summary>
        /// Number of external Assets dependencies cloned for this root graph.
        /// </summary>
        public int ExternalCloneDependencyCount;

        /// <summary>
        /// Number of blocking decisions or errors for this root graph.
        /// </summary>
        public int BlockingIssueCount;

        /// <summary>
        /// Number of non-blocking warnings for this root graph.
        /// </summary>
        public int WarningCount;

        /// <summary>
        /// Builds a summary for one root using the plan's asset records.
        /// </summary>
        public static AssetCloneIsolationRootSummary FromRootPlan(AssetCloneIsolationRootPlan rootPlan, AssetCloneIsolationPlan plan)
        {
            var summary = new AssetCloneIsolationRootSummary();
            if (rootPlan == null)
            {
                return summary;
            }

            HashSet<string> rootGraphPaths = BuildRootGraphPathSet(rootPlan);
            if (plan != null)
            {
                foreach (AssetCloneIsolationAssetRecord assetRecord in plan.Assets)
                {
                    if (!rootGraphPaths.Contains(assetRecord.SourceAssetPath))
                    {
                        continue;
                    }

                    if (assetRecord.TargetAlreadyExists)
                    {
                        summary.ExistingTargetAssetCount++;
                    }
                    else
                    {
                        summary.NewTargetAssetCount++;
                    }
                }
            }

            summary.TargetRewriteFileCount = rootPlan.TargetRewriteRecords.Count;
            summary.WarningCount = rootPlan.Warnings.Count;
            foreach (AssetCloneIsolationRelationNode node in rootPlan.DownstreamDependencies)
            {
                if (node.Decision == AssetCloneIsolationDecision.ExplicitShared)
                {
                    summary.ExplicitSharedDependencyCount++;
                }

                if (node.Decision == AssetCloneIsolationDecision.ExternalShared)
                {
                    summary.ExternalSharedDependencyCount++;
                }

                if (node.Decision == AssetCloneIsolationDecision.ExternalClone)
                {
                    summary.ExternalCloneDependencyCount++;
                }

                if (node.Decision == AssetCloneIsolationDecision.BlockedExternal
                    || node.Decision == AssetCloneIsolationDecision.MissingOrUnknown)
                {
                    summary.BlockingIssueCount++;
                }
            }

            summary.BlockingIssueCount += rootPlan.Errors.Count;
            return summary;
        }

        /// <summary>
        /// Builds the set of root and downstream source paths that belong to one root graph.
        /// </summary>
        static HashSet<string> BuildRootGraphPathSet(AssetCloneIsolationRootPlan rootPlan)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rootPlan == null)
            {
                return paths;
            }

            if (!string.IsNullOrEmpty(rootPlan.RootAssetPath))
            {
                paths.Add(rootPlan.RootAssetPath);
            }

            foreach (AssetCloneIsolationRelationNode node in rootPlan.DownstreamDependencies)
            {
                if (!string.IsNullOrEmpty(node.AssetPath))
                {
                    paths.Add(node.AssetPath);
                }
            }

            return paths;
        }
    }

    /// <summary>
    /// Planned clone record for one source asset.
    /// </summary>
    [Serializable]
    public sealed class AssetCloneIsolationAssetRecord
    {
        /// <summary>
        /// Source asset path under the source root.
        /// </summary>
        public string SourceAssetPath = string.Empty;

        /// <summary>
        /// Target asset path under the target root.
        /// </summary>
        public string TargetAssetPath = string.Empty;

        /// <summary>
        /// Source asset GUID read from Unity's asset database.
        /// </summary>
        public string SourceGuid = string.Empty;

        /// <summary>
        /// Target GUID that references will be rewritten to.
        /// </summary>
        public string TargetGuid = string.Empty;

        /// <summary>
        /// True when the target asset file already exists before applying the plan.
        /// </summary>
        public bool TargetAlreadyExists;

        /// <summary>
        /// True when the asset can be safely rewritten as text.
        /// </summary>
        public bool IsTextAsset;

        /// <summary>
        /// True when a binary asset appears to contain serialized GUID text.
        /// </summary>
        public bool HasBinaryGuidRisk;
    }

    /// <summary>
    /// Planned or applied rewrite record for one target-root text asset.
    /// </summary>
    [Serializable]
    public sealed class AssetCloneIsolationRewriteRecord
    {
        /// <summary>
        /// Asset path that contains rewriteable source GUID references.
        /// </summary>
        public string AssetPath = string.Empty;

        /// <summary>
        /// Number of GUID replacements found for this file.
        /// </summary>
        public int ReplacementCount;

        /// <summary>
        /// Number of distinct source GUID mappings involved in this file rewrite.
        /// </summary>
        public int GuidMappingCount;
    }

    /// <summary>
    /// Clone plan generated by the service before any write happens.
    /// </summary>
    [Serializable]
    public sealed class AssetCloneIsolationPlan
    {
        /// <summary>
        /// Normalized options used to build this plan.
        /// </summary>
        public AssetCloneIsolationOptions Options = new AssetCloneIsolationOptions();

        /// <summary>
        /// Source assets that will be cloned.
        /// </summary>
        public List<AssetCloneIsolationAssetRecord> Assets = new List<AssetCloneIsolationAssetRecord>();

        /// <summary>
        /// Old source GUID to target GUID map.
        /// </summary>
        public Dictionary<string, string> GuidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Existing target-root files that currently contain source GUID references.
        /// </summary>
        public List<AssetCloneIsolationRewriteRecord> TargetRewriteRecords = new List<AssetCloneIsolationRewriteRecord>();

        /// <summary>
        /// Relationship preview grouped by each user-selected root asset or folder.
        /// </summary>
        public List<AssetCloneIsolationRootPlan> RootPlans = new List<AssetCloneIsolationRootPlan>();

        /// <summary>
        /// Dependencies that are allowed to remain shared.
        /// </summary>
        public List<string> SharedDependencies = new List<string>();

        /// <summary>
        /// Dependencies intentionally kept shared by user choice.
        /// </summary>
        public List<string> ExplicitSharedDependencies = new List<string>();

        /// <summary>
        /// External Assets dependencies left shared as visible risks by default.
        /// </summary>
        public List<string> ExternalSharedDependencies = new List<string>();

        /// <summary>
        /// External Assets dependencies explicitly cloned into the target root.
        /// </summary>
        public List<string> ExplicitCloneExternalDependencies = new List<string>();

        /// <summary>
        /// External art dependencies that are blocked by the plan.
        /// </summary>
        public List<string> ExternalArtDependencies = new List<string>();

        /// <summary>
        /// Informational messages generated while building or applying the plan.
        /// </summary>
        public List<string> Infos = new List<string>();

        /// <summary>
        /// Non-blocking risk messages generated while building or applying the plan.
        /// </summary>
        public List<string> Warnings = new List<string>();

        /// <summary>
        /// Blocking errors that must be fixed before applying the plan.
        /// </summary>
        public List<string> Errors = new List<string>();

        /// <summary>
        /// True when the plan contains blocking errors.
        /// </summary>
        public bool HasErrors
        {
            get { return Errors.Count > 0; }
        }

        /// <summary>
        /// Estimated write operation count for confirmation UI.
        /// </summary>
        public int WriteOperationCount
        {
            get { return Assets.Count * 2 + TargetRewriteRecords.Count; }
        }

        /// <summary>
        /// Adds an informational message.
        /// </summary>
        public void AddInfo(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Infos.Add(message);
            }
        }

        /// <summary>
        /// Adds a warning message.
        /// </summary>
        public void AddWarning(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Warnings.Add(message);
            }
        }

        /// <summary>
        /// Adds a blocking error message.
        /// </summary>
        public void AddError(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Errors.Add(message);
            }
        }
    }

    /// <summary>
    /// Audit result for an existing target root.
    /// </summary>
    [Serializable]
    public sealed class AssetCloneIsolationAuditReport
    {
        /// <summary>
        /// Target root audited by the service.
        /// </summary>
        public string TargetRoot = string.Empty;

        /// <summary>
        /// Source root used to detect leaked old-project dependencies.
        /// </summary>
        public string SourceRoot = string.Empty;

        /// <summary>
        /// Number of assets scanned in the target root.
        /// </summary>
        public int AssetCount;

        /// <summary>
        /// Number of shader or shadergraph assets scanned.
        /// </summary>
        public int ShaderAssetCount;

        /// <summary>
        /// Informational audit messages.
        /// </summary>
        public List<string> Infos = new List<string>();

        /// <summary>
        /// Non-blocking audit warnings.
        /// </summary>
        public List<string> Warnings = new List<string>();

        /// <summary>
        /// Blocking audit errors.
        /// </summary>
        public List<string> Errors = new List<string>();

        /// <summary>
        /// True when audit found blocking errors.
        /// </summary>
        public bool HasErrors
        {
            get { return Errors.Count > 0; }
        }

        /// <summary>
        /// Adds an informational audit message.
        /// </summary>
        public void AddInfo(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Infos.Add(message);
            }
        }

        /// <summary>
        /// Adds a warning audit message.
        /// </summary>
        public void AddWarning(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Warnings.Add(message);
            }
        }

        /// <summary>
        /// Adds a blocking audit error.
        /// </summary>
        public void AddError(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                Errors.Add(message);
            }
        }
    }

    /// <summary>
    /// ScriptableObject preset for repeated clone isolation runs.
    /// </summary>
    public sealed class AssetCloneIsolationPreset : ScriptableObject
    {
        /// <summary>
        /// Source root saved in the preset.
        /// </summary>
        public string SourceRoot = AssetCloneIsolationOptions.DefaultSourceRoot;

        /// <summary>
        /// Target root saved in the preset.
        /// </summary>
        public string TargetRoot = AssetCloneIsolationOptions.DefaultTargetRoot;

        /// <summary>
        /// True when the preset allows existing target files to be overwritten.
        /// </summary>
        public bool OverwriteExistingAssets = true;

        /// <summary>
        /// True when the preset rewrites existing target-root references after cloning.
        /// </summary>
        public bool RewriteExistingTargetAssets = true;

        /// <summary>
        /// Dependency paths intentionally kept shared by this preset.
        /// </summary>
        public List<string> ExplicitSharedAssetPaths = new List<string>();

        /// <summary>
        /// External Assets dependency paths intentionally cloned by this preset.
        /// </summary>
        public List<string> ExplicitCloneExternalAssetPaths = new List<string>();
    }
}
