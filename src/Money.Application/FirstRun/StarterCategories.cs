namespace Money.Application.FirstRun;

/// <summary>The starter tree from spec section 10. The user renames or deletes freely.</summary>
public static class StarterCategories
{
    public static IReadOnlyList<string> Expense { get; } =
    [
        "Housing", "Groceries", "Eating out", "Alcohol", "Gaming",
        "Transport", "Health", "Subscriptions", "Other"
    ];

    public static IReadOnlyList<string> Income { get; } = ["Salary", "Other income"];
}
