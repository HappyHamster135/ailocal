using System.Text.Json;
using AiLocal.Core.Nodes;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>v1.42.0: uppdragskön (ett bygge i taget per nod, resten köar med
/// synlig position) och clusterToken-maskeringen i nodlist-svaren.</summary>
public class QueueAndRedactionTests
{
    [Fact]
    public async Task AssignmentQueue_AndraKorningenKoas_MedPosition_OchSlappsFramEfterForsta()
    {
        var queue = new AssignmentQueue();

        var first = await queue.EnterAsync(_ => throw new Exception("första ska aldrig köas"), CancellationToken.None);
        Assert.True(queue.Busy);

        var queuedPosition = -1;
        var secondTask = queue.EnterAsync(position =>
        {
            queuedPosition = position;
            return Task.CompletedTask;
        }, CancellationToken.None);

        // Andra körningen står i kön tills första släpper.
        await Task.Delay(150);
        Assert.False(secondTask.IsCompleted);
        Assert.Equal(1, queuedPosition);
        Assert.Equal(1, queue.WaitingCount);

        first.Dispose();
        var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(queue.Busy);
        Assert.Equal(0, queue.WaitingCount);
        second.Dispose();
        Assert.False(queue.Busy);
    }

    [Fact]
    public async Task AssignmentQueue_DubbelDispose_SlapperBaraEnGang()
    {
        var queue = new AssignmentQueue();
        var slot = await queue.EnterAsync(null, CancellationToken.None);
        slot.Dispose();
        slot.Dispose(); // får inte ge SemaphoreFullException eller extra slot
        var next = await queue.EnterAsync(null, CancellationToken.None);
        Assert.True(queue.Busy);
        next.Dispose();
    }

    [Fact]
    public async Task AssignmentQueue_AvbrutenVantan_LamnarKonRen()
    {
        var queue = new AssignmentQueue();
        var holder = await queue.EnterAsync(null, CancellationToken.None);

        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => queue.EnterAsync(null, cts.Token));
        Assert.Equal(0, queue.WaitingCount);

        holder.Dispose();
        (await queue.EnterAsync(null, CancellationToken.None)).Dispose();
    }

    [Fact]
    public void Redacted_NollarKlustertoken_MenBevararAlltAnnat()
    {
        var node = new NodeInfo
        {
            Id = "host-abc",
            Name = "Stationara",
            Endpoint = "http://10.0.0.5:5080",
            Role = NodeRole.Host,
            ClusterToken = "hemlig-admin-token-1234567890abcdef",
            Version = "1.42.0"
        };

        var redacted = node.Redacted();

        Assert.Null(redacted.ClusterToken);
        Assert.Equal("host-abc", redacted.Id);
        Assert.Equal("Stationara", redacted.Name);
        Assert.Equal("1.42.0", redacted.Version);
        // Originalet rörs inte - announce-vägen serialiserar det med token.
        Assert.Equal("hemlig-admin-token-1234567890abcdef", node.ClusterToken);

        // Slutbeviset: det serialiserade svaret innehåller aldrig tokenvärdet.
        var json = JsonSerializer.Serialize(redacted);
        Assert.DoesNotContain("hemlig-admin-token", json);
    }
}
