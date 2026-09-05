using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;

namespace Money.Api.Pages;

public sealed class CategoriesModel(
    GetCategoryTreeHandler tree,
    CreateCategoryHandler create,
    PatchAccountHandler patch,
    ArchiveAccountHandler archive) : PageModel
{
    public IReadOnlyList<CategoryNodeDto> Nodes { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "Expense";
    [BindProperty(SupportsGet = true)] public bool IncludeArchived { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) => await LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(
        [FromForm] string name, [FromForm] Guid? parentCategoryId, CancellationToken cancellationToken)
    {
        var result = await create.HandleAsync(
            new CreateCategoryRequest(name, Kind, parentCategoryId), cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_CategoryTree", this);
    }

    public async Task<IActionResult> OnPostRenameAsync(
        Guid id, [FromForm] string name, CancellationToken cancellationToken)
    {
        var result = await patch.HandleAsync(
            id, new PatchAccountRequest(name, null, null, null, null, null, null), cancellationToken);

        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_CategoryTree", this);
    }

    public async Task<IActionResult> OnPostArchiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await archive.HandleAsync(id, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;

        await LoadAsync(cancellationToken);
        return Partial("Shared/_CategoryTree", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken) =>
        Nodes = await tree.HandleAsync(Kind, IncludeArchived, cancellationToken);
}
