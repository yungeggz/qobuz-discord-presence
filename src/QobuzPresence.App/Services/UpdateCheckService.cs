using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using QobuzPresence.Helpers;
using QobuzPresence.Models;

namespace QobuzPresence.Services;

internal sealed class UpdateCheckService
{
    private static readonly HttpClient s_httpClient = CreateHttpClient();

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        string currentVersion = GetCurrentVersion();

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, AppConstants.GitHubLatestReleaseApiUrl);
            using HttpResponseMessage response = await s_httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonElement root = document.RootElement;

            string? latestTag = root.TryGetProperty("tag_name", out JsonElement tagNameElement) &&
                tagNameElement.ValueKind == JsonValueKind.String
                ? tagNameElement.GetString()
                : null;

            string? releaseUrl = root.TryGetProperty("html_url", out JsonElement htmlUrlElement) &&
                htmlUrlElement.ValueKind == JsonValueKind.String
                ? htmlUrlElement.GetString()
                : AppConstants.GitHubLatestReleasePageUrl;

            if (string.IsNullOrWhiteSpace(latestTag))
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    ErrorMessage: "Latest release tag was missing.");
            }

            string normalizedLatestVersion = VersionUtility.NormalizeReleaseVersion(latestTag);

            if (!VersionUtility.TryParseComparableVersion(currentVersion, out Version? currentComparable) ||
                !VersionUtility.TryParseComparableVersion(normalizedLatestVersion, out Version? latestComparable))
            {
                return new UpdateCheckResult(
                    false,
                    currentVersion,
                    normalizedLatestVersion,
                    releaseUrl,
                    "Unable to compare release versions.");
            }

            return new UpdateCheckResult(
                latestComparable > currentComparable,
                currentVersion,
                normalizedLatestVersion,
                releaseUrl);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(false, currentVersion, ErrorMessage: "Update check timed out.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, currentVersion, ErrorMessage: ex.Message);
        }
    }

    public static string GetCurrentVersion()
    {
        Assembly assembly = typeof(UpdateCheckService).Assembly;

        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return VersionUtility.NormalizeReleaseVersion(informationalVersion);
        }

        Version? assemblyVersion = assembly.GetName().Version;

        return assemblyVersion is null
            ? "0.0.0"
            : VersionUtility.NormalizeReleaseVersion(assemblyVersion.ToString());
    }

    private static HttpClient CreateHttpClient()
    {
        SocketsHttpHandler handler = new()
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15)
        };

        HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("QobuzPresence", GetCurrentVersion()));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return client;
    }
}
