using Messenger.Domain.Accounts;
using Messenger.Domain.Identity;
using Messenger.Domain.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Messenger.Infrastructure.Persistence.Configurations;

public sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.ToTable("support_tickets"); b.HasKey(x => x.Id);
        b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id"); b.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200);
        b.Property(x => x.Status).HasColumnName("status"); b.Property(x => x.CreatedAt).HasColumnName("created_at"); b.Property(x => x.ClosedAt).HasColumnName("closed_at");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> b)
    {
        b.ToTable("support_messages"); b.HasKey(x => x.Id);
        b.Property(x => x.TicketId).HasColumnName("ticket_id"); b.Property(x => x.AuthorUserId).HasColumnName("author_user_id");
        b.Property(x => x.Body).HasColumnName("body").HasMaxLength(4000); b.Property(x => x.IsStaff).HasColumnName("is_staff"); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasOne<SupportTicket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class AccountDeletionRequestConfiguration : IEntityTypeConfiguration<AccountDeletionRequest>
{
    public void Configure(EntityTypeBuilder<AccountDeletionRequest> b)
    {
        b.ToTable("account_deletion_requests"); b.HasKey(x => x.Id); b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.RequestedAt).HasColumnName("requested_at"); b.Property(x => x.ExecuteAt).HasColumnName("execute_at");
        b.Property(x => x.CancelledAt).HasColumnName("cancelled_at"); b.Property(x => x.ExecutedAt).HasColumnName("executed_at");
        b.HasIndex(x => x.UserId); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
