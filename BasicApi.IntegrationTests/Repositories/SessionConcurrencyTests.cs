using BasicApi.IntegrationTests.Infrastructure;
using BasicApi.Storage.Entities;
using BasicApi.Storage.Repositories;

namespace BasicApi.IntegrationTests.Repositories;

/// <summary>A refresh and a sign-out of the same sign-in at the same moment.</summary>
public class SessionConcurrencyTests(PostgresFixture db) : DbTest(db)
{
    [Fact]
    public async Task SignOut_DuringARotation_EndsTheSessionTheRotationAdds()
    {
        // Found by the review: the sign-out's UPDATE saw only the rows of its snapshot, so the
        // session a racing refresh inserted stayed live — the device was signed out yet kept a
        // working refresh token.
        var userId = await Data.UserAsync("alice");
        var now = DateTime.UtcNow;
        var first = NewSessionRow(userId, Guid.NewGuid(), now);
        await new SessionRepository(NewSession()).CreateAsync(first);

        // Holds the row, so the rotation and then the sign-out both queue behind it, in that order.
        var holding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = NewSession();
        var hold = blocker.InTransactionAsync(async ct =>
        {
            await blocker.ExecuteAsync("SELECT 1 FROM sessions WHERE id = @Id FOR UPDATE", first, ct);
            holding.SetResult();
            await release.Task;
            return 0;
        });
        await holding.Task;

        var replacement = NewSessionRow(userId, first.FamilyId, now);
        var rotate = new SessionRepository(NewSession()).TryRotateAsync(first.Id, replacement, now);
        await WaitForLockWaitsAsync(1);
        var signOut = new SessionRepository(NewSession()).RevokeFamilyAsync(first.FamilyId, now);
        await WaitForLockWaitsAsync(2);
        release.SetResult();
        await Task.WhenAll(hold, rotate, signOut);

        Assert.True(await rotate);
        Assert.False(await new SessionRepository(NewSession()).HasLiveSessionInFamilyAsync(first.FamilyId));
    }

    private static Session NewSessionRow(Guid userId, Guid familyId, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        FamilyId = familyId,
        RefreshTokenHash = Guid.NewGuid().ToString("N"),
        CreatedAt = now,
        ExpiresAt = now.AddDays(30)
    };

    private async Task WaitForLockWaitsAsync(int count)
    {
        var observer = NewSession();
        for (var i = 0; i < 100; i++)
        {
            var waiting = await observer.ExecuteScalarAsync<long>(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()");
            if (waiting >= count)
                return;
            await Task.Delay(50);
        }
        Assert.Fail($"Expected {count} transactions waiting for a lock.");
    }
}
