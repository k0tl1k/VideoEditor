namespace VideoEditor.Application.Abstractions;

/// <summary>
///     A project entry shown in the startup recent projects list.
/// </summary>
public sealed record RecentProjectItem(string FilePath, string DisplayName, DateTime LastOpenedAtUtc);
