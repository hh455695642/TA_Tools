using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    public enum ArtToolChangeSeverity
    {
        Info,
        Warning,
        Error
    }

    public sealed class ArtToolChange
    {
        public string Title;
        public string Detail;
        public string AssetPath;
        public UnityEngine.Object Target;
        public bool IsWriteOperation;
        public ArtToolChangeSeverity Severity;
        public Action Apply;

        public string SeverityText => Severity.ToString();

        public static ArtToolChange Info(string title, string detail, string assetPath = "", UnityEngine.Object target = null)
        {
            return new ArtToolChange
            {
                Title = title,
                Detail = detail,
                AssetPath = assetPath,
                Target = target,
                Severity = ArtToolChangeSeverity.Info,
                IsWriteOperation = false
            };
        }

        public static ArtToolChange Warning(string title, string detail, string assetPath = "", UnityEngine.Object target = null)
        {
            return new ArtToolChange
            {
                Title = title,
                Detail = detail,
                AssetPath = assetPath,
                Target = target,
                Severity = ArtToolChangeSeverity.Warning,
                IsWriteOperation = false
            };
        }

        public static ArtToolChange Write(string title, string detail, Action apply, string assetPath = "", UnityEngine.Object target = null)
        {
            return new ArtToolChange
            {
                Title = title,
                Detail = detail,
                AssetPath = assetPath,
                Target = target,
                Severity = ArtToolChangeSeverity.Warning,
                IsWriteOperation = true,
                Apply = apply
            };
        }

        public static ArtToolChange Error(string title, string detail, string assetPath = "", UnityEngine.Object target = null)
        {
            return new ArtToolChange
            {
                Title = title,
                Detail = detail,
                AssetPath = assetPath,
                Target = target,
                Severity = ArtToolChangeSeverity.Error,
                IsWriteOperation = false
            };
        }
    }

    public sealed class ArtToolReport
    {
        public string ToolName;
        public readonly List<ArtToolChange> Changes = new List<ArtToolChange>();
        public readonly List<string> Logs = new List<string>();

        public int WriteCount
        {
            get
            {
                int count = 0;
                foreach (ArtToolChange change in Changes)
                {
                    if (change != null && change.IsWriteOperation)
                        count++;
                }
                return count;
            }
        }

        public bool HasErrors
        {
            get
            {
                foreach (ArtToolChange change in Changes)
                {
                    if (change != null && change.Severity == ArtToolChangeSeverity.Error)
                        return true;
                }
                return false;
            }
        }

        public static ArtToolReport Empty(string toolName)
        {
            return new ArtToolReport { ToolName = toolName };
        }

        public void AddLog(string message)
        {
            if (!string.IsNullOrEmpty(message))
                Logs.Add(message);
        }
    }

    public sealed class ArtToolContext
    {
        public Action RequestScan;
        public Action RequestApply;
        public Action<ArtToolReport> ShowReport;
        public Action<VisualElement, string> ShowCustomView;
        public Action ExportCurrentReport;
        public Action<string> Log;
        public Func<ArtToolReport> CurrentReport;
    }

    public interface IArtToolModule
    {
        string DisplayName { get; }
        string PanelTitle { get; }
        string Category { get; }
        string Description { get; }
        string HelpText { get; }
        VisualElement CreateView(ArtToolContext context);
        ArtToolReport Scan();
        void Apply(ArtToolReport report);
    }

    public abstract class ArtToolModuleBase : IArtToolModule
    {
        public abstract string DisplayName { get; }
        public virtual string PanelTitle => DisplayName;
        public virtual string Category => "Tools";
        public virtual string Description => "";
        public virtual string HelpText => "";

        public abstract VisualElement CreateView(ArtToolContext context);
        public abstract ArtToolReport Scan();

        public virtual void Apply(ArtToolReport report)
        {
            if (report == null)
                return;

            try
            {
                for (int i = 0; i < report.Changes.Count; i++)
                {
                    ArtToolChange change = report.Changes[i];
                    if (change == null || !change.IsWriteOperation || change.Apply == null)
                        continue;

                    EditorUtility.DisplayProgressBar(PanelTitle, change.Title, report.WriteCount <= 1 ? 1f : (float)i / report.Changes.Count);
                    change.Apply.Invoke();
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        protected static Label Header(string title, string description)
        {
            var label = new Label(string.IsNullOrEmpty(description) ? title : title + "\n" + description);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 8;
            return label;
        }

        protected static Button ActionButton(string text, Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.style.marginTop = 4;
            button.style.marginBottom = 4;
            return button;
        }

        protected static VisualElement ActionRow(params Button[] buttons)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 6;
            row.style.marginBottom = 6;

            if (buttons == null)
                return row;

            foreach (Button button in buttons)
            {
                if (button == null)
                    continue;

                button.style.marginRight = 6;
                row.Add(button);
            }

            return row;
        }
    }
}
