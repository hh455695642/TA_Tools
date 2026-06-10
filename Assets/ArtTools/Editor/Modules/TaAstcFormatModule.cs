using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    public sealed class TaAstcFormatModule : ArtToolModuleBase
    {
        static readonly List<string> TargetFormatLabels = new List<string>
        {
            TextureImporterFormat.ASTC_5x5.ToString(),
            TextureImporterFormat.ASTC_6x6.ToString(),
            TextureImporterFormat.ASTC_8x8.ToString()
        };

        string targetFolder = "Assets";
        bool processAndroid = true;
        bool processIos = true;
        TextureImporterFormat targetFormat = TextureImporterFormat.ASTC_6x6;

        public override string DisplayName => "ASTC Texture Format Batch";
        public override string PanelTitle => "ASTC 贴图格式批处理";
        public override string Category => "Texture";
        public override string Description => "批量设置 Android / iOS 贴图平台压缩格式。";
        public override string HelpText =>
            "功能说明：\n" +
            "1. 选择 Assets 目录下的目标路径，递归扫描该目录内所有贴图。\n" +
            "2. 可勾选 Android、iOS 平台，并选择目标 ASTC 格式。\n" +
            "3. 只修改实际格式与目标格式不同的平台项，保留原 max size。\n" +
            "4. 处理完成后，右下会列出每张贴图、平台、原格式和目标格式。\n" +
            "5. 执行过程可在进度条中取消，已完成的修改会保留。";

        public override VisualElement CreateView(ArtToolContext context)
        {
            var root = new VisualElement();
            root.Add(Header(PanelTitle, Description));

            var pathRow = new VisualElement();
            pathRow.style.flexDirection = FlexDirection.Row;
            pathRow.style.marginBottom = 4;

            var folderField = new TextField("目标路径") { value = targetFolder };
            folderField.style.flexGrow = 1;
            folderField.RegisterValueChangedCallback(evt => targetFolder = TaAssetSearchUtility.NormalizeAssetPath(evt.newValue));
            pathRow.Add(folderField);

            pathRow.Add(ActionButton("选择...", () =>
            {
                string selectedPath = EditorUtility.OpenFolderPanel("选择贴图文件夹", Application.dataPath, "");
                if (string.IsNullOrEmpty(selectedPath))
                    return;

                string assetPath = TaAssetSearchUtility.NormalizeAssetPath(selectedPath);
                if (!AssetDatabase.IsValidFolder(assetPath))
                {
                    EditorUtility.DisplayDialog("路径无效", "请选择当前项目 Assets 目录下的文件夹。", "确定");
                    return;
                }

                targetFolder = assetPath;
                folderField.SetValueWithoutNotify(targetFolder);
            }));

            pathRow.Add(ActionButton("使用选中", () =>
            {
                string selectedFolder = TaAssetSearchUtility.ResolveCurrentProjectSelectionFolderPath();
                if (string.IsNullOrEmpty(selectedFolder))
                {
                    context.Log?.Invoke("请先在 Project 中选择文件夹或资源。");
                    return;
                }

                targetFolder = selectedFolder;
                folderField.SetValueWithoutNotify(targetFolder);
            }));

            root.Add(pathRow);

            var platformRow = new VisualElement();
            platformRow.style.flexDirection = FlexDirection.Row;

            var androidToggle = new Toggle("Android") { value = processAndroid };
            androidToggle.RegisterValueChangedCallback(evt => processAndroid = evt.newValue);
            androidToggle.style.marginRight = 14;
            platformRow.Add(androidToggle);

            var iosToggle = new Toggle("iOS") { value = processIos };
            iosToggle.RegisterValueChangedCallback(evt => processIos = evt.newValue);
            platformRow.Add(iosToggle);
            root.Add(platformRow);

            int defaultFormatIndex = Mathf.Max(0, TargetFormatLabels.IndexOf(targetFormat.ToString()));
            var formatField = new DropdownField("目标格式", TargetFormatLabels, defaultFormatIndex);
            formatField.RegisterValueChangedCallback(evt => targetFormat = ParseAstcFormat(evt.newValue));
            root.Add(formatField);

            root.Add(ActionRow(
                ActionButton("开始处理", () => RunBatch(context)),
                ActionButton("导出 CSV", () => context.ExportCurrentReport?.Invoke())));

            return root;
        }

        public override ArtToolReport Scan()
        {
            var report = ArtToolReport.Empty(PanelTitle);
            report.Changes.Add(ArtToolChange.Info("直接执行工具", "请使用本模块中的“开始处理”执行贴图格式批处理。"));
            return report;
        }

        void RunBatch(ArtToolContext context)
        {
            ArtToolReport report = ExecuteBatch();
            context.ShowReport?.Invoke(report);
        }

        ArtToolReport ExecuteBatch()
        {
            var report = ArtToolReport.Empty(PanelTitle);
            targetFolder = TaAssetSearchUtility.NormalizeAssetPath(targetFolder);

            if (string.IsNullOrEmpty(targetFolder) || !AssetDatabase.IsValidFolder(targetFolder))
            {
                report.Changes.Add(ArtToolChange.Error("目标路径无效", targetFolder));
                return report;
            }

            if (!processAndroid && !processIos)
            {
                report.Changes.Add(ArtToolChange.Error("未选择平台", "请至少勾选 Android 或 iOS。"));
                return report;
            }

            if (!TaTexturePlatformUtility.IsAstcBatchTargetFormat(targetFormat))
            {
                report.Changes.Add(ArtToolChange.Error("不支持的目标格式", targetFormat.ToString()));
                return report;
            }

            List<string> texturePaths = TaAssetSearchUtility.FindAssetPaths("t:Texture", targetFolder);
            int scannedTextures = 0;
            int changedPlatformEntries = 0;
            int skippedPlatformEntries = 0;
            int errorCount = 0;
            bool canceled = false;

            try
            {
                for (int i = 0; i < texturePaths.Count; i++)
                {
                    string path = texturePaths[i];
                    if (EditorUtility.DisplayCancelableProgressBar(PanelTitle, path, texturePaths.Count == 0 ? 1f : (float)i / texturePaths.Count))
                    {
                        canceled = true;
                        break;
                    }

                    scannedTextures++;
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (importer == null)
                    {
                        errorCount++;
                        report.Changes.Add(ArtToolChange.Error("未找到 TextureImporter", path, path, texture));
                        continue;
                    }

                    bool textureChanged = false;
                    if (processAndroid)
                        ProcessPlatform(report, importer, path, texture, "Android", "Android", ref textureChanged, ref changedPlatformEntries, ref skippedPlatformEntries);
                    if (processIos)
                        ProcessPlatform(report, importer, path, texture, "iPhone", "iOS", ref textureChanged, ref changedPlatformEntries, ref skippedPlatformEntries);

                    if (textureChanged)
                        importer.SaveAndReimport();
                }
            }
            catch (Exception e)
            {
                errorCount++;
                report.Changes.Add(ArtToolChange.Error("批处理失败", e.ToString()));
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (changedPlatformEntries > 0)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }

            if (canceled)
                report.Changes.Add(ArtToolChange.Warning("已取消", "处理被用户取消，已完成的修改会保留。"));

            if (changedPlatformEntries == 0 && errorCount == 0)
                report.Changes.Add(ArtToolChange.Info("没有格式变更", "所有选中平台项都已经是目标格式。"));

            report.AddLog($"扫描贴图：{scannedTextures}/{texturePaths.Count}。");
            report.AddLog($"修改平台项：{changedPlatformEntries}。跳过平台项：{skippedPlatformEntries}。错误：{errorCount}。取消：{canceled}。");
            return report;
        }

        void ProcessPlatform(
            ArtToolReport report,
            TextureImporter importer,
            string path,
            Texture2D texture,
            string platformName,
            string displayName,
            ref bool textureChanged,
            ref int changedPlatformEntries,
            ref int skippedPlatformEntries)
        {
            TextureImporterFormat oldFormat = TaTexturePlatformUtility.GetActualFormat(importer, platformName);
            if (!TaTexturePlatformUtility.ShouldChangeFormat(oldFormat, targetFormat))
            {
                skippedPlatformEntries++;
                return;
            }

            TextureImporterFormat recordedOldFormat;
            TextureImporterFormat newFormat;
            bool changed = TaTexturePlatformUtility.SetPlatformFormatPreserveSize(importer, platformName, targetFormat, out recordedOldFormat, out newFormat);
            if (!changed)
            {
                skippedPlatformEntries++;
                return;
            }

            textureChanged = true;
            changedPlatformEntries++;
            report.Changes.Add(ArtToolChange.Info(
                "已修改",
                $"{displayName}: {recordedOldFormat} -> {newFormat}",
                path,
                texture));
        }

        static TextureImporterFormat ParseAstcFormat(string value)
        {
            TextureImporterFormat parsed;
            if (Enum.TryParse(value, out parsed) && TaTexturePlatformUtility.IsAstcBatchTargetFormat(parsed))
                return parsed;

            return TextureImporterFormat.ASTC_6x6;
        }
    }
}
