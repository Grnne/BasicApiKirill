using BasicApi.IntegrationTests.Infrastructure;

namespace BasicApi.IntegrationTests.Migrations;

/// <summary>Every foreign key has an index on its columns.</summary>
public class ForeignKeyIndexTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task EveryForeignKey_IsIndexed()
    {
        // Found by the review: deleting a group cascades to its messages, and for each message
        // Postgres looked for rows that reply to it, forward it or hide it — a full scan of the
        // table every time without an index. A group with a long history could not be deleted
        // within the command timeout, nor left by its last member.
        var unindexed = await NewSession().QueryAsync<string>(@"
            SELECT c.conrelid::regclass::text || '(' || (
                       SELECT string_agg(a.attname, ', ' ORDER BY k.n)
                       FROM unnest(c.conkey) WITH ORDINALITY AS k(attnum, n)
                       JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = k.attnum) || ')'
            FROM pg_constraint c
            WHERE c.contype = 'f' AND c.connamespace = 'public'::regnamespace
              AND NOT EXISTS (
                    SELECT 1 FROM pg_index i
                    WHERE i.indrelid = c.conrelid
                      AND (string_to_array(i.indkey::text, ' ')::int2[])[1:cardinality(c.conkey)]::int2[] @> c.conkey
                      AND (string_to_array(i.indkey::text, ' ')::int2[])[1:cardinality(c.conkey)]::int2[] <@ c.conkey)
            ORDER BY 1");

        Assert.True(unindexed.Count == 0, "No index on: " + string.Join(", ", unindexed));
    }
}
