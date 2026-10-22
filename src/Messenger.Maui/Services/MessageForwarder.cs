using System.Net;
using Messenger.Contracts.Media;
using Messenger.Contracts.Messages;

namespace Messenger.Maui.Services;

public sealed class MessageForwarder(IConversationApi api, IMediaUploader uploads, IMessageAttachmentClient attachments, IMusicApi music)
{
    private readonly Dictionary<(Guid ClientId, Guid ObjectId), StoredObjectResponse> _copies = [];

    public async Task<MessageResponse> ForwardAsync(MessageResponse source, Guid targetChatId, Guid clientId, CancellationToken ct = default)
    {
        try { return await api.ForwardMessageAsync(source.Id, targetChatId, clientId, ct); }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Older deployments do not expose /messages/{id}/forward yet. Confirm both
            // memberships and reload the original before using their regular send endpoints.
            await api.GetChatAsync(targetChatId, ct);
            var current = (await api.GetMessagesAsync(source.ChatId, 0, ct)).SingleOrDefault(message => message.Id == source.Id && message.DeletedAt is null)
                ?? throw new InvalidOperationException("Сообщение удалено или больше недоступно для пересылки.");
            MessageResponse sent;
            if (current.Type == "music")
            {
                if (current.MusicTrack is not { } track) throw new InvalidOperationException("Песня больше недоступна.");
                sent = await music.ShareAsync(track.Id, targetChatId, clientId, ct);
            }
            else if (current.Type == "text") sent = await api.SendAsync(targetChatId, clientId, current.Body ?? string.Empty, ct);
            else
            {
                if (current.Attachments is not { Count: > 0 } files) throw new InvalidOperationException("Вложения больше недоступны.");
                var copied = new List<StoredObjectResponse>();
                foreach (var file in files)
                {
                    if (!_copies.TryGetValue((clientId, file.Id), out var copy))
                    {
                        using var buffer = new MemoryStream();
                        await attachments.DownloadAsync(file.Id, buffer, ct);
                        var bytes = buffer.ToArray();
                        var selection = new MediaSelection(MediaKind.File, file.FileName, file.ContentType, bytes.LongLength, null,
                            () => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
                        copy = await uploads.UploadAsync(selection, ct: ct);
                        _copies[(clientId, file.Id)] = copy;
                    }
                    copied.Add(copy);
                }
                sent = await api.SendWithAttachmentsAsync(targetChatId, clientId, current.Type, current.Body, copied.Select(file => file.Id).ToArray(), ct);
                if (sent.Attachments is not { Count: > 0 }) sent = sent with { Attachments = copied };
            }
            foreach (var key in _copies.Keys.Where(key => key.ClientId == clientId).ToArray()) _copies.Remove(key);
            return sent with { IsForwarded = true };
        }
    }
}
