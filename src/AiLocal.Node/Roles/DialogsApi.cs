using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

public sealed record PickFolderRequest(string? InitialDirectory = null);

/// <summary>
/// Native-OS UI helpers for the dashboard, starting with a folder picker (see
/// NativeDialogs) - local-only, same trust level as /api/sessions (a local
/// browser tab needs this without a cluster token; the dialog itself just
/// hands back a path string the operator could already see by browsing their
/// own filesystem, no new exposure).
/// </summary>
public static class DialogsApi
{
    public static void MapEndpoints(WebApplication app)
    {
        app.MapPost("/api/dialogs/pick-folder", async (PickFolderRequest? req) =>
        {
            if (!NativeDialogs.IsSupported)
                return Results.Problem(
                    detail: "Bläddra stöds bara på Windows just nu - skriv sökvägen manuellt.",
                    statusCode: StatusCodes.Status501NotImplemented);

            var path = await NativeDialogs.PickFolderAsync(req?.InitialDirectory);
            return Results.Ok(new { cancelled = path is null, path });
        });
    }
}
