using System.Collections.Generic;
using UnityEngine;

namespace TA.ArtTools.Editor
{
    /// <summary>
    /// Shader Usage Analyzer 的结果过滤规则工具。
    /// </summary>
    internal static class TaShaderUsageFilterUtility
    {
        /// <summary>
        /// 判断结果过滤是否启用。
        /// </summary>
        /// <param name="filterShader">用户指定的过滤 Shader。</param>
        /// <returns>过滤 Shader 不为空时返回 true。</returns>
        internal static bool IsFilterEnabled(Object filterShader)
        {
            return filterShader != null;
        }

        /// <summary>
        /// 判断一个扫描对象是否应该显示。
        /// </summary>
        /// <param name="slotShaders">扫描对象内所有材质槽使用的 Shader。</param>
        /// <param name="filterShader">用户指定的过滤 Shader。</param>
        /// <returns>未启用过滤或任一槽位精确命中过滤 Shader 时返回 true。</returns>
        internal static bool ShouldDisplayObject(IEnumerable<Object> slotShaders, Object filterShader)
        {
            if (!IsFilterEnabled(filterShader))
                return true;

            if (slotShaders == null)
                return false;

            foreach (Object slotShader in slotShaders)
            {
                if (slotShader == filterShader)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 统计一个扫描对象内命中过滤 Shader 的槽位数量。
        /// </summary>
        /// <param name="slotShaders">扫描对象内所有材质槽使用的 Shader。</param>
        /// <param name="filterShader">用户指定的过滤 Shader。</param>
        /// <returns>过滤未启用或没有命中时返回 0。</returns>
        internal static int CountMatchingSlots(IEnumerable<Object> slotShaders, Object filterShader)
        {
            if (!IsFilterEnabled(filterShader) || slotShaders == null)
                return 0;

            int matchCount = 0;
            foreach (Object slotShader in slotShaders)
            {
                if (slotShader == filterShader)
                    matchCount++;
            }

            return matchCount;
        }

        /// <summary>
        /// 判断单个材质槽是否应该显示在当前过滤结果中。
        /// </summary>
        /// <param name="slotShader">材质槽当前使用的 Shader。</param>
        /// <param name="filterShader">用户指定的过滤 Shader。</param>
        /// <param name="showNonFilterShaderSlots">是否在命中对象内显示未命中过滤 Shader 的槽位。</param>
        /// <returns>过滤未启用、允许显示非过滤槽位或槽位精确命中过滤 Shader 时返回 true。</returns>
        internal static bool ShouldDisplaySlot(Object slotShader, Object filterShader, bool showNonFilterShaderSlots)
        {
            return !IsFilterEnabled(filterShader)
                   || showNonFilterShaderSlots
                   || slotShader == filterShader;
        }

        /// <summary>
        /// 判断材质槽是否应该被“自动勾选过滤shader的材质”操作选中。
        /// </summary>
        /// <param name="slotShader">材质槽当前使用的 Shader。</param>
        /// <param name="filterShader">用户指定的过滤 Shader。</param>
        /// <param name="editable">材质槽是否允许直接编辑材质资源。</param>
        /// <param name="isPackageDefaultLitMaterial">材质槽是否引用 URP 包内默认 Lit.mat。</param>
        /// <returns>材质槽可编辑、不是包内默认材质且精确命中过滤 Shader 时返回 true。</returns>
        internal static bool ShouldAutoSelectSlot(Object slotShader, Object filterShader, bool editable, bool isPackageDefaultLitMaterial)
        {
            return editable
                   && !isPackageDefaultLitMaterial
                   && IsFilterEnabled(filterShader)
                   && slotShader == filterShader;
        }
    }
}
