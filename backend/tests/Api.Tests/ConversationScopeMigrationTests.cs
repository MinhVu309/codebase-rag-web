using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Api.Tests;

/// <summary>
/// The Conversation Scope migration on a database that already holds data from before it: the backfill (one membership
/// per conversation whose project has a matching active index), that nothing old is dropped, and that Down undoes it.
/// Uses its own throwaway Postgres so it can stop the schema at the previous migration.
/// </summary>
public class ConversationScopeMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261115132351_MakeRepositoryRefNullable";
    private const string Scope = "20261126053042_AddConversationScope";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // fixed ids so the assertions read naturally
    private static Guid G(int n) => new($"00000000-0000-0000-0000-{n:D12}");
    private static readonly Guid U1 = G(1), U2 = G(2);
    private static readonly Guid P1 = G(11), P2 = G(12), P3 = G(13), P4 = G(14), P5 = G(15), P6 = G(16);
    private static readonly Guid R1 = G(21), R2 = G(22), R3 = G(23), R4Old = G(24), R4New = G(25), R6 = G(26);
    private static readonly Guid C1 = G(31), C2 = G(32), C3 = G(33), C4 = G(34), C5 = G(35), C6 = G(36);
    private static readonly DateTimeOffset T = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        return connection;
    }

    private async Task ExecAsync(string sql, params (string, object)[] args)
    {
        await using var c = await OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<object?[]>> QueryAsync(string sql)
    {
        await using var c = await OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<object?[]>();
        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];
            for (var i = 0; i < row.Length; i++) row[i] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Data as it looked before Conversation Scope: the schema at the previous migration, then rows.</summary>
    private async Task SeedOldDataAsync()
    {
        await using (var db = NewContext())
            await db.GetService<IMigrator>().MigrateAsync(Previous);

        await ExecAsync("""
            INSERT INTO users (id, email, password_hash, created_at) VALUES
              (@u1, 'a@example.test', 'x', @t), (@u2, 'b@example.test', 'x', @t);
            INSERT INTO projects (id, user_id, name, active_index_id, created_at) VALUES
              (@p1, @u1, 'p1', 'idx_p1',   @t),   -- active index matches R1
              (@p2, @u1, 'p2', NULL,       @t),   -- no active index
              (@p3, @u1, 'p3', 'idx_gone', @t),   -- active index that no repository has any more
              (@p4, @u1, 'p4', 'idx_dup',  @t),   -- two repositories carry the active index id
              (@p5, @u1, 'p5', 'idx_p5',   @t),   -- no conversations
              (@p6, @u2, 'p6', 'idx_p1',   @t);   -- same index id as p1 but another project: must not match R1
            INSERT INTO repositories (id, project_id, repo_url, ref, index_id, status, files_done, total_files, created_at, updated_at) VALUES
              (@r1,    @p1, 'https://github.com/o/r1',   NULL, 'idx_p1',    'Ready',  1, 1, @t, @t),
              (@r2,    @p2, 'https://github.com/o/r2',   NULL, 'idx_p2',    'Ready',  1, 1, @t, @t),
              (@r3,    @p3, 'https://github.com/o/r3',   NULL, 'idx_p3',    'Ready',  1, 1, @t, @t),
              (@r4old, @p4, 'https://github.com/o/r4a',  NULL, 'idx_dup',   'Ready',  1, 1, @t, @t),
              (@r4new, @p4, 'https://github.com/o/r4b',  NULL, 'idx_dup',   'Ready',  1, 1, @t2, @t2),
              (@r6,    @p6, 'https://github.com/o/r6',   NULL, 'idx_other', 'Ready',  1, 1, @t, @t);
            INSERT INTO conversations (id, project_id, user_id, title, created_at) VALUES
              (@c1, @p1, @u1, 'c1', @c1at), (@c2, @p1, @u1, 'c2', @c2at), (@c3, @p2, @u1, 'c3', @t),
              (@c4, @p3, @u1, 'c4', @t),    (@c5, @p4, @u1, 'c5', @t),    (@c6, @p6, @u2, 'c6', @t);
            INSERT INTO messages (id, conversation_id, role, content, citations, created_at) VALUES
              (@m1, @c1, 'User', 'q', NULL, @t), (@m2, @c1, 'Assistant', 'a', '[{"refId":1}]', @t);
            """,
            ("u1", U1), ("u2", U2), ("p1", P1), ("p2", P2), ("p3", P3), ("p4", P4), ("p5", P5), ("p6", P6),
            ("r1", R1), ("r2", R2), ("r3", R3), ("r4old", R4Old), ("r4new", R4New), ("r6", R6),
            ("c1", C1), ("c2", C2), ("c3", C3), ("c4", C4), ("c5", C5), ("c6", C6),
            ("t", T), ("t2", T.AddHours(1)), ("c1at", T.AddMinutes(5)), ("c2at", T.AddMinutes(9)),
            ("m1", G(41)), ("m2", G(42)));
    }

    private async Task<List<(Guid Conversation, Guid Repository, DateTimeOffset AddedAt, Guid? AddedBy)>> MembershipsAsync() =>
        (await QueryAsync("SELECT conversation_id, repository_id, added_at, added_by_user_id FROM conversation_repositories ORDER BY conversation_id"))
        .Select(r => ((Guid)r[0]!, (Guid)r[1]!, new DateTimeOffset((DateTime)r[2]!, TimeSpan.Zero), (Guid?)r[3])).ToList();

    private async Task ApplyScopeMigrationAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    [Fact]
    public async Task Backfill_Gives_Each_Conversation_Whose_Project_Has_A_Matching_Active_Index_One_Membership()
    {
        await SeedOldDataAsync();

        await ApplyScopeMigrationAsync();

        var rows = await MembershipsAsync();
        Assert.Equal(
            [(C1, R1), (C2, R1), (C5, R4New)], // C3 (no active index), C4 (no such repository), C6 (other project) stay empty
            rows.Select(r => (r.Conversation, r.Repository)).OrderBy(r => r.Conversation).ToList());
        // one row per conversation, even though two repositories carried idx_dup (the newer one wins)
        Assert.Equal(rows.Count, rows.Select(r => r.Conversation).Distinct().Count());
        // added_at / added_by_user_id come from the conversation itself
        Assert.Equal(T.AddMinutes(5), rows.Single(r => r.Conversation == C1).AddedAt);
        Assert.Equal(T.AddMinutes(9), rows.Single(r => r.Conversation == C2).AddedAt);
        Assert.All(rows, r => Assert.Equal(U1, r.AddedBy));
    }

    [Fact]
    public async Task The_Migration_Drops_Nothing_And_The_New_Columns_Start_Empty()
    {
        await SeedOldDataAsync();

        await ApplyScopeMigrationAsync();

        Assert.Equal(6L, (await QueryAsync("SELECT count(*) FROM conversations"))[0][0]);
        Assert.Equal(2L, (await QueryAsync("SELECT count(*) FROM messages"))[0][0]);
        Assert.Equal(6L, (await QueryAsync("SELECT count(*) FROM repositories"))[0][0]);
        Assert.Equal(
            ["idx_dup", "idx_gone", "idx_p1", "idx_p1", "idx_p5"],
            (await QueryAsync("SELECT active_index_id FROM projects WHERE active_index_id IS NOT NULL ORDER BY 1")).Select(r => (string)r[0]!));
        var snapshotColumns = await QueryAsync("SELECT retrieval_snapshot, focus_repository_ids FROM messages");
        Assert.Equal(2, snapshotColumns.Count);
        Assert.All(snapshotColumns, r => Assert.All(r, Assert.Null));
        var types = await QueryAsync("SELECT data_type FROM information_schema.columns WHERE table_name = 'messages' AND column_name IN ('retrieval_snapshot','focus_repository_ids')");
        Assert.All(types, r => Assert.Equal("jsonb", r[0]));
    }

    [Fact]
    public async Task A_Database_With_No_Conversations_Migrates_To_An_Empty_Table()
    {
        await using (var db = NewContext()) await db.Database.MigrateAsync();

        Assert.Equal(0L, (await QueryAsync("SELECT count(*) FROM conversation_repositories"))[0][0]);
    }

    [Fact]
    public async Task Membership_Rows_Follow_Their_Repository_And_Conversation_And_History_Stays()
    {
        await SeedOldDataAsync();
        await ApplyScopeMigrationAsync();

        await ExecAsync("DELETE FROM repositories WHERE id = @r", ("r", R1));
        Assert.Equal([C5], (await MembershipsAsync()).Select(r => r.Conversation).ToList()); // C1 and C2 lost R1
        Assert.Equal(2L, (await QueryAsync("SELECT count(*) FROM messages"))[0][0]);          // history untouched

        await ExecAsync("DELETE FROM conversations WHERE id = @c", ("c", C5));
        Assert.Empty(await MembershipsAsync());
    }

    [Fact]
    public async Task A_Repository_Can_Only_Be_In_A_Scope_Once()
    {
        await SeedOldDataAsync();
        await ApplyScopeMigrationAsync();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(
            "INSERT INTO conversation_repositories (conversation_id, repository_id, added_at) VALUES (@c, @r, @t)",
            ("c", C1), ("r", R1), ("t", T)));

        Assert.Equal("23505", ex.SqlState);
    }

    [Fact]
    public async Task Down_Removes_The_Table_And_Both_Columns_And_Leaves_The_Old_Data_And_Up_Can_Run_Again()
    {
        await SeedOldDataAsync();
        await ApplyScopeMigrationAsync();
        await using (var db = NewContext())
        {
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Assert.Equal(Scope, applied[^1]);
            await db.GetService<IMigrator>().MigrateAsync(Previous);
        }

        Assert.Null((await QueryAsync("SELECT to_regclass('conversation_repositories')"))[0][0]);
        Assert.Empty(await QueryAsync("SELECT 1 FROM information_schema.columns WHERE table_name = 'messages' AND column_name IN ('retrieval_snapshot','focus_repository_ids')"));
        Assert.Equal(6L, (await QueryAsync("SELECT count(*) FROM conversations"))[0][0]);
        Assert.Equal(2L, (await QueryAsync("SELECT count(*) FROM messages"))[0][0]);
        Assert.Equal(5L, (await QueryAsync("SELECT count(active_index_id) FROM projects"))[0][0]); // the old column was never touched

        await ApplyScopeMigrationAsync();

        Assert.Equal([C1, C2, C5], (await MembershipsAsync()).Select(r => r.Conversation).ToList());
    }

    [Fact]
    public async Task The_Model_Matches_The_Migrations_No_Pending_Changes()
    {
        await using var db = NewContext();

        Assert.False(db.Database.HasPendingModelChanges());
    }
}
