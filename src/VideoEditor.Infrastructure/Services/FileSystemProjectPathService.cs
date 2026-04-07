using System;
using System.IO;
using VideoEditor.Application.Abstractions;

namespace VideoEditor.Infrastructure.Services;

/// <summary>
/// 	Определяет пути хранения проектов на диске.
/// </summary>
public sealed class FileSystemProjectPathService : IProjectPathService
{
    /// <inheritdoc />
    public string BuildProjectDirectory(string projectName)
    {
        var safeName = string.IsNullOrWhiteSpace(projectName) ? "New Project" : projectName.Trim();
        var root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(root, "VideoEditor", safeName);
    }
}
