namespace Moongazing.OrionGuard.Outbox.SqlServerBroker.Tests;

using System.Data;

/// <summary>
/// The listener's <c>SELECT @h</c> returns one row whether or not <c>WAITFOR (RECEIVE ...)</c> received a message;
/// only a non-NULL conversation handle means one arrived.
/// </summary>
public sealed class SqlServerBrokerReceiveResultTests
{
    [Fact]
    public async Task ReceivedConversationAsync_is_false_when_the_WAITFOR_timed_out()
    {
        using var reader = HandleResult(DBNull.Value);

        Assert.False(await SqlServerBrokerOutboxWakeSignal.ReceivedConversationAsync(reader, CancellationToken.None));
    }

    [Fact]
    public async Task ReceivedConversationAsync_is_true_when_a_conversation_handle_came_back()
    {
        using var reader = HandleResult(Guid.NewGuid());

        Assert.True(await SqlServerBrokerOutboxWakeSignal.ReceivedConversationAsync(reader, CancellationToken.None));
    }

    [Fact]
    public async Task ReceivedConversationAsync_is_false_when_no_row_came_back()
    {
        using var table = new DataTable();
        table.Columns.Add("conversation_handle", typeof(Guid));
        using var reader = table.CreateDataReader();

        Assert.False(await SqlServerBrokerOutboxWakeSignal.ReceivedConversationAsync(reader, CancellationToken.None));
    }

    private static DataTableReader HandleResult(object handle)
    {
        var table = new DataTable();
        table.Columns.Add("conversation_handle", typeof(Guid));
        table.Rows.Add(handle);
        return table.CreateDataReader();
    }
}
