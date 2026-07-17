using AiLocal.Core.Configuration;
using AiLocal.Node.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace AiLocal.Node.Roles;

/// <summary>P3: opt-in desktop control. The agent (via the Studio "Skärm" tab)
/// can capture the screen and click/type on this machine - so it can actually
/// SEE what a built game looks like and drive it. Every call is refused
/// unless Worker.AllowDesktopControl is on (default OFF) - the operator
/// enables it deliberately in Settings.</summary>
public static class DesktopApi
{
    public static void MapEndpoints(WebApplication app)
    {
        // All desktop endpoints require explicit opt-in.
        app.MapGet("/api/desktop/screenshot", (DesktopControlService svc, NodeSettings settings) =>
        {
            if (!settings.Worker.AllowDesktopControl)
                return Results.Problem(detail: "Skärmkontroll är avstängd. Slå på 'Tillåt skärmkontroll' i Inställningar.", statusCode: StatusCodes.Status403Forbidden);
            var png = svc.CaptureScreen();
            return png is null
                ? Results.Problem(detail: "Skärmdump misslyckades: " + (svc.LastError ?? "okänt fel"), statusCode: StatusCodes.Status501NotImplemented)
                : Results.File(png, "image/png");
        });

        app.MapPost("/api/desktop/click", (DesktopControlService svc, NodeSettings settings, HttpContext ctx, CancellationToken ct) =>
        {
            if (!settings.Worker.AllowDesktopControl)
                return Results.Problem(detail: "Skärmkontroll är avstängd.", statusCode: StatusCodes.Status403Forbidden);
            var body = ctx.Request.ReadFromJsonAsync<DesktopPointRequest>(ct).Result;
            if (body is null) return Results.Problem(detail: "x + y krävs", statusCode: StatusCodes.Status400BadRequest);
            svc.Click(body.X, body.Y);
            return Results.Ok(new { clicked = true, x = body.X, y = body.Y });
        });

        app.MapPost("/api/desktop/type", (DesktopControlService svc, NodeSettings settings, HttpContext ctx, CancellationToken ct) =>
        {
            if (!settings.Worker.AllowDesktopControl)
                return Results.Problem(detail: "Skärmkontroll är avstängd.", statusCode: StatusCodes.Status403Forbidden);
            var body = ctx.Request.ReadFromJsonAsync<DesktopTypeRequest>(ct).Result;
            if (body is null || string.IsNullOrEmpty(body.Text))
                return Results.Problem(detail: "text krävs", statusCode: StatusCodes.Status400BadRequest);
            svc.TypeText(body.Text);
            return Results.Ok(new { typed = true, length = body.Text.Length });
        });
    }

    private sealed record DesktopPointRequest(int X, int Y);
    private sealed record DesktopTypeRequest(string? Text);
}
