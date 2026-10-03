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
        ArtToolContext activeContext;
        readonly IsolationPreviewState previewState = new IsolationPreviewState();
        TaAssetCloneIsolationPreviewWorkspace previewWorkspace;
        ArtToolReport previewReport;
        AssetCloneIsolationOptions previewOptions;
        int configurationVersion;
        int previewVersion = -1;
        bool consumed;
        Button applyButton;
        TextField sourceRootField;
        TextField targetRootField;
        Toggle overwriteToggle;
        Toggle rewriteToggle;
        Label sharedCountLabel;
        Label externalCountLabel;

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
            + "3. 点击“预览计划”，先核对去重后的写入清单，再切换“依赖处理”和“问题与风险”。选择一行查看完整路径、GUID 和所属对象的引用关系。\n"
            + "4. 在依赖页使用 Ctrl / Shift 多选，可批量选择“留在原地”或“迁移到目标”；显式共享会保留对 SourceRoot 的引用。\n"
            + "5. 只有预览没有阻断错误时才点击“应用计划”。\n"
            + "6. 应用后点击“审计 TargetRoot”，确认目标目录没有非预期旧项目美术依赖。\n\n"
            + "规则：直接上游只表示直接引用待克隆对象本身的资产；共享依赖引用只表示共用 Shader/贴图等下游依赖。SourceRoot 内依赖默认一起克隆；SourceRoot 外的 Assets 美术依赖默认作为外部共享风险保留，可选择迁移到 TargetRoot/_External/Assets；Packages、Unity built-in、脚本和程序集依赖保持共享。"
            + " Shader 审计会提示 multi_compile 和 shader_feature 的移动端 variant 风险。";

        /// <summary>
        /// Creates the UI Toolkit view for clone isolation.
        /// </summary>
        public override VisualElement CreateView(ArtToolContext context)
        {
            activeContext = context;
            previewWorkspace = null;
            previewReport = null;
            previewVersion = -1;
            var root = new VisualElement();
            root.Add(Header(PanelTitle, Description));
            root.Add(CreateIntroHelpBox());
            root.Add(CreateConfigurationView());
            root.Add(CreateTargetPickerView(context));
            applyButton = ActionButton("应用计划", () => context.RequestApply?.Invoke());
            applyButton.SetEnabled(false);
            root.Add(ActionRow(
                ActionButton("预览计划", () => ShowPlanPreview(context)),
                applyButton,
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

        public override void Apply(ArtToolReport report)
        {
            if (!CanApplyPreview(report))
                throw new InvalidOperationException("计划已过期或不可应用，请重新预览。");
            base.Apply(report);
        }

        internal bool CanApplyPreview(ArtToolReport report)
        {
            return report != null && ReferenceEquals(report, previewReport) && report.WriteCount > 0
                && !report.HasErrors && !consumed && previewVersion == configurationVersion
                && (activeContext?.CurrentReport == null || ReferenceEquals(activeContext.CurrentReport(), report))
                && OptionsMatch(previewOptions, CreateOptions());
        }

        static bool OptionsMatch(AssetCloneIsolationOptions left, AssetCloneIsolationOptions right)
        {
            if (left == null || right == null) return false;
            return left.SourceRoot == right.SourceRoot && left.TargetRoot == right.TargetRoot
                && left.OverwriteExistingAssets == right.OverwriteExistingAssets
                && left.RewriteExistingTargetAssets == right.RewriteExistingTargetAssets
                && left.SelectedAssetPaths.SequenceEqual(right.SelectedAssetPaths, StringComparer.OrdinalIgnoreCase)
                && left.ExplicitSharedAssetPaths.SequenceEqual(right.ExplicitSharedAssetPaths, StringComparer.OrdinalIgnoreCase)
                && left.ExplicitCloneExternalAssetPaths.SequenceEqual(right.ExplicitCloneExternalAssetPaths, StringComparer.OrdinalIgnoreCase);
        }

        void MarkPlanDirty()
        {
            configurationVersion++;
            applyButton?.SetEnabled(false);
            RefreshConfigurationFields();
            if (previewReport == null) return;
            previewWorkspace?.SetUnavailable("计划已过期：配置或待克隆对象已变化，请重新预览。");
            activeContext?.InvalidateCurrentReport?.Invoke("计划已过期，请重新预览。");
        }

        void RefreshConfigurationFields()
        {
            sourceRootField?.SetValueWithoutNotify(sourceRoot);
            targetRootField?.SetValueWithoutNotify(targetRoot);
            overwriteToggle?.SetValueWithoutNotify(overwriteExistingAssets);
            rewriteToggle?.SetValueWithoutNotify(rewriteExistingTargetAssets);
            if (sharedCountLabel != null) sharedCountLabel.text = "显式共享依赖：" + explicitSharedPaths.Count;
            if (externalCountLabel != null) externalCountLabel.text = "外部依赖迁移：" + explicitCloneExternalPaths.Count;
        }

        /// <summary>
        /// Creates the top workflow summary box.
        /// </summary>
        static HelpBox CreateIntroHelpBox()
        {
            return new HelpBox(
                "拖入资源后预览：先核对写入计划，再检查依赖和风险。文件夹会递归展开；配置变化后需要重新预览。",
                HelpBoxMessageType.Info);
        }

        /// <summary>
        /// Creates root path, overwrite, rewrite, and preset controls.
        /// </summary>
        VisualElement CreateConfigurationView()
        {
            var root = new VisualElement();

            sourceRootField = new TextField("SourceRoot") { value = sourceRoot };
            sourceRootField.RegisterValueChangedCallback(evt => { sourceRoot = evt.newValue; MarkPlanDirty(); });
            root.Add(sourceRootField);

            targetRootField = new TextField("TargetRoot") { value = targetRoot };
            targetRootField.RegisterValueChangedCallback(evt => { targetRoot = evt.newValue; MarkPlanDirty(); });
            root.Add(targetRootField);

            overwriteToggle = new Toggle("允许覆盖已有目标文件，但保留目标 GUID") { value = overwriteExistingAssets };
            overwriteToggle.RegisterValueChangedCallback(evt => { overwriteExistingAssets = evt.newValue; MarkPlanDirty(); });
            root.Add(overwriteToggle);

            rewriteToggle = new Toggle("应用后修复 TargetRoot 已有旧 GUID 引用") { value = rewriteExistingTargetAssets };
            rewriteToggle.RegisterValueChangedCallback(evt => { rewriteExistingTargetAssets = evt.newValue; MarkPlanDirty(); });
            root.Add(rewriteToggle);

            var explicitSharedRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            sharedCountLabel = new Label("显式共享依赖：" + explicitSharedPaths.Count) { style = { flexGrow = 1 } };
            explicitSharedRow.Add(sharedCountLabel);
            explicitSharedRow.Add(ActionButton("清空显式共享", () => { explicitSharedPaths.Clear(); MarkPlanDirty(); }));
            root.Add(explicitSharedRow);

            var externalCloneRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
            externalCountLabel = new Label("外部依赖迁移：" + explicitCloneExternalPaths.Count) { style = { flexGrow = 1 } };
            externalCloneRow.Add(externalCountLabel);
            externalCloneRow.Add(ActionButton("清空外部迁移", () => { explicitCloneExternalPaths.Clear(); MarkPlanDirty(); }));
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
                            MarkPlanDirty();
                        }
                        else if (evt.newValue == null) { targets.RemoveAt(rowIndex); MarkPlanDirty(); }

                        refreshTargetList();
                    });
                    row.Add(field);
                    row.Add(ActionButton("移除", () =>
                    {
                        targets.RemoveAt(rowIndex);
                        MarkPlanDirty();
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
                MarkPlanDirty();
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
                    MarkPlanDirty();
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
            if (previewWorkspace == null)
                previewWorkspace = new TaAssetCloneIsolationPreviewWorkspace(previewState, ChangeDependencyDecisions,
                    () => ShowPlanPreview(context), () => ShowAuditReport(context), () => context.RequestApply?.Invoke());
            previewWorkspace.SetPlan(plan);
            VisualElement view = previewWorkspace;
            string status = BuildPreviewStatus(plan, report);
            if (context.ShowCustomReportWorkspace != null)
            {
                context.ShowCustomReportWorkspace.Invoke(report, view, status);
            }
            else if (context.ShowCustomReportView != null)
            {
                context.ShowCustomReportView.Invoke(report, view, status);
            }
            else
            {
                context.ShowReport?.Invoke(report);
            }
            applyButton?.SetEnabled(CanApplyPreview(report));
        }

        void ChangeDependencyDecisions(IReadOnlyList<IsolationPreviewRow> rows, IsolationDependencyAction action)
        {
            if (previewReport == null || consumed || previewVersion != configurationVersion) return;
            List<string> paths = rows.Where(row => row.Supports(action)).Select(row => row.SourcePath)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 0) return;
            if (action == IsolationDependencyAction.Share && !EditorUtility.DisplayDialog("确认显式共享依赖",
                $"{paths.Count} 个资源将留在原地，不会克隆。目标资源会继续引用 SourceRoot 中的原资源。\n\n"
                + string.Join("\n", paths.Take(8)) + (paths.Count > 8 ? "\n…" : ""), "留在原地", "取消")) return;
            SetDependencyPaths(paths, action);
            MarkPlanDirty();
            ShowPlanPreview(activeContext);
        }

        internal void SetDependencyPaths(IEnumerable<string> paths, IsolationDependencyAction action)
        {
            List<string> chosen = action == IsolationDependencyAction.Share || action == IsolationDependencyAction.FollowClone
                ? explicitSharedPaths : explicitCloneExternalPaths;
            bool add = action == IsolationDependencyAction.Share || action == IsolationDependencyAction.MigrateExternal;
            foreach (string path in paths)
            {
                int index = chosen.FindIndex(value => value.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (add && index < 0) chosen.Add(path);
                else if (!add && index >= 0) chosen.RemoveAt(index);
            }
            chosen.Sort(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Converts a clone plan to the shared TA Art Tools report format.
        /// </summary>
        ArtToolReport BuildPlanReport(AssetCloneIsolationPlan plan)
        {
            var report = ArtToolReport.Empty(PanelTitle);
            previewReport = report;
            previewVersion = configurationVersion;
            previewOptions = CreateOptions().Clone();
            consumed = false;
            report.ApplySummary = IsolationPreviewData.BuildApplySummary(plan);
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

            if (!plan.HasErrors && (plan.Assets.Count > 0 || plan.TargetRewriteRecords.Count > 0))
            {
                report.Changes.Add(ArtToolChange.Write(
                    "应用资产克隆隔离计划",
                    $"写入克隆资产 {plan.Assets.Count} 个，并修复 TargetRoot 引用 {plan.TargetRewriteRecords.Count} 个文件。",
                    () => ExecutePreviewedPlan(plan, report),
                    plan.Options.TargetRoot));
            }

            applyButton?.SetEnabled(CanApplyPreview(report));

            return report;
        }

        void ExecutePreviewedPlan(AssetCloneIsolationPlan plan, ArtToolReport report)
        {
            if (!CanApplyPreview(report)) throw new InvalidOperationException("计划已过期，请重新预览。");
            consumed = true;
            applyButton?.SetEnabled(false);
            activeContext?.InvalidateCurrentReport?.Invoke("正在应用计划。");
            previewWorkspace?.SetUnavailable("正在应用计划。");
            try
            {
                AssetCloneIsolationService.ApplyPlan(plan);
                string message = "计划已应用。可审计目标目录，或重新预览；此计划不能重复执行。";
                previewWorkspace?.SetUnavailable(message);
                activeContext?.Log?.Invoke(message);
            }
            catch
            {
                previewWorkspace?.SetUnavailable("应用失败，可能已有部分文件写入。请检查 Console 并重新预览。");
                throw;
            }
        }

        /// <summary>
        /// Builds and displays a target-root audit report.
        /// </summary>
        void ShowAuditReport(ArtToolContext context)
        {
            previewReport = null;
            previewVersion = -1;
            applyButton?.SetEnabled(false);
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
                : "预览完成：" + IsolationPreviewData.BuildApplySummary(plan);
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
            MarkPlanDirty();
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

    }
}
