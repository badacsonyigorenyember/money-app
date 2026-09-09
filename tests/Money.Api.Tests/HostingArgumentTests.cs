using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Money.Api;

namespace Money.Api.Tests;

/// <summary>
/// --server is the app's own flag, not a configuration key. These pin the fact that it never
/// reaches the command-line configuration provider, which has no notion of a valueless flag.
/// </summary>
public class HostingArgumentTests
{
    [Fact]
    public void The_server_flag_alone_would_swallow_the_urls_that_follow_it()
    {
        // Not a test of our code - a test of the premise. If this ever stops being true, the
        // filtering below is dead weight and should go.
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(["--server", "--urls", "http://127.0.0.1:5099"])
            .Build();

        configuration["urls"].Should().BeNull();
        configuration["server"].Should().Be("--urls");
    }

    [Fact]
    public void Stripping_the_server_flag_leaves_the_urls_pair_intact()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(MoneyWebApp.ConfigurationArgs(["--server", "--urls", "http://127.0.0.1:5099"]))
            .Build();

        configuration["urls"].Should().Be("http://127.0.0.1:5099");
    }

    [Fact]
    public void Stripping_the_server_flag_leaves_every_other_argument_alone()
    {
        MoneyWebApp.ConfigurationArgs(["--urls", "http://127.0.0.1:5099", "--server", "--environment", "Production"])
            .Should().Equal("--urls", "http://127.0.0.1:5099", "--environment", "Production");
    }

    [Fact]
    public void Arguments_without_the_server_flag_pass_through_unchanged()
    {
        MoneyWebApp.ConfigurationArgs(["--urls", "http://127.0.0.1:5099"])
            .Should().Equal("--urls", "http://127.0.0.1:5099");
    }
}
