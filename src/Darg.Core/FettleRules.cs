namespace Darg.Core;

/// <summary>Rules in Fettle's briefing that are worth testing on their own (WOR-0132).</summary>
public static class FettleRules
{
    /// <summary>A described change is a ticket, not a code edit. Free of the characters a shell might take as
    /// syntax (angle brackets, double quotes, ampersands, pipes): the briefing goes on a command line.</summary>
    public const string ChangeRequests =
        "When he describes a change to the app or to a project's code (X should do Y, pressing Z should..., make it do W), " +
        "make a card for it on the board of the project he is looking at, in its backlog, saying what he asked, and do not edit code or commit. " +
        "Edit code only when he says to do it now (just do it, fix it, do it now, work on it). " +
        "If it is unclear which he means, ask with AskUserQuestion with options New ticket and Do it now. " +
        "Cards and notes he asks you to change directly are unaffected.";
}
