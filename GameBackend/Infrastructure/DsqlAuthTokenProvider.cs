using Amazon;
using Amazon.DSQL.Util;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using GameBackend.Options;

namespace GameBackend.Infrastructure;

internal sealed class DsqlAuthTokenProvider(string endpoint, DsqlOptions options)
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private AWSCredentials? _credentials;

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsCachedTokenValid())
        {
            return _cachedToken!;
        }

        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (IsCachedTokenValid())
            {
                return _cachedToken!;
            }

            var regionName = ResolveRegion(options.Region);
            var region = RegionEndpoint.GetBySystemName(regionName);
            _credentials ??= await ResolveCredentialsAsync();

            _cachedToken = await DSQLAuthTokenGenerator.GenerateDbConnectAdminAuthTokenAsync(
                _credentials,
                region,
                endpoint);

            _cachedAt = DateTimeOffset.UtcNow;
            return _cachedToken;
        }
        finally
        {
            _sync.Release();
        }
    }

    private static string ResolveRegion(string configuredRegion)
    {
        var region = Environment.GetEnvironmentVariable("AWS_REGION") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(region))
        {
            region = configuredRegion;
        }

        if (string.IsNullOrWhiteSpace(region))
        {
            throw new InvalidOperationException(
                "AWS Region이 설정되지 않았습니다. Dsql:Region 또는 AWS_REGION을 설정해 주세요.");
        }

        return region;
    }

    private static async Task<AWSCredentials> ResolveCredentialsAsync()
    {
        var profile = Environment.GetEnvironmentVariable("AWS_PROFILE");
        if (!string.IsNullOrWhiteSpace(profile))
        {
            var profileStore = new Amazon.Runtime.CredentialManagement.CredentialProfileStoreChain();
            if (!profileStore.TryGetAWSCredentials(profile, out var profileCredentials))
            {
                throw new InvalidOperationException($"AWS profile '{profile}'를 찾을 수 없습니다.");
            }

            return profileCredentials;
        }

        return await DefaultAWSCredentialsIdentityResolver.GetCredentialsAsync();
    }

    private bool IsCachedTokenValid()
    {
        if (string.IsNullOrWhiteSpace(_cachedToken))
        {
            return false;
        }

        var expiryMinutes = Math.Max(options.TokenExpiryMinutes, 1);
        var refreshBufferMinutes = Math.Clamp(options.TokenRefreshBufferMinutes, 0, Math.Max(expiryMinutes - 1, 0));
        var refreshAfter = TimeSpan.FromMinutes(expiryMinutes - refreshBufferMinutes);

        return DateTimeOffset.UtcNow - _cachedAt < refreshAfter;
    }
}
