using Eudoria.Core.Identity.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Eudoria.Core.Identity.Persistence;

/// <summary>Table identity.accounts (snake_case, PostgreSQL convention).</summary>
internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public const string EmailIndex = "ux_accounts_email";
    public const string PseudoIndex = "ux_accounts_pseudo_normalized";

    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.HasKey(account => account.Id).HasName("pk_accounts");

        builder.Property(account => account.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(account => account.Email).HasColumnName("email").HasMaxLength(AccountRules.EmailMaxLength);
        builder.Property(account => account.Pseudo).HasColumnName("pseudo").HasMaxLength(AccountRules.PseudoMaxLength);
        builder.Property(account => account.PseudoNormalized).HasColumnName("pseudo_normalized").HasMaxLength(AccountRules.PseudoMaxLength);
        builder.Property(account => account.PasswordHash).HasColumnName("password_hash");
        builder.Property(account => account.CreatedAt).HasColumnName("created_at");

        // Uniqueness is guaranteed by the database, even for simultaneous registrations.
        builder.HasIndex(account => account.Email).IsUnique().HasDatabaseName(EmailIndex);
        builder.HasIndex(account => account.PseudoNormalized).IsUnique().HasDatabaseName(PseudoIndex);
    }
}
