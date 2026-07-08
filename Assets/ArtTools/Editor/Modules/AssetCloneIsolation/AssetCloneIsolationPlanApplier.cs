using System;

namespace AssetCloneIsolation.Editor
{
    /// <summary>
    /// Applies previously previewed clone isolation plans to project files.
    /// </summary>
    internal static class AssetCloneIsolationPlanApplier
    {
        /// <summary>
        /// Writes cloned assets, cloned meta files, and target-root GUID rewrites.
        /// </summary>
        public static void ApplyPlan(AssetCloneIsolationPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException("plan");
            }

            AssetCloneIsolationService.ApplyPlanCore(plan);
        }
    }
}
