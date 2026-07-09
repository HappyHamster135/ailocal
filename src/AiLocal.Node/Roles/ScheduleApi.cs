using AiLocal.Core.Configuration;
using AiLocal.Core.Providers;
using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

public sealed record ScheduleCreateRequest(
    string Name,
    string Prompt,
    string? System,
    int? Parallelism,
    int IntervalMinutes,
    string? AtTimeOfDay,
    bool Enabled = true);

public sealed record ScheduleUpdateRequest(
    string? Name = null,
    string? Prompt = null,
    string? System = null,
    int? Parallelism = null,
    int? IntervalMinutes = null,
    string? AtTimeOfDay = null,
    bool? Enabled = null);

public static class ScheduleApi
{
    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/schedules", (ScheduleStore store) => Results.Ok(store.All()));

        app.MapPost("/api/schedules", (ScheduleCreateRequest req, ScheduleStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Prompt))
                return Results.BadRequest(new { error = "name and prompt are required" });

            var schedule = store.Create(req.Name.Trim(), req.Prompt, req.System, req.Parallelism,
                req.IntervalMinutes, req.AtTimeOfDay);
            if (!req.Enabled)
                store.Update(schedule.Id, s => s.Enabled = false);
            return Results.Ok(store.Get(schedule.Id));
        });

        app.MapPut("/api/schedules/{id}", (string id, ScheduleUpdateRequest req, ScheduleStore store) =>
        {
            var updated = store.Update(id, s =>
            {
                if (req.Name is { Length: > 0 }) s.Name = req.Name;
                if (req.Prompt is { Length: > 0 }) s.Prompt = req.Prompt;
                if (req.System is not null) s.System = req.System.Length == 0 ? null : req.System;
                if (req.Parallelism is not null) s.Parallelism = req.Parallelism;
                if (req.IntervalMinutes is not null) s.IntervalMinutes = Math.Max(1, req.IntervalMinutes.Value);
                if (req.AtTimeOfDay is not null) s.AtTimeOfDay = req.AtTimeOfDay.Length == 0 ? null : req.AtTimeOfDay;
                if (req.Enabled is not null) s.Enabled = req.Enabled.Value;
            });
            return updated ? Results.Ok(store.Get(id)) : Results.NotFound();
        });

        app.MapDelete("/api/schedules/{id}", (string id, ScheduleStore store) =>
            store.Remove(id) ? Results.NoContent() : Results.NotFound());

        app.MapPost("/api/schedules/{id}/run", (
            string id, ScheduleStore store, TaskBoard board, WorkerRegistry reg, IHttpClientFactory httpFactory,
            FallbackChatProvider providers, WorkerSlotBroker broker, TaskStreamHub streamHub,
            TaskCancellationRegistry cancellationRegistry, NodeSettings settings, ILoggerFactory loggerFactory) =>
        {
            var schedule = store.Get(id);
            if (schedule is null) return Results.NotFound();

            var log = loggerFactory.CreateLogger("schedule");
            var request = new SubmitTaskRequest(schedule.Prompt, schedule.System, schedule.Parallelism);
            var submission = HostRole.SubmitGoal(request, null, board, reg, httpFactory, providers, broker,
                streamHub, cancellationRegistry, settings, log);
            store.Update(id, s => s.LastRunAt = DateTimeOffset.UtcNow);
            return Results.Ok(new { submission.Root.Id, state = submission.Root.State.ToString() });
        });
    }
}
