using System.Text.Json;

namespace FeatBit.AppHost;

internal sealed class GitHubRepositoryClient : IDisposable
{
    private const string RepositoryPath = "featbit/featbit";
    private const string InitDirectory = "infra/postgresql/docker-entrypoint-initdb.d";
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    public GitHubRepositoryClient()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("featbit-aspire/1.0");
    }

    public static string GetSourceDirectoryUrl(string version) =>
        $"https://github.com/{RepositoryPath}/tree/{Uri.EscapeDataString(version)}/{InitDirectory}";

    public async Task<IReadOnlyList<string>> ListInitFilesAsync(string version)
    {
        var encodedVersion = Uri.EscapeDataString(version);
        var requestUrl =
            $"https://api.github.com/repos/{RepositoryPath}/contents/{InitDirectory}" +
            $"?ref={encodedVersion}";

        using var response = await _httpClient.GetAsync(
            requestUrl,
            HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccess(response, $"list FeatBit {version} PostgreSQL init files", requestUrl);

        await using var content = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(content);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"GitHub returned an unexpected response for {GetSourceDirectoryUrl(version)}.");
        }

        var fileNames = new List<string>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var type) || type.GetString() != "file" ||
                !item.TryGetProperty("name", out var name))
            {
                continue;
            }

            var fileName = name.GetString();
            ValidateFileName(fileName);
            fileNames.Add(fileName!);
        }

        fileNames.Sort(StringComparer.Ordinal);
        return fileNames;
    }

    public async Task DownloadInitFileAsync(
        string version,
        string fileName,
        string destinationPath)
    {
        var requestUrl =
            $"https://raw.githubusercontent.com/{RepositoryPath}/" +
            $"{Uri.EscapeDataString(version)}/{InitDirectory}/{Uri.EscapeDataString(fileName)}";

        using var response = await _httpClient.GetAsync(
            requestUrl,
            HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccess(response, $"download {fileName} for FeatBit {version}", requestUrl);

        await using var destination = File.Create(destinationPath);
        await response.Content.CopyToAsync(destination);
    }

    public void Dispose() => _httpClient.Dispose();

    private static void ValidateFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName is "." or ".." ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException(
                $"GitHub returned an unsafe PostgreSQL init file name: {fileName}");
        }
    }

    private static void EnsureSuccess(
        HttpResponseMessage response,
        string operation,
        string requestUrl)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to {operation}. GitHub returned {(int)response.StatusCode} " +
                $"({response.ReasonPhrase}) for {requestUrl}.");
        }
    }
}
