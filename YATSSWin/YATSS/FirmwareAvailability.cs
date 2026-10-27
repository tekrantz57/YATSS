using System.Globalization;
using System.Text.RegularExpressions;

namespace YATSS;

internal sealed record FirmwareUpdateOffer(string Board, string Installed, string Available)
{
    internal string Key(ControllerIdentity identity, string port) =>
        $"{port}|{identity.BoardProfile}|{identity.FlashCapacityBytes}|{Installed}|{Available}";
}

internal static class FirmwareAvailability
{
    internal static FirmwareUpdateOffer? Find(ControllerIdentity? identity,
        IReadOnlyList<ControllerFirmwarePackage> packages)
    {
        if (identity?.FlashCapacityBytes is not > 0 || !identity.HasBoardProfile) return null;
        var matches = packages.Where(package => package.Matches(identity)).ToArray();
        if (matches.Length != 1) return null;
        var target = matches[0].Manifest;
        return Offer(identity, target.BoardDisplayName, target.FirmwareVersion);
    }

    internal static FirmwareUpdateOffer? Offer(ControllerIdentity identity, string board, string available) =>
        Compare(available, identity.FirmwareVersion) is > 0
            ? new(board, identity.FirmwareVersion, available) : null;

    // Semantic ordering, including numeric prerelease identifiers; unknown versions are not guessed.
    internal static int? Compare(string available, string installed)
    {
        var left = Parse(available); var right = Parse(installed);
        if (left == null || right == null) return null;
        for (int index = 0; index < 3; index++)
        {
            int comparison = left.Value.Core[index].CompareTo(right.Value.Core[index]);
            if (comparison != 0) return comparison;
        }
        string[] a = left.Value.Pre, b = right.Value.Pre;
        if (a.Length == 0 || b.Length == 0) return a.Length == b.Length ? 0 : a.Length == 0 ? 1 : -1;
        for (int index = 0; index < Math.Min(a.Length, b.Length); index++)
        {
            bool numericA = a[index].All(char.IsAsciiDigit), numericB = b[index].All(char.IsAsciiDigit);
            int comparison = numericA && numericB
                ? a[index].Length != b[index].Length ? a[index].Length.CompareTo(b[index].Length) : string.CompareOrdinal(a[index], b[index])
                : numericA != numericB ? numericA ? -1 : 1 : string.CompareOrdinal(a[index], b[index]);
            if (comparison != 0) return comparison;
        }
        return a.Length.CompareTo(b.Length);
    }

    private static (uint[] Core, string[] Pre)? Parse(string value)
    {
        if (value.Length > 128) return null;
        var match = Regex.Match(value, @"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z");
        if (!match.Success) return null;
        uint[] core = new uint[3];
        for (int index = 0; index < 3; index++)
            if (!uint.TryParse(match.Groups[index + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out core[index])) return null;
        string[] pre = match.Groups[4].Success ? match.Groups[4].Value.Split('.') : Array.Empty<string>();
        if (pre.Any(part => part.Length > 1 && part[0] == '0' && part.All(char.IsAsciiDigit))) return null;
        return (core, pre);
    }
}
