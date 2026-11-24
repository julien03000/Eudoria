using Eudoria.Core.Identity.Accounts;
using Eudoria.Core.Identity.Tokens;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eudoria.Core.Identity.Persistence;

/// <summary>Table identity.refresh_tokens (snake_case, PostgreSQL convention).</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(token => token.Id).HasName("pk_refresh_tokens");

        builder.Property(token => token.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(token => token.AccountId).HasColumnName("account_id");
        builder.Property(token => token.FamilyId).HasColumnName("family_id");
        builder.Property(token => token.TokenHash).HasColumnName("token_hash").HasMaxLength(32);
        builder.Property(token => token.CreatedAt).HasColumnName("created_at");
        builder.Property(token => token.ExpiresAt).HasColumnName("expires_at");
        builder.Property(token => token.SessionExpiresAt).HasColumnName("session_expires_at");
        builder.Property(token => token.RevokedAt).HasColumnName("revoked_at");

        builder.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
        builder.HasIndex(token => token.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");
        builder.HasIndex(token => token.AccountId).HasDatabaseName("ix_refresh_tokens_account_id");

        // A deleted account takes its sessions with it.
        builder.HasOne<Account>().WithMany()
            .HasForeignKey(token => token.AccountId)
            .HasConstraintName("fk_refresh_tokens_accounts_account_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
