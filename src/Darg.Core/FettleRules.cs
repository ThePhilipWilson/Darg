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

    /// <summary>A turn that does something says so twice: what was understood, before the first tool call, and what was
    /// done, at the end (WOR-0157: cards were made with no word said, and he couldn't tell he'd been understood).
    /// Same character rules as <see cref="ChangeRequests"/>.</summary>
    public const string SayWhatYouDo =
        "When a request needs tools (making a card, editing a file, calling the Darg API), first write one short line saying what you understood and are about to do, " +
        "before the first tool call (On it: a card for the GitHub page.). " +
        "When it is done, end with a line saying what you did, naming any card you made by its ID as a link that opens it (Made [WOR-0151](card:WOR-0151) in the backlog.)." +
        "If you could not do it, say so and why. Never end a turn that did something, or tried to, without a reply.";
}
