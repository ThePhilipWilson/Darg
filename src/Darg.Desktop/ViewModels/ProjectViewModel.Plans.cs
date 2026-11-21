using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>The planner (WOR-0176): an agent drafts a plan in docs/design/ from a line, then stays beside its page in the
/// Design section to change it. Started from the page's + Plan box or the API's plan command.</summary>
public partial class ProjectViewModel
{
    /// <summary>Start a planner on <paramref name="idea"/>: a new plan file named for it, drafted in full. The Design
    /// page shows the planner's chat at once and the plan when its turn ends.</summary>
    /// <exception cref="InvalidOperationException">No agents, no docs folder, or no words.</exception>
    public (AgentViewModel Agent, string File) StartPlan(string idea, PlanKind kind, string? parent = null)
    {
        if (Agents is null) throw new InvalidOperationException($"{Name} doesn't use agents.");
        if (Design is null) throw new InvalidOperationException($"{Name} has no docs folder for its plans to go in.");
        idea = idea.Trim();
        if (idea.Length == 0) throw new InvalidOperationException("Say in a line what the plan is for.");
        Directory.CreateDirectory(Design.Folder);
        var file = Planner.FileFor(Design.Folder, idea, Agents.PlanFiles);
        var plans = Directory.EnumerateFiles(Design.Folder, "*.md").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        var prompt = Planner.Prompt(Name, idea, kind, file, Config.Docs, Config.Boards, plans, PlanCommits(), parent);
        var agent = Agents.StartPlanner(prompt, file, $"Plan: {ChatTitle.Guess(idea)}", idea, PlanDirs());
        Design.Drafting(agent, file);
        return (agent, file);
    }

    /// <summary>A plan with no planner (written by hand, or its agent forgotten): a new one reads it first, then does
    /// what <paramref name="message"/> asks.</summary>
    AgentViewModel? StartReviser(string file, string message)
    {
        if (Agents is null) return null;
        var prompt = Planner.ReviseFirst(Name, file, PlanCommits()) + Environment.NewLine + message;
        var title = Path.GetFileNameWithoutExtension(file);
        return Agents.StartPlanner(prompt, file, $"Plan: {title}", message, PlanDirs());
    }

    string? PlanCommits() =>
        Design is not null && Commits.For(Config, Design.Folder) is { } rule ? Commits.Describe(rule, Design.Folder) : null;

    /// <summary>The plans folder (it may be outside the repos, in a vault) and the boards, to read.</summary>
    List<string> PlanDirs() =>
        new[] { Design?.Folder, Config.Boards }.OfType<string>().Where(d => d.Length > 0).ToList();
}
