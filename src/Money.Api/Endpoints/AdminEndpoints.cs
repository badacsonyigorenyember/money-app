using System.Text;
using Money.Api.Infrastructure;
using Money.Application.Admin;
using Money.Application.Contracts;

namespace Money.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin").WithTags("Admin");

        admin.MapPost("/backup", async (
            CreateBackupHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(cancellationToken);
            return result.IsSuccess ? Results.Ok(result.Value) : DomainErrorResults.ToProblem(result);
        }).WithName("CreateBackup");

        admin.MapGet("/backups", async (
            ListBackupsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("ListBackups");

        // Staging only. The swap itself happens at the next start, before anything opens the
        // database - so the last thing this does is ask the host to stop. In desktop mode the
        // host relaunches; in server mode the operator restarts.
        admin.MapPost("/restore", (
            RestoreRequest? request, StageRestoreHandler handler, IHostApplicationLifetime lifetime) =>
        {
            var result = handler.Handle(request?.FileName);
            if (result.IsFailure) return DomainErrorResults.ToProblem(result);

            lifetime.StopApplication();
            return Results.Accepted();
        }).WithName("StageRestore");

        admin.MapGet("/export", async (
            string? format, ExportLedgerHandler handler, CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(format, cancellationToken);
            if (result.IsFailure) return DomainErrorResults.ToProblem(result);

            var (content, contentType, fileName) = result.Value;
            return Results.File(Encoding.UTF8.GetBytes(content), contentType, fileName);
        }).WithName("ExportLedger");

        admin.MapPost("/integrity-check", async (
            RunIntegrityCheckHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(cancellationToken)))
            .WithName("RunIntegrityCheck");

        return group;
    }
}
