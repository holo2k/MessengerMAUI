using Messenger.Application.Chats;
using Messenger.Domain.Chats;
using Messenger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Messenger.Infrastructure.Social;

public sealed class EfMessageStore(MessengerDbContext dbContext) : IMessageStore
{
    public Task<Message?> FindByClientIdAsync(Guid senderId, Guid clientMessageId, CancellationToken cancellationToken) =>
        dbContext.Messages.SingleOrDefaultAsync(message =>
            message.SenderUserId == senderId && message.ClientMessageId == clientMessageId, cancellationToken);

    public Task<Message?> FindMessageAsync(Guid messageId, CancellationToken cancellationToken) =>
        dbContext.Messages.SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken);

    public async Task<IReadOnlyList<Message>> ListMessagesAsync(
        Guid chatId,
        Guid userId,
        long afterSequence,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.Messages.Where(message => message.ChatId == chatId && message.Sequence > afterSequence &&
                !dbContext.HiddenMessages.Any(hidden => hidden.MessageId == message.Id && hidden.UserId == userId))
            .OrderBy(message => message.Sequence).Take(take).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> SearchMessagesAsync(
        Guid chatId,
        Guid userId,
        IReadOnlyCollection<string> tokens,
        int take,
        CancellationToken cancellationToken)
    {
        var tokenArray = tokens.Distinct().ToArray();
        var ids = await dbContext.MessageSearchTokens.Where(token => tokenArray.Contains(token.Token))
            .GroupBy(token => token.MessageId)
            .Where(group => group.Count() == tokenArray.Length)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken);
        return await dbContext.Messages.Where(message => message.ChatId == chatId && ids.Contains(message.Id) &&
                message.DeletedAt == null &&
                !dbContext.HiddenMessages.Any(hidden => hidden.MessageId == message.Id && hidden.UserId == userId))
            .OrderByDescending(message => message.Sequence).Take(take).ToListAsync(cancellationToken);
    }

    public Task AddMessageAsync(
        Message message,
        IReadOnlyCollection<MessageSearchToken> tokens,
        CancellationToken cancellationToken)
    {
        dbContext.Messages.Add(message);
        dbContext.MessageSearchTokens.AddRange(tokens);
        return Task.CompletedTask;
    }

    public async Task ReplaceSearchTokensAsync(
        Guid messageId,
        IReadOnlyCollection<MessageSearchToken> tokens,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.MessageSearchTokens.Where(token => token.MessageId == messageId)
            .ToListAsync(cancellationToken);
        dbContext.MessageSearchTokens.RemoveRange(existing);
        await dbContext.MessageSearchTokens.AddRangeAsync(tokens, cancellationToken);
    }

    public async Task AddHiddenAsync(HiddenMessage hidden, CancellationToken cancellationToken) =>
        await dbContext.HiddenMessages.AddAsync(hidden, cancellationToken);

    public async Task AddPinAsync(PinnedMessage pin, CancellationToken cancellationToken) =>
        await dbContext.PinnedMessages.AddAsync(pin, cancellationToken);

    public Task<PinnedMessage?> FindPinAsync(Guid chatId, Guid messageId, CancellationToken cancellationToken) =>
        dbContext.PinnedMessages.SingleOrDefaultAsync(pin =>
            pin.ChatId == chatId && pin.MessageId == messageId, cancellationToken);

    public async Task<IReadOnlyList<PinnedMessage>> ListPinsAsync(Guid chatId, CancellationToken cancellationToken) =>
        await dbContext.PinnedMessages.Where(pin => pin.ChatId == chatId)
            .OrderByDescending(pin => pin.PinnedAt).ToListAsync(cancellationToken);

    public void RemovePin(PinnedMessage pin) => dbContext.PinnedMessages.Remove(pin);
}
