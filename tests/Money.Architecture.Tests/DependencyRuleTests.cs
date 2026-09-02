using NetArchTest.Rules;

namespace Money.Architecture.Tests;

public sealed class DependencyRuleTests
{
    private static readonly System.Reflection.Assembly Domain =
        typeof(Money.Domain.DomainAssemblyMarker).Assembly;

    private static readonly System.Reflection.Assembly Application =
        typeof(Money.Application.Abstractions.IUnitOfWork).Assembly;

    [Fact]
    public void Domain_references_nothing_but_the_base_class_library()
    {
        var offenders = Domain.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                        && name != "netstandard"
                        && name != "mscorlib")
            .ToArray();

        offenders.Should().BeEmpty(
            "Money.Domain must have zero project and third-party dependencies");
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_api()
    {
        var offenders = Application.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name is "Money.Infrastructure" or "Money.Api")
            .ToArray();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Domain_types_do_not_depend_on_application_or_higher()
    {
        Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny("Money.Application", "Money.Infrastructure", "Money.Api")
            .GetResult().IsSuccessful.Should().BeTrue();
    }
}
