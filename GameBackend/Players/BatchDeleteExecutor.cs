namespace GameBackend.Players;

internal static class BatchDeleteExecutor
{
    public static async Task ExecuteUntilEmptyAsync(
        Func<CancellationToken, Task<int>> deleteBatchAsync,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var deleted = await deleteBatchAsync(cancellationToken);
            if (deleted == 0)
            {
                break;
            }
        }
    }
}
