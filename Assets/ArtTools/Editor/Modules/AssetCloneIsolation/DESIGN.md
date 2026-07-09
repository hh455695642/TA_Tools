# TA Art Tools 资产克隆隔离工具设计

## 总体设计

工具源码整体位于 `Assets/ArtTools/Editor/Modules/AssetCloneIsolation`，与 TA Art Tools 其它功能模块保持同级组织。内部拆成核心服务和 TA Art Tools 模块两层：核心服务位于 `AssetCloneIsolation.Editor` 命名空间，只处理路径映射、依赖收集、GUID 映射、文件写入、引用修复和审计；TA 模块位于 `TA.ArtTools.Editor` 命名空间，只负责 UI、对象拾取、报告展示和触发服务。
`AssetCloneIsolationService` 保持公共门面接口，内部通过 `AssetCloneIsolationPlanBuilder`、`AssetCloneIsolationPlanApplier`、`AssetCloneIsolationAuditService` 和 `AssetCloneIsolationReferenceIndex` 分离计划构建、写入、审计和引用索引职责。

旧的 `.unitypackage` 隔离导入工具已移除；新流程只处理项目内已有资产的克隆隔离，不再解包 unitypackage。

## UI 设计

- 模块注册到 `TaArtToolsWindow.OnEnable()`。
- 模块入口显示在 `Asset Pipeline` 分类下。
- 配置区包含 `SourceRoot`、`TargetRoot`、覆盖目标内容开关、修复目标目录旧 GUID 开关和 preset 控件。
- 待克隆隔离资产区参考 Shader Usage Analyzer：
  - 显示对象数量。
  - 每行一个 `ObjectField` 和一个 `移除` 按钮。
  - 只提供 `清空对象`，新增对象统一通过拖拽区域拾取。
  - 提供拖拽区域。
- 列表为空时，预览计划会提示先拖入待克隆隔离对象。
- 结果区使用 `ArtToolContext.ShowCustomReportView` 展示自定义关系预览，同时保留 `ArtToolReport` 作为应用和 CSV 导出数据。
- 每个待克隆隔离对象显示为一个 foldout，顶部展示 `新建目标 / 覆盖已有目标 / 外部共享 / 外部迁移 / TargetRoot 修复 / 显式共享 / 阻断 / 警告` 摘要。
- Foldout 内部分为 `下游依赖`、`直接上游引用`、`共享依赖引用`、`问题与风险`、`写入清单`。
- `直接上游引用` 只使用 root 资产本身 GUID 查找引用；`共享依赖引用` 使用下游依赖 GUID 查找共用 shader/贴图等风险，默认折叠显示。
- `共享依赖引用` 默认显示类型统计和前 20 条；路径过滤或选择共享依赖筛选时显示所有匹配项。
- `写入清单` 分为 `新建目标资产`、`覆盖已有目标并保留 GUID`、`外部依赖迁移`、`TargetRoot GUID 修复`、`共享风险，不写入`。
- 每行关系资产只提供 `定位`；如果映射后的目标路径已有资产，还提供 `定位已有目标`。
- `定位已有目标` 表示应用计划时会覆盖该目标资产内容并保留目标 `.meta` GUID。
- 点击 `留在原地` 会弹确认框，提示该资源不会克隆且 TargetRoot 会继续依赖 SourceRoot。
- SourceRoot 外的 `Assets/...` 依赖默认显示为 `外部共享`，可点击 `迁移到目标` 切换为 `外部迁移`；迁移目标固定为 `TargetRoot/_External/Assets/...`。
- 右侧帮助区通过 `HelpText` 展示详细功能介绍和显式共享风险。

## 数据流

1. UI 将 Project 资源、文件夹或 Hierarchy prefab 实例解析为 Project asset path。
2. `AssetCloneIsolationOptions.SelectedAssetPaths` 保存规范化后的 asset path。
3. `AssetCloneIsolationOptions.ExplicitSharedAssetPaths` 保存用户主动标记为留在原地的依赖。
4. `AssetCloneIsolationOptions.ExplicitCloneExternalAssetPaths` 保存用户主动标记为迁移到目标的外部 `Assets/...` 依赖。
5. `AssetCloneIsolationService.BuildPlan` 只读生成计划。
6. 计划同时输出平铺写入清单和按 root 分组的关系视图数据。
7. 计划无错误时，报告中添加唯一写入操作 `应用资产克隆隔离计划`。
8. 用户点击 `应用计划` 后，TA Art Tools 调用模块 `Apply`，最终执行 `AssetCloneIsolationService.ApplyPlan`。
9. 用户点击 `审计 TargetRoot` 后，模块调用 `AuditTargetRoot(targetRoot, sourceRoot, explicitSharedAssetPaths)` 并展示只读审计报告。

## 核心接口

- `AssetCloneIsolationOptions`：保存源根、目标根、选中路径、显式共享路径、外部迁移路径、覆盖目标内容开关和目标目录修复开关。
- `AssetCloneIsolationPlan`：保存克隆项、GUID 映射、共享依赖、外部共享风险、外部迁移依赖、目标目录修复项、按 root 分组关系、错误、警告和信息。
- `AssetCloneIsolationRootPlan`：保存一个待克隆隔离对象的下游依赖、直接上游引用、共享依赖引用、相关写入项和风险汇总。
- `AssetCloneIsolationPlanSummary`：保存完整计划的新建、覆盖、外部共享、外部迁移、修复、显式共享、阻断和警告统计。
- `AssetCloneIsolationRootSummary`：保存单个 Root 的同类统计。
- `AssetCloneIsolationRelationNode`：保存一条资源关系的路径、GUID、目标路径、目标 GUID、资源类型、关系方向、深度、决策状态和说明。
- `AssetCloneIsolationService.BuildPlan(options)`：只读生成迁移计划和关系预览数据。
- `AssetCloneIsolationService.ApplyPlan(plan)`：写入克隆资源、写入 `.meta`、重写目标目录引用并刷新 AssetDatabase。
- `AssetCloneIsolationService.AuditTargetRoot(targetRoot, sourceRoot)`：只读审计目标目录隔离状态。
- `AssetCloneIsolationService.AuditTargetRoot(targetRoot, sourceRoot, explicitSharedAssetPaths)`：按显式共享 allowlist 审计目标目录。
- `AssetCloneIsolationReferenceIndex`：构建 SourceRoot/TargetRoot 文本 GUID 引用索引和直接依赖列表。
- `AssetCloneIsolationTargetResolver`：把 Unity 对象解析为 Project asset path，并负责路径去重。
- `AssetCloneIsolationPreset`：保存常用 `SourceRoot -> TargetRoot` 配置、显式共享路径和外部迁移路径。

## 安全策略

- 源目录文件不写入。
- 应用前必须有预览报告。
- 目标文件覆盖默认保留目标 GUID。
- 目标文件如果和源文件 GUID 相同，生成新的目标 GUID。
- SourceRoot 外的美术依赖默认作为外部共享风险保留，不自动搬运；用户显式选择后才迁移到 `TargetRoot/_External/Assets/...`。
- 二进制资源不做内部 GUID 重写。
- `TargetRoot` 已有文本资源只按本次 GUID 映射修复，不做其它重写。
- 显式共享只允许作用于依赖牵出的 `SourceRoot` 资源，外部依赖使用 `外部共享 / 外部迁移` 决策，直接选择的 root 资源始终克隆。
- 显式共享项不进入 GUID 映射，因此目标资源会保留对旧目录资源的引用；UI 和审计必须持续提示这是有意保留的隔离例外。

## 扩展点

- 增加多个 SourceRoot 到 TargetRoot 的批量映射。
- 增加 YooAsset、Addressables 或 AssetBundle 收集配置审计。
- 增加 CSV 批处理入口。
- 增加更细粒度 shader variant 统计。
- 增加可配置共享目录白名单。
- 增加完整依赖图编辑器；v1 先使用 foldout/table/filter 保证稳定和可维护。
