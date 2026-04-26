using System.IO;
using System.Text.Json;
using VideoEditor.Application.Abstractions;

namespace VideoEditor.Infrastructure.Services;

/// <summary>
///     Stores recent project file paths in the user profile.
/// </summary>
public sealed class RecentProjectService : IRecentProjectService
{
    private const int MaxRecentProjects = 8;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<RecentProjectItem> GetRecentProjects()
    {
        try
        {
            var entries = ReadEntries()
                .Where(entry => !string.IsNullOrWhiteSpace(entry.FilePath) && File.Exists(entry.FilePath))
                .OrderByDescending(entry => entry.LastOpenedAtUtc)
                .Take(MaxRecentProjects)
                .Select(entry => new RecentProjectItem(
                    entry.FilePath,
                    Path.GetFileNameWithoutExtension(entry.FilePath),
                    entry.LastOpenedAtUtc))
                .ToList();

            return entries;
        }
        catch
        {
            return Array.Empty<RecentProjectItem>();
        }
    }

    public void AddRecentProject(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var entries = ReadEntries()
                .Where(entry => !entry.FilePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                .Prepend(new RecentProjectEntry(fullPath, DateTime.UtcNow))
                .Where(entry => File.Exists(entry.FilePath))
                .OrderByDescending(entry => entry.LastOpenedAtUtc)
                .Take(MaxRecentProjects)
                .ToList();

            var settingsPath = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch
        {
            // Recent history should never block project work.
        }
    }

    private static IReadOnlyList<RecentProjectEntry> ReadEntries()
    {
        var settingsPath = GetSettingsPath();
        if (!File.Exists(settingsPath))
            return Array.Empty<RecentProjectEntry>();

        var entries = JsonSerializer.Deserialize<List<RecentProjectEntry>>(File.ReadAllText(settingsPath), JsonOptions);
        return entries is not null ? entries : Array.Empty<RecentProjectEntry>();
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoEditor",
            "recent-projects.json");
    }

    private sealed record RecentProjectEntry(string FilePath, DateTime LastOpenedAtUtc);
}
