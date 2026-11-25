using AgenticRpg.Core.Repositories;
using Microsoft.Azure.Cosmos;
using Moq;

namespace AgenticRpg.PlayerExperienceTests;

public class RecoveryRepositoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryInfoUsesPartitionScopedScalarAggregatesAndReadsPastEmptyPages(bool emptyScope)
    {
        const string scope = "session-with-an-apostrophe'";
        var savedAt = new DateTime(2026, 10, 10, 6, 26, 0, DateTimeKind.Utc);
        var container = new Mock<Container>(MockBehavior.Strict);
        var revision = Aggregate(container, "SELECT VALUE SUM(c.Version) FROM c WHERE c.scopeId = @scope",
            scope, emptyScope ? [] : new long?[] { 17 });
        var timestamp = Aggregate(container, "SELECT VALUE MAX(c.UpdatedAt) FROM c WHERE c.scopeId = @scope",
            scope, emptyScope ? [] : new DateTime?[] { savedAt });
        var repository = Repository(container.Object);

        var info = await repository.InfoAsync(scope);

        Assert.Equal(emptyScope ? 0 : 17, info.Revision);
        Assert.Equal(emptyScope ? (DateTime?)null : savedAt, info.SavedAt);
        revision.Verify(i => i.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        timestamp.Verify(i => i.ReadNextAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        container.VerifyAll();
    }

    [Fact]
    public async Task UndefinedAggregatesReturnEmptyRecoveryInfo()
    {
        var container = new Mock<Container>(MockBehavior.Strict);
        Aggregate(container, "SELECT VALUE SUM(c.Version) FROM c WHERE c.scopeId = @scope", "new-session", new long?[] { null });
        Aggregate(container, "SELECT VALUE MAX(c.UpdatedAt) FROM c WHERE c.scopeId = @scope", "new-session", new DateTime?[] { null });

        Assert.Equal((0L, (DateTime?)null), await Repository(container.Object).InfoAsync("new-session"));
    }

    private static Mock<FeedIterator<T>> Aggregate<T>(Mock<Container> container, string sql, string scope, T[] result)
    {
        var pages = new Queue<T[]>([[], result]);
        var iterator = new Mock<FeedIterator<T>>();
        iterator.SetupGet(i => i.HasMoreResults).Returns(() => pages.Count > 0);
        iterator.Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            var rows = pages.Dequeue();
            var response = new Mock<FeedResponse<T>>();
            response.SetupGet(r => r.Count).Returns(rows.Length);
            response.Setup(r => r.GetEnumerator()).Returns(() => ((IEnumerable<T>)rows).GetEnumerator());
            return Task.FromResult(response.Object);
        });
        container.Setup(c => c.GetItemQueryIterator<T>(It.IsAny<QueryDefinition>(), null, It.IsAny<QueryRequestOptions>()))
            .Callback<QueryDefinition, string, QueryRequestOptions>((query, _, options) =>
            {
                // Protect the production query contract: no object composed around aggregates,
                // no interpolated scope, and no cross-partition recovery metadata.
                Assert.Equal(sql, query.QueryText);
                var parameter = Assert.Single(query.GetQueryParameters());
                Assert.Equal("@scope", parameter.Name);
                Assert.Equal(scope, parameter.Value);
                Assert.Equal(new PartitionKey(scope), options.PartitionKey);
            }).Returns(iterator.Object);
        return iterator;
    }

    private static RecoveryRepository Repository(Container container)
    {
        var response = new Mock<ContainerResponse>();
        response.SetupGet(r => r.Container).Returns(container);
        var database = new Mock<Database>();
        database.Setup(d => d.CreateContainerIfNotExistsAsync("ScopeRecovery", "/scopeId", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response.Object);
        var client = new Mock<CosmosClient>();
        client.Setup(c => c.GetDatabase("AgenticRpgDb")).Returns(database.Object);
        return new RecoveryRepository(client.Object);
    }
}
