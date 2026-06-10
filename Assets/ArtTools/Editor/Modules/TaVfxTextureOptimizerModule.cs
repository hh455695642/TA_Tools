using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    public sealed class TaVfxTextureOptimizerModule : ArtToolModuleBase
    {
        readonly List<Texture2D> textures = new List<Texture2D>();
        readonly Dictionary<Texture2D, Vector2Int> originalResolutions = new Dictionary<Texture2D, Vector2Int>();
        readonly Dictionary<string, int> csvRecords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<Texture2D, List<Material>> materialIndex = new Dictionary<Texture2D, List<Material>>();

        GameObject vfxPrefab;

        public override string DisplayName => "VFX Texture Optimizer";
        public override string PanelTitle => "特效贴图优化";
        public override string Category => "Texture";
        public override string Description => "从特效 Prefab 收集贴图，快速设置贴图最大尺寸并记录优化结果。";
        public override string HelpText =>
            "功能说明：\n" +
            "1. 指定一个特效 Prefab，点击“收集贴图”。\n" +
            "2. 工具会递归扫描 Renderer / ParticleSystemRenderer 材质中引用的贴图。\n" +
            "3. 右下会显示贴图、原始分辨率、当前设置、历史记录和引用材质。\n" +
            "4. 点击 32 / 64 / 128 / 256 / 512 会同时设置默认、Android、iOS 尺寸，并使用 ASTC_6x6。\n" +
            "5. 优化记录写入 TAArtTools/Data/VFX_Texture_Optimize_Log.csv。";

        public override VisualElement CreateView(ArtToolContext context)
        {
            LoadCsvRecords();
            RebuildMaterialIndex();

            var root = new VisualElement();
            root.style.flexGrow = 1;
            root.Add(Header(PanelTitle, Description));

            var prefabField = new ObjectField("特效 Prefab")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = false,
                value = vfxPrefab
            };
            prefabField.RegisterValueChangedCallback(evt => vfxPrefab = evt.newValue as GameObject);
            root.Add(prefabField);

            root.Add(ActionRow(
                ActionButton("收集贴图", () => CollectAndRender(context))));
            return root;
        }

        public override ArtToolReport Scan()
        {
            return BuildCurrentListReport();
        }

        void CollectAndRender(ArtToolContext context)
        {
            textures.Clear();
            originalResolutions.Clear();
            LoadCsvRecords();
            RebuildMaterialIndex();

            if (vfxPrefab == null)
            {
                context.ShowCustomView?.Invoke(BuildTextureListView(context), "请先指定特效 Prefab。");
                return;
            }

            var collected = new HashSet<Texture2D>();
            CollectTexturesFromObject(vfxPrefab, collected);
            textures.AddRange(collected);
            textures.Sort((a, b) => string.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b), StringComparison.OrdinalIgnoreCase));

            context.ShowCustomView?.Invoke(BuildTextureListView(context), $"已收集贴图：{textures.Count} 张");
        }

        void CollectTexturesFromObject(GameObject root, HashSet<Texture2D> result)
        {
            if (root == null)
                return;

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                foreach (Material material in renderer.sharedMaterials)
                {
                    foreach (Texture2D texture in TaMaterialScanUtility.EnumerateMaterialTextures(material))
                    {
                        if (texture == null || !result.Add(texture))
                            continue;

                        string path = AssetDatabase.GetAssetPath(texture);
                        Texture2D textureAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        if (textureAsset != null && !originalResolutions.ContainsKey(texture))
                            originalResolutions.Add(texture, new Vector2Int(textureAsset.width, textureAsset.height));
                    }
                }
            }
        }

        VisualElement BuildTextureListView(ArtToolContext context)
        {
            var root = new VisualElement();
            root.style.flexGrow = 1;

            if (textures.Count == 0)
            {
                root.Add(new Label("请指定特效 Prefab 后点击“收集贴图”。"));
                return root;
            }

            foreach (Texture2D texture in textures)
                root.Add(CreateTextureRow(texture, context));

            return root;
        }

        VisualElement CreateTextureRow(Texture2D texture, ArtToolContext context)
        {
            var box = new VisualElement();
            box.style.borderTopWidth = 1;
            box.style.borderBottomWidth = 1;
            box.style.borderLeftWidth = 1;
            box.style.borderRightWidth = 1;
            box.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f);
            box.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);
            box.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f);
            box.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            box.style.marginBottom = 6;
            box.style.paddingLeft = 6;
            box.style.paddingRight = 6;
            box.style.paddingTop = 5;
            box.style.paddingBottom = 5;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            box.Add(row);

            var textureButton = new Button(() => EditorGUIUtility.PingObject(texture)) { text = texture != null ? texture.name : "贴图丢失" };
            textureButton.style.width = 180;
            textureButton.style.marginRight = 6;
            row.Add(textureButton);

            row.Add(FixedLabel(BuildResolutionText(texture), 120));
            row.Add(FixedLabel(BuildCurrentSizeText(texture), 110));
            row.Add(FixedLabel(BuildCsvText(texture), 80));

            foreach (int size in TaVfxTextureOptimizerUtility.GetSizeOptions())
            {
                int capturedSize = size;
                var sizeButton = new Button(() => SetSize(texture, capturedSize, context)) { text = capturedSize.ToString() };
                sizeButton.style.width = 48;
                sizeButton.style.marginRight = 4;
                row.Add(sizeButton);
            }

            AddMaterialRows(box, texture);
            return box;
        }

        static Label FixedLabel(string text, float width)
        {
            var label = new Label(text);
            label.style.width = width;
            label.style.marginRight = 6;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        string BuildResolutionText(Texture2D texture)
        {
            if (texture != null && originalResolutions.TryGetValue(texture, out Vector2Int resolution))
                return $"原始: {resolution.x}x{resolution.y}";

            return "原始: N/A";
        }

        static string BuildCurrentSizeText(Texture2D texture)
        {
            TextureImporter importer = GetImporter(texture);
            return importer != null ? $"当前: {importer.maxTextureSize}" : "当前: N/A";
        }

        string BuildCsvText(Texture2D texture)
        {
            string key = BuildCsvKey(texture);
            if (!string.IsNullOrEmpty(key) && csvRecords.TryGetValue(key, out int size))
                return $"记录: {size}";

            return "记录: -";
        }

        string BuildTextureSummary(Texture2D texture)
        {
            return $"{BuildResolutionText(texture)}, {BuildCurrentSizeText(texture)}, {BuildCsvText(texture)}";
        }

        void AddMaterialRows(VisualElement box, Texture2D texture)
        {
            if (texture == null || !materialIndex.TryGetValue(texture, out List<Material> materials) || materials.Count == 0)
                return;

            foreach (Material material in materials)
            {
                if (material == null)
                    continue;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginTop = 3;
                row.style.marginLeft = 20;

                var materialButton = new Button(() => EditorGUIUtility.PingObject(material)) { text = material.name };
                materialButton.style.width = 150;
                materialButton.style.marginRight = 6;
                row.Add(materialButton);

                var pathLabel = new Label(AssetDatabase.GetAssetPath(material));
                pathLabel.style.flexGrow = 1;
                pathLabel.style.whiteSpace = WhiteSpace.Normal;
                row.Add(pathLabel);
                box.Add(row);
            }
        }

        void SetSize(Texture2D texture, int size, ArtToolContext context)
        {
            if (texture == null)
            {
                context.ShowCustomView?.Invoke(BuildTextureListView(context), "贴图引用丢失，无法设置尺寸。");
                return;
            }

            string path = AssetDatabase.GetAssetPath(texture);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                context.ShowCustomView?.Invoke(BuildTextureListView(context), $"未找到 TextureImporter：{path}");
                return;
            }

            int oldDefaultSize = importer.maxTextureSize;
            TextureImporterPlatformSettings oldAndroid = importer.GetPlatformTextureSettings("Android");
            TextureImporterPlatformSettings oldIos = importer.GetPlatformTextureSettings("iPhone");

            importer.maxTextureSize = size;
            SetPlatform(importer, "Android", size);
            SetPlatform(importer, "iPhone", size);
            importer.SaveAndReimport();

            string key = BuildCsvKey(path, texture.name);
            csvRecords[key] = size;
            WriteCsvRecords();

            string status = $"{texture.name}：默认 {oldDefaultSize} -> {size}；Android {FormatPlatform(oldAndroid)} -> {size}/ASTC_6x6；iOS {FormatPlatform(oldIos)} -> {size}/ASTC_6x6。记录已更新。";
            context.ShowCustomView?.Invoke(BuildTextureListView(context), status);
        }

        static void SetPlatform(TextureImporter importer, string platform, int size)
        {
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
            settings.overridden = true;
            settings.maxTextureSize = size;
            settings.format = TextureImporterFormat.ASTC_6x6;
            importer.SetPlatformTextureSettings(settings);
        }

        static string FormatPlatform(TextureImporterPlatformSettings settings)
        {
            if (!settings.overridden)
                return "自动";

            return $"{settings.maxTextureSize}/{settings.format}";
        }

        void RebuildMaterialIndex()
        {
            materialIndex.Clear();

            string[] guids = AssetDatabase.FindAssets("t:Material");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                    continue;

                foreach (Texture2D texture in TaMaterialScanUtility.EnumerateMaterialTextures(material))
                {
                    if (texture == null)
                        continue;

                    if (!materialIndex.TryGetValue(texture, out List<Material> materials))
                    {
                        materials = new List<Material>();
                        materialIndex.Add(texture, materials);
                    }

                    if (!materials.Contains(material))
                        materials.Add(material);
                }
            }
        }

        void LoadCsvRecords()
        {
            csvRecords.Clear();
            if (!File.Exists(TaVfxTextureOptimizerUtility.CsvPath))
                return;

            string[] lines = File.ReadAllLines(TaVfxTextureOptimizerUtility.CsvPath);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] columns = lines[i].Split(',');
                if (columns.Length != 3)
                    continue;

                int size;
                if (!int.TryParse(columns[2], out size))
                    continue;

                csvRecords[columns[1] + "/" + columns[0]] = size;
            }
        }

        void WriteCsvRecords()
        {
            string directory = Path.GetDirectoryName(TaVfxTextureOptimizerUtility.CsvPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            using (var writer = new StreamWriter(TaVfxTextureOptimizerUtility.CsvPath, false))
            {
                writer.WriteLine("Name,Path,Size");
                foreach (KeyValuePair<string, int> record in csvRecords)
                {
                    int lastSlash = record.Key.LastIndexOf('/');
                    if (lastSlash < 0)
                        continue;

                    string dir = record.Key.Substring(0, lastSlash);
                    string name = record.Key.Substring(lastSlash + 1);
                    writer.WriteLine($"{name},{dir},{record.Value}");
                }
            }

            AssetDatabase.Refresh();
        }

        ArtToolReport BuildCurrentListReport()
        {
            var report = ArtToolReport.Empty(PanelTitle);
            foreach (Texture2D texture in textures)
            {
                report.Changes.Add(ArtToolChange.Info(
                    texture != null ? texture.name : "贴图丢失",
                    BuildTextureSummary(texture),
                    AssetDatabase.GetAssetPath(texture),
                    texture));
            }

            report.AddLog($"已收集特效贴图：{textures.Count} 张。");
            return report;
        }

        string BuildCsvKey(Texture2D texture)
        {
            if (texture == null)
                return string.Empty;

            return BuildCsvKey(AssetDatabase.GetAssetPath(texture), texture.name);
        }

        static string BuildCsvKey(string path, string textureName)
        {
            return TaVfxTextureOptimizerUtility.MakeCsvRecordKey(path, textureName);
        }

        static TextureImporter GetImporter(Texture2D texture)
        {
            if (texture == null)
                return null;

            return AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
        }
    }
}
