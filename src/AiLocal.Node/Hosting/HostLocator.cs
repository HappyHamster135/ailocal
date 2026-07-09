namespace AiLocal.Node.Hosting;

/// <summary>Shared holder for the discovered (or configured) Host endpoint.</summary>
public sealed class HostLocator
{
    private volatile string? _endpoint;

    public string? HostEndpoint
    {
        get => _endpoint;
        set => _endpoint = value;
    }
}
