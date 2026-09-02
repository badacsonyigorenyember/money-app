namespace Money.Application.Contracts;

public sealed record CategoryNodeDto(
    Guid Id, string Name, string Path, string Kind, bool IsArchived,
    string? ColorHex, string? Icon, IReadOnlyList<CategoryNodeDto> Children);

public sealed record CreateCategoryRequest(string Name, string Kind, Guid? ParentCategoryId);
