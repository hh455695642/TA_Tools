# TA Art Tools 资产克隆隔离工具需求

## 背景

旧项目美术资源位于 `Assets/NewWorld/ArtResources`，新项目资源位于 `Assets/ArtResources_Mountainbike`。当资源被直接复制或移动后，Unity 仍通过 `.meta` GUID 解析引用，容易出现新目录 prefab、材质、贴图或 shader 继续指向旧目录资源的问题。

本工具用于把旧项目中的部分美术资产克隆到新项目目录，并在克隆过程中建立新的目标 GUID 映射，确保新旧目录的资源引用互相隔离。

## 入口

- 工具入口统一放在 `Tools/TA/Art Tools`。
- 模块名称为 `Asset Clone Isolation / 资产克隆隔离`。
- 不再保留旧的 `.unitypackage` 隔离导入工具，也不再提供独立 IMGUI 克隆窗口。

## 主要功能

- 支持配置 `SourceRoot` 和 `TargetRoot`。
- 默认 `SourceRoot` 为 `Assets/NewWorld/ArtResources`。
- 默认 `TargetRoot` 为 `Assets/ArtResources_Mountainbike`。
- 支持保存和加载 `AssetCloneIsolationPreset` 预设。
- 支持从 Project 或 Hierarchy 拖拽拾取多个待克隆隔离对象。
- 支持拖入 Project 资源、文件夹、材质、贴图、shader、prefab，以及 Hierarchy 中的 prefab 实例。
- Hierarchy 中的 prefab 实例必须解析回 Project prefab 资源。
- Project 资源按 asset path 去重。
- 文件夹不在 UI 列表中展开，由计划构建阶段递归收集。
- 资源添加入口只使用拖拽列表；列表为空时不得隐式使用 Unity 当前 Selection。
- 必须先预览计划，再应用计划。
- 应用后可审计 `TargetRoot` 的隔离状态。
- 预览结果必须按每个待克隆隔离对象分组展示，不允许把多个对象的依赖、引用修复和风险混在一个平铺列表中。
- 每个分组必须展示下游依赖、直接上游引用、共享依赖引用、目标目录修复项、写入清单和风险汇总。
- 每个分组必须展示摘要：新建目标、覆盖已有目标、外部共享、外部迁移、TargetRoot 修复、显式共享、阻断和警告。
- 写入清单必须拆分为新建目标资产、覆盖已有目标并保留 GUID、外部依赖迁移、TargetRoot GUID 修复、共享风险不写入。
- 共享依赖引用默认只显示类型统计和前 20 条，路径过滤或选择共享依赖筛选时显示完整匹配。
- 相关资产必须提供快速定位能力，方便在 Project 面板中手动确认资源关系。

## 克隆规则

- 选中资源及其 `SourceRoot` 内递归依赖会进入克隆计划。
- 目标路径按 `SourceRoot` 前缀替换为 `TargetRoot` 前缀生成。
- 目标资产不存在时生成新的目标 GUID。
- 目标资产已存在时覆盖资源内容，但保留目标 `.meta` GUID。
- 如果目标 `.meta` 误保留了源 GUID，必须生成新的隔离 GUID 并给出警告。
- 文本资源和 `.meta` 需要重写 Unity YAML `guid:` 引用。
- 二进制资源本体只复制，不尝试内部重写；如果检测到可疑 `guid:` 文本，需要报告风险。
- 应用后可扫描 `TargetRoot` 已有文本资源，把指向本次源 GUID 的引用改为目标 GUID。
- 用户可以把被依赖牵出的 `SourceRoot` 内资源标记为显式共享；显式共享资源不会被复制，也不会进入 GUID 映射。
- `SourceRoot` 外、`TargetRoot` 外的 `Assets/...` 美术依赖默认作为外部共享风险保留，不阻断应用，也不进入 GUID 映射。
- 用户可以把外部共享依赖标记为迁移到目标；迁移目标路径固定为 `TargetRoot/_External/Assets/...`。
- 直接选中的待克隆隔离对象不能被显式共享，必须进入克隆计划。
- 点击显式共享操作时必须确认风险；显式共享会保留跨目录 GUID 引用，预览和审计必须单独提示风险。

## 依赖规则

- `SourceRoot` 内非共享依赖会一起克隆。
- `SourceRoot` 外的 `Assets/...` 美术依赖默认显示为外部共享风险，不自动复制；用户选择迁移后复制到 `TargetRoot/_External/Assets/...`。
- `TargetRoot` 内依赖允许保留。
- `Packages/...`、`Library/PackageCache/...`、`ProjectSettings/...`、`Resources/unity_builtin_extra` 允许共享。
- `.cs`、`.asmdef`、`.asmref`、`.dll` 允许作为共享代码依赖。
- 下游依赖表示当前资源引用到的材质、贴图、shader、prefab、controller、asset 等资源。
- 直接上游引用表示扫描范围内哪些 prefab、scene、timeline、ScriptableObject 或其它文本资产正在直接引用当前资源本身。
- 共享依赖引用表示扫描范围内哪些资产只引用了当前资源的下游依赖，例如共用同一个 shader 或贴图；它不等同于直接引用当前资源。
- 默认关系引用扫描范围为 `SourceRoot + TargetRoot`，不默认扫描整个 `Assets`，避免大项目预览过慢。

## 审计规则

- `TargetRoot` 中不得残留指向 `SourceRoot` 的非共享美术依赖。
- `TargetRoot` 中引用外部 `Assets/...` 美术依赖时给出 warning，说明它是外部共享风险，可选择迁移以彻底隔离。
- 材质引用 shader 优先来自 `TargetRoot`、Unity/URP/ShaderGraph package、PackageCache 或 built-in；引用 SourceRoot shader 且未显式共享时仍为 error，引用其它外部 `Assets/...` shader 时为 warning。
- 文本资源中无法解析的 GUID 需要报错。
- Shader、ShaderGraph、Compute 需要扫描 `multi_compile` 与 `shader_feature`，提示移动端 variant 风险。
- 重名资源需要提示潜在按名称寻址风险。
- 显式共享路径传入审计时，`TargetRoot -> SourceRoot` 的对应引用降级为风险提示；未显式共享的旧目录引用仍然报错。

## 非目标

- 不修改 YooAsset、Addressables 或 AssetBundle 收集配置。
- 不自动删除旧项目资源。
- 不处理脚本类型缺失或重复类名问题。
- 不提供默认静默全自动模式。
- 不保留旧 `.unitypackage` 隔离导入流程。
- 不在 v1 引入第三方依赖图插件；优先使用 UI Toolkit foldout/table/filter 实现稳定关系视图。
- 不实现 Package 导出预检；本轮只优化现有克隆隔离预览、应用和审计体验。
