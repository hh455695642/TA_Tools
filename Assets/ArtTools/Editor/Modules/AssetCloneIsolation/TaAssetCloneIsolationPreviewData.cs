using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetCloneIsolation.Editor;
using UnityEditor;

namespace TA.ArtTools.Editor
{
    internal enum IsolationPreviewTab { Writes, Dependencies, Issues }
    internal enum IsolationDependencyAction { FollowClone, Share, MigrateExternal, KeepExternal }

    internal sealed class IsolationPreviewRow
    {
        public string Key;
        public string SourcePath = string.Empty;
        public string TargetPath = string.Empty;
        public string AssetType = string.Empty;
        public string Status = string.Empty;
        public string Detail = string.Empty;
        public string SourceGuid = string.Empty;
        public string TargetGuid = string.Empty;
        public bool CanChangeSource;
        public bool CanChangeExternal;
        public AssetCloneIsolationDecision Decision;
        public ArtToolChangeSeverity Severity;
        public readonly HashSet<string> Owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Operations = new HashSet<string>();
        public string Name => string.IsNullOrEmpty(SourcePath) && string.IsNullOrEmpty(TargetPath)
            ? "计划问题" : Path.GetFileName(string.IsNullOrEmpty(SourcePath) ? TargetPath : SourcePath);

        public bool Matches(string search, string status, string owner)
        {
            return (string.IsNullOrEmpty(search) || Contains(Name, search) || Contains(SourcePath, search)
                    || Contains(TargetPath, search) || Contains(Detail, search))
                && (status == "全部" || Status == status || Operations.Contains(status)
                    || (status == "保留共享" && (Decision == AssetCloneIsolationDecision.ExplicitShared
                        || Decision == AssetCloneIsolationDecision.ExternalShared
                        || Decision == AssetCloneIsolationDecision.SharedDependency)))
                && (owner == "全部对象" || Owners.Contains(owner) || (Key.StartsWith("issue:", StringComparison.Ordinal) && Owners.Count == 0));
        }

        static bool Contains(string text, string value) => (text ?? string.Empty).IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

        public bool Supports(IsolationDependencyAction action)
        {
            if (action == IsolationDependencyAction.Share)
                return CanChangeSource && Decision == AssetCloneIsolationDecision.Clone;
            if (action == IsolationDependencyAction.FollowClone)
                return CanChangeSource && Decision == AssetCloneIsolationDecision.ExplicitShared;
            if (action == IsolationDependencyAction.MigrateExternal)
                return CanChangeExternal && Decision == AssetCloneIsolationDecision.ExternalShared;
            return CanChangeExternal && Decision == AssetCloneIsolationDecision.ExternalClone;
        }
    }

    internal sealed class IsolationPreviewData
    {
        public readonly List<IsolationPreviewRow> Writes = new List<IsolationPreviewRow>();
        public readonly List<IsolationPreviewRow> Dependencies = new List<IsolationPreviewRow>();
        public readonly List<IsolationPreviewRow> Issues = new List<IsolationPreviewRow>();
        public AssetCloneIsolationPlan Plan;

        public static string BuildApplySummary(AssetCloneIsolationPlan plan)
        {
            AssetCloneIsolationPlanSummary summary = AssetCloneIsolationPlanSummary.FromPlan(plan);
            return $"新建 {summary.NewTargetAssetCount} 个资产文件，覆盖 {summary.ExistingTargetAssetCount} 个资产文件，修复引用 {summary.TargetRewriteFileCount} 个文件。"
                + "（克隆同时写入对应 .meta；同一文件可能涉及多项操作。）";
        }

        public List<IsolationPreviewRow> Rows(IsolationPreviewTab tab) => tab == IsolationPreviewTab.Writes
            ? Writes : tab == IsolationPreviewTab.Dependencies ? Dependencies : Issues;

        public static IsolationPreviewData Build(AssetCloneIsolationPlan plan)
        {
            var data = new IsolationPreviewData { Plan = plan };
            var dependencies = new Dictionary<string, IsolationPreviewRow>(StringComparer.OrdinalIgnoreCase);
            var writes = new Dictionary<string, IsolationPreviewRow>(StringComparer.OrdinalIgnoreCase);
            var selected = new HashSet<string>(plan.Options.SelectedAssetPaths, StringComparer.OrdinalIgnoreCase);
            var rootPaths = new HashSet<string>(plan.RootPlans.Select(root => root.RootAssetPath), StringComparer.OrdinalIgnoreCase);

            foreach (AssetCloneIsolationRootPlan root in plan.RootPlans)
            {
                foreach (AssetCloneIsolationRelationNode node in root.DownstreamDependencies)
                {
                    IsolationPreviewRow row = GetDependency(dependencies, node);
                    row.Owners.Add(root.RootAssetPath);
                }
            }

            foreach (AssetCloneIsolationAssetRecord record in plan.Assets)
            {
                if (!dependencies.TryGetValue(record.SourceAssetPath, out IsolationPreviewRow dependency))
                {
                    dependency = GetDependency(dependencies, new AssetCloneIsolationRelationNode
                    {
                        AssetPath = record.SourceAssetPath, TargetAssetPath = record.TargetAssetPath,
                        Guid = record.SourceGuid, TargetGuid = record.TargetGuid,
                        Decision = AssetCloneIsolationUtility.IsUnderRoot(record.SourceAssetPath, plan.Options.SourceRoot)
                            ? AssetCloneIsolationDecision.Clone : AssetCloneIsolationDecision.ExternalClone
                    });
                }
                if (rootPaths.Contains(record.SourceAssetPath)) dependency.Owners.Add(record.SourceAssetPath);
                AssetCloneIsolationDecision writeDecision = AssetCloneIsolationUtility.IsUnderRoot(record.SourceAssetPath, plan.Options.SourceRoot)
                    ? AssetCloneIsolationDecision.Clone : AssetCloneIsolationDecision.ExternalClone;
                if (dependency.Decision != writeDecision)
                    dependency.Detail = selected.Contains(record.SourceAssetPath)
                        ? "该资源已直接选为待克隆对象，按实际写入计划克隆；共享依赖设置不覆盖直接选择。"
                        : "该资源已纳入实际写入计划，将克隆到目标目录。";
                dependency.Decision = writeDecision;
                dependency.Status = DecisionText(dependency.Decision);
                dependency.TargetPath = record.TargetAssetPath;
                dependency.SourceGuid = record.SourceGuid;
                dependency.TargetGuid = record.TargetGuid;

                string key = AssetCloneIsolationUtility.NormalizeAssetPath(record.TargetAssetPath);
                if (!writes.TryGetValue(key, out IsolationPreviewRow row))
                {
                    row = new IsolationPreviewRow
                    {
                        Key = "write:" + key, SourcePath = record.SourceAssetPath, TargetPath = key,
                        AssetType = dependency.AssetType, SourceGuid = record.SourceGuid,
                        TargetGuid = record.TargetGuid,
                        Detail = record.TargetAlreadyExists ? "覆盖资产内容，保留目标 .meta GUID。" : "创建目标资产及隔离 GUID。"
                    };
                    writes.Add(key, row);
                }
                row.Operations.Add(record.TargetAlreadyExists ? "覆盖" : "新建");
                if (!AssetCloneIsolationUtility.IsUnderRoot(record.SourceAssetPath, plan.Options.SourceRoot))
                    row.Operations.Add("外部迁移");
                row.Owners.UnionWith(dependency.Owners);
            }

            foreach (AssetCloneIsolationRewriteRecord record in plan.TargetRewriteRecords)
            {
                string key = AssetCloneIsolationUtility.NormalizeAssetPath(record.AssetPath);
                if (!writes.TryGetValue(key, out IsolationPreviewRow row))
                {
                    row = new IsolationPreviewRow { Key = "write:" + key, TargetPath = key, AssetType = AssetType(key),
                        TargetGuid = AssetDatabase.AssetPathToGUID(key) };
                    writes.Add(key, row);
                }
                row.Operations.Add("引用修复");
                row.Detail += $" 修复旧 GUID 引用 {record.ReplacementCount} 次，涉及 {record.GuidMappingCount} 个映射。";
                foreach (AssetCloneIsolationRootPlan root in plan.RootPlans)
                    if (root.TargetRewriteRecords.Any(item => string.Equals(item.AssetPath, key, StringComparison.OrdinalIgnoreCase)))
                        row.Owners.Add(root.RootAssetPath);
            }

            foreach (IsolationPreviewRow row in dependencies.Values)
            {
                bool adjustable = !selected.Contains(row.SourcePath)
                    && !AssetCloneIsolationUtility.IsSharedCodeAssetPath(row.SourcePath);
                row.CanChangeSource = adjustable
                    && AssetCloneIsolationUtility.IsUnderRoot(row.SourcePath, plan.Options.SourceRoot)
                    && (row.Decision == AssetCloneIsolationDecision.Clone || row.Decision == AssetCloneIsolationDecision.ExplicitShared);
                row.CanChangeExternal = adjustable && row.SourcePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                    && !AssetCloneIsolationUtility.IsUnderRoot(row.SourcePath, plan.Options.SourceRoot)
                    && !AssetCloneIsolationUtility.IsUnderRoot(row.SourcePath, plan.Options.TargetRoot)
                    && (row.Decision == AssetCloneIsolationDecision.ExternalShared || row.Decision == AssetCloneIsolationDecision.ExternalClone);
            }
            foreach (IsolationPreviewRow row in writes.Values)
                row.Status = string.Join(" / ", new[] { "新建", "覆盖", "外部迁移", "引用修复" }.Where(row.Operations.Contains));

            data.Writes.AddRange(writes.Values.OrderBy(row => row.TargetPath, StringComparer.OrdinalIgnoreCase));
            data.Dependencies.AddRange(dependencies.Values.OrderBy(row => row.SourcePath, StringComparer.OrdinalIgnoreCase));
            var issueKeys = new HashSet<string>();
            foreach (string message in plan.Errors)
                AddIssue(data, issueKeys, message, ArtToolChangeSeverity.Error);
            foreach (string message in plan.Warnings)
                AddIssue(data, issueKeys, message, ArtToolChangeSeverity.Warning);
            foreach (AssetCloneIsolationRootPlan root in plan.RootPlans)
            {
                foreach (string message in root.Errors)
                    AddIssue(data, issueKeys, message, ArtToolChangeSeverity.Error, root.RootAssetPath);
                foreach (string message in root.Warnings)
                    AddIssue(data, issueKeys, message, ArtToolChangeSeverity.Warning, root.RootAssetPath);
            }
            foreach (IsolationPreviewRow row in data.Dependencies)
            {
                if (row.Decision != AssetCloneIsolationDecision.ExternalShared && row.Decision != AssetCloneIsolationDecision.ExplicitShared
                    && row.Decision != AssetCloneIsolationDecision.MissingOrUnknown && row.Decision != AssetCloneIsolationDecision.BlockedExternal)
                    continue;
                ArtToolChangeSeverity severity = row.Decision == AssetCloneIsolationDecision.BlockedExternal && plan.HasErrors
                    ? ArtToolChangeSeverity.Error : ArtToolChangeSeverity.Warning;
                if (!data.Issues.Any(issue => issue.Severity == severity && issue.SourcePath == row.SourcePath))
                    AddIssue(data, issueKeys, row.Status + "：" + row.SourcePath + "。" + row.Detail, severity);
            }
            data.Issues.Sort((a, b) => b.Severity.CompareTo(a.Severity));
            return data;
        }

        static IsolationPreviewRow GetDependency(Dictionary<string, IsolationPreviewRow> rows, AssetCloneIsolationRelationNode node)
        {
            string path = AssetCloneIsolationUtility.NormalizeAssetPath(node.AssetPath);
            if (!rows.TryGetValue(path, out IsolationPreviewRow row))
            {
                row = new IsolationPreviewRow
                {
                    Key = "dependency:" + path, SourcePath = path, TargetPath = node.TargetAssetPath,
                    AssetType = string.IsNullOrEmpty(node.AssetType) ? AssetType(path) : node.AssetType,
                    Decision = node.Decision, Status = DecisionText(node.Decision), Detail = node.Detail,
                    SourceGuid = node.Guid, TargetGuid = node.TargetGuid
                };
                rows.Add(path, row);
            }
            return row;
        }

        static void AddIssue(IsolationPreviewData data, HashSet<string> keys, string message, ArtToolChangeSeverity severity, string owner = null)
        {
            string key = "issue:" + severity + ":" + message;
            IsolationPreviewRow row = data.Issues.FirstOrDefault(item => item.Key == key);
            if (row == null && keys.Add(key))
            {
                row = new IsolationPreviewRow { Key = key, Detail = message, Severity = severity,
                    Status = severity == ArtToolChangeSeverity.Error ? "阻断错误" : "警告" };
                foreach (IsolationPreviewRow dependency in data.Dependencies)
                {
                    if (message.IndexOf(dependency.SourcePath, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (string.IsNullOrEmpty(row.SourcePath))
                    {
                        row.SourcePath = dependency.SourcePath;
                        row.TargetPath = dependency.TargetPath;
                        row.AssetType = dependency.AssetType;
                        row.SourceGuid = dependency.SourceGuid;
                        row.TargetGuid = dependency.TargetGuid;
                    }
                    row.Owners.UnionWith(dependency.Owners);
                }
                data.Issues.Add(row);
            }
            if (row != null && !string.IsNullOrEmpty(owner)) row.Owners.Add(owner);
        }

        public static string DecisionText(AssetCloneIsolationDecision decision)
        {
            switch (decision)
            {
                case AssetCloneIsolationDecision.Clone: return "跟随克隆";
                case AssetCloneIsolationDecision.ExplicitShared: return "显式共享";
                case AssetCloneIsolationDecision.ExternalShared: return "外部共享";
                case AssetCloneIsolationDecision.ExternalClone: return "外部迁移";
                case AssetCloneIsolationDecision.SharedDependency: return "系统共享";
                case AssetCloneIsolationDecision.AlreadyInTarget: return "目标目录";
                case AssetCloneIsolationDecision.BlockedExternal: return "阻断";
                case AssetCloneIsolationDecision.ReferenceOnly: return "仅引用";
                default: return "未知";
            }
        }

        static string AssetType(string path)
        {
            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type == null ? Path.GetExtension(path).TrimStart('.') : type.Name;
        }
    }
}
