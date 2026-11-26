using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "focus_repository_ids",
                table: "messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retrieval_snapshot",
                table: "messages",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "conversation_repositories",
                columns: table => new
                {
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    added_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_repositories", x => new { x.conversation_id, x.repository_id });
                    table.ForeignKey(
                        name: "FK_conversation_repositories_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_repositories_repositories_repository_id",
                        column: x => x.repository_id,
                        principalTable: "repositories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_conversation_repositories_users_added_by_user_id",
                        column: x => x.added_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_conversation_repositories_added_by_user_id",
                table: "conversation_repositories",
                column: "added_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_conversation_repositories_repository_id",
                table: "conversation_repositories",
                column: "repository_id");

            // D40 backfill: a conversation that existed before Conversation Scope searched its project's active index
            // (projects.active_index_id), so its Scope starts as exactly that repository. No match = empty Scope
            // (asking will answer 409 NO_REPOSITORY_IN_SCOPE once the multi-repository flow is on). Nothing is
            // dropped: projects.active_index_id stays. DISTINCT ON keeps one row per conversation even if two
            // repositories of a project ever carried the same index_id (newest wins).
            migrationBuilder.Sql("""
                INSERT INTO conversation_repositories (conversation_id, repository_id, added_at, added_by_user_id)
                SELECT DISTINCT ON (c.id) c.id, r.id, c.created_at, c.user_id
                FROM conversations c
                JOIN projects p ON p.id = c.project_id
                JOIN repositories r ON r.project_id = p.id AND r.index_id = p.active_index_id
                WHERE p.active_index_id IS NOT NULL
                ORDER BY c.id, r.created_at DESC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Scope membership (and anything written to the two message columns) is lost; projects.active_index_id was
            // never touched by Up, so the old single-index behaviour works again as it was.
            migrationBuilder.DropTable(
                name: "conversation_repositories");

            migrationBuilder.DropColumn(
                name: "focus_repository_ids",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "retrieval_snapshot",
                table: "messages");
        }
    }
}
