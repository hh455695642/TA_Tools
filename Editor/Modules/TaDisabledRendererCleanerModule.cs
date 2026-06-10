using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    public sealed class TaDisabledRendererCleanerModule : ArtToolModuleBase
    {
        public override string DisplayName => "Disabled MeshRenderer Cleaner";
        public override string PanelTitle => "禁用 MeshRenderer 清理";
        public override string Category => "Cleanup";
        public override string Description => "清理选中对象中的禁用 MeshRenderer，并在安全条件下同时移除 MeshFilter。";
        public override string HelpText =>
            "功能说明：\n" +
            "1. 选中场景 GameObject 或 Project 中的 Prefab 资产后点击清理。\n" +
            "2. 工具会递归查找 enabled=false 的 MeshRenderer。\n" +
            "3. 场景对象使用 Undo 删除组件。\n" +
            "4. Prefab 资产通过 LoadPrefabContents / SaveAsPrefabAsset 安全写入。\n" +
            "5. 如果同物体上已没有 MeshRenderer，会同时删除 MeshFilter。";

        public override VisualElement CreateView(ArtToolContext context)
        {
            var root = new VisualElement();
            root.Add(Header(PanelTitle, Description));
            root.Add(new HelpBox("选中场景 GameObject 或 Project 中的 Prefab 资产后点击清理。执行前会在右下列出将清理的对象并弹出确认。", HelpBoxMessageType.Info));
            root.Add(ActionButton("清理选中对象的禁用 MeshRenderer", () => CleanSelected(context)));
            return root;
        }

        void CleanSelected(ArtToolContext context)
        {
            ArtToolReport report = Scan();
            context.ShowReport?.Invoke(report);

            if (report == null || report.WriteCount == 0)
            {
                context.Log?.Invoke("没有找到禁用的 MeshRenderer。");
                return;
            }

            bool confirm = EditorUtility.DisplayDialog(
                "清理禁用 MeshRenderer",
                $"即将清理 {report.WriteCount} 个选中根对象 / Prefab。\n\n右下已列出将清理的内容，执行前请确认项目已纳入版本管理。",
                "清理",
                "取消");
            if (!confirm)
                return;

            Apply(report);
            context.ShowReport?.Invoke(report);
            context.Log?.Invoke($"禁用 MeshRenderer 清理：已清理 {report.WriteCount} 项。");
        }

        public override ArtToolReport Scan()
        {
            var report = ArtToolReport.Empty(PanelTitle);
            if (Selection.objects == null || Selection.objects.Length == 0)
            {
                report.Changes.Add(ArtToolChange.Info("未选择对象", "请选择场景 GameObject 或 Prefab 资产。"));
                return report;
            }

            foreach (UnityEngine.Object selected in Selection.objects)
            {
                GameObject gameObject = selected as GameObject;
                if (gameObject == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(gameObject);
                if (TaAssetSearchUtility.IsEditableAssetPath(path, ".prefab"))
                    AddPrefabChange(report, path);
                else
                    AddSceneChange(report, gameObject);
            }

            if (report.Changes.Count == 0)
                report.Changes.Add(ArtToolChange.Info("未找到禁用 MeshRenderer", "当前选择无需清理。"));

            return report;
        }

        static void AddSceneChange(ArtToolReport report, GameObject root)
        {
            int count = CountDisabledMeshRenderers(root);
            if (count == 0)
                return;

            report.Changes.Add(ArtToolChange.Write(
                root.name,
                $"场景对象：移除 {count} 个禁用 MeshRenderer 组件。",
                () => RemoveFromSceneRoot(root),
                "",
                root));

            AddDisabledRendererLogs(report, root, "场景对象");
        }

        static void AddPrefabChange(ArtToolReport report, string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            int count = prefab != null ? CountDisabledMeshRenderers(prefab) : 0;
            if (count == 0)
                return;

            report.Changes.Add(ArtToolChange.Write(
                prefab.name,
                $"Prefab 资产：移除 {count} 个禁用 MeshRenderer 组件。",
                () => RemoveFromPrefab(prefabPath),
                prefabPath,
                prefab));

            AddDisabledRendererLogs(report, prefab, "Prefab 资产");
        }

        static int CountDisabledMeshRenderers(GameObject root)
        {
            if (root == null)
                return 0;

            int count = 0;
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer != null && !renderer.enabled)
                    count++;
            }
            return count;
        }

        static void AddDisabledRendererLogs(ArtToolReport report, GameObject root, string sourceType)
        {
            if (report == null || root == null)
                return;

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null || renderer.enabled)
                    continue;

                string objectPath = GetTransformPath(renderer.transform, root.transform);
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                string filterText = filter != null ? " + MeshFilter" : "";
                string displayPath = string.IsNullOrEmpty(objectPath) ? root.name : root.name + "/" + objectPath;
                report.AddLog($"{sourceType}：{displayPath} -> 移除禁用 MeshRenderer{filterText}");
            }
        }

        static string GetTransformPath(Transform transform, Transform root)
        {
            if (transform == null)
                return "";

            if (root == null || transform == root)
                return "";

            string path = transform.name;
            Transform current = transform.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        static void RemoveFromSceneRoot(GameObject root)
        {
            if (root == null)
                return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("TA Remove Disabled MeshRenderer");
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null || renderer.enabled)
                    continue;

                GameObject owner = renderer.gameObject;
                Undo.DestroyObjectImmediate(renderer);
                RemoveSiblingMeshFilterFromSceneIfSafe(owner);
            }
            Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        }

        static void RemoveFromPrefab(string prefabPath)
        {
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                bool changed = false;
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer == null || renderer.enabled)
                        continue;

                    GameObject owner = renderer.gameObject;
                    UnityEngine.Object.DestroyImmediate(renderer, true);
                    RemoveSiblingMeshFilterFromPrefabIfSafe(owner);
                    changed = true;
                }

                if (changed)
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void RemoveSiblingMeshFilterFromSceneIfSafe(GameObject owner)
        {
            if (owner == null || owner.GetComponent<MeshRenderer>() != null)
                return;

            MeshFilter filter = owner.GetComponent<MeshFilter>();
            if (filter != null)
                Undo.DestroyObjectImmediate(filter);
        }

        static void RemoveSiblingMeshFilterFromPrefabIfSafe(GameObject owner)
        {
            if (owner == null || owner.GetComponent<MeshRenderer>() != null)
                return;

            MeshFilter filter = owner.GetComponent<MeshFilter>();
            if (filter != null)
                UnityEngine.Object.DestroyImmediate(filter, true);
        }
    }
}
