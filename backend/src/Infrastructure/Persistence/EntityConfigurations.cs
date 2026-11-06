using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence;

internal class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.PasswordHash).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
    }
}

internal class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("projects");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ActiveIndexId).HasMaxLength(200);
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.User).WithMany(u => u.Projects).HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal class CodeRepositoryConfiguration : IEntityTypeConfiguration<CodeRepository>
{
    public void Configure(EntityTypeBuilder<CodeRepository> b)
    {
        b.ToTable("repositories");
        b.HasKey(x => x.Id);
        b.Property(x => x.RepoUrl).HasMaxLength(500).IsRequired();
        b.Property(x => x.Ref).HasMaxLength(255).IsRequired();
        b.Property(x => x.IndexId).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Stage).HasMaxLength(50);
        b.Property(x => x.CommitSha).HasMaxLength(64);
        b.Property(x => x.Stats).HasColumnType("jsonb");
        b.Property(x => x.ErrorCode).HasMaxLength(50);
        b.HasIndex(x => x.ProjectId);
        b.HasIndex(x => x.IndexId);
        b.HasOne(x => x.Project).WithMany(p => p.Repositories).HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> b)
    {
        b.ToTable("conversations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.ProjectId);
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.Project).WithMany(p => p.Conversations).HasForeignKey(x => x.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.ToTable("messages");
        b.HasKey(x => x.Id);
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.Content).IsRequired();
        b.Property(x => x.Citations).HasColumnType("jsonb");
        b.Property(x => x.ErrorCode).HasMaxLength(50);
        b.HasIndex(x => new { x.ConversationId, x.CreatedAt });
        b.HasOne(x => x.Conversation).WithMany(c => c.Messages).HasForeignKey(x => x.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
