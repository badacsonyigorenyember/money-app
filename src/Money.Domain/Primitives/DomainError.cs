namespace Money.Domain.Primitives;

/// <summary>An expected failure. Programmer errors throw; these are returned.</summary>
public sealed record DomainError(string Code, string Message);
