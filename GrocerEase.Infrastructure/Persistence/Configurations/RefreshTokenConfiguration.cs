using GrocerEase.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrocerEase.Infrastructure.Persistence.Configurations;

/// <summary>How a <see cref="RefreshToken"/> maps to the <c>refresh_tokens</c> table. Same conventions as <see cref="UserConfiguration"/>.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <summary>The unique index on the hash: a lookup by hash is an index hit, and two rows can never share a token.</summary>
    public const string TokenHashIndexName = "ix_refresh_tokens_token_hash";

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_tokens", table =>
        {
            // The database refuses what the entity refuses, whatever wrote the row.
            table.HasCheckConstraint("ck_refresh_tokens_id_is_uuid_v7", $"uuid_extract_version(id) = {RefreshToken.RequiredIdVersion}");
            table.HasCheckConstraint("ck_refresh_tokens_token_hash_is_sha256", $"octet_length(token_hash) = {RefreshToken.HashLength}");
            table.HasCheckConstraint("ck_refresh_tokens_expires_after_created", "expires_at > created_at");
            table.HasCheckConstraint("ck_refresh_tokens_within_family", "expires_at <= family_expires_at");
            table.HasCheckConstraint(
                "ck_refresh_tokens_at_most_30_days", $"expires_at <= created_at + interval '{RefreshToken.MaxLifetimeDays} days'");
        });

        builder.HasKey(token => token.Id).HasName("pk_refresh_tokens");

        builder.Property(token => token.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .HasDefaultValueSql("uuidv7()");

        builder.Property(token => token.UserId).HasColumnName("user_id").IsRequired();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .HasConstraintName("fk_refresh_tokens_users_user_id")
            .OnDelete(DeleteBehavior.Cascade); // a deleted account has no sessions left
        builder.HasIndex(token => token.UserId).HasDatabaseName("ix_refresh_tokens_user_id");

        // Looked up to revoke a whole family at once.
        builder.Property(token => token.FamilyId).HasColumnName("family_id").IsRequired();
        builder.HasIndex(token => token.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");

        // Only the SHA-256 hash (32 bytes): the token itself is never stored.
        builder.Property(token => token.TokenHash).HasColumnName("token_hash").IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique().HasDatabaseName(TokenHashIndexName);

        // Set by the application from its clock (TimeProvider), not by the database, so the rules and the tests agree on "now".
        builder.Property(token => token.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(token => token.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(token => token.FamilyExpiresAt).HasColumnName("family_expires_at").IsRequired();
        builder.Property(token => token.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(token => token.RevokedAt).HasColumnName("revoked_at");
    }
}
