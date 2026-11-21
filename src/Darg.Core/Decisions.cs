namespace Darg.Core;

/// <summary>What notifies the person (WOR-0078): an agent needing a decision from them, an AskUserQuestion or a
/// permission prompt, and the end of a turn that ran <see cref="LongTurn"/> or more, finished or failed. A shorter turn,
/// running out of usage, or a flow ending makes no toast and no entry in Fettle's list; the agent's unseen dot and Home
/// still show it. So a question has to come as AskUserQuestion, and every agent is told so.</summary>
public static class Decisions
{
    /// <summary>A turn that ends sooner than this isn't worth a notification (Philip's pick, 2026-11-17).</summary>
    public static readonly TimeSpan LongTurn = TimeSpan.FromMinutes(15);

    /// <summary>Whether a turn's end notifies: only after a long turn, error or not. A turn whose start
    /// isn't known (null) doesn't.</summary>
    public static bool TellTurnEnded(TimeSpan? took) => took >= LongTurn;

    /// <summary>What an agent is told about asking (appended to its system prompt). Free of the characters a shell
    /// might take as syntax (angle brackets, double quotes, ampersands, pipes): it goes on a command line.</summary>
    public const string Briefing =
        "Darg tells the person when you need a decision from them: an AskUserQuestion or a permission prompt. " +
        "A reply that ends your turn reaches them only after a long turn. So any question for the person, however small, goes through " +
        "AskUserQuestion, never written as prose at the end of a reply. " +
        "When a question is about how something looks, put the full path of a screenshot of it in the question text (for example C:/path/shot.png): Darg shows the picture above the question.";
}
