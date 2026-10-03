using System;
using System.Collections.Generic;
using System.Linq;
using AssetCloneIsolation.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    internal sealed class IsolationPreviewState
    {
        public IsolationPreviewTab Tab;
        public string Search = string.Empty;
        public string Status = "全部";
        public string Owner = "全部对象";
        public readonly HashSet<string> SelectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public Vector2 ScrollOffset;
        public string RelationOwner = string.Empty;
        public readonly Dictionary<string, bool> ExpandedDetails = new Dictionary<string, bool>();
    }

    internal sealed class TaAssetCloneIsolationPreviewWorkspace : VisualElement
    {
        const string StylePath = "Assets/ArtTools/Editor/Modules/AssetCloneIsolation/AssetCloneIsolationPreview.uss";
        readonly IsolationPreviewState state;
        readonly Action<IReadOnlyList<IsolationPreviewRow>, IsolationDependencyAction> changeDecision;
        readonly Action preview;
        readonly Action audit;
        readonly Action apply;
        readonly Label lifecycle;
        readonly VisualElement summary;
        readonly List<Button> tabs = new List<Button>();
        readonly PopupField<string> statusField;
        readonly PopupField<string> ownerField;
        readonly Label matches;
        readonly Label empty;
        readonly Label sourceHeader;
        readonly Label targetHeader;
        readonly VisualElement batch;
        readonly List<Button> batchButtons = new List<Button>();
        readonly ListView list;
        readonly ScrollView details;
        readonly Button applyButton;
        IsolationPreviewData data;
        List<IsolationPreviewRow> visibleRows = new List<IsolationPreviewRow>();
        bool updating;
        bool current;

        internal IsolationPreviewData Data => data;
        internal ListView AssetList => list;

        public TaAssetCloneIsolationPreviewWorkspace(IsolationPreviewState state,
            Action<IReadOnlyList<IsolationPreviewRow>, IsolationDependencyAction> changeDecision,
            Action preview, Action audit, Action apply)
        {
            this.state = state;
            this.changeDecision = changeDecision;
            this.preview = preview;
            this.audit = audit;
            this.apply = apply;
            name = "asset-clone-isolation-preview";
            AddToClassList("aci-workspace");
            EnableInClassList("aci-light", !EditorGUIUtility.isProSkin);
            StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (sheet != null) styleSheets.Add(sheet);

            lifecycle = Label("请预览计划。", "aci-lifecycle");
            Add(lifecycle);
            summary = Element("aci-summary");
            Add(summary);
            var toolbar = Element("aci-toolbar");
            string[] titles = { "写入计划", "依赖处理", "问题与风险" };
            for (int i = 0; i < titles.Length; i++)
            {
                IsolationPreviewTab tab = (IsolationPreviewTab)i;
                Button button = Button(titles[i], () => Navigate(tab, "全部"));
                button.AddToClassList("aci-tab");
                tabs.Add(button);
                toolbar.Add(button);
            }
            var spacer = Element("aci-spacer");
            toolbar.Add(spacer);
            applyButton = Button("应用计划", () => this.apply?.Invoke());
            toolbar.Add(applyButton);
            toolbar.Add(Button("重新预览", () => this.preview?.Invoke()));
            toolbar.Add(Button("审计目标目录", () => this.audit?.Invoke()));
            Add(toolbar);

            var filters = Element("aci-filters");
            var search = new ToolbarSearchField { value = state.Search, name = "aci-search" };
            search.AddToClassList("aci-search");
            search.tooltip = "搜索资源名称、源路径、目标路径或问题说明";
            search.RegisterValueChangedCallback(evt => { state.Search = evt.newValue ?? string.Empty; RefreshRows(false); });
            filters.Add(search);
            statusField = new PopupField<string>(new List<string> { "全部" }, 0) { name = "aci-status-filter" };
            statusField.AddToClassList("aci-status-filter");
            statusField.tooltip = "按操作或依赖处理状态筛选";
            statusField.RegisterValueChangedCallback(evt => { if (!updating) { state.Status = evt.newValue; RefreshRows(false); } });
            filters.Add(statusField);
            ownerField = new PopupField<string>(new List<string> { "全部对象" }, 0) { name = "aci-owner-filter" };
            ownerField.AddToClassList("aci-owner-filter");
            ownerField.tooltip = "按所属待克隆对象筛选；全局错误始终可见";
            ownerField.RegisterValueChangedCallback(evt => { if (!updating) { state.Owner = evt.newValue; RefreshRows(false); } });
            filters.Add(ownerField);
            Add(filters);
            matches = Label("", "aci-matches");
            Add(matches);

            batch = Element("aci-batch");
            string[] actions = { "留在原地", "跟随克隆", "迁移到目标", "取消迁移" };
            IsolationDependencyAction[] decisions = { IsolationDependencyAction.Share, IsolationDependencyAction.FollowClone,
                IsolationDependencyAction.MigrateExternal, IsolationDependencyAction.KeepExternal };
            for (int i = 0; i < actions.Length; i++)
            {
                IsolationDependencyAction action = decisions[i];
                Button button = Button(actions[i], () => ChangeSelection(action));
                button.userData = action;
                batchButtons.Add(button);
                batch.Add(button);
            }
            Add(batch);

            var body = Element("aci-body");
            var table = Element("aci-table");
            var header = Element("aci-table-row", "aci-table-header");
            header.Add(Label("操作 / 状态", "aci-col-status"));
            header.Add(Label("资源名称", "aci-col-name"));
            header.Add(Label("类型", "aci-col-type"));
            sourceHeader = Label("源路径", "aci-col-path");
            targetHeader = Label("目标路径", "aci-col-path");
            header.Add(sourceHeader);
            header.Add(targetHeader);
            table.Add(header);

            list = new ListView
            {
                name = "aci-asset-list", fixedItemHeight = 28,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Multiple,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                makeItem = MakeRow,
                bindItem = BindRow
            };
            list.AddToClassList("aci-list");
            list.selectionChanged += SelectionChanged;
            list.unbindItem = (element, _) => element.userData = null;
            list.Q<ScrollView>().RegisterCallback<GeometryChangedEvent>(_ => RestoreScroll());
            list.Q<ScrollView>().verticalScroller.valueChanged += _ =>
            {
                if (!updating) state.ScrollOffset = list.Q<ScrollView>().scrollOffset;
            };
            table.Add(list);
            empty = Label("", "aci-empty");
            table.Add(empty);
            body.Add(table);
            details = new ScrollView { name = "aci-details" };
            details.AddToClassList("aci-details");
            body.Add(details);
            Add(body);
            RegisterCallback<GeometryChangedEvent>(evt => EnableInClassList("aci-narrow", evt.newRect.width < 840));
        }

        public void SetPlan(AssetCloneIsolationPlan plan)
        {
            data = IsolationPreviewData.Build(plan);
            current = true;
            lifecycle.text = plan.HasErrors ? "预览完成：存在阻断错误，请在“问题与风险”中查看原因。"
                : data.Writes.Count == 0 ? "预览完成：没有待写入文件。" : "计划有效：核对写入内容和共享风险后应用。";
            EnableInClassList("aci-stale", false);
            applyButton.SetEnabled(!plan.HasErrors && data.Writes.Count > 0);
            BuildSummary();
            if (plan.HasErrors && data.Writes.Count == 0 && state.Tab == IsolationPreviewTab.Writes)
                state.Tab = IsolationPreviewTab.Issues;
            RefreshChoices();
            RefreshRows(true);
        }

        public void SetUnavailable(string message)
        {
            current = false;
            lifecycle.text = message;
            applyButton.SetEnabled(false);
            EnableInClassList("aci-stale", true);
            UpdateBatch();
            RenderDetails();
        }

        void BuildSummary()
        {
            summary.Clear();
            AssetCloneIsolationPlanSummary counts = AssetCloneIsolationPlanSummary.FromPlan(data.Plan);
            AddSummary("新建", counts.NewTargetAssetCount, IsolationPreviewTab.Writes, "新建");
            AddSummary("覆盖", counts.ExistingTargetAssetCount, IsolationPreviewTab.Writes, "覆盖");
            AddSummary("引用修复", counts.TargetRewriteFileCount, IsolationPreviewTab.Writes, "引用修复");
            int shared = data.Dependencies.Count(row => row.Decision == AssetCloneIsolationDecision.SharedDependency
                || row.Decision == AssetCloneIsolationDecision.ExplicitShared || row.Decision == AssetCloneIsolationDecision.ExternalShared);
            AddSummary("保留共享", shared, IsolationPreviewTab.Dependencies, "保留共享");
            AddSummary("阻断错误", data.Issues.Count(row => row.Severity == ArtToolChangeSeverity.Error), IsolationPreviewTab.Issues, "阻断错误");
            AddSummary("警告", data.Issues.Count(row => row.Severity == ArtToolChangeSeverity.Warning), IsolationPreviewTab.Issues, "警告");
        }

        void AddSummary(string title, int count, IsolationPreviewTab tab, string status)
        {
            Button button = Button(title + "  " + count, () => Navigate(tab, status, true));
            button.AddToClassList("aci-summary-card");
            if (title == "阻断错误" && count > 0) button.AddToClassList("aci-error");
            if (title == "警告" && count > 0) button.AddToClassList("aci-warning");
            summary.Add(button);
        }

        void Navigate(IsolationPreviewTab tab, string status, bool resetOwner = false)
        {
            if (state.Tab != tab) { state.SelectedKeys.Clear(); state.ScrollOffset = Vector2.zero; }
            state.Tab = tab;
            state.Status = status;
            if (resetOwner) state.Owner = "全部对象";
            RefreshChoices();
            RefreshRows(false);
        }

        void RefreshChoices()
        {
            if (data == null) return;
            updating = true;
            var statuses = new List<string> { "全部" };
            if (state.Tab == IsolationPreviewTab.Writes)
                statuses.AddRange(new[] { "新建", "覆盖", "外部迁移", "引用修复" });
            else if (state.Tab == IsolationPreviewTab.Dependencies)
                statuses.AddRange(new[] { "跟随克隆", "显式共享", "外部共享", "外部迁移", "系统共享", "保留共享", "目标目录", "阻断", "未知" });
            else statuses.AddRange(new[] { "阻断错误", "警告" });
            if (!statuses.Contains(state.Status)) state.Status = "全部";
            statusField.choices = statuses;
            statusField.SetValueWithoutNotify(state.Status);
            var owners = new List<string> { "全部对象" };
            owners.AddRange(data.Plan.RootPlans.Select(root => root.RootAssetPath).Distinct(StringComparer.OrdinalIgnoreCase));
            if (!owners.Contains(state.Owner)) state.Owner = "全部对象";
            ownerField.choices = owners;
            ownerField.SetValueWithoutNotify(state.Owner);
            for (int i = 0; i < tabs.Count; i++) tabs[i].EnableInClassList("aci-tab-active", i == (int)state.Tab);
            batch.EnableInClassList("aci-hidden", state.Tab != IsolationPreviewTab.Dependencies);
            sourceHeader.text = state.Tab == IsolationPreviewTab.Issues ? "相关资源" : "源路径";
            targetHeader.text = state.Tab == IsolationPreviewTab.Issues ? "问题说明" : "目标路径";
            updating = false;
        }

        void RefreshRows(bool preserveScroll)
        {
            if (data == null) return;
            if (!preserveScroll) state.ScrollOffset = Vector2.zero;
            visibleRows = data.Rows(state.Tab).Where(row => row.Matches(state.Search, state.Status, state.Owner)).ToList();
            var visibleKeys = new HashSet<string>(visibleRows.Select(row => row.Key), StringComparer.OrdinalIgnoreCase);
            state.SelectedKeys.IntersectWith(visibleKeys);
            updating = true;
            list.itemsSource = visibleRows;
            list.RefreshItems();
            list.SetSelectionWithoutNotify(visibleRows.Select((row, index) => new { row, index })
                .Where(item => state.SelectedKeys.Contains(item.row.Key)).Select(item => item.index));
            updating = false;
            matches.text = $"匹配 {visibleRows.Count} / {data.Rows(state.Tab).Count} 项 · 顶部统计为完整计划 · 已选 {state.SelectedKeys.Count} 项";
            empty.text = data.Rows(state.Tab).Count == 0
                ? state.Tab == IsolationPreviewTab.Issues ? "没有问题与风险。" : "没有此类数据。请先添加资源并预览。"
                : "没有匹配项，请调整筛选。";
            empty.EnableInClassList("aci-hidden", visibleRows.Count != 0);
            RestoreScroll();
            schedule.Execute(RestoreScroll);
            UpdateBatch();
            RenderDetails();
        }

        void RestoreScroll()
        {
            updating = true;
            list.Q<ScrollView>().scrollOffset = state.ScrollOffset;
            updating = false;
        }

        VisualElement MakeRow()
        {
            var row = Element("aci-table-row");
            row.Add(Label("", "aci-col-status"));
            row.Add(Label("", "aci-col-name"));
            row.Add(Label("", "aci-col-type"));
            row.Add(Label("", "aci-col-path"));
            row.Add(Label("", "aci-col-path"));
            return row;
        }

        void BindRow(VisualElement element, int index)
        {
            IsolationPreviewRow row = visibleRows[index];
            element.userData = row;
            string[] values = { row.Status, row.Name, row.AssetType, row.SourcePath,
                state.Tab == IsolationPreviewTab.Issues ? row.Detail : row.TargetPath };
            for (int i = 0; i < values.Length; i++)
            {
                var label = (Label)element[i];
                label.text = values[i];
                label.tooltip = values[i];
            }
            element.EnableInClassList("aci-error", state.Tab == IsolationPreviewTab.Issues && row.Severity == ArtToolChangeSeverity.Error);
            element.EnableInClassList("aci-warning", state.Tab == IsolationPreviewTab.Issues && row.Severity == ArtToolChangeSeverity.Warning);
        }

        void SelectionChanged(IEnumerable<object> selection)
        {
            if (updating) return;
            state.SelectedKeys.Clear();
            foreach (IsolationPreviewRow row in selection.OfType<IsolationPreviewRow>()) state.SelectedKeys.Add(row.Key);
            matches.text = $"匹配 {visibleRows.Count} / {data.Rows(state.Tab).Count} 项 · 顶部统计为完整计划 · 已选 {state.SelectedKeys.Count} 项";
            UpdateBatch();
            RenderDetails();
        }

        List<IsolationPreviewRow> SelectedRows() => visibleRows.Where(row => state.SelectedKeys.Contains(row.Key)).ToList();

        void UpdateBatch()
        {
            List<IsolationPreviewRow> rows = SelectedRows();
            foreach (Button button in batchButtons)
            {
                var action = (IsolationDependencyAction)button.userData;
                int eligible = rows.Count(row => row.Supports(action));
                button.SetEnabled(current && eligible > 0);
                button.tooltip = $"对已选资源中 {eligible} 个可操作项执行；其他项不变。";
            }
        }

        void ChangeSelection(IsolationDependencyAction action)
        {
            if (!current) return;
            List<IsolationPreviewRow> rows = SelectedRows().Where(row => row.Supports(action)).ToList();
            if (rows.Count > 0) changeDecision?.Invoke(rows, action);
        }

        void RenderDetails()
        {
            details.Clear();
            List<IsolationPreviewRow> rows = SelectedRows();
            if (rows.Count == 0) { details.Add(Label("选中一项查看路径、处理原因和引用关系。\n依赖页支持 Ctrl / Shift 多选后批量处理。", "aci-detail-text")); return; }
            IsolationPreviewRow row = rows[0];
            details.Add(Label(rows.Count == 1 ? row.Name : $"已选 {rows.Count} 项 · 当前详情：{row.Name}", "aci-detail-title"));
            details.Add(Label(row.Status, "aci-detail-text"));
            AddDetail("源路径", row.SourcePath);
            AddDetail("目标路径", row.TargetPath);
            AddDetail("处理原因", string.IsNullOrEmpty(row.Detail) ? "按当前计划规则处理。" : row.Detail);
            AddDetail("所属待克隆对象", row.Owners.Count == 0 ? "全局计划" : string.Join("\n", row.Owners.OrderBy(path => path)));
            var actions = Element("aci-toolbar");
            if (!string.IsNullOrEmpty(row.SourcePath)) actions.Add(Button("定位源资源", () => Ping(row.SourcePath)));
            if (!string.IsNullOrEmpty(row.TargetPath))
            {
                Button target = Button("定位目标", () => Ping(row.TargetPath));
                target.SetEnabled(AssetDatabase.LoadMainAssetAtPath(row.TargetPath) != null);
                actions.Add(target);
            }
            details.Add(actions);
            IsolationPreviewRow dependency = data.Dependencies.FirstOrDefault(item => item.SourcePath == row.SourcePath);
            if (dependency != null)
            {
                foreach (IsolationDependencyAction action in Enum.GetValues(typeof(IsolationDependencyAction)))
                {
                    if (!dependency.Supports(action)) continue;
                    string text = action == IsolationDependencyAction.Share ? "留在原地" : action == IsolationDependencyAction.FollowClone
                        ? "跟随克隆" : action == IsolationDependencyAction.MigrateExternal ? "迁移到目标" : "取消迁移";
                    Button button = Button(text, () => { if (current) changeDecision?.Invoke(new[] { dependency }, action); });
                    button.SetEnabled(current);
                    details.Add(button);
                }
            }
            if (!string.IsNullOrEmpty(row.SourceGuid) || !string.IsNullOrEmpty(row.TargetGuid))
            {
                var advanced = new Foldout { text = "高级信息：GUID", value = false };
                PreserveFoldout(advanced, "guid:" + row.Key);
                var guids = new TextField { value = "源 GUID：" + row.SourceGuid + "\n目标 GUID：" + row.TargetGuid, isReadOnly = true, multiline = true };
                guids.AddToClassList("aci-detail-value");
                advanced.Add(guids);
                details.Add(advanced);
            }
            AddRelations(row);
        }

        void AddDetail(string title, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            details.Add(Label(title, "aci-detail-heading"));
            var field = new TextField { value = value, isReadOnly = true, multiline = true };
            field.AddToClassList("aci-detail-value");
            details.Add(field);
        }

        void AddRelations(IsolationPreviewRow row)
        {
            List<AssetCloneIsolationRootPlan> roots = data.Plan.RootPlans.Where(root => row.Owners.Contains(root.RootAssetPath)).ToList();
            if (roots.Count == 0) return;
            details.Add(Label("引用扫描范围：SourceRoot 与 TargetRoot 的文本 GUID 引用。以下关系以所属待克隆对象为中心。", "aci-detail-text"));
            var relations = Element("aci-root-relations");
            if (roots.Count > 1)
            {
                List<string> paths = roots.Select(root => root.RootAssetPath).ToList();
                int selectedOwner = Math.Max(0, paths.IndexOf(state.RelationOwner));
                var ownerField = new PopupField<string>("关系所属对象", paths, selectedOwner);
                ownerField.RegisterValueChangedCallback(evt =>
                {
                    state.RelationOwner = evt.newValue;
                    ShowRootRelations(relations, roots.First(root => root.RootAssetPath == evt.newValue));
                });
                details.Add(ownerField);
                state.RelationOwner = paths[selectedOwner];
            }
            details.Add(relations);
            ShowRootRelations(relations, roots.FirstOrDefault(root => root.RootAssetPath == state.RelationOwner) ?? roots[0]);
        }

        void ShowRootRelations(VisualElement container, AssetCloneIsolationRootPlan root)
        {
            container.Clear();
            container.Add(Label(root.RootAssetPath, "aci-detail-text"));
            container.Add(RelationList("下游依赖", root.DownstreamDependencies, root.RootAssetPath));
            container.Add(RelationList("直接上游引用", root.UpstreamReferences, root.RootAssetPath));
            container.Add(RelationList("共用下游依赖的资产（不是直接上游）", root.SharedDependencyReferences, root.RootAssetPath));
        }

        void PreserveFoldout(Foldout foldout, string key)
        {
            foldout.SetValueWithoutNotify(state.ExpandedDetails.TryGetValue(key, out bool expanded) && expanded);
            foldout.RegisterValueChangedCallback(evt => state.ExpandedDetails[key] = evt.newValue);
        }

        VisualElement RelationList(string title, IReadOnlyList<AssetCloneIsolationRelationNode> nodes, string owner)
        {
            var foldout = new Foldout { text = title + " (" + nodes.Count + ")", value = false };
            PreserveFoldout(foldout, owner + ":" + title);
            if (nodes.Count == 0) return foldout;
            var relations = new ListView
            {
                fixedItemHeight = 24, virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                itemsSource = nodes.ToList(), selectionType = SelectionType.Single,
                makeItem = () => Label("", "aci-relation-row"),
                bindItem = (element, index) => { ((Label)element).text = nodes[index].AssetPath; element.tooltip = nodes[index].AssetPath; }
            };
            relations.AddToClassList("aci-relations");
            relations.itemsChosen += items => { foreach (AssetCloneIsolationRelationNode node in items) Ping(node.AssetPath); };
            foldout.Add(relations);
            return foldout;
        }

        static void Ping(string path)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        static VisualElement Element(params string[] classes)
        {
            var element = new VisualElement();
            foreach (string value in classes) element.AddToClassList(value);
            return element;
        }
        static Label Label(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }
        static Button Button(string text, Action action) => new Button(action) { text = text };
    }
}
