using System.Diagnostics;

namespace FeatBit.AppHost;

internal static class PostgresInitFilesProvider
{
    public static async Task<string> GetAsync(
        string appHostDirectory,
        string version)
    {
        var cacheRoot = Path.Combine(appHostDirectory, ".aspire", "cache", "featbit");
        var versionCache = Path.Combine(cacheRoot, version);
        var initFiles = Path.Combine(
            versionCache,
            "infra",
            "postgresql",
            "docker-entrypoint-initdb.d");
        var completedMarker = Path.Combine(versionCache, ".complete");
        var lockPath = Path.Combine(cacheRoot, $"{version}.lock");

        Directory.CreateDirectory(cacheRoot);
        await using var cacheLock = await AcquireFileLockAsync(lockPath);

        if (IsComplete(completedMarker, initFiles))
        {
            return initFiles;
        }

        ResetDirectory(versionCache, initFiles);

        try
        {
            using var client = new GitHubRepositoryClient();
            var fileNames = await client.ListInitFilesAsync(version);
            if (fileNames.Count == 0)
            {
                throw new InvalidDataException(
                    $"FeatBit {version} does not contain PostgreSQL init files at " +
                    GitHubRepositoryClient.GetSourceDirectoryUrl(version));
            }

            foreach (var fileName in fileNames)
            {
                await client.DownloadInitFileAsync(
                    version,
                    fileName,
                    Path.Combine(initFiles, fileName));
            }

            await File.WriteAllTextAsync(
                completedMarker,
                GitHubRepositoryClient.GetSourceDirectoryUrl(version) + Environment.NewLine);
            return initFiles;
        }
        catch
        {
            if (Directory.Exists(versionCache))
            {
                Directory.Delete(versionCache, recursive: true);
            }

            throw;
        }
    }

    private static bool IsComplete(string completedMarker, string initFiles) =>
        File.Exists(completedMarker) &&
        new FileInfo(completedMarker).Length > 0 &&
        Directory.Exists(initFiles) &&
        Directory.EnumerateFiles(initFiles).Any();

    private static void ResetDirectory(string versionCache, string initFiles)
    {
        if (Directory.Exists(versionCache))
        {
            Directory.Delete(versionCache, recursive: true);
        }

        Directory.CreateDirectory(initFiles);
    }

    private static async Task<FileStream> AcquireFileLockAsync(string lockPath)
    {
        var timeout = TimeSpan.FromMinutes(2);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (stopwatch.Elapsed < timeout)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }
    }
}
