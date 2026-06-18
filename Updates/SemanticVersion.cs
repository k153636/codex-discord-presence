using System.Text.RegularExpressions;

namespace CodexDiscordPresence;

public sealed record SemanticVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<SemanticVersion>
{
    private static readonly Regex VersionPattern = new(
        @"^\s*v?(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<prerelease>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var match = VersionPattern.Match(text);
        if (!match.Success)
        {
            return false;
        }

        version = new SemanticVersion(
            int.Parse(match.Groups["major"].Value),
            int.Parse(match.Groups["minor"].Value),
            int.Parse(match.Groups["patch"].Value),
            match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : null);
        return true;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        if (minorComparison != 0)
        {
            return minorComparison;
        }

        var patchComparison = Patch.CompareTo(other.Patch);
        if (patchComparison != 0)
        {
            return patchComparison;
        }

        if (string.IsNullOrWhiteSpace(PreRelease) && string.IsNullOrWhiteSpace(other.PreRelease))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(PreRelease))
        {
            return 1;
        }

        if (string.IsNullOrWhiteSpace(other.PreRelease))
        {
            return -1;
        }

        var leftIdentifiers = PreRelease.Split('.');
        var rightIdentifiers = other.PreRelease.Split('.');

        for (var index = 0; index < Math.Min(leftIdentifiers.Length, rightIdentifiers.Length); index++)
        {
            var leftIdentifier = leftIdentifiers[index];
            var rightIdentifier = rightIdentifiers[index];
            var leftNumeric = int.TryParse(leftIdentifier, out var leftNumber);
            var rightNumeric = int.TryParse(rightIdentifier, out var rightNumber);

            if (leftNumeric && rightNumeric)
            {
                var numberComparison = leftNumber.CompareTo(rightNumber);
                if (numberComparison != 0)
                {
                    return numberComparison;
                }

                continue;
            }

            if (leftNumeric)
            {
                return -1;
            }

            if (rightNumeric)
            {
                return 1;
            }

            var identifierComparison = string.CompareOrdinal(leftIdentifier, rightIdentifier);
            if (identifierComparison != 0)
            {
                return identifierComparison;
            }
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(PreRelease)
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{PreRelease}";
    }
}
