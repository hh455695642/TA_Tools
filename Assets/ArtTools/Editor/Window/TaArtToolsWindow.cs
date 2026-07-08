using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    public sealed class TaArtToolsWindow : EditorWindow
    {
        const float NavigationInitialWidth = 270f;
        const float NavigationMinWidth = 210f;
        const float RightPaneMinWidth = 520f;
        const float ModulePanelInitialHeight = 260f;
        const float ModulePanelMinHeight = 150f;
        const float PreviewPanelMinHeight = 240f;
        const float HelpPanelWidth = 340f;

        readonly List<IArtToolModule> modules = new List<IArtToolModule>();
        readonly Dictionary<IArtToolModule, Button> moduleButtons = new Dictionary<IArtToolModule, Button>();

        IArtToolModule activeModule;
        ArtToolReport currentReport;
        VisualElement modulePanel;
        ScrollView moduleScroll;
        ScrollView helpScroll;
        ScrollView resultScroll;
        Label statusLabel;

        [MenuItem("Tools/TA/Art Tools")]
        public static void Open()
        {
            TaArtToolsWindow window = GetWindow<TaArtToolsWindow>("TA Art Tools");
            window.minSize = new Vector2(NavigationMinWidth + RightPaneMinWidth, ModulePanelMinHeight + PreviewPanelMinHeight);
            window.Show();
            window.Focus();
        }

        void OnEnable()
        {
            modules.Clear();
            modules.Add(new TaAstcFormatModule());
            modules.Add(new TaVfxTextureOptimizerModule());
            modules.Add(new TaMeshUsageModule());
            modules.Add(new TaTextureUsageModule());
            modules.Add(new TaShaderUsageModule());
            modules.Add(new TaSimpleLitRefreshModule());
            modules.Add(new TaAssetCloneIsolationModule());
            modules.Add(new TaDisabledRendererCleanerModule());
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexGrow = 1;
            rootVisualElement.style.minWidth = NavigationMinWidth + RightPaneMinWidth;

            var rootSplit = new TwoPaneSplitView(0, NavigationInitialWidth, TwoPaneSplitViewOrientation.Horizontal);
            rootSplit.style.flexGrow = 1;
            rootVisualElement.Add(rootSplit);

            var leftPane = new VisualElement();
            leftPane.style.minWidth = NavigationMinWidth;
            leftPane.style.flexGrow = 1;
            leftPane.style.paddingLeft = 8;
            leftPane.style.paddingRight = 8;
            leftPane.style.paddingTop = 8;
            leftPane.style.paddingBottom = 8;
            leftPane.style.borderRightWidth = 1;
            leftPane.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            leftPane.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            rootSplit.Add(leftPane);

            var title = new Label("TA Art Tools");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 15;
            title.style.marginBottom = 8;
            leftPane.Add(title);

            var nav = new ScrollView();
            nav.style.flexGrow = 1;
            nav.style.marginTop = 4;
            leftPane.Add(nav);

            var rightSplit = new TwoPaneSplitView(0, ModulePanelInitialHeight, TwoPaneSplitViewOrientation.Vertical);
            rightSplit.style.flexGrow = 1;
            rightSplit.style.minWidth = RightPaneMinWidth;
            rootSplit.Add(rightSplit);

            modulePanel = new VisualElement();
            modulePanel.style.flexGrow = 1;
            modulePanel.style.minHeight = ModulePanelMinHeight;
            modulePanel.style.overflow = Overflow.Hidden;
            modulePanel.style.paddingLeft = 10;
            modulePanel.style.paddingRight = 10;
            modulePanel.style.paddingTop = 8;
            modulePanel.style.paddingBottom = 8;
            rightSplit.Add(modulePanel);

            var moduleTopRow = new VisualElement();
            moduleTopRow.style.flexDirection = FlexDirection.Row;
            moduleTopRow.style.flexGrow = 1;
            moduleTopRow.style.minHeight = 0;
            modulePanel.Add(moduleTopRow);

            moduleScroll = new ScrollView();
            moduleScroll.name = "ta-art-tools-module-scroll";
            moduleScroll.style.flexGrow = 1;
            moduleScroll.style.minHeight = 0;
            moduleScroll.style.overflow = Overflow.Hidden;
            moduleScroll.style.marginRight = 10;
            moduleTopRow.Add(moduleScroll);

            helpScroll = new ScrollView();
            helpScroll.name = "ta-art-tools-help-scroll";
            helpScroll.style.width = HelpPanelWidth;
            helpScroll.style.minWidth = HelpPanelWidth;
            helpScroll.style.minHeight = 0;
            helpScroll.style.flexShrink = 0;
            helpScroll.style.paddingLeft = 10;
            helpScroll.style.borderLeftWidth = 1;
            helpScroll.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f);
            moduleTopRow.Add(helpScroll);

            var previewPanel = new VisualElement();
            previewPanel.style.flexGrow = 1;
            previewPanel.style.minHeight = PreviewPanelMinHeight;
            previewPanel.style.overflow = Overflow.Hidden;
            previewPanel.style.borderTopWidth = 1;
            previewPanel.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f);
            previewPanel.style.paddingLeft = 8;
            previewPanel.style.paddingRight = 8;
            previewPanel.style.paddingTop = 6;
            previewPanel.style.paddingBottom = 8;
            rightSplit.Add(previewPanel);

            statusLabel = new Label("就绪。");
            statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            statusLabel.style.marginBottom = 4;
            previewPanel.Add(statusLabel);

            resultScroll = new ScrollView();
            resultScroll.style.flexGrow = 1;
            resultScroll.style.minHeight = 0;
            previewPanel.Add(resultScroll);

            moduleButtons.Clear();
            foreach (KeyValuePair<string, List<IArtToolModule>> categoryGroup in BuildModuleCategoryGroups())
            {
                var category = new Label(categoryGroup.Key);
                category.style.unityFontStyleAndWeight = FontStyle.Bold;
                category.style.marginTop = 8;
                nav.Add(category);

                foreach (IArtToolModule module in categoryGroup.Value)
                {
                    IArtToolModule captured = module;
                    var button = new Button(() => SelectModule(captured)) { text = module.DisplayName };
                    button.style.unityTextAlign = TextAnchor.MiddleLeft;
                    button.style.marginBottom = 2;
                    nav.Add(button);
                    moduleButtons.Add(module, button);
                }
            }

            SelectModule(modules.Count > 0 ? modules[0] : null);
        }

        /// <summary>
        /// Builds navigation groups while preserving the first-seen category order and module order inside each category.
        /// </summary>
        List<KeyValuePair<string, List<IArtToolModule>>> BuildModuleCategoryGroups()
        {
            var categoryGroups = new List<KeyValuePair<string, List<IArtToolModule>>>();

            foreach (IArtToolModule module in modules)
            {
                string categoryName = string.IsNullOrEmpty(module.Category) ? "Tools" : module.Category;
                int categoryIndex = categoryGroups.FindIndex(pair => string.Equals(pair.Key, categoryName, StringComparison.Ordinal));
                if (categoryIndex < 0)
                {
                    categoryGroups.Add(new KeyValuePair<string, List<IArtToolModule>>(categoryName, new List<IArtToolModule> { module }));
                    continue;
                }

                categoryGroups[categoryIndex].Value.Add(module);
            }

            return categoryGroups;
        }

        void SelectModule(IArtToolModule module)
        {
            activeModule = module;
            currentReport = null;
            moduleScroll.Clear();
            resultScroll.Clear();

            foreach (KeyValuePair<IArtToolModule, Button> pair in moduleButtons)
                pair.Value.SetEnabled(pair.Key != activeModule);

            if (activeModule == null)
            {
                SetStatus("没有可用模块。");
                UpdateButtons();
                return;
            }

            var context = new ArtToolContext
            {
                RequestScan = RunScan,
                RequestApply = RunApply,
                ShowReport = ShowReport,
                ShowCustomView = ShowCustomView,
                ShowCustomReportView = ShowCustomReportView,
                ExportCurrentReport = ExportCurrentReport,
                Log = SetStatus,
                CurrentReport = () => currentReport
            };
            moduleScroll.Add(activeModule.CreateView(context));
            RenderHelp(activeModule);
            SetStatus(GetModuleReadyStatus(activeModule));
            UpdateButtons();
        }

        /// <summary>
        /// Returns an informative ready status for the active module without implying that user assets were selected.
        /// </summary>
        static string GetModuleReadyStatus(IArtToolModule module)
        {
            if (module == null)
                return "就绪。";

            return string.IsNullOrEmpty(module.Description)
                ? module.PanelTitle + "：就绪。"
                : module.Description;
        }

        void RunScan()
        {
            if (activeModule == null)
                return;

            try
            {
                currentReport = activeModule.Scan();
                RenderReport(currentReport);
                if (currentReport.WriteCount > 0)
                    SetStatus($"{activeModule.PanelTitle}：{currentReport.Changes.Count} 条结果，{currentReport.WriteCount} 条写入变更。");
                else
                    SetStatus($"{activeModule.PanelTitle}：{currentReport.Changes.Count} 条结果。");
            }
            catch (Exception e)
            {
                currentReport = ArtToolReport.Empty(activeModule.PanelTitle);
                currentReport.Changes.Add(ArtToolChange.Error("扫描失败", e.ToString()));
                RenderReport(currentReport);
                Debug.LogException(e);
            }
            finally
            {
                UpdateButtons();
            }
        }

        void ShowReport(ArtToolReport report)
        {
            currentReport = report;
            RenderReport(currentReport);

            if (currentReport == null)
            {
                SetStatus("没有结果。");
                return;
            }

            if (currentReport.WriteCount > 0)
                SetStatus($"{currentReport.ToolName}：{currentReport.Changes.Count} 条结果，{currentReport.WriteCount} 条写入变更。");
            else
                SetStatus($"{currentReport.ToolName}：{currentReport.Changes.Count} 条结果。");
        }

        void ShowCustomView(VisualElement view, string status)
        {
            currentReport = null;
            resultScroll.Clear();
            if (view != null)
                resultScroll.Add(view);

            SetStatus(string.IsNullOrEmpty(status) ? "就绪。" : status);
            UpdateButtons();
        }

        void ShowCustomReportView(ArtToolReport report, VisualElement view, string status)
        {
            currentReport = report;
            resultScroll.Clear();
            if (view != null)
                resultScroll.Add(view);

            SetStatus(string.IsNullOrEmpty(status) ? "就绪。" : status);
            UpdateButtons();
        }

        void RunApply()
        {
            if (activeModule == null)
                return;

            if (currentReport == null)
            {
                SetStatus("请先预览变更。");
                return;
            }

            if (currentReport.WriteCount == 0)
            {
                SetStatus("当前结果没有写入变更。");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "应用 TA 美术工具变更",
                $"工具：{activeModule.PanelTitle}\n待写入变更：{currentReport.WriteCount}\n\n执行前请确认项目已纳入版本管理。",
                "应用",
                "取消");

            if (!confirm)
                return;

            try
            {
                activeModule.Apply(currentReport);
                SetStatus($"{activeModule.PanelTitle}：已应用 {currentReport.WriteCount} 条变更，请重新扫描刷新结果。");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                SetStatus(activeModule.PanelTitle + "：应用失败，请查看 Console。");
            }
        }

        void ExportCurrentReport()
        {
            if (currentReport == null)
            {
                SetStatus("没有可导出的结果。");
                return;
            }

            string defaultName = (currentReport.ToolName ?? "TAArtTools").Replace(" ", "_") + "_Report.csv";
            string path = EditorUtility.SaveFilePanel("导出 TA 美术工具结果", "", defaultName, "csv");
            if (string.IsNullOrEmpty(path))
                return;

            TaCsvUtility.ExportReport(path, currentReport);
            SetStatus("已导出结果：" + path);
            EditorUtility.RevealInFinder(path);
        }

        void RenderReport(ArtToolReport report)
        {
            resultScroll.Clear();
            if (report == null)
                return;

            foreach (ArtToolChange change in report.Changes)
            {
                if (change == null)
                    continue;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingTop = 3;
                row.style.paddingBottom = 3;
                row.style.borderBottomWidth = 1;
                row.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);

                var severity = new Label(SeverityText(change.Severity));
                severity.style.width = 70;
                severity.style.color = SeverityColor(change.Severity);
                row.Add(severity);

                var write = new Label(change.IsWriteOperation ? "写入" : "只读");
                write.style.width = 52;
                row.Add(write);

                var text = new Label($"{change.Title}    {change.Detail}    {change.AssetPath}");
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.flexGrow = 1;
                row.Add(text);

                if (change.Target != null)
                {
                    UnityEngine.Object target = change.Target;
                    row.Add(new Button(() => EditorGUIUtility.PingObject(target)) { text = "定位" });
                }

                resultScroll.Add(row);
            }

            foreach (string log in report.Logs)
                resultScroll.Add(new Label(log));
        }

        void SetStatus(string status)
        {
            if (statusLabel != null)
                statusLabel.text = status;
        }

        void RenderHelp(IArtToolModule module)
        {
            helpScroll.Clear();

            var title = new Label("使用说明");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 13;
            title.style.marginBottom = 6;
            helpScroll.Add(title);

            string help = module != null ? module.HelpText : "";
            if (string.IsNullOrEmpty(help))
                help = "暂无使用说明。";

            var body = new Label(help);
            body.style.whiteSpace = WhiteSpace.Normal;
            helpScroll.Add(body);
        }

        void UpdateButtons()
        {
        }

        static Color SeverityColor(ArtToolChangeSeverity severity)
        {
            switch (severity)
            {
                case ArtToolChangeSeverity.Warning:
                    return new Color(1f, 0.72f, 0.25f);
                case ArtToolChangeSeverity.Error:
                    return new Color(1f, 0.35f, 0.35f);
                default:
                    return new Color(0.78f, 0.78f, 0.78f);
            }
        }

        static string SeverityText(ArtToolChangeSeverity severity)
        {
            switch (severity)
            {
                case ArtToolChangeSeverity.Warning:
                    return "警告";
                case ArtToolChangeSeverity.Error:
                    return "错误";
                default:
                    return "信息";
            }
        }
    }
}
