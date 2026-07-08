using System.Collections.Generic;

namespace AssetCloneIsolation.Editor
{
    /// <summary>
    /// Audits target roots for clone isolation leaks and art asset risks.
    /// </summary>
    internal static class AssetCloneIsolationAuditService
    {
        /// <summary>
        /// Audits the target root using the default source root.
        /// </summary>
        public static AssetCloneIsolationAuditReport AuditTargetRoot(string targetRoot)
        {
            return AuditTargetRoot(targetRoot, AssetCloneIsolationOptions.DefaultSourceRoot);
        }

        /// <summary>
        /// Audits the target root using a specific source root.
        /// </summary>
        public static AssetCloneIsolationAuditReport AuditTargetRoot(string targetRoot, string sourceRoot)
        {
            return AuditTargetRoot(targetRoot, sourceRoot, null);
        }

        /// <summary>
        /// Audits the target root while treating explicit source dependencies as intentional shared risks.
        /// </summary>
        public static AssetCloneIsolationAuditReport AuditTargetRoot(
            string targetRoot,
            string sourceRoot,
            IReadOnlyCollection<string> explicitSharedAssetPaths)
        {
            return AssetCloneIsolationService.AuditTargetRootCore(targetRoot, sourceRoot, explicitSharedAssetPaths);
        }
    }
}
