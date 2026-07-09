using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AssetCloneIsolation.Editor
{
    /// <summary>
    /// Builds, applies, and audits isolated art asset clone operations.
    /// </summary>
    public static class AssetCloneIsolationService
    {
        /// <summary>
        /// Builds a read-only clone plan from user options.
        /// </summary>
        public static AssetCloneIsolationPlan BuildPlan(AssetCloneIsolationOptions options)
        {
            return AssetCloneIsolationPlanBuilder.BuildPlan(options);
        }

        /// <summary>
        /// Contains the existing plan building implementation used by the plan builder facade.
        /// </summary>
        internal static AssetCloneIsolationPlan BuildPlanCore(AssetCloneIsolationOptions options)
        {
            AssetCloneIsolationOptions normalizedOptions = NormalizeOptions(options);
            var plan = new AssetCloneIsolationPlan
            {
                Options = normalizedOptions.Clone()
            };

            ValidateOptions(normalizedOptions, plan);
            if (plan.HasErrors)
            {
                return plan;
            }

            List<string> sourceAssetPaths = CollectSourceAssets(normalizedOptions, plan);
            BuildGuidMapAndAssetRecords(sourceAssetPaths, plan);
            CollectExistingTargetRewriteRecords(plan);
            BuildRootPlans(plan);

            plan.AddInfo("Clone assets: " + plan.Assets.Count);
            plan.AddInfo("Shared dependencies: " + plan.SharedDependencies.Count);
            plan.AddInfo("Explicit shared dependencies: " + plan.ExplicitSharedDependencies.Count);
            plan.AddInfo("External shared dependencies: " + plan.ExternalSharedDependencies.Count);
            plan.AddInfo("External clone dependencies: " + plan.ExplicitCloneExternalDependencies.Count);
            plan.AddInfo("Blocked external art dependencies: " + plan.ExternalArtDependencies.Count);
            plan.AddInfo("Existing target rewrite files: " + plan.TargetRewriteRecords.Count);
            return plan;
        }

        /// <summary>
        /// Applies a previously built plan and refreshes Unity's AssetDatabase.
        /// </summary>
        public static void ApplyPlan(AssetCloneIsolationPlan plan)
        {
            AssetCloneIsolationPlanApplier.ApplyPlan(plan);
        }

        /// <summary>
        /// Contains the existing apply implementation used by the plan applier facade.
        /// </summary>
        internal static void ApplyPlanCore(AssetCloneIsolationPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            if (plan.HasErrors)
            {
                throw new InvalidOperationException("Cannot apply a plan that contains errors.");
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (AssetCloneIsolationAssetRecord assetRecord in plan.Assets)
                {
                    WriteClonedAsset(assetRecord, plan);
                }

                if (plan.Options.RewriteExistingTargetAssets)
                {
                    RewriteTargetRootFiles(plan);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        /// <summary>
        /// Audits the target root using the default source root.
        /// </summary>
        public static AssetCloneIsolationAuditReport AuditTargetRoot(string targetRoot)
        {
            return AssetCloneIsolationAuditService.AuditTargetRoot(targetRoot);
        }

        /// <summary>
        /// Audits the target root for leaked source-root dependencies and shader risks.
        /// </summary>
        public static AssetCloneIsolationAuditReport AuditTargetRoot(string targetRoot, string sourceRoot)
        {
            return AssetCloneIsolationAuditService.AuditTargetRoot(targetRoot, sourceRoot);
        }

        /// <summary>
        /// Audits the target root while treating selected source dependencies as intentional shared risks.
        /// </summary>
        public static AssetCloneIsolationAuditReport AuditTargetRoot(string targetRoot, string sourceRoot, IReadOnlyCollection<string> explicitSharedAssetPaths)
        {
            return AssetCloneIsolationAuditService.AuditTargetRoot(targetRoot, sourceRoot, explicitSharedAssetPaths);
        }

        /// <summary>
        /// Contains the existing audit implementation used by the audit service facade.
        /// </summary>
        internal static AssetCloneIsolationAuditReport AuditTargetRootCore(string targetRoot, string sourceRoot, IReadOnlyCollection<string> explicitSharedAssetPaths)
        {
            string normalizedTargetRoot = AssetCloneIsolationUtility.NormalizeAssetPath(targetRoot);
            string normalizedSourceRoot = AssetCloneIsolationUtility.NormalizeAssetPath(sourceRoot);
            HashSet<string> explicitSharedSet = BuildExplicitSharedSet(explicitSharedAssetPaths);
            var report = new AssetCloneIsolationAuditReport
            {
                TargetRoot = normalizedTargetRoot,
                SourceRoot = normalizedSourceRoot
            };

            if (!AssetDatabase.IsValidFolder(normalizedTargetRoot))
            {
                report.AddError("Target root does not exist: " + normalizedTargetRoot);
                return report;
            }

            List<string> assetPaths = FindAssetsUnderRoot(normalizedTargetRoot);
            report.AssetCount = assetPaths.Count;

            AuditDependencies(assetPaths, normalizedTargetRoot, normalizedSourceRoot, explicitSharedSet, report);
            AuditMaterialShaders(assetPaths, normalizedTargetRoot, normalizedSourceRoot, explicitSharedSet, report);
            AuditUnknownGuidReferences(assetPaths, report);
            AuditShaderVariantRisk(assetPaths, report);
            AuditDuplicateFileNames(assetPaths, report);
            report.AddInfo("Target audit finished: " + normalizedTargetRoot);
            return report;
        }

        /// <summary>
        /// Normalizes nullable user options into deterministic values.
        /// </summary>
        static AssetCloneIsolationOptions NormalizeOptions(AssetCloneIsolationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            return new AssetCloneIsolationOptions
            {
                SourceRoot = string.IsNullOrWhiteSpace(options.SourceRoot)
                    ? AssetCloneIsolationOptions.DefaultSourceRoot
                    : AssetCloneIsolationUtility.NormalizeAssetPath(options.SourceRoot),
                TargetRoot = string.IsNullOrWhiteSpace(options.TargetRoot)
                    ? AssetCloneIsolationOptions.DefaultTargetRoot
                    : AssetCloneIsolationUtility.NormalizeAssetPath(options.TargetRoot),
                SelectedAssetPaths = options.SelectedAssetPaths == null
                    ? new List<string>()
                    : options.SelectedAssetPaths
                        .Select(AssetCloneIsolationUtility.NormalizeAssetPath)
                        .Where(path => !string.IsNullOrEmpty(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                ExplicitSharedAssetPaths = options.ExplicitSharedAssetPaths == null
                    ? new List<string>()
                    : options.ExplicitSharedAssetPaths
                        .Select(AssetCloneIsolationUtility.NormalizeAssetPath)
                        .Where(path => !string.IsNullOrEmpty(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                ExplicitCloneExternalAssetPaths = options.ExplicitCloneExternalAssetPaths == null
                    ? new List<string>()
                    : options.ExplicitCloneExternalAssetPaths
                        .Select(AssetCloneIsolationUtility.NormalizeAssetPath)
                        .Where(path => !string.IsNullOrEmpty(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                OverwriteExistingAssets = options.OverwriteExistingAssets,
                RewriteExistingTargetAssets = options.RewriteExistingTargetAssets
            };
        }

        /// <summary>
        /// Adds blocking option errors to the plan.
        /// </summary>
        static void ValidateOptions(AssetCloneIsolationOptions options, AssetCloneIsolationPlan plan)
        {
            if (string.IsNullOrEmpty(options.SourceRoot) || !options.SourceRoot.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                plan.AddError("SourceRoot must start with Assets/: " + options.SourceRoot);
            }

            if (string.IsNullOrEmpty(options.TargetRoot) || !options.TargetRoot.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                plan.AddError("TargetRoot must start with Assets/: " + options.TargetRoot);
            }

            if (!AssetDatabase.IsValidFolder(options.SourceRoot))
            {
                plan.AddError("SourceRoot does not exist: " + options.SourceRoot);
            }

            if (options.SourceRoot.Equals(options.TargetRoot, StringComparison.OrdinalIgnoreCase))
            {
                plan.AddError("SourceRoot and TargetRoot must be different.");
            }

            if (options.SelectedAssetPaths.Count == 0)
            {
                plan.AddError("No selected assets were provided.");
            }

            foreach (string selectedAssetPath in options.SelectedAssetPaths)
            {
                if (!AssetCloneIsolationUtility.IsUnderRoot(selectedAssetPath, options.SourceRoot))
                {
                    plan.AddError("Selected root asset must be under SourceRoot: " + selectedAssetPath);
                }
            }
        }

        /// <summary>
        /// Collects selected assets plus recursive source-root dependencies.
        /// </summary>
        static List<string> CollectSourceAssets(AssetCloneIsolationOptions options, AssetCloneIsolationPlan plan)
        {
            var sourceAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectedRootPaths = new HashSet<string>(options.SelectedAssetPaths, StringComparer.OrdinalIgnoreCase);
            var pendingPaths = new Queue<string>();

            foreach (string selectedPath in options.SelectedAssetPaths)
            {
                pendingPaths.Enqueue(selectedPath);
            }

            while (pendingPaths.Count > 0)
            {
                string currentPath = AssetCloneIsolationUtility.NormalizeAssetPath(pendingPaths.Dequeue());
                if (string.IsNullOrEmpty(currentPath) || !visitedPaths.Add(currentPath))
                {
                    continue;
                }

                if (AssetDatabase.IsValidFolder(currentPath))
                {
                    EnqueueFolderAssets(currentPath, pendingPaths);
                    continue;
                }

                if (!File.Exists(AssetCloneIsolationUtility.ToProjectAbsolutePath(currentPath)))
                {
                    plan.AddError("Selected or dependent asset file does not exist: " + currentPath);
                    continue;
                }

                if (!AssetCloneIsolationUtility.IsUnderRoot(currentPath, options.SourceRoot))
                {
                    if (IsExplicitExternalCloneDependency(currentPath, options))
                    {
                        AddUnique(plan.ExplicitCloneExternalDependencies, currentPath);
                        sourceAssets.Add(currentPath);
                        EnqueueAssetDatabaseDependencies(currentPath, options, plan, pendingPaths);
                        EnqueueTextGuidDependencies(currentPath, options, plan, pendingPaths);
                        continue;
                    }

                    HandleExternalDependency(currentPath, options, plan);
                    continue;
                }

                if (AssetCloneIsolationUtility.IsSharedCodeAssetPath(currentPath))
                {
                    AddUnique(plan.SharedDependencies, currentPath);
                    continue;
                }

                if (!selectedRootPaths.Contains(currentPath) && IsExplicitSharedDependency(currentPath, options))
                {
                    AddUnique(plan.ExplicitSharedDependencies, currentPath);
                    plan.AddWarning("Explicit shared source dependency will remain linked to SourceRoot: " + currentPath);
                    continue;
                }

                sourceAssets.Add(currentPath);
                EnqueueAssetDatabaseDependencies(currentPath, options, plan, pendingPaths);
                EnqueueTextGuidDependencies(currentPath, options, plan, pendingPaths);
            }

            return sourceAssets.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Enqueues every non-folder asset under a selected folder.
        /// </summary>
        static void EnqueueFolderAssets(string folderPath, Queue<string> pendingPaths)
        {
            string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            foreach (string guid in guids)
            {
                string assetPath = AssetCloneIsolationUtility.NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                if (!string.IsNullOrEmpty(assetPath) && !AssetDatabase.IsValidFolder(assetPath))
                {
                    pendingPaths.Enqueue(assetPath);
                }
            }
        }

        /// <summary>
        /// Enqueues dependencies reported by Unity's AssetDatabase.
        /// </summary>
        static void EnqueueAssetDatabaseDependencies(string assetPath, AssetCloneIsolationOptions options, AssetCloneIsolationPlan plan, Queue<string> pendingPaths)
        {
            string[] dependencyPaths = AssetDatabase.GetDependencies(assetPath, true);
            foreach (string rawDependencyPath in dependencyPaths)
            {
                string dependencyPath = AssetCloneIsolationUtility.NormalizeAssetPath(rawDependencyPath);
                if (string.IsNullOrEmpty(dependencyPath) || dependencyPath.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                EnqueueOrReportDependency(dependencyPath, options, plan, pendingPaths);
            }
        }

        /// <summary>
        /// Enqueues dependencies found by scanning text GUID references.
        /// </summary>
        static void EnqueueTextGuidDependencies(string assetPath, AssetCloneIsolationOptions options, AssetCloneIsolationPlan plan, Queue<string> pendingPaths)
        {
            if (!AssetCloneIsolationUtility.IsTextAssetPath(assetPath))
            {
                AuditBinaryGuidRisk(assetPath, plan);
                return;
            }

            string absolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetPath);
            string sourceText = File.ReadAllText(absolutePath);
            foreach (string guid in AssetCloneIsolationUtility.ExtractGuidReferences(sourceText))
            {
                if (AssetCloneIsolationUtility.IsBuiltInOrEmptyGuid(guid))
                {
                    continue;
                }

                string dependencyPath = AssetCloneIsolationUtility.NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                if (string.IsNullOrEmpty(dependencyPath))
                {
                    plan.AddWarning("Unresolved GUID reference in source asset: " + assetPath + " -> " + guid);
                    continue;
                }

                EnqueueOrReportDependency(dependencyPath, options, plan, pendingPaths);
            }
        }

        /// <summary>
        /// Routes one dependency into cloning, sharing, or blocking error buckets.
        /// </summary>
        static void EnqueueOrReportDependency(string dependencyPath, AssetCloneIsolationOptions options, AssetCloneIsolationPlan plan, Queue<string> pendingPaths)
        {
            if (AssetCloneIsolationUtility.IsUnderRoot(dependencyPath, options.SourceRoot)
                && !AssetCloneIsolationUtility.IsSharedCodeAssetPath(dependencyPath))
            {
                if (IsExplicitSharedDependency(dependencyPath, options))
                {
                    AddUnique(plan.ExplicitSharedDependencies, dependencyPath);
                    plan.AddWarning("Explicit shared source dependency will remain linked to SourceRoot: " + dependencyPath);
                    return;
                }

                pendingPaths.Enqueue(dependencyPath);
                return;
            }

            if (AssetCloneIsolationUtility.IsAllowedSharedDependencyPath(dependencyPath, options.SourceRoot, options.TargetRoot))
            {
                AddUnique(plan.SharedDependencies, dependencyPath);
                return;
            }

            if (IsExplicitExternalCloneDependency(dependencyPath, options))
            {
                AddUnique(plan.ExplicitCloneExternalDependencies, dependencyPath);
                pendingPaths.Enqueue(dependencyPath);
                return;
            }

            HandleExternalDependency(dependencyPath, options, plan);
        }

        /// <summary>
        /// Reports a dependency that is kept shared by default or is non-Assets shared infrastructure.
        /// </summary>
        static void HandleExternalDependency(string dependencyPath, AssetCloneIsolationOptions options, AssetCloneIsolationPlan plan)
        {
            if (dependencyPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                if (!plan.ExternalSharedDependencies.Contains(dependencyPath, StringComparer.OrdinalIgnoreCase))
                {
                    plan.ExternalSharedDependencies.Add(dependencyPath);
                    plan.AddWarning("External shared art dependency remains outside SourceRoot and TargetRoot: " + dependencyPath);
                }

                return;
            }

            AddUnique(plan.SharedDependencies, dependencyPath);
            plan.AddWarning("Non-Assets dependency treated as shared: " + dependencyPath);
        }

        /// <summary>
        /// Builds copy records and GUID mappings for collected source assets.
        /// </summary>
        static void BuildGuidMapAndAssetRecords(IReadOnlyList<string> sourceAssetPaths, AssetCloneIsolationPlan plan)
        {
            var targetPathOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string sourceAssetPath in sourceAssetPaths)
            {
                string sourceGuid = AssetDatabase.AssetPathToGUID(sourceAssetPath).ToLowerInvariant();
                if (string.IsNullOrEmpty(sourceGuid))
                {
                    plan.AddError("Source asset is missing GUID: " + sourceAssetPath);
                    continue;
                }

                string targetAssetPath = AssetCloneIsolationUtility.BuildCloneTargetPath(sourceAssetPath, plan.Options);
                if (targetPathOwners.ContainsKey(targetAssetPath))
                {
                    plan.AddError("Target path conflict: " + targetAssetPath + " from " + sourceAssetPath + " and " + targetPathOwners[targetAssetPath]);
                    continue;
                }

                targetPathOwners.Add(targetAssetPath, sourceAssetPath);
                bool targetAlreadyExists = File.Exists(AssetCloneIsolationUtility.ToProjectAbsolutePath(targetAssetPath));
                if (targetAlreadyExists && !plan.Options.OverwriteExistingAssets)
                {
                    plan.AddError("Target asset already exists and overwrite is disabled: " + targetAssetPath);
                    continue;
                }

                string targetGuid = ResolveTargetGuid(targetAssetPath, targetAlreadyExists, sourceGuid, plan);
                plan.GuidMap[sourceGuid] = targetGuid;
                if (!AssetCloneIsolationUtility.IsUnderRoot(sourceAssetPath, plan.Options.SourceRoot))
                {
                    AddUnique(plan.ExplicitCloneExternalDependencies, sourceAssetPath);
                }

                plan.Assets.Add(new AssetCloneIsolationAssetRecord
                {
                    SourceAssetPath = sourceAssetPath,
                    TargetAssetPath = targetAssetPath,
                    SourceGuid = sourceGuid,
                    TargetGuid = targetGuid,
                    TargetAlreadyExists = targetAlreadyExists,
                    IsTextAsset = AssetCloneIsolationUtility.IsTextAssetPath(sourceAssetPath),
                    HasBinaryGuidRisk = !AssetCloneIsolationUtility.IsTextAssetPath(sourceAssetPath)
                                        && File.Exists(AssetCloneIsolationUtility.ToProjectAbsolutePath(sourceAssetPath))
                                        && AssetCloneIsolationUtility.ContainsAsciiGuidMarker(AssetCloneIsolationUtility.ToProjectAbsolutePath(sourceAssetPath))
                });
            }
        }

        /// <summary>
        /// Resolves an existing target GUID or generates a new one.
        /// </summary>
        static string ResolveTargetGuid(string targetAssetPath, bool targetAlreadyExists, string sourceGuid, AssetCloneIsolationPlan plan)
        {
            if (targetAlreadyExists)
            {
                string targetGuid = AssetDatabase.AssetPathToGUID(targetAssetPath).ToLowerInvariant();
                if (!string.IsNullOrEmpty(targetGuid))
                {
                    if (targetGuid.Equals(sourceGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        plan.AddWarning("Existing target asset kept the source GUID; generating an isolated GUID: " + targetAssetPath);
                        return AssetCloneIsolationUtility.GenerateUnityGuid();
                    }

                    return targetGuid;
                }

                string targetMetaPath = AssetCloneIsolationUtility.ToProjectAbsolutePath(targetAssetPath + ".meta");
                if (File.Exists(targetMetaPath)
                    && AssetCloneIsolationUtility.TryReadMetaGuid(File.ReadAllText(targetMetaPath), out targetGuid))
                {
                    if (targetGuid.Equals(sourceGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        plan.AddWarning("Existing target meta kept the source GUID; generating an isolated GUID: " + targetAssetPath);
                        return AssetCloneIsolationUtility.GenerateUnityGuid();
                    }

                    return targetGuid;
                }
            }

            return AssetCloneIsolationUtility.GenerateUnityGuid();
        }

        /// <summary>
        /// Builds a preview list of existing target-root files that need GUID rewrites.
        /// </summary>
        static void CollectExistingTargetRewriteRecords(AssetCloneIsolationPlan plan)
        {
            if (!plan.Options.RewriteExistingTargetAssets || !AssetDatabase.IsValidFolder(plan.Options.TargetRoot) || plan.GuidMap.Count == 0)
            {
                return;
            }

            foreach (string assetPath in FindAssetsUnderRoot(plan.Options.TargetRoot))
            {
                if (!AssetCloneIsolationUtility.IsTextAssetPath(assetPath))
                {
                    continue;
                }

                string absolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetPath);
                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                string originalText = File.ReadAllText(absolutePath);
                AssetCloneIsolationUtility.RewriteGuidReferences(originalText, plan.GuidMap, out int replacementCount);
                if (replacementCount > 0)
                {
                    plan.TargetRewriteRecords.Add(new AssetCloneIsolationRewriteRecord
                    {
                        AssetPath = assetPath,
                        ReplacementCount = replacementCount,
                        GuidMappingCount = CountMappedGuidReferences(originalText, plan)
                    });
                }
            }
        }

        /// <summary>
        /// Builds per-selected-root relationship preview data after the flat write plan is ready.
        /// </summary>
        static void BuildRootPlans(AssetCloneIsolationPlan plan)
        {
            Dictionary<string, HashSet<string>> referenceIndex = BuildReferenceIndex(plan.Options.SourceRoot, plan.Options.TargetRoot);
            foreach (string selectedPath in plan.Options.SelectedAssetPaths)
            {
                AssetCloneIsolationRootPlan rootPlan = CreateRootPlan(selectedPath, plan);
                BuildDownstreamDependencies(rootPlan, plan);
                BuildUpstreamReferences(rootPlan, plan, referenceIndex);
                BuildRootRewriteRecords(rootPlan, plan, referenceIndex);
                plan.RootPlans.Add(rootPlan);
            }
        }

        /// <summary>
        /// Creates the root preview shell for one selected asset or folder.
        /// </summary>
        static AssetCloneIsolationRootPlan CreateRootPlan(string selectedPath, AssetCloneIsolationPlan plan)
        {
            string normalizedPath = AssetCloneIsolationUtility.NormalizeAssetPath(selectedPath);
            bool isFolder = AssetDatabase.IsValidFolder(normalizedPath);
            string rootGuid = isFolder ? string.Empty : AssetDatabase.AssetPathToGUID(normalizedPath).ToLowerInvariant();
            string targetPath = string.Empty;
            string targetGuid = string.Empty;

            if (!isFolder && AssetCloneIsolationUtility.IsUnderRoot(normalizedPath, plan.Options.SourceRoot))
            {
                targetPath = AssetCloneIsolationUtility.BuildTargetPath(normalizedPath, plan.Options.SourceRoot, plan.Options.TargetRoot);
                if (!string.IsNullOrEmpty(rootGuid))
                {
                    plan.GuidMap.TryGetValue(rootGuid, out targetGuid);
                }
            }

            return new AssetCloneIsolationRootPlan
            {
                RootAssetPath = normalizedPath,
                TargetAssetPath = targetPath,
                RootGuid = rootGuid,
                TargetGuid = targetGuid ?? string.Empty,
                IsFolderRoot = isFolder
            };
        }

        /// <summary>
        /// Adds recursive downstream dependency nodes for one root preview.
        /// </summary>
        static void BuildDownstreamDependencies(AssetCloneIsolationRootPlan rootPlan, AssetCloneIsolationPlan plan)
        {
            var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pendingPaths = new Queue<KeyValuePair<string, int>>();

            if (rootPlan.IsFolderRoot)
            {
                foreach (string assetPath in FindAssetsUnderRoot(rootPlan.RootAssetPath))
                {
                    pendingPaths.Enqueue(new KeyValuePair<string, int>(assetPath, 1));
                }
            }
            else
            {
                foreach (string dependencyPath in GetDirectDependencyPaths(rootPlan.RootAssetPath))
                {
                    pendingPaths.Enqueue(new KeyValuePair<string, int>(dependencyPath, 1));
                }
            }

            while (pendingPaths.Count > 0)
            {
                KeyValuePair<string, int> pending = pendingPaths.Dequeue();
                string dependencyPath = AssetCloneIsolationUtility.NormalizeAssetPath(pending.Key);
                if (string.IsNullOrEmpty(dependencyPath)
                    || dependencyPath.Equals(rootPlan.RootAssetPath, StringComparison.OrdinalIgnoreCase)
                    || !visitedPaths.Add(dependencyPath))
                {
                    continue;
                }

                AssetCloneIsolationRelationNode node = CreateRelationNode(
                    dependencyPath,
                    plan,
                    AssetCloneIsolationRelationKind.Dependency,
                    DetermineDependencyDecision(dependencyPath, plan),
                    pending.Value);
                rootPlan.DownstreamDependencies.Add(node);

                if (node.Decision != AssetCloneIsolationDecision.Clone
                    && node.Decision != AssetCloneIsolationDecision.ExternalClone)
                {
                    continue;
                }

                foreach (string childDependencyPath in GetDirectDependencyPaths(dependencyPath))
                {
                    pendingPaths.Enqueue(new KeyValuePair<string, int>(childDependencyPath, pending.Value + 1));
                }
            }
        }

        /// <summary>
        /// Adds source-root and target-root assets that directly reference this root and separately tracks shared dependency users.
        /// </summary>
        static void BuildUpstreamReferences(
            AssetCloneIsolationRootPlan rootPlan,
            AssetCloneIsolationPlan plan,
            Dictionary<string, HashSet<string>> referenceIndex)
        {
            HashSet<string> directUpstreamGuids = BuildDirectUpstreamGuidSet(rootPlan);
            HashSet<string> sharedDependencyGuids = BuildSharedDependencyGuidSet(rootPlan, directUpstreamGuids);
            if (directUpstreamGuids.Count == 0 && sharedDependencyGuids.Count == 0)
            {
                return;
            }

            var ownedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootPlan.RootAssetPath };
            foreach (AssetCloneIsolationRelationNode dependencyNode in rootPlan.DownstreamDependencies)
            {
                ownedPaths.Add(dependencyNode.AssetPath);
            }

            foreach (KeyValuePair<string, HashSet<string>> pair in referenceIndex)
            {
                if (ownedPaths.Contains(pair.Key))
                {
                    continue;
                }

                if (pair.Value.Overlaps(directUpstreamGuids))
                {
                    rootPlan.UpstreamReferences.Add(CreateRelationNode(
                        pair.Key,
                        plan,
                        AssetCloneIsolationRelationKind.UpstreamReference,
                        AssetCloneIsolationDecision.ReferenceOnly,
                        0));
                    continue;
                }

                if (pair.Value.Overlaps(sharedDependencyGuids))
                {
                    rootPlan.SharedDependencyReferences.Add(CreateRelationNode(
                        pair.Key,
                        plan,
                        AssetCloneIsolationRelationKind.SharedDependencyReference,
                        AssetCloneIsolationDecision.ReferenceOnly,
                        0));
                }
            }
        }

        /// <summary>
        /// Adds target-root rewrite records that reference this root graph.
        /// </summary>
        static void BuildRootRewriteRecords(
            AssetCloneIsolationRootPlan rootPlan,
            AssetCloneIsolationPlan plan,
            Dictionary<string, HashSet<string>> referenceIndex)
        {
            HashSet<string> rewriteGuids = BuildRewriteSourceGuidSet(rootPlan, plan);
            if (rewriteGuids.Count == 0)
            {
                return;
            }

            foreach (AssetCloneIsolationRewriteRecord rewriteRecord in plan.TargetRewriteRecords)
            {
                HashSet<string> referencedGuids;
                if (referenceIndex.TryGetValue(rewriteRecord.AssetPath, out referencedGuids)
                    && referencedGuids.Overlaps(rewriteGuids))
                {
                    rootPlan.TargetRewriteRecords.Add(rewriteRecord);
                }
            }
        }

        /// <summary>
        /// Builds a text GUID reference index for the source and target roots.
        /// </summary>
        static Dictionary<string, HashSet<string>> BuildReferenceIndex(string sourceRoot, string targetRoot)
        {
            return AssetCloneIsolationReferenceIndex.BuildReferenceIndex(sourceRoot, targetRoot);
        }

        /// <summary>
        /// Returns direct dependency paths found by Unity and by text GUID scan.
        /// </summary>
        static List<string> GetDirectDependencyPaths(string assetPath)
        {
            return AssetCloneIsolationReferenceIndex.GetDirectDependencyPaths(assetPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Creates one relation node with mapped target path and GUID data.
        /// </summary>
        static AssetCloneIsolationRelationNode CreateRelationNode(
            string assetPath,
            AssetCloneIsolationPlan plan,
            AssetCloneIsolationRelationKind relationKind,
            AssetCloneIsolationDecision decision,
            int depth)
        {
            string normalizedPath = AssetCloneIsolationUtility.NormalizeAssetPath(assetPath);
            string guid = AssetDatabase.AssetPathToGUID(normalizedPath).ToLowerInvariant();
            string targetGuid = string.Empty;
            string targetPath = string.Empty;

            if (!string.IsNullOrEmpty(guid))
            {
                plan.GuidMap.TryGetValue(guid, out targetGuid);
            }

            if (AssetCloneIsolationUtility.IsUnderRoot(normalizedPath, plan.Options.SourceRoot))
            {
                targetPath = AssetCloneIsolationUtility.BuildTargetPath(normalizedPath, plan.Options.SourceRoot, plan.Options.TargetRoot);
            }
            else if (IsExplicitExternalCloneDependency(normalizedPath, plan.Options))
            {
                targetPath = AssetCloneIsolationUtility.BuildExternalTargetPath(normalizedPath, plan.Options.TargetRoot);
            }
            else if (AssetCloneIsolationUtility.IsUnderRoot(normalizedPath, plan.Options.TargetRoot))
            {
                targetPath = normalizedPath;
            }

            return new AssetCloneIsolationRelationNode
            {
                AssetPath = normalizedPath,
                TargetAssetPath = targetPath,
                Guid = guid,
                TargetGuid = targetGuid ?? string.Empty,
                AssetType = GetAssetTypeName(normalizedPath),
                RelationKind = relationKind,
                Decision = decision,
                Depth = depth,
                Detail = BuildDecisionDetail(normalizedPath, decision, plan)
            };
        }

        /// <summary>
        /// Determines how one downstream dependency should be handled.
        /// </summary>
        static AssetCloneIsolationDecision DetermineDependencyDecision(string dependencyPath, AssetCloneIsolationPlan plan)
        {
            string normalizedPath = AssetCloneIsolationUtility.NormalizeAssetPath(dependencyPath);
            if (string.IsNullOrEmpty(normalizedPath) || string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(normalizedPath)))
            {
                return AssetCloneIsolationDecision.MissingOrUnknown;
            }

            if (AssetCloneIsolationUtility.IsUnderRoot(normalizedPath, plan.Options.TargetRoot))
            {
                return AssetCloneIsolationDecision.AlreadyInTarget;
            }

            if (AssetCloneIsolationUtility.IsUnderRoot(normalizedPath, plan.Options.SourceRoot))
            {
                if (AssetCloneIsolationUtility.IsSharedCodeAssetPath(normalizedPath))
                {
                    return AssetCloneIsolationDecision.SharedDependency;
                }

                return IsExplicitSharedDependency(normalizedPath, plan.Options)
                    ? AssetCloneIsolationDecision.ExplicitShared
                    : AssetCloneIsolationDecision.Clone;
            }

            if (AssetCloneIsolationUtility.IsAllowedSharedDependencyPath(normalizedPath, plan.Options.SourceRoot, plan.Options.TargetRoot))
            {
                return AssetCloneIsolationDecision.SharedDependency;
            }

            if (IsExplicitExternalCloneDependency(normalizedPath, plan.Options))
            {
                return AssetCloneIsolationDecision.ExternalClone;
            }

            return normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? AssetCloneIsolationDecision.ExternalShared
                : AssetCloneIsolationDecision.SharedDependency;
        }

        /// <summary>
        /// Builds a short display reason for one relation decision.
        /// </summary>
        static string BuildDecisionDetail(string assetPath, AssetCloneIsolationDecision decision, AssetCloneIsolationPlan plan)
        {
            switch (decision)
            {
                case AssetCloneIsolationDecision.Clone:
                    return "Clone to " + AssetCloneIsolationUtility.BuildCloneTargetPath(assetPath, plan.Options);
                case AssetCloneIsolationDecision.ExplicitShared:
                    return "Explicit shared dependency; target assets keep the source GUID.";
                case AssetCloneIsolationDecision.ExternalShared:
                    return "External Assets dependency stays shared by default; migrate it to fully isolate.";
                case AssetCloneIsolationDecision.ExternalClone:
                    return "External Assets dependency clones to " + AssetCloneIsolationUtility.BuildExternalTargetPath(assetPath, plan.Options.TargetRoot);
                case AssetCloneIsolationDecision.SharedDependency:
                    return "Allowed shared dependency.";
                case AssetCloneIsolationDecision.BlockedExternal:
                    return "External art dependency blocks applying the plan.";
                case AssetCloneIsolationDecision.AlreadyInTarget:
                    return "Already under TargetRoot.";
                case AssetCloneIsolationDecision.ReferenceOnly:
                    return "References this root graph; not cloned unless selected separately.";
                default:
                    return "Missing or unresolved dependency.";
            }
        }

        /// <summary>
        /// Builds the GUID set used to find direct upstream references for one root.
        /// </summary>
        static HashSet<string> BuildDirectUpstreamGuidSet(AssetCloneIsolationRootPlan rootPlan)
        {
            var directGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (rootPlan.IsFolderRoot)
            {
                foreach (string assetPath in FindAssetsUnderRoot(rootPlan.RootAssetPath))
                {
                    string guid = AssetDatabase.AssetPathToGUID(assetPath).ToLowerInvariant();
                    if (!string.IsNullOrEmpty(guid))
                    {
                        directGuids.Add(guid);
                    }
                }

                return directGuids;
            }

            if (!string.IsNullOrEmpty(rootPlan.RootGuid))
            {
                directGuids.Add(rootPlan.RootGuid);
            }

            return directGuids;
        }

        /// <summary>
        /// Builds the GUID set used to find assets that share downstream dependencies with one root.
        /// </summary>
        static HashSet<string> BuildSharedDependencyGuidSet(AssetCloneIsolationRootPlan rootPlan, HashSet<string> directUpstreamGuids)
        {
            var sharedDependencyGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AssetCloneIsolationRelationNode dependencyNode in rootPlan.DownstreamDependencies)
            {
                if (!string.IsNullOrEmpty(dependencyNode.Guid)
                    && (directUpstreamGuids == null || !directUpstreamGuids.Contains(dependencyNode.Guid)))
                {
                    sharedDependencyGuids.Add(dependencyNode.Guid);
                }
            }

            return sharedDependencyGuids;
        }

        /// <summary>
        /// Builds the source GUID set that should be rewritten for one root graph.
        /// </summary>
        static HashSet<string> BuildRewriteSourceGuidSet(AssetCloneIsolationRootPlan rootPlan, AssetCloneIsolationPlan plan)
        {
            var rewriteGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(rootPlan.RootGuid) && plan.GuidMap.ContainsKey(rootPlan.RootGuid))
            {
                rewriteGuids.Add(rootPlan.RootGuid);
            }

            foreach (AssetCloneIsolationRelationNode dependencyNode in rootPlan.DownstreamDependencies)
            {
                if (!string.IsNullOrEmpty(dependencyNode.Guid) && plan.GuidMap.ContainsKey(dependencyNode.Guid))
                {
                    rewriteGuids.Add(dependencyNode.Guid);
                }
            }

            return rewriteGuids;
        }

        /// <summary>
        /// Returns a compact asset type name for UI grouping.
        /// </summary>
        static string GetAssetTypeName(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return "Folder";
            }

            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
            if (assetType != null)
            {
                return assetType.Name;
            }

            string extension = Path.GetExtension(assetPath);
            return string.IsNullOrEmpty(extension) ? "Asset" : extension.TrimStart('.').ToUpperInvariant();
        }

        /// <summary>
        /// Writes one cloned asset and its meta file to the target path.
        /// </summary>
        static void WriteClonedAsset(AssetCloneIsolationAssetRecord assetRecord, AssetCloneIsolationPlan plan)
        {
            string sourceAbsolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetRecord.SourceAssetPath);
            string targetAbsolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetRecord.TargetAssetPath);
            string targetDirectory = Path.GetDirectoryName(targetAbsolutePath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            if (assetRecord.IsTextAsset)
            {
                string sourceText = File.ReadAllText(sourceAbsolutePath);
                string rewrittenText = AssetCloneIsolationUtility.RewriteGuidReferences(sourceText, plan.GuidMap, out int replacementCount);
                File.WriteAllText(targetAbsolutePath, rewrittenText, new UTF8Encoding(false));
                if (replacementCount > 0)
                {
                    plan.AddInfo("Rewrote cloned asset GUIDs: " + assetRecord.TargetAssetPath + " count=" + replacementCount);
                }
            }
            else
            {
                File.Copy(sourceAbsolutePath, targetAbsolutePath, true);
                if (assetRecord.HasBinaryGuidRisk)
                {
                    plan.AddWarning("Binary asset may contain GUID text and was copied without internal rewrite: " + assetRecord.SourceAssetPath);
                }
            }

            WriteMetaFile(assetRecord, plan);
        }

        /// <summary>
        /// Writes a cloned meta file using source import settings and target GUID.
        /// </summary>
        static void WriteMetaFile(AssetCloneIsolationAssetRecord assetRecord, AssetCloneIsolationPlan plan)
        {
            string sourceMetaPath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetRecord.SourceAssetPath + ".meta");
            string targetMetaPath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetRecord.TargetAssetPath + ".meta");
            string targetDirectory = Path.GetDirectoryName(targetMetaPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            string metaText = File.Exists(sourceMetaPath) ? File.ReadAllText(sourceMetaPath) : string.Empty;
            metaText = AssetCloneIsolationUtility.ReplaceOrCreateMetaGuid(metaText, assetRecord.TargetGuid);
            metaText = AssetCloneIsolationUtility.RewriteGuidReferences(metaText, plan.GuidMap, out int replacementCount);
            File.WriteAllText(targetMetaPath, metaText, new UTF8Encoding(false));

            if (!File.Exists(sourceMetaPath))
            {
                plan.AddWarning("Source meta was missing, generated minimal target meta: " + assetRecord.SourceAssetPath);
            }

            if (replacementCount > 0)
            {
                plan.AddInfo("Rewrote cloned meta GUIDs: " + assetRecord.TargetAssetPath + ".meta count=" + replacementCount);
            }
        }

        /// <summary>
        /// Rewrites all target-root text files on disk after clone writes complete.
        /// </summary>
        static void RewriteTargetRootFiles(AssetCloneIsolationPlan plan)
        {
            string targetRootAbsolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(plan.Options.TargetRoot);
            if (!Directory.Exists(targetRootAbsolutePath))
            {
                return;
            }

            foreach (string filePath in Directory.GetFiles(targetRootAbsolutePath, "*", SearchOption.AllDirectories))
            {
                string assetPath = AssetCloneIsolationUtility.ToAssetPath(filePath);
                if (string.IsNullOrEmpty(assetPath) || !AssetCloneIsolationUtility.IsTextAssetPath(assetPath))
                {
                    continue;
                }

                string originalText = File.ReadAllText(filePath);
                string rewrittenText = AssetCloneIsolationUtility.RewriteGuidReferences(originalText, plan.GuidMap, out int replacementCount);
                if (replacementCount <= 0)
                {
                    continue;
                }

                File.WriteAllText(filePath, rewrittenText, new UTF8Encoding(false));
                if (!plan.TargetRewriteRecords.Exists(record => record.AssetPath.Equals(assetPath, StringComparison.OrdinalIgnoreCase)))
                {
                    plan.TargetRewriteRecords.Add(new AssetCloneIsolationRewriteRecord
                    {
                        AssetPath = assetPath,
                        ReplacementCount = replacementCount,
                        GuidMappingCount = CountMappedGuidReferences(originalText, plan)
                    });
                }
            }
        }

        /// <summary>
        /// Audits dependencies reported by Unity for every target-root asset.
        /// </summary>
        static void AuditDependencies(
            IReadOnlyList<string> assetPaths,
            string targetRoot,
            string sourceRoot,
            IReadOnlyCollection<string> explicitSharedAssetPaths,
            AssetCloneIsolationAuditReport report)
        {
            foreach (string assetPath in assetPaths)
            {
                foreach (string rawDependencyPath in AssetDatabase.GetDependencies(assetPath, true))
                {
                    string dependencyPath = AssetCloneIsolationUtility.NormalizeAssetPath(rawDependencyPath);
                    if (string.IsNullOrEmpty(dependencyPath) || dependencyPath.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (AssetCloneIsolationUtility.IsAllowedSharedDependencyPath(dependencyPath, sourceRoot, targetRoot))
                    {
                        continue;
                    }

                    if (AssetCloneIsolationUtility.IsUnderRoot(dependencyPath, sourceRoot))
                    {
                        if (explicitSharedAssetPaths != null && explicitSharedAssetPaths.Contains(dependencyPath))
                        {
                            report.AddWarning("Explicit shared source dependency remains linked by user choice: " + assetPath + " -> " + dependencyPath);
                            continue;
                        }

                        report.AddError("Target asset still depends on source art: " + assetPath + " -> " + dependencyPath);
                        continue;
                    }

                    if (dependencyPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        report.AddWarning("Target asset keeps external shared art dependency risk; migrate it to fully isolate: " + assetPath + " -> " + dependencyPath);
                    }
                }
            }
        }

        /// <summary>
        /// Audits material shader paths.
        /// </summary>
        static void AuditMaterialShaders(
            IReadOnlyList<string> assetPaths,
            string targetRoot,
            string sourceRoot,
            IReadOnlyCollection<string> explicitSharedAssetPaths,
            AssetCloneIsolationAuditReport report)
        {
            foreach (string assetPath in assetPaths)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (material == null)
                {
                    continue;
                }

                Shader shader = material.shader;
                string shaderPath = shader == null ? string.Empty : AssetDatabase.GetAssetPath(shader);
                if (!AssetCloneIsolationUtility.IsAllowedShaderPath(shaderPath, targetRoot))
                {
                    if (explicitSharedAssetPaths != null && explicitSharedAssetPaths.Contains(shaderPath))
                    {
                        report.AddWarning("Material uses explicitly shared shader: " + assetPath + " -> " + shaderPath);
                        continue;
                    }

                    if (AssetCloneIsolationUtility.IsUnderRoot(shaderPath, sourceRoot))
                    {
                        report.AddError("Material uses non-isolated source shader: " + assetPath + " -> " + shaderPath);
                        continue;
                    }

                    if (shaderPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        report.AddWarning("Material uses external shared shader risk: " + assetPath + " -> " + shaderPath);
                        continue;
                    }

                    report.AddError("Material uses non-isolated shader: " + assetPath + " -> " + shaderPath);
                }
            }
        }

        /// <summary>
        /// Audits text files for GUID references that Unity cannot resolve.
        /// </summary>
        static void AuditUnknownGuidReferences(IReadOnlyList<string> assetPaths, AssetCloneIsolationAuditReport report)
        {
            foreach (string assetPath in assetPaths)
            {
                if (!AssetCloneIsolationUtility.IsTextAssetPath(assetPath))
                {
                    continue;
                }

                string absolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetPath);
                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                foreach (string guid in AssetCloneIsolationUtility.ExtractGuidReferences(File.ReadAllText(absolutePath)))
                {
                    if (!AssetCloneIsolationUtility.IsBuiltInOrEmptyGuid(guid)
                        && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)))
                    {
                        report.AddError("Target asset contains unresolved GUID: " + assetPath + " -> " + guid);
                    }
                }
            }
        }

        /// <summary>
        /// Audits shader files for mobile variant risk markers.
        /// </summary>
        static void AuditShaderVariantRisk(IReadOnlyList<string> assetPaths, AssetCloneIsolationAuditReport report)
        {
            foreach (string assetPath in assetPaths)
            {
                string extension = Path.GetExtension(assetPath);
                if (!extension.Equals(".shader", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".shadergraph", StringComparison.OrdinalIgnoreCase)
                    && !extension.Equals(".compute", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                report.ShaderAssetCount++;
                string absolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetPath);
                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(absolutePath);
                int multiCompileCount = lines.Count(line => line.IndexOf("multi_compile", StringComparison.OrdinalIgnoreCase) >= 0);
                int shaderFeatureCount = lines.Count(line => line.IndexOf("shader_feature", StringComparison.OrdinalIgnoreCase) >= 0);

                if (multiCompileCount > 0)
                {
                    report.AddWarning("Shader uses multi_compile; check mobile variant count: " + assetPath + " count=" + multiCompileCount);
                }

                if (shaderFeatureCount > 0)
                {
                    report.AddInfo("Shader uses shader_feature; keep keyword combinations bounded: " + assetPath + " count=" + shaderFeatureCount);
                }
            }
        }

        /// <summary>
        /// Audits duplicate filenames that can affect address-by-name workflows.
        /// </summary>
        static void AuditDuplicateFileNames(IReadOnlyList<string> assetPaths, AssetCloneIsolationAuditReport report)
        {
            foreach (IGrouping<string, string> group in assetPaths
                         .Where(path => !string.IsNullOrEmpty(Path.GetFileName(path)))
                         .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1)
                         .Take(20))
            {
                report.AddWarning("Duplicate file name risk: " + group.Key + " count=" + group.Count());
            }
        }

        /// <summary>
        /// Finds all non-folder assets under an asset root.
        /// </summary>
        static List<string> FindAssetsUnderRoot(string rootPath)
        {
            var assetPaths = new List<string>();
            if (!AssetDatabase.IsValidFolder(rootPath))
            {
                return assetPaths;
            }

            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { rootPath }))
            {
                string assetPath = AssetCloneIsolationUtility.NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                if (!string.IsNullOrEmpty(assetPath) && !AssetDatabase.IsValidFolder(assetPath))
                {
                    assetPaths.Add(assetPath);
                }
            }

            return assetPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Adds an item to a list when it does not already exist.
        /// </summary>
        static void AddUnique(List<string> list, string value)
        {
            if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(value);
            }
        }

        /// <summary>
        /// Counts the distinct GUID mappings present in one text file.
        /// </summary>
        static int CountMappedGuidReferences(string sourceText, AssetCloneIsolationPlan plan)
        {
            if (plan == null || plan.GuidMap.Count == 0)
            {
                return 0;
            }

            return AssetCloneIsolationUtility.ExtractGuidReferences(sourceText)
                .Count(guid => plan.GuidMap.ContainsKey(guid));
        }

        /// <summary>
        /// Returns true when the dependency is configured as an explicit shared asset.
        /// </summary>
        static bool IsExplicitSharedDependency(string assetPath, AssetCloneIsolationOptions options)
        {
            if (options == null || options.ExplicitSharedAssetPaths == null)
            {
                return false;
            }

            string normalizedPath = AssetCloneIsolationUtility.NormalizeAssetPath(assetPath);
            return options.ExplicitSharedAssetPaths.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true when an external Assets dependency should be cloned into the target external bucket.
        /// </summary>
        static bool IsExplicitExternalCloneDependency(string assetPath, AssetCloneIsolationOptions options)
        {
            if (options == null || options.ExplicitCloneExternalAssetPaths == null)
            {
                return false;
            }

            string normalizedPath = AssetCloneIsolationUtility.NormalizeAssetPath(assetPath);
            return normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                   && !AssetCloneIsolationUtility.IsUnderRoot(normalizedPath, options.SourceRoot)
                   && !AssetCloneIsolationUtility.IsUnderRoot(normalizedPath, options.TargetRoot)
                   && options.ExplicitCloneExternalAssetPaths.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Normalizes explicit shared paths into a case-insensitive lookup set.
        /// </summary>
        static HashSet<string> BuildExplicitSharedSet(IReadOnlyCollection<string> explicitSharedAssetPaths)
        {
            var explicitSharedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (explicitSharedAssetPaths == null)
            {
                return explicitSharedSet;
            }

            foreach (string assetPath in explicitSharedAssetPaths)
            {
                string normalizedPath = AssetCloneIsolationUtility.NormalizeAssetPath(assetPath);
                if (!string.IsNullOrEmpty(normalizedPath))
                {
                    explicitSharedSet.Add(normalizedPath);
                }
            }

            return explicitSharedSet;
        }

        /// <summary>
        /// Reports binary GUID risk for one source asset.
        /// </summary>
        static void AuditBinaryGuidRisk(string assetPath, AssetCloneIsolationPlan plan)
        {
            string absolutePath = AssetCloneIsolationUtility.ToProjectAbsolutePath(assetPath);
            if (File.Exists(absolutePath) && AssetCloneIsolationUtility.ContainsAsciiGuidMarker(absolutePath))
            {
                plan.AddWarning("Binary asset may contain GUID text and cannot be safely rewritten internally: " + assetPath);
            }
        }
    }
}
