using MediCore.Identity.Application.Entities;

namespace MediCore.Identity.Tests.Unit;

public sealed class OutboxMessageTests
{
    [Fact]
    public void New_message_has_an_id_and_current_timestamp()
    {
        var message = new OutboxMessage
        {
            EventType = "staff.updated",
            Payload = """{"staffId":"example"}"""
        };

        Assert.NotEqual(Guid.Empty, message.Id);
        Assert.True(message.OccurredOnUtc <= DateTime.UtcNow);
        Assert.Null(message.ProcessedOnUtc);
        Assert.Equal(0, message.RetryCount);
    }
}