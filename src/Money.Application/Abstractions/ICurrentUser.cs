namespace Money.Application.Abstractions;

/// <summary>
/// Present so that server mode (phase 9) is a registration change. There are deliberately no
/// OwnerId columns anywhere (spec D12).
/// </summary>
public interface ICurrentUser
{
    string DisplayName { get; }
}
