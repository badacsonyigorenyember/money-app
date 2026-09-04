using Money.Application.Abstractions;

namespace Money.Infrastructure.Identity;

/// <summary>Desktop mode has one user and no login. Server mode (phase 9) replaces this.</summary>
public sealed class LocalCurrentUser : ICurrentUser
{
    public string DisplayName => "Local user";
}
