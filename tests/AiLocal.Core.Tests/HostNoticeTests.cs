using AiLocal.Core.Contracts;
using Xunit;

namespace AiLocal.Core.Tests;

public class HostNoticeTests
{
    [Fact]
    public void NoticeType_HasTheFourOperatorEvents()
    {
        var values = Enum.GetNames<NoticeType>();
        Assert.Contains("TaskDone", values);
        Assert.Contains("TaskFailed", values);
        Assert.Contains("NeedsYou", values);
        Assert.Contains("WorkerDown", values);
    }

    [Fact]
    public void HostNotice_DefaultsAtToUtcNow()
    {
        var before = DateTimeOffset.UtcNow;
        var notice = new HostNotice(NoticeType.TaskDone, "klart");
        Assert.InRange(notice.At, before, DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void HostNotice_RoundTripsThroughJson()
    {
        var notice = new HostNotice(NoticeType.WorkerDown, "worker nere", "w1", DateTimeOffset.UtcNow);
        var json = System.Text.Json.JsonSerializer.Serialize(notice);
        var back = System.Text.Json.JsonSerializer.Deserialize<HostNotice>(json);
        Assert.Equal(NoticeType.WorkerDown, back!.Type);
        Assert.Equal("worker nere", back.Message);
        Assert.Equal("w1", back.RefId);
    }
}
