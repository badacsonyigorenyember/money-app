using System.Text.RegularExpressions;

namespace Money.Architecture.Tests;

public sealed class RoundingTests
{
    // Money.cs defines RoundToMinor; DisplayAmountMapper.ToStored is the one call site allowed
    // to invoke it directly on a raw (displayed amount * MinorUnitScale) expression. Every other
    // "convert a decimal to minor units" need must go through ToStored, so the sign convention
    // only ever gets negated in the one place CLAUDE.md names.
    private static readonly string[] AllowedPathFragments =
    [
        Path.Combine("Money.Domain", "Money", "Money.cs"),
        Path.Combine("Money.Application", "Presentation", "DisplayAmountMapper.cs"),
    ];

    private static readonly Regex Forbidden = new(@"\bRoundToMinor\s*\(", RegexOptions.Compiled);

    [Fact]
    public void RoundToMinor_is_called_only_from_the_single_place_that_owns_scaling()
    {
        var src = Path.Combine(RepositoryRoot.Find(), "src");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !AllowedPathFragments.Any(a => f.Contains(a, StringComparison.Ordinal)))
            .Where(f => Forbidden.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(src, f))
            .ToArray();

        offenders.Should().BeEmpty(
            "a second raw RoundToMinor(displayed * MinorUnitScale) call site is a second " +
            "conversion path the sign convention does not run through (CLAUDE.md I2) - use " +
            "DisplayAmountMapper.ToStored instead");
    }
}
