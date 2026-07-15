using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

public sealed record GitCommitRequest(string Message);

/// <summary>
/// Git awareness for agent sessions. These endpoints act on a session's
/// FolderPath via <see cref="GitService"/> - they never touch a remote
/// (no push/pull/fetch), on purpose: an operator approving a *local* commit
/// from their own dashboard is a reasonable, low-risk action, but pushing to
/// a shared remote changes state other people can see and wasn't in scope.
/// Pushing, if ever wanted, is a separate, explicit decision - not something
/// this surface should slip into doing.
///
/// Read-only by default: status + diff need no operator input; commit takes a
/// message and is the only mutating call here.
/// </summary>
public static class GitApi
{
    public static void MapEndpoints(WebApplication app)
    {
        var group = app.MapGroup("/api/sessions/{id}/git");

        group.MapGet("/status", async (string id, SessionStore store, GitService git, CancellationToken ct) =>
            store.Get(id) is { } session
                ? Results.Ok(await git.GetStatusAsync(session.FolderPath, ct))
                : Results.NotFound());

        group.MapGet("/diff", async (string id, bool staged, SessionStore store, GitService git, CancellationToken ct) =>
            store.Get(id) is { } session
                ? Results.Ok(new { diff = await git.GetDiffAsync(session.FolderPath, staged, ct) })
                : Results.NotFound());

        group.MapPost("/commit", async (string id, GitCommitRequest req, SessionStore store, GitService git, CancellationToken ct) =>
        {
            var session = store.Get(id);
            if (session is null)
                return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Message))
                return Results.Problem(detail: "commit message is required", statusCode: StatusCodes.Status400BadRequest);

            var result = await git.CommitAsync(session.FolderPath, req.Message, ct);
            return result.Success
                ? Results.Ok(new { success = true, output = result.Output })
                : Results.Problem(detail: result.Output, statusCode: StatusCodes.Status400BadRequest);
        });
    }
}
