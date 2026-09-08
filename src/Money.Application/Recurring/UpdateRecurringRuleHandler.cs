using Money.Application.Abstractions;
using Money.Domain.Primitives;
using Money.Domain.Time;

namespace Money.Application.Recurring;

/// <summary>
/// Pause, resume or delete a rule. Deleting a rule never touches the transactions it already
/// posted: those are ordinary history, voided individually if the user wants them gone.
/// </summary>
public sealed class UpdateRecurringRuleHandler(
    IRecurringRuleRepository rules,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<Result> SetActiveAsync(
        Guid id, bool isActive, CancellationToken cancellationToken = default)
    {
        var rule = await rules.FindAsync(id, cancellationToken);
        if (rule is null) return Result.Fail(DomainErrors.Schedule.NotFound(id));

        rule.SetActive(isActive, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await rules.FindAsync(id, cancellationToken);
        if (rule is null) return Result.Fail(DomainErrors.Schedule.NotFound(id));

        rules.Remove(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
