using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    public sealed class TaMeshUsageModule : ArtToolModuleBase
    {
        bool includeInactive = true;

        public override string DisplayName => "Mesh Usage Analyzer";
        public override string PanelTitle => "网格使用统计";
        public override string Category => "Analyzer";
        public override string Description => "统计场景中 Mesh 的实例数量、索引数、三角面数和启用状态。";
        public override string HelpText =>
            "功能说明：\n" +
            "1. 扫描当前场景中的 MeshFilter 和 SkinnedMeshRenderer。\n" +
            "2. 按唯一 Mesh 合并统计实例数量、启用 Renderer 数量、顶点数、索引数和三角面数。\n" +
            "3. 可选择是否包含未激活对象。\n" +
            "4. 右下结果可定位到对应 Mesh 资源。";

        public override VisualElement CreateView(ArtToolContext context)
        {
            var root = new VisualElement();
            root.Add(Header(PanelTitle, Description));

            var toggle = new Toggle("包含未激活对象") { value = includeInactive };
            toggle.RegisterValueChangedCallback(evt => includeInactive = evt.newValue);
            root.Add(toggle);

            root.Add(ActionRow(
                ActionButton("扫描", () => context.RequestScan?.Invoke()),
                ActionButton("导出 CSV", () => context.ExportCurrentReport?.Invoke())));
            return root;
        }

        public override ArtToolReport Scan()
        {
            var report = ArtToolReport.Empty(PanelTitle);
            var dict = new Dictionary<Mesh, MeshInfo>();

            foreach (MeshFilter meshFilter in Object.FindObjectsOfType<MeshFilter>(includeInactive))
            {
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                Renderer renderer = meshFilter.GetComponent<Renderer>();
                AddMesh(dict, meshFilter.sharedMesh, renderer != null && renderer.enabled, "MeshRenderer");
            }

            foreach (SkinnedMeshRenderer renderer in Object.FindObjectsOfType<SkinnedMeshRenderer>(includeInactive))
            {
                if (renderer == null || renderer.sharedMesh == null)
                    continue;

                AddMesh(dict, renderer.sharedMesh, renderer.enabled, "SkinnedMeshRenderer");
            }

            foreach (MeshInfo info in dict.Values.OrderByDescending(m => m.TotalTriangles))
            {
                report.Changes.Add(ArtToolChange.Info(
                    info.Mesh.name,
                    $"实例: {info.InstanceCount}，启用: {info.EnabledRendererCount}，顶点: {info.VertexCount}，三角面: {info.TriangleCount}，GPU索引: {info.IndexCount}，Renderer类型: {string.Join("/", info.RendererTypes)}",
                    info.AssetPath,
                    info.Mesh));
            }

            report.AddLog($"唯一 Mesh 数量：{dict.Count}。");
            return report;
        }

        static void AddMesh(Dictionary<Mesh, MeshInfo> dict, Mesh mesh, bool rendererEnabled, string rendererType)
        {
            if (!dict.TryGetValue(mesh, out MeshInfo info))
            {
                int indexCount = 0;
                int triangleCount = 0;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    int subIndexCount = (int)mesh.GetIndexCount(i);
                    indexCount += subIndexCount;
                    triangleCount += subIndexCount / 3;
                }

                info = new MeshInfo
                {
                    Mesh = mesh,
                    AssetPath = AssetDatabase.GetAssetPath(mesh),
                    VertexCount = mesh.vertexCount,
                    IndexCount = indexCount,
                    TriangleCount = triangleCount
                };
                dict.Add(mesh, info);
            }

            info.InstanceCount++;
            if (rendererEnabled)
                info.EnabledRendererCount++;
            info.RendererTypes.Add(rendererType);
        }

        sealed class MeshInfo
        {
            public Mesh Mesh;
            public string AssetPath;
            public int VertexCount;
            public int IndexCount;
            public int TriangleCount;
            public int InstanceCount;
            public int EnabledRendererCount;
            public readonly HashSet<string> RendererTypes = new HashSet<string>();
            public int TotalTriangles => TriangleCount * InstanceCount;
        }
    }

    public sealed class TaTextureUsageModule : ArtToolModuleBase
    {
        enum SortType
        {
            RefCount,
            AndroidSize,
            IOSSize,
            Width,
            Height
        }

        bool includeInactive = true;
        bool descending = true;
        SortType sortType = SortType.RefCount;
        readonly List<TextureInfo> textureList = new List<TextureInfo>();

        public override string DisplayName => "Texture Usage Analyzer";
        public override string PanelTitle => "贴图使用统计";
        public override string Category => "Analyzer";
        public override string Description => "统计场景中贴图引用次数和移动端平台导入设置。";
        public override string HelpText =>
            "功能说明：\n" +
            "1. 扫描当前场景 Renderer 材质中引用的 Texture2D。\n" +
            "2. 统计引用次数、贴图宽高、Android / iOS 最大尺寸与实际格式。\n" +
            "3. 可选择是否包含未激活对象。\n" +
            "4. 支持按引用数、平台尺寸、宽度、高度排序。";

        public override VisualElement CreateView(ArtToolContext context)
        {
            var root = new VisualElement();
            root.Add(Header(PanelTitle, Description));

            var toggle = new Toggle("包含未激活对象") { value = includeInactive };
            toggle.RegisterValueChangedCallback(evt => includeInactive = evt.newValue);
            root.Add(toggle);

            var sortLabels = new List<string>(TaTextureUsageAnalyzerUtility.GetSortLabels());
            var sortField = new DropdownField("排序方式", sortLabels, (int)sortType);
            sortField.RegisterValueChangedCallback(evt => sortType = ParseSortType(evt.newValue));
            root.Add(sortField);

            var descendingToggle = new Toggle("降序") { value = descending };
            descendingToggle.RegisterValueChangedCallback(evt => descending = evt.newValue);
            root.Add(descendingToggle);

            root.Add(ActionRow(
                ActionButton("扫描", () => context.RequestScan?.Invoke()),
                ActionButton("应用排序", () => context.ShowReport?.Invoke(BuildReportFromCurrentList())),
                ActionButton("导出 CSV", () => context.ExportCurrentReport?.Invoke())));
            return root;
        }

        public override ArtToolReport Scan()
        {
            var report = ArtToolReport.Empty(PanelTitle);
            var dict = new Dictionary<Texture, TextureInfo>();

            foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>(includeInactive))
            {
                if (renderer == null)
                    continue;

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || material.shader == null)
                        continue;

                    foreach (Texture2D texture in TaMaterialScanUtility.EnumerateMaterialTextures(material))
                    {
                        if (!dict.TryGetValue(texture, out TextureInfo info))
                        {
                            info = TextureInfo.Create(texture);
                            dict.Add(texture, info);
                        }

                        info.ReferenceCount++;
                    }
                }
            }

            textureList.Clear();
            textureList.AddRange(dict.Values);
            SortTextureList();
            report = BuildReportFromCurrentList();

            return report;
        }

        ArtToolReport BuildReportFromCurrentList()
        {
            var report = ArtToolReport.Empty(PanelTitle);
            SortTextureList();

            foreach (TextureInfo info in textureList)
            {
                report.Changes.Add(ArtToolChange.Info(
                    info.Texture.name,
                    $"引用: {info.ReferenceCount}，Android尺寸: {info.AndroidMaxSize}，Android格式: {info.AndroidFormat}，iOS尺寸: {info.IosMaxSize}，iOS格式: {info.IosFormat}，贴图尺寸: {info.Width}x{info.Height}",
                    info.AssetPath,
                    info.Texture));
            }

            report.AddLog($"唯一贴图数量：{textureList.Count}。排序：{GetSortLabel(sortType)}。降序：{(descending ? "是" : "否")}。");
            return report;
        }

        void SortTextureList()
        {
            IEnumerable<TextureInfo> ordered;
            switch (sortType)
            {
                case SortType.AndroidSize:
                    ordered = descending ? textureList.OrderByDescending(t => t.AndroidMaxSize) : textureList.OrderBy(t => t.AndroidMaxSize);
                    break;
                case SortType.IOSSize:
                    ordered = descending ? textureList.OrderByDescending(t => t.IosMaxSize) : textureList.OrderBy(t => t.IosMaxSize);
                    break;
                case SortType.Width:
                    ordered = descending ? textureList.OrderByDescending(t => t.Width) : textureList.OrderBy(t => t.Width);
                    break;
                case SortType.Height:
                    ordered = descending ? textureList.OrderByDescending(t => t.Height) : textureList.OrderBy(t => t.Height);
                    break;
                default:
                    ordered = descending ? textureList.OrderByDescending(t => t.ReferenceCount) : textureList.OrderBy(t => t.ReferenceCount);
                    break;
            }

            List<TextureInfo> sorted = ordered.ToList();
            textureList.Clear();
            textureList.AddRange(sorted);
        }

        static SortType ParseSortType(string label)
        {
            switch (label)
            {
                case "Android尺寸":
                    return SortType.AndroidSize;
                case "iOS尺寸":
                    return SortType.IOSSize;
                case "宽度":
                    return SortType.Width;
                case "高度":
                    return SortType.Height;
                case "引用数":
                    return SortType.RefCount;
            }
            return SortType.RefCount;
        }

        static string GetSortLabel(SortType type)
        {
            switch (type)
            {
                case SortType.AndroidSize:
                    return "Android尺寸";
                case SortType.IOSSize:
                    return "iOS尺寸";
                case SortType.Width:
                    return "宽度";
                case SortType.Height:
                    return "高度";
                default:
                    return "引用数";
            }
        }

        sealed class TextureInfo
        {
            public Texture Texture;
            public string AssetPath;
            public int ReferenceCount;
            public int Width;
            public int Height;
            public int AndroidMaxSize;
            public string AndroidFormat;
            public int IosMaxSize;
            public string IosFormat;

            public static TextureInfo Create(Texture texture)
            {
                string path = AssetDatabase.GetAssetPath(texture);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                TextureImporterPlatformSettings android = importer != null ? importer.GetPlatformTextureSettings("Android") : default(TextureImporterPlatformSettings);
                TextureImporterPlatformSettings ios = importer != null ? importer.GetPlatformTextureSettings("iPhone") : default(TextureImporterPlatformSettings);

                return new TextureInfo
                {
                    Texture = texture,
                    AssetPath = path,
                    Width = texture.width,
                    Height = texture.height,
                    AndroidMaxSize = importer != null ? android.maxTextureSize : 0,
                    AndroidFormat = importer != null ? TaTexturePlatformUtility.GetActualFormat(importer, "Android").ToString() : "N/A",
                    IosMaxSize = importer != null ? ios.maxTextureSize : 0,
                    IosFormat = importer != null ? TaTexturePlatformUtility.GetActualFormat(importer, "iPhone").ToString() : "N/A"
                };
            }
        }
    }
}
