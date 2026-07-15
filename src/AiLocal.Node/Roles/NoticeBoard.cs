using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

/// <summary>
/// In-memory event board for operator-facing notices (goal done / goal failed /
/// agent needs you / worker down). Wraps <see cref="HostStateStore"/> so notices
/// survive a Host restart, and caps the retained list so it can't grow forever.
/// Static (not DI) so the Host's static dispatch methods can fire events without
/// threading the board through every signature; the store is set once at startup.
/// </summary>
public static class NoticeBoard
{
    private static readonly object Gate = new();
    private static HostStateStore? _store;
    private const int MaxNotices = 100;

    public static void Bind(HostStateStore store) => _store = store;

    public static void Add(NoticeType type, string message, string? refId = null)
    {
        var notice = new HostNotice(type, message, refId);
        List<HostNotice> list;
        lock (Gate)
        {
            list = _store is null ? new List<HostNotice>() : [.. _store.ReadNotices()];
            list.Add(notice);
            if (list.Count > MaxNotices)
                list = list.Skip(list.Count - MaxNotices).ToList();
        }
        _store?.SaveNotices(list);
    }

    public static IReadOnlyList<HostNotice> All()
    {
        lock (Gate)
            return _store is null ? [] : [.. _store.ReadNotices()];
    }

    public static void Clear() => _store?.SaveNotices([]);
}
