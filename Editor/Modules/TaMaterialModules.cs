using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    public sealed class TaShaderUsageModule : ArtToolModuleBase
    {
        const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        const string UrpSimpleLitShaderName = "Universal Render Pipeline/Simple Lit";
        const string UrpPackageDefaultLitMaterialPath = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat";

        sealed class ScanObjectResult
        {
            public UnityEngine.Object SourceAsset;
            public string SourcePath;
            public bool Foldout = true;
            public readonly List<MaterialSlotInfo> Slots = new List<MaterialSlotInfo>();
        }

        sealed class MaterialSlotInfo
        {
            public bool Selected;
            public string RendererName;
            public int SlotIndex;
            public Material Material;
            public string MaterialPath;
            public Shader Shader;
            public bool Editable;
            public bool IsMissingMaterial;
            public bool IsPackageDefaultLitMaterial;
            public string Note;
        }

        readonly List<UnityEngine.Object> targets = new List<UnityEngine.Object>();
        readonly List<ScanObjectResult> results = new List<ScanObjectResult>();
        Shader replaceShader;
        Material replaceDefaultLitMaterial;

        public override string DisplayName => "Shader Usage Analyzer";
        public override string PanelTitle => "Shader 使用统计";
        public override string Category => "Material";
        public override string Description => "扫描 Prefab / Material / 文件夹中的材质槽，统计并替换 Shader 使用。";
        public override string HelpText =>
            "功能说明：\n" +
            "1. 支持拖入 Project 中的 Prefab / Material / 文件夹。\n" +
            "2. 支持拖入 Hierarchy 中的 Prefab 实例或子节点，会自动解析到本地 .prefab 资源。\n" +
            "3. 如果场景对象解析到的是 .fbx，会跳过，避免误处理模型源文件。\n" +
            "4. 项目内可编辑 .mat 可以勾选后批量替换 Shader。\n" +
            "5. 使用 URP 包内默认 Lit.mat 的材质槽不能直接改 Shader，可以一键替换为指定材质球。\n" +
            "6. 空材质槽会列出来，但不会自动处理。";

        public override VisualElement CreateView(ArtToolContext context)
        {
            if (replaceShader == null)
                replaceShader = Shader.Find(UrpSimpleLitShaderName) ?? Shader.Find(UrpLitShaderName);

            var root = new VisualElement();
            root.Add(Header(PanelTitle, Description));

            var scanTargetsLabel = new Label("扫描对象 (0)");
            scanTargetsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            scanTargetsLabel.style.marginTop = 6;
            root.Add(scanTargetsLabel);

            var targetList = new VisualElement();
            Action refreshTargetList = null;
            refreshTargetList = () =>
            {
                scanTargetsLabel.text = $"扫描对象 ({targets.Count})";
                targetList.Clear();
                if (targets.Count == 0)
                {
                    var empty = new Label("没有固定扫描对象。列表为空时，“扫描”会使用当前选择。");
                    empty.style.whiteSpace = WhiteSpace.Normal;
                    targetList.Add(empty);
                    return;
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    int index = i;
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
                    var field = new ObjectField { objectType = typeof(UnityEngine.Object), allowSceneObjects = true, value = targets[index] };
                    field.style.flexGrow = 1;
                    field.RegisterValueChangedCallback(evt =>
                    {
                        UnityEngine.Object resolved = ResolveToProjectAsset(evt.newValue);
                        if (resolved != null)
                            targets[index] = resolved;
                        refreshTargetList();
                    });
                    row.Add(field);
                    row.Add(new Button(() =>
                    {
                        targets.RemoveAt(index);
                        refreshTargetList();
                    }) { text = "移除" });
                    targetList.Add(row);
                }
            };

            root.Add(ActionRow(
                ActionButton("使用当前选择", () =>
                {
                    targets.Clear();
                    foreach (UnityEngine.Object selected in Selection.objects)
                        AddTarget(selected);
                    refreshTargetList();
                    context.Log?.Invoke($"Shader 扫描对象：{targets.Count} 个");
                }),
                ActionButton("清空对象", () =>
                {
                    targets.Clear();
                    refreshTargetList();
                })));

            var targetScroll = new ScrollView();
            targetScroll.style.height = 112;
            targetScroll.style.minHeight = 80;
            targetScroll.style.marginBottom = 6;
            targetScroll.style.paddingLeft = 4;
            targetScroll.style.paddingRight = 4;
            targetScroll.style.paddingTop = 3;
            targetScroll.style.paddingBottom = 3;
            targetScroll.style.borderLeftWidth = 1;
            targetScroll.style.borderRightWidth = 1;
            targetScroll.style.borderTopWidth = 1;
            targetScroll.style.borderBottomWidth = 1;
            targetScroll.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f);
            targetScroll.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            targetScroll.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f);
            targetScroll.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);
            targetScroll.Add(targetList);
            root.Add(targetScroll);

            root.Add(CreateDragDropArea("拖入 Prefab / Material / 文件夹 / Hierarchy Prefab 实例", objects =>
            {
                foreach (UnityEngine.Object obj in objects)
                    AddTarget(obj);

                refreshTargetList();
                context.Log?.Invoke($"Shader 扫描对象：{targets.Count} 个");
            }));
            refreshTargetList();

            root.Add(ActionRow(
                ActionButton("扫描", () => AnalyzeShaderUsage(context)),
                ActionButton("自动勾选 Lit Shader 材质", () =>
                {
                    if (results.Count == 0)
                        ScanUsageResults();
                    SelectEditableURPLitShaderMaterials();
                    ShowUsageResults(context, "已自动勾选可编辑的 URP Lit Shader 材质。");
                }),
                ActionButton("取消材质勾选", () =>
                {
                    ClearMaterialSelection();
                    ShowUsageResults(context, "已取消材质勾选。");
                })));

            var shaderField = new ObjectField("替换目标 Shader") { objectType = typeof(Shader), value = replaceShader };
            shaderField.RegisterValueChangedCallback(evt => replaceShader = evt.newValue as Shader);
            root.Add(shaderField);

            var materialField = new ObjectField("默认 Lit 替换材质") { objectType = typeof(Material), value = replaceDefaultLitMaterial };
            materialField.RegisterValueChangedCallback(evt => replaceDefaultLitMaterial = evt.newValue as Material);
            root.Add(materialField);

            root.Add(ActionRow(
                ActionButton("替换勾选材质 Shader", () => ReplaceSelectedMaterialShaders(context)),
                ActionButton("替换默认 Lit 材质引用", () => ReplacePackageDefaultLitMaterialReferences(context))));

            root.Add(new HelpBox(
                "文件夹会扫描 Prefab 和 Material 资源。Hierarchy 中的 Prefab 实例会解析到对应 Prefab 资产。可编辑材质槽可勾选后替换；URP 包内 Lit.mat 引用会在 Prefab 资产上替换。",
                HelpBoxMessageType.Info));
            return root;
        }

        static VisualElement CreateDragDropArea(string text, Action<UnityEngine.Object[]> onDrop)
        {
            var area = new Label(text);
            area.style.height = 42;
            area.style.marginTop = 2;
            area.style.marginBottom = 8;
            area.style.unityTextAlign = TextAnchor.MiddleCenter;
            area.style.whiteSpace = WhiteSpace.Normal;
            area.style.borderLeftWidth = 1;
            area.style.borderRightWidth = 1;
            area.style.borderTopWidth = 1;
            area.style.borderBottomWidth = 1;
            area.style.borderLeftColor = new Color(0.36f, 0.36f, 0.36f);
            area.style.borderRightColor = new Color(0.36f, 0.36f, 0.36f);
            area.style.borderTopColor = new Color(0.36f, 0.36f, 0.36f);
            area.style.borderBottomColor = new Color(0.36f, 0.36f, 0.36f);
            area.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            area.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            area.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                onDrop?.Invoke(DragAndDrop.objectReferences);
                evt.StopPropagation();
            });
            return area;
        }

        public override ArtToolReport Scan()
        {
            ScanUsageResults();

            var report = ArtToolReport.Empty(PanelTitle);
            int totalSlots = results.Sum(r => r.Slots.Count);
            int editableCount = results.Sum(r => r.Slots.Count(s => s.Editable));
            int packageDefaultLitCount = results.Sum(r => r.Slots.Count(s => s.IsPackageDefaultLitMaterial));
            int missingCount = results.Sum(r => r.Slots.Count(s => s.IsMissingMaterial));

            report.Changes.Add(ArtToolChange.Info(
                "Shader 使用统计",
                $"对象: {results.Count}，材质槽: {totalSlots}，可编辑: {editableCount}，默认 Lit 材质槽: {packageDefaultLitCount}，空材质槽: {missingCount}"));

            foreach (ScanObjectResult result in results)
            {
                foreach (MaterialSlotInfo slot in result.Slots)
                {
                    report.Changes.Add(ArtToolChange.Info(
                        GetSlotStatus(slot),
                        $"{result.SourcePath} | {FormatRendererSlot(slot)} | {GetObjectName(slot.Material)} | {GetObjectName(slot.Shader)}",
                        slot.MaterialPath,
                        slot.Material != null ? (UnityEngine.Object)slot.Material : result.SourceAsset));
                }
            }

            return report;
        }

        void AnalyzeShaderUsage(ArtToolContext context)
        {
            ScanUsageResults();
            ShowUsageResults(context, $"Shader 使用统计：{results.Count} 个对象，{results.Sum(r => r.Slots.Count)} 个材质槽。");
        }

        void ShowUsageResults(ArtToolContext context, string status)
        {
            context.ShowCustomView?.Invoke(BuildUsageResultsView(context), status);
        }

        VisualElement BuildUsageResultsView(ArtToolContext context)
        {
            var root = new VisualElement();
            int totalSlots = results.Sum(r => r.Slots.Count);
            int editableCount = results.Sum(r => r.Slots.Count(s => s.Editable));
            int packageDefaultLitCount = results.Sum(r => r.Slots.Count(s => s.IsPackageDefaultLitMaterial));
            int missingCount = results.Sum(r => r.Slots.Count(s => s.IsMissingMaterial));
            int selectedCount = results.Sum(r => r.Slots.Count(s => s.Selected));

            var summary = new Label($"对象 {results.Count} | 材质槽 {totalSlots} | 可编辑 {editableCount} | 包内 Lit.mat {packageDefaultLitCount} | 空材质 {missingCount} | 已勾选 {selectedCount}");
            summary.style.unityFontStyleAndWeight = FontStyle.Bold;
            summary.style.marginBottom = 6;
            root.Add(summary);

            if (results.Count == 0)
            {
                root.Add(new HelpBox("暂无扫描结果。请拖入对象或选择 Prefab / Material / 文件夹后点击“扫描”。", HelpBoxMessageType.Info));
                return root;
            }

            var header = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 3 } };
            header.Add(HeaderLabel("勾选", 52));
            header.Add(HeaderLabel("状态", 145));
            header.Add(HeaderLabel("Renderer / 槽位", 230));
            header.Add(HeaderLabel("材质", 260));
            header.Add(HeaderLabel("Shader", 320));
            header.Add(HeaderLabel("路径 / 说明", 260));
            root.Add(header);

            foreach (ScanObjectResult result in results)
            {
                var foldout = new Foldout { text = $"{result.SourcePath}    ({result.Slots.Count} material slots)", value = result.Foldout };
                foldout.RegisterValueChangedCallback(evt => result.Foldout = evt.newValue);

                var objectRow = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 4 } };
                objectRow.Add(new Label(result.SourcePath) { style = { flexGrow = 1, unityFontStyleAndWeight = FontStyle.Bold } });
                if (result.SourceAsset != null)
                {
                    UnityEngine.Object source = result.SourceAsset;
                    objectRow.Add(new Button(() => EditorGUIUtility.PingObject(source)) { text = "定位" });
                    objectRow.Add(new Button(() =>
                    {
                        Selection.activeObject = source;
                        EditorGUIUtility.PingObject(source);
                    }) { text = "选中" });
                }
                foldout.Add(objectRow);

                foreach (MaterialSlotInfo slot in result.Slots)
                    foldout.Add(BuildSlotRow(slot, context));

                root.Add(foldout);
            }

            return root;
        }

        static Label HeaderLabel(string text, float width)
        {
            var label = new Label(text);
            label.style.width = width;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        VisualElement BuildSlotRow(MaterialSlotInfo slot, ArtToolContext context)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 2;
            row.style.paddingBottom = 2;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);

            if (slot.IsPackageDefaultLitMaterial)
                row.style.backgroundColor = new Color(0.34f, 0.27f, 0.12f);
            else if (slot.IsMissingMaterial)
                row.style.backgroundColor = new Color(0.25f, 0.25f, 0.25f);

            var toggle = new Toggle { value = slot.Selected };
            toggle.style.width = 52;
            toggle.SetEnabled(slot.Editable);
            toggle.RegisterValueChangedCallback(evt =>
            {
                slot.Selected = evt.newValue && slot.Editable;
                ShowUsageResults(context, "材质勾选已更新。");
            });
            row.Add(toggle);

            row.Add(new Label(GetSlotStatus(slot)) { style = { width = 145 } });
            row.Add(new Label(FormatRendererSlot(slot)) { style = { width = 230 } });

            var materialField = new ObjectField { objectType = typeof(Material), value = slot.Material };
            materialField.style.width = 260;
            materialField.SetEnabled(false);
            row.Add(materialField);

            var shaderField = new ObjectField { objectType = typeof(Shader), value = slot.Shader };
            shaderField.style.width = 320;
            shaderField.SetEnabled(false);
            row.Add(shaderField);

            var path = new Label(!string.IsNullOrEmpty(slot.MaterialPath) ? slot.MaterialPath : slot.Note);
            path.style.flexGrow = 1;
            path.style.whiteSpace = WhiteSpace.Normal;
            row.Add(path);

            return row;
        }

        void AddTarget(UnityEngine.Object target)
        {
            UnityEngine.Object resolved = ResolveToProjectAsset(target);
            if (resolved == null)
                return;

            string path = AssetDatabase.GetAssetPath(resolved);
            if (targets.Any(t => string.Equals(AssetDatabase.GetAssetPath(t), path, StringComparison.OrdinalIgnoreCase)))
                return;

            targets.Add(resolved);
        }

        void ScanUsageResults()
        {
            results.Clear();
            IEnumerable<UnityEngine.Object> sourceTargets = targets.Count > 0
                ? targets.Cast<UnityEngine.Object>()
                : Selection.objects.Cast<UnityEngine.Object>();
            List<UnityEngine.Object> assets = CollectAssetsFromTargets(sourceTargets);

            foreach (UnityEngine.Object asset in assets)
            {
                if (asset == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(asset);
                if (asset is Material mat)
                    ScanMaterialAsset(mat, path);
                else if (asset is GameObject go)
                    ScanPrefabOrModel(go, asset, path);
            }

            SortResults();
        }

        static List<UnityEngine.Object> CollectAssetsFromTargets(IEnumerable<UnityEngine.Object> rawTargets)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.Object raw in rawTargets)
            {
                UnityEngine.Object target = ResolveToProjectAsset(raw);
                if (target == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(target);
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string assetPath in TaAssetSearchUtility.FindPrefabAndMaterialPaths(new[] { path }))
                        paths.Add(assetPath);
                }
                else if (IsValidScanPath(path))
                {
                    paths.Add(path);
                }
            }

            return paths
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(AssetDatabase.LoadMainAssetAtPath)
                .Where(asset => asset != null)
                .ToList();
        }

        void ScanMaterialAsset(Material mat, string path)
        {
            var result = new ScanObjectResult
            {
                SourceAsset = mat,
                SourcePath = path,
                Foldout = true
            };
            result.Slots.Add(CreateMaterialSlotInfo("材质球资源", -1, mat, "材质球资源"));
            results.Add(result);
        }

        void ScanPrefabOrModel(GameObject go, UnityEngine.Object sourceAsset, string sourcePath)
        {
            var result = new ScanObjectResult
            {
                SourceAsset = sourceAsset,
                SourcePath = sourcePath,
                Foldout = true
            };

            foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] sharedMaterials = renderer.sharedMaterials;
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                {
                    result.Slots.Add(CreateMissingMaterialSlotInfo(renderer.name, -1, "Renderer 没有材质数组。"));
                    continue;
                }

                for (int i = 0; i < sharedMaterials.Length; i++)
                {
                    Material mat = sharedMaterials[i];
                    result.Slots.Add(mat == null
                        ? CreateMissingMaterialSlotInfo(renderer.name, i, "空材质槽。")
                        : CreateMaterialSlotInfo(renderer.name, i, mat, ""));
                }
            }

            results.Add(result);
        }

        MaterialSlotInfo CreateMaterialSlotInfo(string rendererName, int slotIndex, Material material, string note)
        {
            string matPath = material != null ? AssetDatabase.GetAssetPath(material) : "";
            bool isPackageDefaultLit = IsPackageDefaultLitMaterial(material);
            return new MaterialSlotInfo
            {
                RendererName = rendererName,
                SlotIndex = slotIndex,
                Material = material,
                MaterialPath = matPath,
                Shader = material != null ? material.shader : null,
                Editable = IsEditableMaterialAsset(material) && !isPackageDefaultLit,
                IsMissingMaterial = false,
                IsPackageDefaultLitMaterial = isPackageDefaultLit,
                Note = note
            };
        }

        static MaterialSlotInfo CreateMissingMaterialSlotInfo(string rendererName, int slotIndex, string note)
        {
            return new MaterialSlotInfo
            {
                RendererName = rendererName,
                SlotIndex = slotIndex,
                MaterialPath = "",
                Editable = false,
                IsMissingMaterial = true,
                IsPackageDefaultLitMaterial = false,
                Note = note
            };
        }

        void SortResults()
        {
            foreach (ScanObjectResult result in results)
            {
                result.Slots.Sort((a, b) =>
                {
                    int defaultMatCompare = b.IsPackageDefaultLitMaterial.CompareTo(a.IsPackageDefaultLitMaterial);
                    if (defaultMatCompare != 0)
                        return defaultMatCompare;

                    int missingCompare = b.IsMissingMaterial.CompareTo(a.IsMissingMaterial);
                    if (missingCompare != 0)
                        return missingCompare;

                    int litShaderCompare = IsURPLitShader(b.Shader).CompareTo(IsURPLitShader(a.Shader));
                    if (litShaderCompare != 0)
                        return litShaderCompare;

                    int editableCompare = b.Editable.CompareTo(a.Editable);
                    if (editableCompare != 0)
                        return editableCompare;

                    return string.Compare(a.RendererName, b.RendererName, StringComparison.Ordinal);
                });
            }

            results.Sort((a, b) =>
            {
                bool aHasDefaultLitMat = a.Slots.Any(s => s.IsPackageDefaultLitMaterial);
                bool bHasDefaultLitMat = b.Slots.Any(s => s.IsPackageDefaultLitMaterial);
                int defaultMatCompare = bHasDefaultLitMat.CompareTo(aHasDefaultLitMat);
                if (defaultMatCompare != 0)
                    return defaultMatCompare;

                bool aHasLitShader = a.Slots.Any(s => IsURPLitShader(s.Shader));
                bool bHasLitShader = b.Slots.Any(s => IsURPLitShader(s.Shader));
                int litShaderCompare = bHasLitShader.CompareTo(aHasLitShader);
                if (litShaderCompare != 0)
                    return litShaderCompare;

                return string.Compare(a.SourcePath, b.SourcePath, StringComparison.Ordinal);
            });
        }

        void SelectEditableURPLitShaderMaterials()
        {
            foreach (ScanObjectResult result in results)
            {
                foreach (MaterialSlotInfo slot in result.Slots)
                    slot.Selected = slot.Editable && !slot.IsPackageDefaultLitMaterial && IsURPLitShader(slot.Shader);
            }
        }

        void ClearMaterialSelection()
        {
            foreach (ScanObjectResult result in results)
            {
                foreach (MaterialSlotInfo slot in result.Slots)
                    slot.Selected = false;
            }
        }

        void ReplaceSelectedMaterialShaders(ArtToolContext context)
        {
            if (replaceShader == null)
            {
                EditorUtility.DisplayDialog("替换失败", "请先指定目标 Shader。", "确定");
                return;
            }

            List<Material> selectedMaterials = results
                .SelectMany(r => r.Slots)
                .Where(s => s.Selected && s.Editable && !s.IsPackageDefaultLitMaterial && s.Material != null)
                .Select(s => s.Material)
                .Distinct()
                .ToList();

            if (selectedMaterials.Count == 0)
            {
                EditorUtility.DisplayDialog("没有勾选材质", "请先扫描对象并勾选可编辑材质槽。", "确定");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "替换材质 Shader",
                $"即将把 {selectedMaterials.Count} 个材质替换为：\n{replaceShader.name}\n\n执行前请确认项目已纳入版本管理。",
                "替换",
                "取消");
            if (!confirm)
                return;

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (Material mat in selectedMaterials)
                {
                    Undo.RecordObject(mat, "TA Replace Material Shader");
                    mat.shader = replaceShader;
                    MaterialEditor.ApplyMaterialPropertyDrawers(mat);
                    EditorUtility.SetDirty(mat);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            RefreshSlotShaderInfoAfterReplace();
            ShowUsageResults(context, $"已替换 {selectedMaterials.Count} 个材质的 Shader。");
        }

        void ReplacePackageDefaultLitMaterialReferences(ArtToolContext context)
        {
            if (replaceDefaultLitMaterial == null)
            {
                EditorUtility.DisplayDialog("替换失败", "请先指定替换材质。", "确定");
                return;
            }

            string replacementPath = AssetDatabase.GetAssetPath(replaceDefaultLitMaterial);
            if (!TaAssetSearchUtility.IsEditableAssetPath(replacementPath, ".mat"))
            {
                EditorUtility.DisplayDialog("替换失败", "替换材质必须是 Assets 目录下可编辑的 .mat 资源。", "确定");
                return;
            }

            IEnumerable<UnityEngine.Object> sourceTargets = targets.Count > 0
                ? targets.Cast<UnityEngine.Object>()
                : Selection.objects.Cast<UnityEngine.Object>();
            List<string> prefabPaths = CollectPrefabPathsFromTargets(sourceTargets);
            if (prefabPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("没有 Prefab", "当前对象中没有可编辑的 Prefab 资源。", "确定");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "替换默认 Lit 材质引用",
                $"即将扫描 {prefabPaths.Count} 个 Prefab，并将：\n{UrpPackageDefaultLitMaterialPath}\n\n替换为：\n{replacementPath}",
                "替换",
                "取消");
            if (!confirm)
                return;

            int changedPrefabCount = 0;
            int changedSlotCount = 0;
            var logs = new List<string>();
            try
            {
                for (int i = 0; i < prefabPaths.Count; i++)
                {
                    string prefabPath = prefabPaths[i];
                    EditorUtility.DisplayProgressBar(PanelTitle, prefabPath, prefabPaths.Count <= 1 ? 1f : (float)i / prefabPaths.Count);
                    int changed = ReplaceDefaultLitMaterialInPrefab(prefabPath, replaceDefaultLitMaterial);
                    if (changed <= 0)
                        continue;

                    changedPrefabCount++;
                    changedSlotCount += changed;
                    logs.Add($"{prefabPath} | slots {changed}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            ScanUsageResults();
            ShowUsageResults(context, $"已替换默认 Lit 材质引用：{changedPrefabCount} 个 Prefab，{changedSlotCount} 个槽位。");
            if (logs.Count > 0)
                Debug.Log("TA Shader 使用统计：默认 Lit 材质引用替换结果\n" + string.Join("\n", logs));
        }

        List<string> CollectPrefabPathsFromTargets(IEnumerable<UnityEngine.Object> rawTargets)
        {
            var prefabPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityEngine.Object raw in rawTargets)
            {
                UnityEngine.Object target = ResolveToProjectAsset(raw);
                if (target == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(target);
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string prefabPath in TaAssetSearchUtility.FindAssetPaths("t:Prefab", path))
                    {
                        if (IsEditablePrefabPath(prefabPath))
                            prefabPaths.Add(prefabPath);
                    }
                }
                else if (IsEditablePrefabPath(path))
                {
                    prefabPaths.Add(path);
                }
            }

            return prefabPaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        void RefreshSlotShaderInfoAfterReplace()
        {
            foreach (ScanObjectResult result in results)
            {
                foreach (MaterialSlotInfo slot in result.Slots)
                {
                    if (slot.Material == null)
                        continue;

                    slot.Shader = slot.Material.shader;
                    slot.MaterialPath = AssetDatabase.GetAssetPath(slot.Material);
                    slot.IsPackageDefaultLitMaterial = IsPackageDefaultLitMaterial(slot.Material);
                    slot.Editable = IsEditableMaterialAsset(slot.Material) && !slot.IsPackageDefaultLitMaterial;
                    slot.Selected = false;
                }
            }
        }

        static string GetSlotStatus(MaterialSlotInfo slot)
        {
            if (slot.IsPackageDefaultLitMaterial)
                return "包内 Lit.mat";

            if (slot.IsMissingMaterial)
                return "空材质";

            if (!slot.Editable)
                return "不可编辑";

            if (IsURPLitShader(slot.Shader))
                return "Lit Shader";

            return "可编辑";
        }

        static string FormatRendererSlot(MaterialSlotInfo slot)
        {
            if (slot == null)
                return "";

            return slot.SlotIndex >= 0 ? $"{slot.RendererName} / Slot {slot.SlotIndex}" : slot.RendererName;
        }

        static string GetObjectName(UnityEngine.Object obj)
        {
            return obj != null ? obj.name : "无";
        }

        static UnityEngine.Object ResolveToProjectAsset(UnityEngine.Object obj)
        {
            if (obj == null)
                return null;

            string directPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(directPath))
                return IsValidScanPath(directPath) || AssetDatabase.IsValidFolder(directPath) ? obj : null;

            if (obj is GameObject sceneGo)
            {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(sceneGo) ?? PrefabUtility.GetNearestPrefabInstanceRoot(sceneGo);
                string prefabPath = root != null ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) : "";
                if (!string.IsNullOrEmpty(prefabPath) && Path.GetExtension(prefabPath).Equals(".prefab", StringComparison.OrdinalIgnoreCase))
                    return AssetDatabase.LoadMainAssetAtPath(prefabPath);
            }

            return null;
        }

        static bool IsValidScanPath(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".prefab" || ext == ".mat";
        }

        static bool IsEditableMaterialAsset(Material mat)
        {
            string path = mat != null ? AssetDatabase.GetAssetPath(mat) : "";
            return TaAssetSearchUtility.IsEditableAssetPath(path, ".mat");
        }

        static bool IsPackageDefaultLitMaterial(Material mat)
        {
            string path = mat != null ? AssetDatabase.GetAssetPath(mat) : "";
            return string.Equals(path, UrpPackageDefaultLitMaterialPath, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsURPLitShader(Shader shader)
        {
            return shader != null && string.Equals(shader.name, UrpLitShaderName, StringComparison.Ordinal);
        }

        static bool IsEditablePrefabPath(string path)
        {
            return TaAssetSearchUtility.IsEditableAssetPath(path, ".prefab");
        }

        static int ReplaceDefaultLitMaterialInPrefab(string prefabPath, Material replacementMaterial)
        {
            if (!IsEditablePrefabPath(prefabPath) || replacementMaterial == null)
                return 0;

            GameObject root = null;
            int changedSlotCount = 0;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                bool prefabChanged = false;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool rendererChanged = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (!IsPackageDefaultLitMaterial(materials[i]))
                            continue;

                        materials[i] = replacementMaterial;
                        rendererChanged = true;
                        prefabChanged = true;
                        changedSlotCount++;
                    }

                    if (rendererChanged)
                    {
                        renderer.sharedMaterials = materials;
                        EditorUtility.SetDirty(renderer);
                    }
                }

                if (prefabChanged)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"TA Shader 使用统计：替换默认 Lit 材质失败：{prefabPath}\n{e}");
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }

            return changedSlotCount;
        }
    }

    public sealed class TaSimpleLitRefreshModule : ArtToolModuleBase
    {
        static readonly string[] SimpleLitShaderNames =
        {
            "Universal Render Pipeline/Simple Lit",
            "Universal Render Pipeline/SimpleLit"
        };

        readonly List<UnityEngine.Object> targets = new List<UnityEngine.Object>();
        bool includeInactive = true;
        bool cleanupUnusedProperties = true;
        bool forceReserialize;

        public override string DisplayName => "Simple Lit Material Refresher";
        public override string PanelTitle => "SimpleLit 材质球刷新";
        public override string Category => "Material";
        public override string Description => "刷新 URP Simple Lit 材质球，并可清理旧 Shader 残留属性。";
        public override string HelpText =>
            "功能说明：\n" +
            "1. 支持拖入 Hierarchy 场景 GameObject 或 Project 文件夹 / Material。\n" +
            "2. 场景对象会递归遍历子物体 Renderer.sharedMaterials。\n" +
            "3. Project 文件夹会递归查找 .mat 材质球。\n" +
            "4. 只处理使用 URP Simple Lit 的材质球。\n" +
            "5. 可选择清理 .mat 中当前 Shader 不再使用的残留属性引用。";

        public override VisualElement CreateView(ArtToolContext context)
        {
            var root = new VisualElement();
            root.Add(Header(PanelTitle, Description));

            var scanTargetsLabel = new Label("扫描对象 (0)");
            scanTargetsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            scanTargetsLabel.style.marginTop = 6;
            root.Add(scanTargetsLabel);

            var targetList = new VisualElement();
            Action refreshTargetList = null;
            refreshTargetList = () =>
            {
                scanTargetsLabel.text = $"扫描对象 ({targets.Count})";
                targetList.Clear();
                if (targets.Count == 0)
                {
                    var empty = new Label("暂无扫描对象。请拖入场景物体、Project 文件夹或 Material 后再开始。");
                    empty.style.whiteSpace = WhiteSpace.Normal;
                    targetList.Add(empty);
                    return;
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    int index = i;
                    var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginBottom = 2 } };
                    var field = new ObjectField { objectType = typeof(UnityEngine.Object), allowSceneObjects = true, value = targets[index] };
                    field.style.flexGrow = 1;
                    field.RegisterValueChangedCallback(evt =>
                    {
                        UnityEngine.Object resolved = ResolveSimpleLitTarget(evt.newValue);
                        if (resolved != null)
                            targets[index] = resolved;
                        refreshTargetList();
                    });
                    row.Add(field);
                    row.Add(new Button(() =>
                    {
                        targets.RemoveAt(index);
                        refreshTargetList();
                    }) { text = "移除" });
                    targetList.Add(row);
                }
            };

            root.Add(ActionRow(
                ActionButton("清空列表", () =>
                {
                    targets.Clear();
                    refreshTargetList();
                })));

            var targetScroll = new ScrollView();
            targetScroll.style.height = 112;
            targetScroll.style.minHeight = 80;
            targetScroll.style.marginBottom = 6;
            targetScroll.style.paddingLeft = 4;
            targetScroll.style.paddingRight = 4;
            targetScroll.style.paddingTop = 3;
            targetScroll.style.paddingBottom = 3;
            targetScroll.style.borderLeftWidth = 1;
            targetScroll.style.borderRightWidth = 1;
            targetScroll.style.borderTopWidth = 1;
            targetScroll.style.borderBottomWidth = 1;
            targetScroll.style.borderLeftColor = new Color(0.25f, 0.25f, 0.25f);
            targetScroll.style.borderRightColor = new Color(0.25f, 0.25f, 0.25f);
            targetScroll.style.borderTopColor = new Color(0.25f, 0.25f, 0.25f);
            targetScroll.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);
            targetScroll.Add(targetList);
            root.Add(targetScroll);

            root.Add(CreateDragDropArea("拖入场景 GameObject / Project 文件夹 / Material", objects =>
            {
                foreach (UnityEngine.Object obj in objects)
                    AddTarget(obj);

                refreshTargetList();
                context.Log?.Invoke($"SimpleLit 扫描对象：{targets.Count} 个");
            }));
            refreshTargetList();

            var inactiveToggle = new Toggle("包含未激活子物体") { value = includeInactive };
            inactiveToggle.RegisterValueChangedCallback(evt => includeInactive = evt.newValue);
            root.Add(inactiveToggle);

            var cleanupToggle = new Toggle("清理无效残留属性") { value = cleanupUnusedProperties };
            cleanupToggle.RegisterValueChangedCallback(evt => cleanupUnusedProperties = evt.newValue);
            root.Add(cleanupToggle);

            var reserializeToggle = new Toggle("强制重新序列化材质") { value = forceReserialize };
            reserializeToggle.RegisterValueChangedCallback(evt => forceReserialize = evt.newValue);
            root.Add(reserializeToggle);

            root.Add(new HelpBox("拖入场景 GameObject、Project 文件夹或 Material 后开始。执行前会先在右下显示待处理材质列表，并弹出确认。", HelpBoxMessageType.Info));
            root.Add(ActionRow(
                ActionButton("开始刷新 SimpleLit 材质球", () => StartRefresh(context))));
            return root;
        }

        static VisualElement CreateDragDropArea(string text, Action<UnityEngine.Object[]> onDrop)
        {
            var area = new Label(text);
            area.style.height = 42;
            area.style.marginTop = 2;
            area.style.marginBottom = 8;
            area.style.unityTextAlign = TextAnchor.MiddleCenter;
            area.style.whiteSpace = WhiteSpace.Normal;
            area.style.borderLeftWidth = 1;
            area.style.borderRightWidth = 1;
            area.style.borderTopWidth = 1;
            area.style.borderBottomWidth = 1;
            area.style.borderLeftColor = new Color(0.36f, 0.36f, 0.36f);
            area.style.borderRightColor = new Color(0.36f, 0.36f, 0.36f);
            area.style.borderTopColor = new Color(0.36f, 0.36f, 0.36f);
            area.style.borderBottomColor = new Color(0.36f, 0.36f, 0.36f);
            area.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);
            area.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            area.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                onDrop?.Invoke(DragAndDrop.objectReferences);
                evt.StopPropagation();
            });
            return area;
        }

        void AddTarget(UnityEngine.Object target)
        {
            UnityEngine.Object resolved = ResolveSimpleLitTarget(target);
            if (resolved == null)
                return;

            string path = AssetDatabase.GetAssetPath(resolved);
            bool exists = targets.Any(existing =>
            {
                if (existing == null)
                    return false;

                if (string.IsNullOrEmpty(path))
                    return existing == resolved;

                return string.Equals(AssetDatabase.GetAssetPath(existing), path, StringComparison.OrdinalIgnoreCase);
            });

            if (!exists)
                targets.Add(resolved);
        }

        static UnityEngine.Object ResolveSimpleLitTarget(UnityEngine.Object target)
        {
            if (target == null)
                return null;

            if (target is GameObject || target is Material)
                return target;

            string path = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path) && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return target;

            return null;
        }

        void StartRefresh(ArtToolContext context)
        {
            ArtToolReport report = Scan();
                context.ShowReport?.Invoke(report);

            if (report == null || report.WriteCount == 0)
            {
                context.Log?.Invoke("没有需要刷新的 SimpleLit 材质球。");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "刷新 SimpleLit 材质球",
                $"即将刷新 {report.WriteCount} 个材质球。\n\n清理无效残留属性：{(cleanupUnusedProperties ? "是" : "否")}\n强制重新序列化：{(forceReserialize ? "是" : "否")}\n\n执行前请确认项目已纳入版本管理。",
                "刷新",
                "取消");
            if (!confirm)
                return;

            Apply(report);
            context.ShowReport?.Invoke(report);
            context.Log?.Invoke($"SimpleLit 材质球刷新：已刷新 {report.WriteCount} 个材质球。");
        }

        public override ArtToolReport Scan()
        {
            var report = ArtToolReport.Empty(PanelTitle);
            Shader simpleLitShader = FindSimpleLitShader();
            if (simpleLitShader == null)
            {
                report.Changes.Add(ArtToolChange.Error("未找到 Simple Lit Shader", "无法解析项目中的 URP Simple Lit Shader。"));
                return report;
            }

            var materials = new HashSet<Material>();
            foreach (UnityEngine.Object target in targets)
                CollectMaterials(target, materials);

            foreach (Material mat in materials.OrderBy(m => AssetDatabase.GetAssetPath(m)))
            {
                string path = AssetDatabase.GetAssetPath(mat);
                if (!TaAssetSearchUtility.IsEditableAssetPath(path, ".mat"))
                {
                    report.Changes.Add(ArtToolChange.Info("跳过不可编辑材质", mat.name, path, mat));
                    continue;
                }

                Material captured = mat;
                report.Changes.Add(ArtToolChange.Write(
                    "刷新 SimpleLit 材质球",
                    captured.name,
                    () => RefreshMaterial(captured, simpleLitShader, cleanupUnusedProperties),
                    path,
                    captured));
            }

            if (report.Changes.Count == 0)
                report.Changes.Add(ArtToolChange.Info("未找到 SimpleLit 材质球", "请先拖入扫描对象，或当前对象中没有可编辑的 SimpleLit 材质球。"));

            return report;
        }

        public override void Apply(ArtToolReport report)
        {
            base.Apply(report);

            if (!forceReserialize || report == null)
                return;

            List<string> paths = report.Changes
                .Where(c => c != null && c.IsWriteOperation && !string.IsNullOrEmpty(c.AssetPath))
                .Select(c => c.AssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count > 0)
                AssetDatabase.ForceReserializeAssets(paths);
        }

        void CollectMaterials(UnityEngine.Object target, HashSet<Material> materials)
        {
            if (target == null)
                return;

            if (target is GameObject go)
            {
                foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(includeInactive))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (IsSimpleLitMaterial(material))
                            materials.Add(material);
                    }
                }
                return;
            }

            string path = AssetDatabase.GetAssetPath(target);
            if (AssetDatabase.IsValidFolder(path))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { path }))
                {
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (IsSimpleLitMaterial(material))
                        materials.Add(material);
                }
            }
            else if (target is Material material && IsSimpleLitMaterial(material))
            {
                materials.Add(material);
            }
        }

        static bool IsSimpleLitMaterial(Material mat)
        {
            if (mat == null || mat.shader == null)
                return false;

            foreach (string shaderName in SimpleLitShaderNames)
            {
                if (mat.shader.name == shaderName)
                    return true;
            }
            return false;
        }

        static Shader FindSimpleLitShader()
        {
            foreach (string shaderName in SimpleLitShaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader != null)
                    return shader;
            }
            return null;
        }

        static void RefreshMaterial(Material mat, Shader shader, bool cleanup)
        {
            Undo.RecordObject(mat, "TA Refresh Simple Lit Material");
            mat.shader = shader;
            MaterialEditor.ApplyMaterialPropertyDrawers(mat);
            if (cleanup)
                RemoveUnusedSavedProperties(mat);
            EditorUtility.SetDirty(mat);
        }

        static int RemoveUnusedSavedProperties(Material mat)
        {
            if (mat == null || mat.shader == null)
                return 0;

            var validTextureProperties = new HashSet<string>();
            var validFloatProperties = new HashSet<string>();
            var validColorProperties = new HashSet<string>();

            int propertyCount = ShaderUtil.GetPropertyCount(mat.shader);
            for (int i = 0; i < propertyCount; i++)
            {
                string propertyName = ShaderUtil.GetPropertyName(mat.shader, i);
                switch (ShaderUtil.GetPropertyType(mat.shader, i))
                {
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        validTextureProperties.Add(propertyName);
                        break;
                    case ShaderUtil.ShaderPropertyType.Float:
                    case ShaderUtil.ShaderPropertyType.Range:
                        validFloatProperties.Add(propertyName);
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                    case ShaderUtil.ShaderPropertyType.Vector:
                        validColorProperties.Add(propertyName);
                        break;
                }
            }

            var serializedObject = new SerializedObject(mat);
            SerializedProperty savedProperties = serializedObject.FindProperty("m_SavedProperties");
            if (savedProperties == null)
                return 0;

            int removed = 0;
            removed += RemoveInvalidEntries(savedProperties.FindPropertyRelative("m_TexEnvs"), validTextureProperties);
            removed += RemoveInvalidEntries(savedProperties.FindPropertyRelative("m_Floats"), validFloatProperties);
            removed += RemoveInvalidEntries(savedProperties.FindPropertyRelative("m_Colors"), validColorProperties);
            if (removed > 0)
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
            return removed;
        }

        static int RemoveInvalidEntries(SerializedProperty arrayProperty, HashSet<string> validNames)
        {
            if (arrayProperty == null || !arrayProperty.isArray)
                return 0;

            int removed = 0;
            for (int i = arrayProperty.arraySize - 1; i >= 0; i--)
            {
                SerializedProperty first = arrayProperty.GetArrayElementAtIndex(i).FindPropertyRelative("first");
                string propertyName = first != null ? first.stringValue : "";
                if (!string.IsNullOrEmpty(propertyName) && !validNames.Contains(propertyName))
                {
                    arrayProperty.DeleteArrayElementAtIndex(i);
                    removed++;
                }
            }
            return removed;
        }
    }
}
