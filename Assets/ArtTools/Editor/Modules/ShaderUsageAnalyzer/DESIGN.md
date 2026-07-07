# Shader Usage Analyzer 过滤功能设计

## 设计思路

过滤逻辑独立在 `TaShaderUsageFilterUtility` 中，窗口代码只负责收集扫描对象的槽位 Shader、渲染当前可见结果并执行用户操作。过滤分为对象级和槽位级两层：对象级决定扫描对象是否进入结果，槽位级决定命中对象内哪些材质槽显示和参与替换。

## 数据流

1. 原有扫描流程保持不变，`results` 保存完整扫描结果。
2. `resultFilterShader` 保存当前过滤 Shader；为空表示未启用过滤。
3. `showNonFilterShaderSlots` 保存槽位显示策略，默认 `false`。
4. `GetVisibleResults()` 只隐藏未命中过滤 Shader 的对象。
5. `GetVisibleSlots()` 在可见对象内按 `showNonFilterShaderSlots` 返回当前可见槽位。
6. 右侧结果、`Scan()` 报告、摘要统计和 Foldout 标题都基于当前可见槽位生成。

## 替换安全

`ReplaceSelectedMaterialShaders` 从当前可见槽位中收集已勾选材质。过滤启用且 `显示非过滤 Shader 槽位` 关闭时，隐藏槽位即使保留旧勾选状态，也不会被本次替换处理，避免窄范围排查时误改不可见项。

## 自动勾选

`自动勾选过滤shader的材质` 依赖当前 `resultFilterShader`。槽位必须可编辑、不是 URP 包内默认 Lit.mat，并且槽位 Shader 与过滤 Shader 精确相同，才会被自动勾选。该操作不受 `显示非过滤 Shader 槽位` 影响。

## UI 文案

`默认 Lit 替换材质` 改为 `替换目标材质`，下方按钮改为 `将默认 Lit 引用替换为目标材质`。底层行为仍是把 Prefab 中对 URP 包内默认 Lit.mat 的引用替换为用户指定材质。

## Variant 风险

该功能只修改 Editor 工具 UI 和 C# 过滤逻辑，不新增 Shader、keyword、multi_compile 或 shader_feature。Shader variant 增量为 0。
