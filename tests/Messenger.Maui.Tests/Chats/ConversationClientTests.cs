using System.Net;
using System.Net.Http.Json;
using Messenger.Contracts.Messages;
using Messenger.Maui.Services;

namespace Messenger.Maui.Tests.Chats;

public sealed class ConversationClientTests
{
    [Fact]
    public async Task Message_history_follows_every_page_so_read_positions_can_reach_the_latest_message()
    {
        var chatId = Guid.NewGuid();
        var requests = new List<string>();
        using var http = new HttpClient(new PageHandler(request =>
        {
            requests.Add(request.RequestUri!.Query);
            return request.RequestUri.Query.Contains("cursor=0")
                ? new([Message(chatId, 1), Message(chatId, 2)], "2")
                : new([Message(chatId, 3)], null);
        })) { BaseAddress = new Uri("https://example.test/") };
        var messages = await new ConversationClient(http).GetMessagesAsync(chatId, 0);

        Assert.Equal([1L, 2L, 3L], messages.Select(message => message.Sequence));
        Assert.Equal(["?cursor=0&limit=99", "?cursor=2&limit=99"], requests);
    }

    [Fact]
    public async Task A_repeated_cursor_is_rejected_instead_of_looping_forever()
    {
        using var http = new HttpClient(new PageHandler(_ => new([], "0")))
        { BaseAddress = new Uri("https://example.test/") };
        await Assert.ThrowsAsync<InvalidDataException>(() => new ConversationClient(http).GetMessagesAsync(Guid.NewGuid(), 0));
    }

    private static MessageResponse Message(Guid chatId, long sequence) => new(Guid.NewGuid(), chatId, Guid.NewGuid(),
        sequence, Guid.NewGuid(), "text", "Message", false, DateTimeOffset.UtcNow, null, null);

    private sealed class PageHandler(Func<HttpRequestMessage, MessagePageResponse> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(respond(request)) });
    }
}
