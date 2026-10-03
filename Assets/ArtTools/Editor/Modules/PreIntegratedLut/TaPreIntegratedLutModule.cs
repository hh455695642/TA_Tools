using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TA.ArtTools.Editor
{
    public sealed class TaPreIntegratedLutModule : ArtToolModuleBase, IDisposable
    {
        readonly List<string> resolutions = new List<string> { "64", "128", "256", "512" };
        Color falloff = new Color(1f, 0.3f, 0.2f, 1f);
        bool keepDirectBounce;
        int resolution = 512;
        RenderTexture integratedLut;
        Button saveButton;
        Label previewStatus;
        Image previewImage;

        public override string DisplayName => "PreIntegrated Skin LUT";
        public override string Category => "Texture";
        public override string Description => "烘焙皮肤次表面散射 PreIntegrated LUT，预览并保存为线性 TGA。";
        public override string HelpText =>
            "来源：codewings/PreIntegrated-Skin（Subsurface LUT Integrator）。\n\n" +
            "1. 设置 Falloff Color；默认 RGB = (1, 0.3, 0.2)，控制各通道散射宽度。\n" +
            "2. Keep Direct Bounce 决定是否保留最窄的高斯直接反弹项；默认关闭，与源项目一致。\n" +
            "3. 选择 64 / 128 / 256 / 512 分辨率，点击“烘焙预览”。\n" +
            "4. 点击“保存 TGA”，选择 Assets 下的输出路径。修改参数后须重新烘焙才能保存。\n\n" +
            "LUT 横轴：积分角度的 N·L 映射值 u，cos(theta) = 2u − 1；纵轴：逆半径（曲率）。" +
            "源项目 Shader 对光照另有 wrap 处理；接入其他 Shader 时需核对采样坐标。\n\n" +
            "导入设置：sRGB 关闭、无 Mipmap、Clamp、Bilinear、无压缩，尺寸与烘焙分辨率一致。" +
            "TGA 每通道 8 bit；计算和预览为半浮点。保存会清除同名贴图的旧平台覆盖设置。\n\n" +
            "本工具只生成 LUT；在支持该采样约定的皮肤 Shader 上，将输出贴图绑定到 _ScatterLUT。" +
            "需要支持 Compute Shader 的编辑器图形设备。";

        public override VisualElement CreateView(ArtToolContext context)
        {
            var root = new VisualElement();
            root.Add(new HelpBox(Description, HelpBoxMessageType.Info));
            var colorField = new ColorField("Falloff Color")
            {
                value = falloff,
                hdr = false,
                showAlpha = false,
                tooltip = "RGB 控制各通道散射宽度；Alpha 不参与积分。"
            };
            colorField.RegisterValueChangedCallback(evt => { falloff = evt.newValue; Invalidate(context); });
            root.Add(colorField);
            var bounceField = new Toggle("Keep Direct Bounce") { value = keepDirectBounce };
            bounceField.RegisterValueChangedCallback(evt => { keepDirectBounce = evt.newValue; Invalidate(context); });
            root.Add(bounceField);
            var resolutionField = new DropdownField("分辨率", resolutions, resolutions.IndexOf(resolution.ToString()));
            resolutionField.RegisterValueChangedCallback(evt => { resolution = int.Parse(evt.newValue); Invalidate(context); });
            root.Add(resolutionField);
            saveButton = ActionButton("保存 TGA", () => Save(context));
            saveButton.SetEnabled(false);
            root.Add(ActionRow(ActionButton("烘焙预览", () => Bake(context)), saveButton));
            previewStatus = new Label("尚未烘焙。修改参数后，请重新烘焙。") { name = "lut-preview-status" };
            previewStatus.style.whiteSpace = WhiteSpace.Normal;
            root.Add(previewStatus);
            root.RegisterCallback<DetachFromPanelEvent>(_ => Dispose());
            return root;
        }

        public override ArtToolReport Scan()
        {
            var report = ArtToolReport.Empty(PanelTitle);
            report.Changes.Add(ArtToolChange.Info("直接生成工具", "请设置参数后点击“烘焙预览”，再保存 TGA。"));
            return report;
        }

        void Invalidate(ArtToolContext context)
        {
            saveButton?.SetEnabled(false);
            if (previewStatus != null) previewStatus.text = "参数已修改，请重新烘焙；下方预览仍为上次结果。";
            context.Log?.Invoke("LUT 参数已修改，请重新烘焙后保存。");
        }

        void Bake(ArtToolContext context)
        {
            try
            {
                EditorUtility.DisplayProgressBar(PanelTitle, "正在积分生成 LUT…", 0.5f);
                RenderTexture next = TaPreIntegratedLutBaker.Bake(TaPreIntegratedLutBaker.LoadShader(), falloff, keepDirectBounce, resolution);
                Dispose();
                integratedLut = next;
                var view = new VisualElement();
                string summary = $"{resolution} × {resolution} · Falloff RGB ({falloff.r:F3}, {falloff.g:F3}, {falloff.b:F3}) · Direct Bounce {(keepDirectBounce ? "开启" : "关闭")}";
                var label = new Label(summary);
                label.style.whiteSpace = WhiteSpace.Normal;
                view.Add(label);
                previewImage = new Image { image = integratedLut, scaleMode = ScaleMode.ScaleToFit, name = "lut-preview" };
                previewImage.style.height = 320;
                previewImage.style.flexShrink = 0;
                view.Add(previewImage);
                view.Add(new Label("横轴 → 光照映射值 u；纵轴 ↑ 逆半径（曲率）。"));
                context.ShowCustomView?.Invoke(view, "LUT 已烘焙，可保存 TGA。");
                previewStatus.text = "烘焙完成：" + summary;
                saveButton.SetEnabled(true);
            }
            catch (Exception e)
            {
                context.Log?.Invoke("LUT 烘焙失败：" + e.Message);
                if (previewStatus != null) previewStatus.text = "烘焙失败：" + e.Message;
                Debug.LogException(e);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        void Save(ArtToolContext context)
        {
            if (integratedLut == null || !saveButton.enabledSelf) return;
            string path = EditorUtility.SaveFilePanelInProject("保存 PreIntegrated Skin LUT", "Baked_SubsurfaceLookupTexture", "tga", "选择 Assets 目录下的输出文件位置。");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Texture2D texture = TaPreIntegratedLutBaker.SaveTga(integratedLut, path);
                Selection.activeObject = texture;
                EditorGUIUtility.PingObject(texture);
                context.Log?.Invoke("LUT 已保存：" + path);
                previewStatus.text = "已保存：" + path;
            }
            catch (Exception e)
            {
                context.Log?.Invoke("LUT 保存失败：" + e.Message);
                Debug.LogException(e);
            }
        }

        public void Dispose()
        {
            if (previewImage != null) previewImage.image = null;
            previewImage = null;
            TaPreIntegratedLutBaker.Release(integratedLut);
            integratedLut = null;
            saveButton?.SetEnabled(false);
        }
    }
}
