namespace AiLocal.Node.Hosting;

public enum RegistrationState
{
    Unknown,
    Connected,
    Unauthorized,
    Unreachable
}

/// <summary>
/// Tracks the outcome of this Worker/Overseer's most recent attempt to reach
/// its Host, so a bad or missing cluster token surfaces in the dashboard
/// instead of only ever appearing in a debug log line.
/// </summary>
public sealed class RegistrationStatus
{
    private readonly object _gate = new();
    private RegistrationState _state = RegistrationState.Unknown;
    private string? _detail;

    public void Set(RegistrationState state, string? detail = null)
    {
        lock (_gate)
        {
            _state = state;
            _detail = detail;
        }
    }

    public (RegistrationState State, string? Detail) Read()
    {
        lock (_gate)
            return (_state, _detail);
    }
}
