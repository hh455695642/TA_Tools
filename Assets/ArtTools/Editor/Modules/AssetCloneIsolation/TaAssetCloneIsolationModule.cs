using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetCloneIsolation.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    /// <summary>
    /// TA Art Tools module that previews, applies, and audits isolated art asset clones.
    /// </summary>
    public sealed class TaAssetCloneIsolationModule : ArtToolModuleBase
    {
        /// <summary>
        /// Objects explicitly selected for clone isolation by the user.
        /// </summary>
        readonly List<UnityEngine.Object> targets = new List<UnityEngine.Object>();

        /// <summary>
        /// Dependencies intentionally kept shared by the user.
        /// </summary>
        readonly List<string> explicitSharedPaths = new List<string>();

        /// <summary>
        /// External Assets dependencies intentionally cloned into the target root by the user.
        /// </summary>
        readonly List<string> explicitCloneExternalPaths = new List<string>();

        /// <summary>
        /// Source root used when mapping source assets to target assets.
        /// </summary>
        string sourceRoot = AssetCloneIsolationOptions.DefaultSourceRoot;

        /// <summary>
        /// Target root where isolated clones will be written.
        /// </summary>
        string targetRoot = AssetCloneIsolationOptions.DefaultTargetRoot;

        /// <summary>
        /// True when existing target files may be overwritten while target GUIDs are preserved.
        /// </summary>
        bool overwriteExistingAssets = true;

        /// <summary>
        /// True when existing target-root text assets should be rewritten after cloning.
        /// </summary>
        bool rewriteExistingTargetAssets = true;

        /// <summary>
        /// Optional preset used to load and save common root settings.
        /// </summary>
        AssetCloneIsolationPreset preset;

        /// <summary>
        /// Display name shown in the TA Art Tools navigation.
        /// </summary>
        public override string DisplayName => "Asset Clone Isolation";

        /// <summary>
        /// Panel title shown at the top of the module.
        /// </summary>
        public override string PanelTitle => "资产克隆隔离";

        /// <summary>
        /// Navigation category used by TA Art Tools.
        /// </summary>
        public override string Category => "Asset Pipeline";

        /// <summary>
        /// Short description shown in the module header.
        /// </summary>
        public override string Description => "复制旧项目美术资源到新项目目录，生成隔离 GUID，并修复目标目录内旧 GUID 引用。";

        /// <summary>
        /// Detailed help text shown in the TA Art Tools help pane.
        /// </summary>
        public override string HelpText =>
            "用途：把 SourceRoot 下选中的美术资源及其递归依赖克隆到 TargetRoot，避免新旧项目资源继续共用同一个 GUID。\n\n"
            + "推荐流程：\n"
            + "1. 设置 SourceRoot 和 TargetRoot。\n"
            + "2. 拖入 Project 资源、文件夹、材质、贴图、Shader、Prefab，或拖入 Hierarchy 中的 Prefab 实例。\n"
            + "3. 点击“预览计划”，在下方按每个待克隆对象查看下游依赖、直接上游、共享依赖引用、TargetRoot 修复项和风险。\n"
            + "4. 如确实希望某个 SourceRoot 依赖留在原地，可在关系视图中切换为“显式共享”。SourceRoot 外的公共 Assets 依赖默认留在原地并显示风险，可按需点“迁移到目标”。\n"
            + "5. 只有预览没有阻断错误时才点击“应用计划”。\n"
            + "6. 应用后点击“审计 TargetRoot”，确认目标目录没有非预期旧项目美术依赖。\n\n"
            + "规则：直接上游只表示直接引用待克隆对象本身的资产；共享依赖引用只表示共用 Shader/贴图等下游依赖。SourceRoot 内依赖默认一起克隆；SourceRoot 外的 Assets 美术依赖默认作为外部共享风险保留，可选择迁移到 TargetRoot/_External/Assets；Packages、Unity built-in、脚本和程序集依赖保持共享。"
            + " Shader 审计会提示 multi_compile 和 shader_feature 的移动端 variant 风险。";

        /// <summary>
        /// Creates the UI Toolkit view for clone isolation.
        /// </summary>
        public override VisualElement CreateView(ArtToolContext context)
        {
            var root = new VisualElement();
            root.Add(Header(PanelTitle, Description));
            root.Add(CreateIntroHelpBox());
            root.Add(CreateConfigurationView());
            root.Add(CreateTargetPickerView(context));
            root.Add(ActionRow(
                ActionButton("预览计划", () => ShowPlanPreview(context)),
                ActionButton("应用计划", () => context.RequestApply?.Invoke()),
                ActionButton("审计 TargetRoot", () => ShowAuditReport(context)),
                ActionButton("导出 CSV", () => context.ExportCurrentReport?.Invoke())));
            return root;
        }

        /// <summary>
        /// Builds a flat preview report for TA Art Tools export and generic scan workflows.
        /// </summary>
        public override ArtToolReport Scan()
        {
            AssetCloneIsolationPlan plan = AssetCloneIsolationService.BuildPlan(CreateOptions());
            return BuildPlanReport(plan);
        }

        /// <summary>
        /// Creates the top workflow summary box.
        /// </summary>
        static HelpBox CreateIntroHelpBox()
        {
            return new HelpBox(
                "先拖入待克隆隔离对象，再预览和应用。下方结果会按每个待克隆对象分组显示下游依赖、直接上游和共享依赖引用；文件夹会在构建计划时递归展开。",
                HelpBoxMessageType.Info);
        }

        /// <summary>
        /// Creates root path, overwrite, rewrite, and preset controls.
        /// </summary>
        VisualElement CreateConfigurationView()
        {
            var root = new VisualElement();

            var sourceRootField = new TextField("SourceRoot") { value = sourceRoot };
            sourceRootField.RegisterValueChangedCallback(evt => sourceRoot = evt.newValue);
            root.Add(sourceRootField);

            var targetRootField = new TextField("TargetRoot") { value = targetRoot };
            targetRootField.RegisterValueChangedCallback(evt => targetRoot = evt.newValue);
            root.Add(targetRootField);

            var overwriteToggle = new Toggle("允许覆盖已有目标文件，但保留目标 GUID") { value = overwriteExistingAssets };
            overwriteToggle.RegisterValueChangedCallback(evt => overwriteExistingAssets = evt.newValue);
            root.Add(overwriteToggle);

            var rewriteToggle = new Toggle("应用后修复 TargetRoot 已有旧 GUID 引用") { value = rewriteExistingTargetAssets };
            rewriteToggle.RegisterValueChangedCallback(evt => rewriteExistingTargetAssets = evt.newValue);
            root.Add(rewriteToggle);

            var explicitSharedRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            explicitSharedRow.Add(new Label("显式共享依赖：" + explicitSharedPaths.Count) { style = { flexGrow = 1 } });
            explicitSharedRow.Add(ActionButton("清空显式共享", () => explicitSharedPaths.Clear()));
            root.Add(explicitSharedRow);

            var externalCloneRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            externalCloneRow.Add(new Label("外部依赖迁移：" + explicitCloneExternalPaths.Count) { style = { flexGrow = 1 } });
            externalCloneRow.Add(ActionButton("清空外部迁移", () => explicitCloneExternalPaths.Clear()));
            root.Add(externalCloneRow);

            root.Add(CreatePresetView());
            return root;
        }

        /// <summary>
        /// Creates preset load, save, and save-as controls.
        /// </summary>
        VisualElement CreatePresetView()
        {
            var root = new VisualElement { style = { marginTop = 2, marginBottom = 6 } };
            var presetField = new ObjectField("Preset")
            {
                objectType = typeof(AssetCloneIsolationPreset),
                allowSceneObjects = false,
                value = preset
            };
            presetField.style.flexGrow = 1;
            presetField.style.marginBottom = 2;
            presetField.RegisterValueChangedCallback(evt => preset = evt.newValue as AssetCloneIsolationPreset);
            root.Add(presetField);

            var buttonRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            buttonRow.Add(PresetActionButton("加载", () => LoadPreset(presetField), 56));
            buttonRow.Add(PresetActionButton("保存", () => SavePreset(presetField), 56));
            buttonRow.Add(PresetActionButton("另存为", () => SavePresetAs(presetField), 68));
            root.Add(buttonRow);
            return root;
        }

        /// <summary>
        /// Creates the clone target list and drag-drop picker.
        /// </summary>
        VisualElement CreateTargetPickerView(ArtToolContext context)
        {
            var root = new VisualElement();
            var targetLabel = new Label("待克隆隔离资产 (0)") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } };
            var targetList = new VisualElement();

            Action refreshTargetList = null;
            refreshTargetList = () =>
            {
                targetLabel.text = "待克隆隔离资产 (" + targets.Count + ")";
                targetList.Clear();
                if (targets.Count == 0)
                {
                    targetList.Add(WrapLabel("暂无对象。请把 Project 资源/文件夹或 Hierarchy Prefab 实例拖到下方区域。"));
                    return;
                }

                for (int index = 0; index < targets.Count; index++)
                {
                    int rowIndex = index;
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
                    var field = new ObjectField
                    {
                        objectType = typeof(UnityEngine.Object),
                        allowSceneObjects = true,
                        value = targets[rowIndex]
                    };
                    field.style.flexGrow = 1;
                    field.RegisterValueChangedCallback(evt =>
                    {
                        UnityEngine.Object resolved = AssetCloneIsolationTargetResolver.ResolveToProjectObject(evt.newValue);
                        if (resolved != null)
                        {
                            targets[rowIndex] = resolved;
                        }

                        refreshTargetList();
                    });
                    row.Add(field);
                    row.Add(ActionButton("移除", () =>
                    {
                        targets.RemoveAt(rowIndex);
                        refreshTargetList();
                    }));
                    targetList.Add(row);
                }
            };

            root.Add(targetLabel);
            var targetScroll = new ScrollView { style = { height = 126, minHeight = 92, marginBottom = 6 } };
            targetScroll.Add(targetList);
            root.Add(targetScroll);

            root.Add(ActionRow(ActionButton("清空对象", () =>
            {
                targets.Clear();
                refreshTargetList();
            })));

            root.Add(CreateDropZone(refreshTargetList));
            refreshTargetList();
            return root;
        }

        /// <summary>
        /// Creates a Project/Hierarchy drag-drop zone for clone inputs.
        /// </summary>
        VisualElement CreateDropZone(Action refreshTargetList)
        {
            var normalBorderColor = new Color(0.38f, 0.38f, 0.38f);
            var normalBackgroundColor = new Color(0.16f, 0.17f, 0.18f);
            var hoverBorderColor = new Color(0.28f, 0.55f, 0.95f);
            var hoverBackgroundColor = new Color(0.19f, 0.23f, 0.29f);
            var dropZone = new VisualElement();
            dropZone.style.minHeight = 72;
            dropZone.style.marginTop = 2;
            dropZone.style.marginBottom = 8;
            dropZone.style.paddingLeft = 10;
            dropZone.style.paddingRight = 10;
            dropZone.style.paddingTop = 8;
            dropZone.style.paddingBottom = 8;
            dropZone.style.justifyContent = Justify.Center;
            dropZone.style.alignItems = Align.Center;
            dropZone.style.borderLeftWidth = 2;
            dropZone.style.borderRightWidth = 2;
            dropZone.style.borderTopWidth = 2;
            dropZone.style.borderBottomWidth = 2;
            ApplyDropZoneColors(dropZone, normalBorderColor, normalBackgroundColor);

            var title = new Label("拖拽资源到这里");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 3;
            dropZone.Add(title);

            var hint = new Label("支持 Project 资源/文件夹、材质、贴图、Shader、Prefab、Hierarchy Prefab 实例");
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            dropZone.Add(hint);

            dropZone.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                ApplyDropZoneColors(dropZone, hoverBorderColor, hoverBackgroundColor);
                evt.StopPropagation();
            });
            dropZone.RegisterCallback<DragLeaveEvent>(evt =>
            {
                ApplyDropZoneColors(dropZone, normalBorderColor, normalBackgroundColor);
                evt.StopPropagation();
            });
            dropZone.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                AddTargets(DragAndDrop.objectReferences);
                refreshTargetList?.Invoke();
                ApplyDropZoneColors(dropZone, normalBorderColor, normalBackgroundColor);
                evt.StopPropagation();
            });
            return dropZone;
        }

        /// <summary>
        /// Adds resolved Project assets to the target list while preserving path uniqueness.
        /// </summary>
        void AddTargets(IEnumerable<UnityEngine.Object> rawTargets)
        {
            if (rawTargets == null)
            {
                return;
            }

            List<string> selectedPaths = AssetCloneIsolationTargetResolver.BuildSelectedAssetPaths(targets);
            foreach (UnityEngine.Object rawTarget in rawTargets)
            {
                string assetPath = AssetCloneIsolationTargetResolver.ResolveToAssetPath(rawTarget);
                if (string.IsNullOrEmpty(assetPath)
                    || selectedPaths.Any(path => path.Equals(assetPath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                UnityEngine.Object projectObject = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (projectObject != null)
                {
                    targets.Add(projectObject);
                    selectedPaths.Add(assetPath);
                }
            }
        }

        /// <summary>
        /// Creates service options from the current UI state.
        /// </summary>
        AssetCloneIsolationOptions CreateOptions()
        {
            return new AssetCloneIsolationOptions
            {
                SourceRoot = sourceRoot,
                TargetRoot = targetRoot,
                SelectedAssetPaths = AssetCloneIsolationTargetResolver.BuildSelectedAssetPaths(targets),
                ExplicitSharedAssetPaths = new List<string>(explicitSharedPaths),
                ExplicitCloneExternalAssetPaths = new List<string>(explicitCloneExternalPaths),
                OverwriteExistingAssets = overwriteExistingAssets,
                RewriteExistingTargetAssets = rewriteExistingTargetAssets
            };
        }

        /// <summary>
        /// Builds a plan and renders the relationship preview.
        /// </summary>
        void ShowPlanPreview(ArtToolContext context)
        {
            AssetCloneIsolationPlan plan = AssetCloneIsolationService.BuildPlan(CreateOptions());
            ArtToolReport report = BuildPlanReport(plan);
            VisualElement view = BuildRelationshipPreviewView(plan, context);
            string status = BuildPreviewStatus(plan, report);
            if (context.ShowCustomReportView != null)
            {
                context.ShowCustomReportView.Invoke(report, view, status);
            }
            else
            {
                context.ShowReport?.Invoke(report);
            }
        }

        /// <summary>
        /// Builds the bottom relationship preview view.
        /// </summary>
        VisualElement BuildRelationshipPreviewView(AssetCloneIsolationPlan plan, ArtToolContext context)
        {
            var root = new VisualElement { style = { flexGrow = 1 } };
            root.Add(WrapLabel(TaAssetCloneIsolationPreviewView.BuildPlanSummary(plan), true));

            string pathFilter = string.Empty;
            bool riskOnly = false;
            var decisionChoices = new List<string> { "全部", "克隆", "外部共享", "外部迁移", "显式共享", "共享", "阻断", "目标目录", "直接上游", "共享依赖引用" };
            string decisionFilter = decisionChoices[0];

            var filterRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 6 } };
            var pathField = new TextField("路径过滤") { value = pathFilter };
            pathField.style.flexGrow = 1;
            var decisionField = new PopupField<string>("决策", decisionChoices, 0);
            decisionField.style.width = 190;
            var riskToggle = new Toggle("只看风险") { value = riskOnly };
            filterRow.Add(pathField);
            filterRow.Add(decisionField);
            filterRow.Add(riskToggle);
            root.Add(filterRow);

            var content = new VisualElement();
            root.Add(content);

            Action rebuildContent = () =>
            {
                content.Clear();
                if (plan.RootPlans.Count == 0)
                {
                    content.Add(WrapLabel("没有可展示的关系数据。请先把 Project 资源/文件夹或 Hierarchy Prefab 实例拖入上方待克隆隔离资产列表。"));
                    return;
                }

                foreach (AssetCloneIsolationRootPlan rootPlan in plan.RootPlans)
                {
                    content.Add(BuildRootPlanFoldout(rootPlan, plan, context, pathFilter, decisionFilter, riskOnly));
                }
            };

            pathField.RegisterValueChangedCallback(evt =>
            {
                pathFilter = evt.newValue ?? string.Empty;
                rebuildContent();
            });
            decisionField.RegisterValueChangedCallback(evt =>
            {
                decisionFilter = evt.newValue;
                rebuildContent();
            });
            riskToggle.RegisterValueChangedCallback(evt =>
            {
                riskOnly = evt.newValue;
                rebuildContent();
            });
            rebuildContent();
            return root;
        }

        /// <summary>
        /// Builds one selected-root foldout in the relationship preview.
        /// </summary>
        VisualElement BuildRootPlanFoldout(
            AssetCloneIsolationRootPlan rootPlan,
            AssetCloneIsolationPlan plan,
            ArtToolContext context,
            string pathFilter,
            string decisionFilter,
            bool riskOnly)
        {
            var foldout = new Foldout
            {
                text = BuildRootTitle(rootPlan),
                value = true
            };

            foldout.Add(BuildAssetPathRow(rootPlan.RootAssetPath, rootPlan.TargetAssetPath, true));
            foldout.Add(WrapLabel(TaAssetCloneIsolationPreviewView.BuildRootSummary(rootPlan, plan), true));
            AddNodeSection(foldout, "下游依赖", rootPlan.DownstreamDependencies, plan, context, pathFilter, decisionFilter, riskOnly, rootPlan);
            AddNodeSection(foldout, "直接上游引用", rootPlan.UpstreamReferences, plan, context, pathFilter, decisionFilter, riskOnly, rootPlan);
            AddSharedDependencyReferenceSection(foldout, rootPlan, plan, context, pathFilter, decisionFilter, riskOnly);
            AddRiskSection(foldout, rootPlan, pathFilter);
            AddWriteSection(foldout, rootPlan, plan, pathFilter, riskOnly);
            return foldout;
        }

        /// <summary>
        /// Adds a relation-node section to a root foldout.
        /// </summary>
        void AddNodeSection(
            VisualElement parent,
            string title,
            IReadOnlyList<AssetCloneIsolationRelationNode> nodes,
            AssetCloneIsolationPlan plan,
            ArtToolContext context,
            string pathFilter,
            string decisionFilter,
            bool riskOnly,
            AssetCloneIsolationRootPlan rootPlan)
        {
            List<AssetCloneIsolationRelationNode> visibleNodes = nodes
                .Where(node => NodePassesFilter(node, pathFilter, decisionFilter, riskOnly))
                .ToList();
            parent.Add(SectionLabel(title + " (" + visibleNodes.Count + "/" + nodes.Count + ")"));
            if (visibleNodes.Count == 0)
            {
                parent.Add(WrapLabel("无匹配项。"));
                return;
            }

            foreach (AssetCloneIsolationRelationNode node in visibleNodes)
            {
                parent.Add(BuildRelationNodeRow(node, plan, context, rootPlan));
            }
        }

        /// <summary>
        /// Adds a folded section for assets that only share downstream dependencies with the root.
        /// </summary>
        void AddSharedDependencyReferenceSection(
            VisualElement parent,
            AssetCloneIsolationRootPlan rootPlan,
            AssetCloneIsolationPlan plan,
            ArtToolContext context,
            string pathFilter,
            string decisionFilter,
            bool riskOnly)
        {
            List<AssetCloneIsolationRelationNode> visibleNodes = rootPlan.SharedDependencyReferences
                .Where(node => NodePassesFilter(node, pathFilter, decisionFilter, riskOnly))
                .ToList();
            var foldout = new Foldout
            {
                text = "共享依赖引用 (" + visibleNodes.Count + "/" + rootPlan.SharedDependencyReferences.Count + ")",
                value = decisionFilter == "共享依赖引用" || !string.IsNullOrEmpty(pathFilter)
            };
            foldout.Add(WrapLabel("这些资产只引用了当前对象的下游依赖，例如 Shader 或贴图；它们不等于直接引用当前资产。"));
            foldout.Add(WrapLabel(TaAssetCloneIsolationPreviewView.BuildSharedDependencyTypeSummary(visibleNodes)));
            if (visibleNodes.Count == 0)
            {
                foldout.Add(WrapLabel("无匹配项。"));
            }

            bool showAllRows = TaAssetCloneIsolationPreviewView.ShouldShowAllSharedDependencyRows(pathFilter, decisionFilter);
            List<AssetCloneIsolationRelationNode> displayedNodes = showAllRows
                ? visibleNodes
                : visibleNodes.Take(TaAssetCloneIsolationPreviewView.SharedDependencyPreviewLimit).ToList();
            if (!showAllRows && visibleNodes.Count > displayedNodes.Count)
            {
                foldout.Add(WrapLabel("默认仅显示前 " + displayedNodes.Count + " 条。使用路径过滤或选择“共享依赖引用”筛选可查看全部匹配项。"));
            }

            foreach (AssetCloneIsolationRelationNode node in displayedNodes)
            {
                foldout.Add(BuildRelationNodeRow(node, plan, context, rootPlan));
            }

            parent.Add(foldout);
        }

        /// <summary>
        /// Adds root-local risks derived from downstream decisions.
        /// </summary>
        static void AddRiskSection(VisualElement parent, AssetCloneIsolationRootPlan rootPlan, string pathFilter)
        {
            List<AssetCloneIsolationRelationNode> riskNodes = rootPlan.DownstreamDependencies
                .Where(node => IsRiskDecision(node.Decision) && PathPassesFilter(node.AssetPath, pathFilter))
                .ToList();
            parent.Add(SectionLabel("问题与风险 (" + riskNodes.Count + ")"));
            foreach (AssetCloneIsolationRelationNode node in riskNodes)
            {
                parent.Add(WrapLabel(DecisionText(node.Decision) + " | " + node.AssetPath + " | " + node.Detail));
            }
        }

        /// <summary>
        /// Adds root-local write records derived from the flat plan.
        /// </summary>
        static void AddWriteSection(VisualElement parent, AssetCloneIsolationRootPlan rootPlan, AssetCloneIsolationPlan plan, string pathFilter, bool riskOnly)
        {
            if (riskOnly)
            {
                return;
            }

            HashSet<string> rootGraphPaths = BuildRootGraphPathSet(rootPlan);
            List<AssetCloneIsolationAssetRecord> visibleRecords = plan.Assets
                .Where(record => rootGraphPaths.Contains(record.SourceAssetPath) && PathPassesFilter(record.SourceAssetPath, pathFilter))
                .ToList();
            parent.Add(SectionLabel("写入清单 (" + visibleRecords.Count + ")"));
            AddAssetRecordWriteGroup(parent, "新建目标资产", visibleRecords.Where(record =>
                AssetCloneIsolationUtility.IsUnderRoot(record.SourceAssetPath, plan.Options.SourceRoot)
                && !record.TargetAlreadyExists));
            AddAssetRecordWriteGroup(parent, "覆盖已有目标并保留 GUID", visibleRecords.Where(record =>
                AssetCloneIsolationUtility.IsUnderRoot(record.SourceAssetPath, plan.Options.SourceRoot)
                && record.TargetAlreadyExists));
            AddAssetRecordWriteGroup(parent, "外部依赖迁移", visibleRecords.Where(record =>
                !AssetCloneIsolationUtility.IsUnderRoot(record.SourceAssetPath, plan.Options.SourceRoot)));
            AddRewriteWriteGroup(parent, "TargetRoot GUID 修复", rootPlan.TargetRewriteRecords.Where(record => PathPassesFilter(record.AssetPath, pathFilter)));
            AddSharedRiskWriteGroup(parent, "共享风险，不写入", rootPlan.DownstreamDependencies
                .Where(node => (node.Decision == AssetCloneIsolationDecision.ExplicitShared
                                || node.Decision == AssetCloneIsolationDecision.ExternalShared)
                               && PathPassesFilter(node.AssetPath, pathFilter)));
        }

        /// <summary>
        /// Adds a grouped list of clone write records.
        /// </summary>
        static void AddAssetRecordWriteGroup(VisualElement parent, string title, IEnumerable<AssetCloneIsolationAssetRecord> records)
        {
            List<AssetCloneIsolationAssetRecord> recordList = records.ToList();
            parent.Add(SectionLabel(title + " (" + recordList.Count + ")"));
            if (recordList.Count == 0)
            {
                parent.Add(WrapLabel("无。"));
                return;
            }

            foreach (AssetCloneIsolationAssetRecord record in recordList)
            {
                string actionText = record.TargetAlreadyExists ? "覆盖内容，保留目标 GUID" : "创建新目标资产";
                parent.Add(WrapLabel(record.SourceAssetPath + " -> " + record.TargetAssetPath
                                     + " | " + record.SourceGuid + " -> " + record.TargetGuid
                                     + " | " + actionText));
            }
        }

        /// <summary>
        /// Adds a grouped list of existing target-root GUID rewrite records.
        /// </summary>
        static void AddRewriteWriteGroup(VisualElement parent, string title, IEnumerable<AssetCloneIsolationRewriteRecord> records)
        {
            List<AssetCloneIsolationRewriteRecord> recordList = records.ToList();
            parent.Add(SectionLabel(title + " (" + recordList.Count + ")"));
            if (recordList.Count == 0)
            {
                parent.Add(WrapLabel("无。"));
                return;
            }

            foreach (AssetCloneIsolationRewriteRecord record in recordList)
            {
                parent.Add(BuildRewriteRow(record));
            }
        }

        /// <summary>
        /// Adds a grouped list of dependencies intentionally or implicitly kept shared.
        /// </summary>
        static void AddSharedRiskWriteGroup(VisualElement parent, string title, IEnumerable<AssetCloneIsolationRelationNode> nodes)
        {
            List<AssetCloneIsolationRelationNode> nodeList = nodes.ToList();
            parent.Add(SectionLabel(title + " (" + nodeList.Count + ")"));
            if (nodeList.Count == 0)
            {
                parent.Add(WrapLabel("无。"));
                return;
            }

            foreach (AssetCloneIsolationRelationNode node in nodeList)
            {
                string detail = node.Decision == AssetCloneIsolationDecision.ExternalShared
                    ? "外部共享风险，默认不写入目标目录；可在关系行选择迁移到目标。"
                    : "SourceRoot 留在原地风险，不写入目标目录。";
                parent.Add(WrapLabel(node.AssetPath + " | " + detail));
            }
        }

        /// <summary>
        /// Builds one relation row with quick locate and decision controls.
        /// </summary>
        VisualElement BuildRelationNodeRow(
            AssetCloneIsolationRelationNode node,
            AssetCloneIsolationPlan plan,
            ArtToolContext context,
            AssetCloneIsolationRootPlan rootPlan)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
            row.Add(FixedLabel(RelationDisplayText(node), 86));
            row.Add(FixedLabel(node.AssetType, 92));
            string pathText = new string(' ', Math.Min(node.Depth, 6) * 2) + node.AssetPath;
            if (node.Decision == AssetCloneIsolationDecision.ExternalClone && !string.IsNullOrEmpty(node.TargetAssetPath))
            {
                pathText += " -> " + node.TargetAssetPath;
            }

            row.Add(WrapLabel(pathText, false, 1));
            row.Add(ActionButton("定位", () => PingPath(node.AssetPath)));

            if (!string.IsNullOrEmpty(node.TargetAssetPath) && File.Exists(AssetCloneIsolationUtility.ToProjectAbsolutePath(node.TargetAssetPath)))
            {
                row.Add(ActionButton("定位目标", () => PingPath(node.TargetAssetPath)));
            }

            if (CanToggleExplicitShared(node, rootPlan, plan))
            {
                string buttonText = node.Decision == AssetCloneIsolationDecision.ExplicitShared ? "跟随克隆" : "留在原地";
                row.Add(ActionButton(buttonText, () =>
                {
                    if (node.Decision != AssetCloneIsolationDecision.ExplicitShared
                        && !EditorUtility.DisplayDialog(
                            "确认显式共享依赖",
                            "该资源不会被克隆到 TargetRoot，TargetRoot 资源会继续引用 SourceRoot 中的原资源。\n\n" + node.AssetPath,
                            "留在原地",
                            "取消"))
                    {
                        return;
                    }

                    ToggleExplicitShared(node.AssetPath);
                    ShowPlanPreview(context);
                }));
            }

            if (CanToggleExternalClone(node, rootPlan, plan))
            {
                string buttonText = node.Decision == AssetCloneIsolationDecision.ExternalClone ? "取消迁移" : "迁移到目标";
                row.Add(ActionButton(buttonText, () =>
                {
                    ToggleExternalClone(node.AssetPath);
                    ShowPlanPreview(context);
                }));
            }

            return row;
        }

        /// <summary>
        /// Builds one target rewrite row.
        /// </summary>
        static VisualElement BuildRewriteRow(AssetCloneIsolationRewriteRecord record)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 2 } };
            row.Add(FixedLabel("修复 GUID", 86));
            row.Add(WrapLabel(record.AssetPath + " | 替换次数 " + record.ReplacementCount + " | 涉及 GUID 映射 " + record.GuidMappingCount, false, 1));
            row.Add(ActionButton("定位", () => PingPath(record.AssetPath)));
            return row;
        }

        /// <summary>
        /// Builds one root asset row with quick locate actions.
        /// </summary>
        static VisualElement BuildAssetPathRow(string assetPath, string targetPath, bool bold)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginBottom = 4 } };
            row.Add(WrapLabel(assetPath + (string.IsNullOrEmpty(targetPath) ? "" : " -> " + targetPath), bold, 1));
            row.Add(ActionButton("定位", () => PingPath(assetPath)));
            return row;
        }

        /// <summary>
        /// Converts a clone plan to the shared TA Art Tools report format.
        /// </summary>
        ArtToolReport BuildPlanReport(AssetCloneIsolationPlan plan)
        {
            var report = ArtToolReport.Empty(PanelTitle);
            report.Changes.Add(ArtToolChange.Info(
                "迁移计划汇总",
                $"Root {plan.RootPlans.Count} 个，克隆资产 {plan.Assets.Count} 个，GUID 映射 {plan.GuidMap.Count} 个，外部共享 {plan.ExternalSharedDependencies.Count} 个，外部迁移 {plan.ExplicitCloneExternalDependencies.Count} 个，显式共享 {plan.ExplicitSharedDependencies.Count} 个，目标目录修复 {plan.TargetRewriteRecords.Count} 个，预计写入 {plan.WriteOperationCount} 项。",
                plan.Options.TargetRoot));

            foreach (string error in plan.Errors)
            {
                report.Changes.Add(ArtToolChange.Error("阻断错误", error));
            }

            foreach (string warning in plan.Warnings)
            {
                report.Changes.Add(ArtToolChange.Warning("风险提示", warning));
            }

            foreach (string info in plan.Infos)
            {
                report.Changes.Add(ArtToolChange.Info("信息", info));
            }

            foreach (AssetCloneIsolationAssetRecord assetRecord in plan.Assets)
            {
                UnityEngine.Object sourceAsset = AssetDatabase.LoadMainAssetAtPath(assetRecord.SourceAssetPath);
                string detail = assetRecord.SourceAssetPath + " -> " + assetRecord.TargetAssetPath
                                + " | " + assetRecord.SourceGuid + " -> " + assetRecord.TargetGuid
                                + (assetRecord.TargetAlreadyExists ? " | 复用目标路径" : " | 新目标路径");
                report.Changes.Add(ArtToolChange.Info("克隆资产", detail, assetRecord.SourceAssetPath, sourceAsset));
            }

            foreach (AssetCloneIsolationRewriteRecord rewriteRecord in plan.TargetRewriteRecords)
            {
                UnityEngine.Object rewriteAsset = AssetDatabase.LoadMainAssetAtPath(rewriteRecord.AssetPath);
                report.Changes.Add(ArtToolChange.Warning(
                    "TargetRoot 引用修复",
                    "替换旧 GUID 次数：" + rewriteRecord.ReplacementCount + "，涉及 GUID 映射：" + rewriteRecord.GuidMappingCount,
                    rewriteRecord.AssetPath,
                    rewriteAsset));
            }

            foreach (string dependencyPath in plan.ExplicitSharedDependencies)
            {
                report.Changes.Add(ArtToolChange.Warning("显式共享依赖", dependencyPath, dependencyPath));
            }

            foreach (string dependencyPath in plan.ExternalSharedDependencies)
            {
                report.Changes.Add(ArtToolChange.Warning("外部共享风险", "默认保留在原地，可选择迁移到 TargetRoot/_External/Assets：" + dependencyPath, dependencyPath));
            }

            foreach (string dependencyPath in plan.ExplicitCloneExternalDependencies)
            {
                report.Changes.Add(ArtToolChange.Info("外部依赖迁移", dependencyPath, dependencyPath));
            }

            foreach (string dependencyPath in plan.SharedDependencies.Take(80))
            {
                report.Changes.Add(ArtToolChange.Info("共享依赖", dependencyPath, dependencyPath));
            }

            if (!plan.HasErrors)
            {
                report.Changes.Add(ArtToolChange.Write(
                    "应用资产克隆隔离计划",
                    $"写入克隆资产 {plan.Assets.Count} 个，并修复 TargetRoot 引用 {plan.TargetRewriteRecords.Count} 个文件。",
                    () => AssetCloneIsolationService.ApplyPlan(plan),
                    plan.Options.TargetRoot));
            }

            return report;
        }

        /// <summary>
        /// Builds and displays a target-root audit report.
        /// </summary>
        void ShowAuditReport(ArtToolContext context)
        {
            AssetCloneIsolationAuditReport auditReport = AssetCloneIsolationService.AuditTargetRoot(
                targetRoot,
                sourceRoot,
                explicitSharedPaths);
            context.ShowReport?.Invoke(BuildAuditReport(auditReport));
        }

        /// <summary>
        /// Converts an audit result to the shared TA Art Tools report format.
        /// </summary>
        ArtToolReport BuildAuditReport(AssetCloneIsolationAuditReport auditReport)
        {
            var report = ArtToolReport.Empty(PanelTitle + "审计");
            report.Changes.Add(ArtToolChange.Info(
                "TargetRoot 审计汇总",
                $"扫描资产 {auditReport.AssetCount} 个，Shader/ShaderGraph/Compute {auditReport.ShaderAssetCount} 个。",
                auditReport.TargetRoot));

            foreach (string error in auditReport.Errors)
            {
                report.Changes.Add(ArtToolChange.Error("隔离错误", error));
            }

            foreach (string warning in auditReport.Warnings)
            {
                report.Changes.Add(ArtToolChange.Warning("风险提示", warning));
            }

            foreach (string info in auditReport.Infos)
            {
                report.Changes.Add(ArtToolChange.Info("信息", info));
            }

            return report;
        }

        /// <summary>
        /// Builds a concise status line for the relationship preview.
        /// </summary>
        static string BuildPreviewStatus(AssetCloneIsolationPlan plan, ArtToolReport report)
        {
            return plan.HasErrors
                ? $"预览完成：{report.Changes.Count} 条结果，存在 {plan.Errors.Count} 个阻断错误。"
                : $"预览完成：{report.Changes.Count} 条结果，可应用 {report.WriteCount} 条写入操作。";
        }

        /// <summary>
        /// Builds one foldout title with root-local counts.
        /// </summary>
        static string BuildRootTitle(AssetCloneIsolationRootPlan rootPlan)
        {
            int cloneCount = rootPlan.DownstreamDependencies.Count(node => node.Decision == AssetCloneIsolationDecision.Clone);
            int explicitSharedCount = rootPlan.DownstreamDependencies.Count(node => node.Decision == AssetCloneIsolationDecision.ExplicitShared);
            int externalSharedCount = rootPlan.DownstreamDependencies.Count(node => node.Decision == AssetCloneIsolationDecision.ExternalShared);
            int externalCloneCount = rootPlan.DownstreamDependencies.Count(node => node.Decision == AssetCloneIsolationDecision.ExternalClone);
            int blockedCount = rootPlan.DownstreamDependencies.Count(node => node.Decision == AssetCloneIsolationDecision.BlockedExternal);
            return $"{rootPlan.RootAssetPath} | 下游 {rootPlan.DownstreamDependencies.Count} | 克隆 {cloneCount} | 外部共享 {externalSharedCount} | 外部迁移 {externalCloneCount} | 显式共享 {explicitSharedCount} | 直接上游 {rootPlan.UpstreamReferences.Count} | 共享依赖引用 {rootPlan.SharedDependencyReferences.Count} | 修复 {rootPlan.TargetRewriteRecords.Count} | 阻断 {blockedCount}";
        }

        /// <summary>
        /// Returns true when one relation node should be visible under the active filters.
        /// </summary>
        static bool NodePassesFilter(AssetCloneIsolationRelationNode node, string pathFilter, string decisionFilter, bool riskOnly)
        {
            if (node == null || !PathPassesFilter(node.AssetPath, pathFilter))
            {
                return false;
            }

            if (riskOnly
                && node.RelationKind != AssetCloneIsolationRelationKind.SharedDependencyReference
                && !IsRiskDecision(node.Decision))
            {
                return false;
            }

            if (string.IsNullOrEmpty(decisionFilter) || decisionFilter == "全部")
            {
                return true;
            }

            return DecisionText(node.Decision).IndexOf(decisionFilter, StringComparison.OrdinalIgnoreCase) >= 0
                   || RelationDisplayText(node).IndexOf(decisionFilter, StringComparison.OrdinalIgnoreCase) >= 0
                   || RelationFilterText(node).IndexOf(decisionFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns true when one path matches the text filter.
        /// </summary>
        static bool PathPassesFilter(string assetPath, string pathFilter)
        {
            return string.IsNullOrEmpty(pathFilter)
                   || (!string.IsNullOrEmpty(assetPath)
                       && assetPath.IndexOf(pathFilter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Returns true when the decision deserves risk-only visibility.
        /// </summary>
        static bool IsRiskDecision(AssetCloneIsolationDecision decision)
        {
            return decision == AssetCloneIsolationDecision.BlockedExternal
                   || decision == AssetCloneIsolationDecision.MissingOrUnknown
                   || decision == AssetCloneIsolationDecision.ExplicitShared
                   || decision == AssetCloneIsolationDecision.ExternalShared;
        }

        /// <summary>
        /// Returns true when the relation can be toggled between clone and explicit shared.
        /// </summary>
        static bool CanToggleExplicitShared(
            AssetCloneIsolationRelationNode node,
            AssetCloneIsolationRootPlan rootPlan,
            AssetCloneIsolationPlan plan)
        {
            return node != null
                   && rootPlan != null
                   && plan != null
                   && node.RelationKind == AssetCloneIsolationRelationKind.Dependency
                   && !node.AssetPath.Equals(rootPlan.RootAssetPath, StringComparison.OrdinalIgnoreCase)
                   && AssetCloneIsolationUtility.IsUnderRoot(node.AssetPath, plan.Options.SourceRoot)
                   && !AssetCloneIsolationUtility.IsSharedCodeAssetPath(node.AssetPath)
                   && (node.Decision == AssetCloneIsolationDecision.Clone
                       || node.Decision == AssetCloneIsolationDecision.ExplicitShared);
        }

        /// <summary>
        /// Returns true when an external dependency can be toggled between shared risk and target migration.
        /// </summary>
        static bool CanToggleExternalClone(
            AssetCloneIsolationRelationNode node,
            AssetCloneIsolationRootPlan rootPlan,
            AssetCloneIsolationPlan plan)
        {
            return node != null
                   && rootPlan != null
                   && plan != null
                   && node.RelationKind == AssetCloneIsolationRelationKind.Dependency
                   && !node.AssetPath.Equals(rootPlan.RootAssetPath, StringComparison.OrdinalIgnoreCase)
                   && node.AssetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                   && !AssetCloneIsolationUtility.IsUnderRoot(node.AssetPath, plan.Options.SourceRoot)
                   && !AssetCloneIsolationUtility.IsUnderRoot(node.AssetPath, plan.Options.TargetRoot)
                   && !AssetCloneIsolationUtility.IsSharedCodeAssetPath(node.AssetPath)
                   && (node.Decision == AssetCloneIsolationDecision.ExternalShared
                       || node.Decision == AssetCloneIsolationDecision.ExternalClone);
        }

        /// <summary>
        /// Toggles one dependency path in the explicit shared list.
        /// </summary>
        void ToggleExplicitShared(string assetPath)
        {
            string normalizedPath = AssetCloneIsolationUtility.NormalizeAssetPath(assetPath);
            int index = explicitSharedPaths.FindIndex(path => path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                explicitSharedPaths.RemoveAt(index);
                return;
            }

            explicitSharedPaths.Add(normalizedPath);
            explicitSharedPaths.Sort(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Toggles one external dependency path in the explicit external clone list.
        /// </summary>
        void ToggleExternalClone(string assetPath)
        {
            string normalizedPath = AssetCloneIsolationUtility.NormalizeAssetPath(assetPath);
            int index = explicitCloneExternalPaths.FindIndex(path => path.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                explicitCloneExternalPaths.RemoveAt(index);
                return;
            }

            explicitCloneExternalPaths.Add(normalizedPath);
            explicitCloneExternalPaths.Sort(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Builds the path set belonging to one root graph.
        /// </summary>
        static HashSet<string> BuildRootGraphPathSet(AssetCloneIsolationRootPlan rootPlan)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootPlan.RootAssetPath };
            foreach (AssetCloneIsolationRelationNode node in rootPlan.DownstreamDependencies)
            {
                paths.Add(node.AssetPath);
            }

            return paths;
        }

        /// <summary>
        /// Converts one decision to compact Chinese display text.
        /// </summary>
        static string DecisionText(AssetCloneIsolationDecision decision)
        {
            switch (decision)
            {
                case AssetCloneIsolationDecision.Clone:
                    return "克隆";
                case AssetCloneIsolationDecision.ExplicitShared:
                    return "显式共享";
                case AssetCloneIsolationDecision.ExternalShared:
                    return "外部共享";
                case AssetCloneIsolationDecision.ExternalClone:
                    return "外部迁移";
                case AssetCloneIsolationDecision.SharedDependency:
                    return "共享";
                case AssetCloneIsolationDecision.BlockedExternal:
                    return "阻断";
                case AssetCloneIsolationDecision.AlreadyInTarget:
                    return "目标目录";
                case AssetCloneIsolationDecision.ReferenceOnly:
                    return "引用";
                default:
                    return "未知";
            }
        }

        /// <summary>
        /// Converts one relation node to the compact row label shown in relation tables.
        /// </summary>
        static string RelationDisplayText(AssetCloneIsolationRelationNode node)
        {
            if (node == null)
            {
                return "未知";
            }

            switch (node.RelationKind)
            {
                case AssetCloneIsolationRelationKind.UpstreamReference:
                    return "直接上游";
                case AssetCloneIsolationRelationKind.SharedDependencyReference:
                    return "共享依赖";
                default:
                    return DecisionText(node.Decision);
            }
        }

        /// <summary>
        /// Converts one relation node to the broader text used by the relation filter.
        /// </summary>
        static string RelationFilterText(AssetCloneIsolationRelationNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            switch (node.RelationKind)
            {
                case AssetCloneIsolationRelationKind.UpstreamReference:
                    return "直接上游";
                case AssetCloneIsolationRelationKind.SharedDependencyReference:
                    return "共享依赖引用";
                default:
                    return DecisionText(node.Decision);
            }
        }

        /// <summary>
        /// Loads the selected preset into the current module state.
        /// </summary>
        void LoadPreset(ObjectField presetField)
        {
            preset = presetField.value as AssetCloneIsolationPreset;
            if (preset == null)
            {
                return;
            }

            sourceRoot = preset.SourceRoot;
            targetRoot = preset.TargetRoot;
            overwriteExistingAssets = preset.OverwriteExistingAssets;
            rewriteExistingTargetAssets = preset.RewriteExistingTargetAssets;
            explicitSharedPaths.Clear();
            explicitSharedPaths.AddRange(preset.ExplicitSharedAssetPaths ?? new List<string>());
            explicitCloneExternalPaths.Clear();
            explicitCloneExternalPaths.AddRange(preset.ExplicitCloneExternalAssetPaths ?? new List<string>());
        }

        /// <summary>
        /// Saves the current settings into the assigned preset or creates a new one.
        /// </summary>
        void SavePreset(ObjectField presetField)
        {
            if (preset == null)
            {
                SavePresetAs(presetField);
                return;
            }

            WritePresetValues(preset);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Creates a new preset asset and saves the current settings into it.
        /// </summary>
        void SavePresetAs(ObjectField presetField)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "保存资产克隆隔离预设",
                "AssetCloneIsolationPreset",
                "asset",
                "选择预设保存路径");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            AssetCloneIsolationPreset newPreset = ScriptableObject.CreateInstance<AssetCloneIsolationPreset>();
            WritePresetValues(newPreset);
            AssetDatabase.CreateAsset(newPreset, path);
            AssetDatabase.SaveAssets();
            preset = newPreset;
            presetField.SetValueWithoutNotify(preset);
        }

        /// <summary>
        /// Writes the current module settings into a preset object.
        /// </summary>
        void WritePresetValues(AssetCloneIsolationPreset targetPreset)
        {
            targetPreset.SourceRoot = sourceRoot;
            targetPreset.TargetRoot = targetRoot;
            targetPreset.OverwriteExistingAssets = overwriteExistingAssets;
            targetPreset.RewriteExistingTargetAssets = rewriteExistingTargetAssets;
            targetPreset.ExplicitSharedAssetPaths = new List<string>(explicitSharedPaths);
            targetPreset.ExplicitCloneExternalAssetPaths = new List<string>(explicitCloneExternalPaths);
        }

        /// <summary>
        /// Creates a section label used inside relationship foldouts.
        /// </summary>
        static Label SectionLabel(string text)
        {
            return new Label(text) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6, marginBottom = 2 } };
        }

        /// <summary>
        /// Creates a fixed-width preset action button that stays left-aligned in narrow panels.
        /// </summary>
        static Button PresetActionButton(string text, Action clicked, float width)
        {
            Button button = ActionButton(text, clicked);
            button.style.width = width;
            button.style.flexShrink = 0;
            button.style.marginRight = 4;
            return button;
        }

        /// <summary>
        /// Applies matching border and background colors to the drag-drop input zone.
        /// </summary>
        static void ApplyDropZoneColors(VisualElement dropZone, Color borderColor, Color backgroundColor)
        {
            dropZone.style.borderLeftColor = borderColor;
            dropZone.style.borderRightColor = borderColor;
            dropZone.style.borderTopColor = borderColor;
            dropZone.style.borderBottomColor = borderColor;
            dropZone.style.backgroundColor = backgroundColor;
        }

        /// <summary>
        /// Creates a fixed-width label.
        /// </summary>
        static Label FixedLabel(string text, float width)
        {
            var label = new Label(text) { style = { width = width, marginRight = 4 } };
            return label;
        }

        /// <summary>
        /// Creates a wrapping label that can grow inside row layouts.
        /// </summary>
        static Label WrapLabel(string text, bool bold = false, float flexGrow = 0)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexGrow = flexGrow;
            if (bold)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
            }

            return label;
        }

        /// <summary>
        /// Pings one asset path in the Project window.
        /// </summary>
        static void PingPath(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset != null)
            {
                EditorGUIUtility.PingObject(asset);
            }
        }

    }
}
