using AiLocal.Core.Discovery;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

public class PairingCoordinatorTests
{
    [Fact]
    public void Discovered_FiltersByRole()
    {
        var pairing = new PairingCoordinator();
        pairing.NoteDiscovered(new DiscoveryBeacon("h1", "Host1", NodeRole.Host, "http://h1:5080"));
        pairing.NoteDiscovered(new DiscoveryBeacon("w1", "Worker1", NodeRole.Worker, "http://w1:5081"));

        var workers = pairing.Discovered(NodeRole.Worker);

        Assert.Single(workers);
        Assert.Equal("w1", workers[0].Id);
    }

    [Fact]
    public void BeginOutbound_ThenCompleteWithMatchingNonce_Succeeds()
    {
        var pairing = new PairingCoordinator();

        var nonce = pairing.BeginOutbound("w1", "Worker1", "http://w1:5081");
        var completed = pairing.TryCompleteOutbound("w1", nonce, out var request);

        Assert.True(completed);
        Assert.Equal("Worker1", request.PeerName);
    }

    [Fact]
    public void CompleteOutbound_WrongNonce_Fails()
    {
        var pairing = new PairingCoordinator();

        pairing.BeginOutbound("w1", "Worker1", "http://w1:5081");
        var completed = pairing.TryCompleteOutbound("w1", "not-the-real-nonce", out _);

        Assert.False(completed);
    }

    [Fact]
    public void CompleteOutbound_UnknownPeerId_Fails()
    {
        var pairing = new PairingCoordinator();

        var nonce = pairing.BeginOutbound("w1", "Worker1", "http://w1:5081");
        var completed = pairing.TryCompleteOutbound("someone-else", nonce, out _);

        Assert.False(completed);
    }

    [Fact]
    public void CompleteOutbound_IsSingleUse()
    {
        // Prevents replay: once a nonce is spent, it must not validate again.
        var pairing = new PairingCoordinator();
        var nonce = pairing.BeginOutbound("w1", "Worker1", "http://w1:5081");

        Assert.True(pairing.TryCompleteOutbound("w1", nonce, out _));
        Assert.False(pairing.TryCompleteOutbound("w1", nonce, out _));
    }

    [Fact]
    public void Inbound_AddThenAccept_RemovesFromPending()
    {
        var pairing = new PairingCoordinator();

        pairing.AddInbound("h1", "Host1", "http://h1:5080", "abc123");
        Assert.Single(pairing.PendingInbound());

        var taken = pairing.TakeInbound("h1");

        Assert.NotNull(taken);
        Assert.Equal("abc123", taken!.Nonce);
        Assert.Empty(pairing.PendingInbound());
    }

    [Fact]
    public void Inbound_Reject_RemovesFromPendingWithoutReturningIt()
    {
        var pairing = new PairingCoordinator();

        pairing.AddInbound("h1", "Host1", "http://h1:5080", "abc123");
        pairing.RejectInbound("h1");

        Assert.Empty(pairing.PendingInbound());
        Assert.Null(pairing.TakeInbound("h1"));
    }

    [Fact]
    public void Get_ReturnsMostRecentBeaconForThatId()
    {
        var pairing = new PairingCoordinator();
        pairing.NoteDiscovered(new DiscoveryBeacon("w1", "Worker1", NodeRole.Worker, "http://old:5081"));
        pairing.NoteDiscovered(new DiscoveryBeacon("w1", "Worker1", NodeRole.Worker, "http://new:5081"));

        var peer = pairing.Get("w1");

        Assert.NotNull(peer);
        Assert.Equal("http://new:5081", peer!.Endpoint);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var pairing = new PairingCoordinator();

        Assert.Null(pairing.Get("never-seen"));
    }
}
