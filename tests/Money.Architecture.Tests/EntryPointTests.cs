namespace Money.Architecture.Tests;

/// <summary>
/// The window is the only client, so the window is the only thing that starts. Money.Api holds
/// the Razor Pages and the composition root, but it is a library: nothing can serve them to a
/// browser, not even by accident from a shell.
/// </summary>
public sealed class EntryPointTests
{
    [Fact]
    public void Money_Api_cannot_be_started_on_its_own()
    {
        typeof(Money.Api.MoneyWebApp).Assembly.EntryPoint.Should().BeNull(
            "Money.Desktop is the only entry point; an exe here is a web server nobody asked for");
    }
}
