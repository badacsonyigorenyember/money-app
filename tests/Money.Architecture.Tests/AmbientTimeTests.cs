using System.Text.RegularExpressions;

namespace Money.Architecture.Tests;

public sealed class AmbientTimeTests
{
    private static readonly string[] AllowedPathFragments =
    [
        Path.Combine("Money.Infrastructure", "Time", "SystemClock.cs"),
        Path.Combine("Money.Desktop", "")
    ];

    private static readonly Regex Forbidden = new(
        @"DateTime\.Now|DateTime\.UtcNow|DateTimeOffset\.Now|DateTimeOffset\.UtcNow|DateOnly\.FromDateTime\s*\(\s*DateTime\.",
        RegexOptions.Compiled);

    [Fact]
    public void Ambient_time_is_only_read_in_the_composition_root()
    {
        var src = Path.Combine(RepositoryRoot.Find(), "src");

        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !AllowedPathFragments.Any(a => f.Contains(a, StringComparison.Ordinal)))
            .Where(f => Forbidden.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(src, f))
            .ToArray();

        offenders.Should().BeEmpty("time must be injected via IClock (spec section 7)");
    }
}
