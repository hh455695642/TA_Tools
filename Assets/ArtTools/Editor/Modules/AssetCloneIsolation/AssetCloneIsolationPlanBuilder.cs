using System;

namespace AssetCloneIsolation.Editor
{
    /// <summary>
    /// Builds read-only clone isolation plans from user options.
    /// </summary>
    internal static class AssetCloneIsolationPlanBuilder
    {
        /// <summary>
        /// Builds a clone isolation plan without writing project files.
        /// </summary>
        public static AssetCloneIsolationPlan BuildPlan(AssetCloneIsolationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            return AssetCloneIsolationService.BuildPlanCore(options);
        }
    }
}
