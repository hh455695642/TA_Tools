# TA Art Tools

TA 美术生产辅助工具集，面向 Unity Editor 工作流。建议将本仓库放在 Unity 项目的以下路径：

```text
Assets/ArtTools
```

工具全部为 Editor 工具代码，并通过 `TA.ArtTools.Editor` asmdef 独立管理。

## 环境要求

- Unity 2022.x
- Universal Render Pipeline 项目
- Unity UI Toolkit / Editor UIElements

## 安装方式

将本仓库克隆或复制到 Unity 项目中：

```text
Assets/ArtTools
```

等待 Unity 重新编译 Editor 脚本后，通过菜单打开：

```text
Tools/TA/Art Tools
```

## 工具模块

- `ASTC Texture Format Batch`
  - 批量设置 Android / iOS 贴图平台压缩格式。
  - 当前支持 `ASTC_5x5`、`ASTC_6x6`、`ASTC_8x8`。
  - 修改平台格式时保留原 max texture size。

- `VFX Texture Optimizer`
  - 从特效 Prefab 收集材质引用贴图。
  - 快速设置贴图 max size 档位。
  - 优化记录写入 `TAArtTools/Data/VFX_Texture_Optimize_Log.csv`。

- `Mesh Usage Analyzer`
  - 扫描场景中的 `MeshFilter` 与 `SkinnedMeshRenderer`。
  - 统计实例数量、启用 Renderer 数、顶点数、索引数和三角面数。

- `Texture Usage Analyzer`
  - 扫描场景材质贴图引用。
  - 统计引用次数、分辨率、Android / iOS 导入尺寸与实际格式。

- `Shader Usage Analyzer`
  - 扫描 Prefab、Material、文件夹目标。
  - 统计材质槽位与 Shader 使用情况。
  - 支持替换可编辑材质 Shader，以及替换 URP 包内默认 Lit 材质引用。

- `Simple Lit Material Refresher`
  - 刷新 URP Simple Lit 材质。
  - 可选清理无效的序列化材质属性。

- `Disabled Renderer Cleaner`
  - 扫描 Prefab 或场景目标中的禁用 Renderer。
  - 执行清理前先生成预览报告。

## 注意事项

- 必须提交 `.meta` 文件，用于保持 Unity GUID 稳定。
- 执行批量写入类操作前，确认项目已经纳入版本管理。
- 本工具集仅用于 Editor，不应进入运行时 Assembly。
