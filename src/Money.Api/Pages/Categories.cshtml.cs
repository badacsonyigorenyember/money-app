using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Money.Application.Abstractions;
using Money.Application.Accounts;
using Money.Application.Categories;
using Money.Application.Contracts;

namespace Money.Api.Pages;

public sealed class CategoriesModel(
    GetCategoryTreeHandler tree,
    CreateCategoryHandler create,
    PatchAccountHandler patch,
    ArchiveAccountHandler archive,
    RestoreAccountHandler restore,
    DeleteAccountHandler delete,
    ILedgerQueries queries) : PageModel
{
    public sealed record ArchivedRow(CategoryNodeDto Category, int Entries);

    public IReadOnlyList<CategoryNodeDto> Nodes { get; private set; } = [];
    public IReadOnlyList<ArchivedRow> Archived { get; private set; } = [];
    public string? ErrorMessage { get; private set; }
    public string? Message { get; private set; }

    [BindProperty(SupportsGet = true)] public string Kind { get; set; } = "Expense";

    public string KindLabel => Kind == "Income" ? "income" : "spending";

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

        if (result.IsSuccess)
        {
            var archived = Archived.FirstOrDefault(row => row.Category.Id == id);
            Message = archived is { Entries: 0 }
                ? $"{archived.Category.Name} is archived. Nothing in your history uses it, " +
                  "so deleting it will erase it completely."
                : "Archived. It is out of the way but its history is intact.";
        }

        return Partial("Shared/_CategoryTree", this);
    }


    public async Task<IActionResult> OnPostRestoreAsync(Guid id, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        var name = Archived.FirstOrDefault(row => row.Category.Id == id)?.Category.Name;

        var result = await restore.HandleAsync(id, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else Message = $"{name ?? "The category"} is back in your categories.";

        await LoadAsync(cancellationToken);
        return Partial("Shared/_CategoryTree", this);
    }
    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        // Read the name before the delete: afterwards there may be no row left to read it from.
        await LoadAsync(cancellationToken);
        var name = Archived.FirstOrDefault(row => row.Category.Id == id)?.Category.Name;

        var result = await delete.HandleAsync(id, cancellationToken);
        if (result.IsFailure) ErrorMessage = result.Error!.Message;
        else Message = $"{name ?? "The category"} has been deleted.";

        await LoadAsync(cancellationToken);
        return Partial("Shared/_CategoryTree", this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Nodes = await tree.HandleAsync(Kind, includeArchived: false, cancellationToken);

        var everything = await tree.HandleAsync(Kind, includeArchived: true, cancellationToken);

        var archived = new List<ArchivedRow>();
        foreach (var category in Flatten(everything).Where(node => node.IsArchived))
        {
            archived.Add(new ArchivedRow(
                category, await queries.SubtreeEntryCountAsync(category.Path, cancellationToken)));
        }

        Archived = archived;
    }

    private static IEnumerable<CategoryNodeDto> Flatten(IEnumerable<CategoryNodeDto> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
