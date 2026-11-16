using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>One of a project's repos, for the pickers that choose where a new agent or console starts.</summary>
public sealed class RepoChoice(string path)
{
    public string Path { get; } = path;
    public string Name { get; } = ProjectConfig.RepoName(path);
    public override string ToString() => Name;
}
