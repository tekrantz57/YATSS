using System.Reflection;
using System.Text.RegularExpressions;

namespace YATSS
{
    internal static partial class BuildIdentity
    {
        private const string MetadataKey = "GitBuildDescription";

        internal static string GetDisplayVersion(string productVersion)
        {
            string? gitDescription = typeof(BuildIdentity).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == MetadataKey)
                ?.Value;
            return Normalize(gitDescription, productVersion);
        }

        internal static string Normalize(string? gitDescription, string productVersion)
        {
            string fallback = $"v{productVersion.Split('+', 2)[0]}";
            if (string.IsNullOrWhiteSpace(gitDescription))
            {
                return fallback;
            }

            string description = gitDescription.Trim();
            if (!description.EndsWith("-dirty", StringComparison.OrdinalIgnoreCase))
            {
                description = ExactTagSuffix().Replace(description, string.Empty);
            }

            return description.StartsWith('v')
                ? description
                : $"git-{description}";
        }

        [GeneratedRegex(@"-0-g[0-9a-f]+$", RegexOptions.IgnoreCase)]
        private static partial Regex ExactTagSuffix();
    }
}
