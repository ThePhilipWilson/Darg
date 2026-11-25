using System.Text.RegularExpressions;

namespace Darg.Core;

/// <summary>What a plan card's Blocked by entry names (WOR-0201): a card in a plan (this one or one beside it under the
/// same project), a card on a board by id (<see cref="External"/>), or nothing it can be tied to (<see cref="Problem"/>
/// says why: no such card, or one heading in two plans).</summary>
public sealed record PlanRef(string Text, PlanDoc? Plan, PlanCard? Card, string Problem)
{
    public bool Found => Card is not null;
    /// <summary>Shaped like a card id and no plan has it: a card on a board, which <see cref="Blockers"/> looks up there.</summary>
    public bool External => Card is null && Problem.Length == 0;
    /// <summary>The id a made card's Blocked by line gets: the card's own once made, else the text as written.</summary>
    public string? Id => Card is { Made: true } c ? c.Id : External ? Text.ToUpperInvariant() : null;
}

/// <summary>The plans a Blocked by line can reach (WOR-0201): the plan's own cards first, then those of every plan in the
/// same project (the project plan and the feature plans under it). An entry is named by the card's id once made, else by
/// its heading as written, which may hold commas, semicolons and "and": the line is split only where what's on either
/// side names a card or an id, never inside a heading. A heading that two plans share is written <c>Plan: heading</c>
/// (the plan's title or file name).</summary>
public sealed partial class PlanScope
{
    public IReadOnlyList<PlanDoc> Plans { get; }

    public PlanScope(IReadOnlyList<PlanDoc> plans) => Plans = plans;

    /// <summary>The scope of <paramref name="plan"/>: every plan in its tree (<see cref="PlanStore.Tree"/>) from the top,
    /// with <paramref name="plan"/> itself standing for its file's entry. A plan in no tree is alone.</summary>
    public static PlanScope For(PlanDoc plan, IReadOnlyList<PlanDoc> all)
    {
        var list = all.Select(p => SameFile(p, plan) ? plan : p).ToList();
        if (!list.Contains(plan)) list.Add(plan);
        foreach (var root in PlanStore.Tree(list))
        {
            var flat = Flatten(root).ToList();
            if (flat.Contains(plan)) return new PlanScope(flat);
        }
        return new PlanScope([plan]);
    }

    static IEnumerable<PlanDoc> Flatten(PlanStore.Node n) => n.Children.SelectMany(Flatten).Prepend(n.Plan);

    public static bool SameFile(PlanDoc a, PlanDoc b) =>
        a.File.Length > 0 && b.File.Length > 0 && string.Equals(Path.GetFullPath(a.File), Path.GetFullPath(b.File), StringComparison.OrdinalIgnoreCase);

    /// <summary>A plan's name in a message: its title, else its file name.</summary>
    public static string NameOf(PlanDoc p) => p.Title.Length > 0 ? p.Title : p.Slug;

    /// <summary>The card's Blocked by entries, each tied to what it names.</summary>
    public IReadOnlyList<PlanRef> BlockersOf(PlanDoc from, PlanCard card) =>
        Split(card.BlockedByText, Known(from)).Select(r => Resolve(from, r)).ToList();

    /// <summary>Every way a card in scope can be named from <paramref name="from"/>: headings, made ids, and
    /// <c>Plan: heading</c> for another plan's cards.</summary>
    Func<string, bool> Known(PlanDoc from)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Plans)
            foreach (var c in p.Cards)
            {
                names.Add(Norm(c.Title));
                if (c.Made) names.Add(c.Id);
                foreach (var n in new[] { p.Title, p.Slug }.Where(n => n.Length > 0)) names.Add(Norm(n + ": " + c.Title));
            }
        return s => names.Contains(Norm(s)) || IdRx().IsMatch(Norm(s));
    }

    /// <summary>What one entry names, looked for in this order: a made card's id, a heading in <paramref name="from"/>,
    /// <c>Plan: heading</c>, a heading in exactly one other plan; else a card id is taken as a board's, and anything else
    /// is a problem.</summary>
    public PlanRef Resolve(PlanDoc from, string reference)
    {
        var r = Clean(reference);
        var others = Plans.Where(p => p != from).ToList();
        if (IdRx().IsMatch(r))
        {
            foreach (var p in Plans.Prepend(from))
                if (p.Cards.FirstOrDefault(c => c.Made && c.Id.Equals(r, StringComparison.OrdinalIgnoreCase)) is { } made) return Hit(r, p, made, from);
            return new PlanRef(r, null, null, "");
        }
        if (Titled(from, r) is { } own) return Hit(r, from, own, from);
        for (var i = r.IndexOf(':'); i > 0; i = r.IndexOf(':', i + 1))
        {
            var (plan, heading) = (r[..i].Trim(), r[(i + 1)..].Trim());
            if (Plans.FirstOrDefault(p => PlanStore.Names(p, plan)) is { } q && Titled(q, heading) is { } c) return Hit(r, q, c, from);
        }
        var hits = others.Select(p => (p, c: Titled(p, r))).Where(x => x.c is not null).ToList();
        if (hits.Count == 1) return Hit(r, hits[0].p, hits[0].c!, from);
        if (hits.Count > 1)
            return new PlanRef(r, null, null, $"\"{r}\" is a card in {string.Join(" and in ", hits.Select(h => NameOf(h.p)))}: write it as \"{NameOf(hits[0].p)}: {r}\" to say which");
        return new PlanRef(r, null, null, $"\"{r}\" is no card in this plan" + (others.Count > 0 ? " or the plans beside it" : ""));
    }

    static PlanRef Hit(string r, PlanDoc p, PlanCard c, PlanDoc from) =>
        p != from && p.Status == "dropped" ? new PlanRef(r, p, null, $"\"{r}\" is in {NameOf(p)}, which is dropped") : new PlanRef(r, p, c, "");

    static PlanCard? Titled(PlanDoc p, string heading) =>
        p.Cards.FirstOrDefault(c => Norm(c.Title).Equals(Norm(heading), StringComparison.OrdinalIgnoreCase));

    /// <summary>Cut a Blocked by line into its entries. The line is cut at commas, semicolons and "and", but a run of
    /// pieces that together name something <paramref name="known"/> (a heading with a comma in it) stays one entry, the
    /// longest such run first. "none" and the like are left out.</summary>
    public static List<string> Split(string text, Func<string, bool> known)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var pieces = new List<(int Start, int End)>();
        var at = 0;
        foreach (Match m in SepRx().Matches(text)) { pieces.Add((at, m.Index)); at = m.Index + m.Length; }
        pieces.Add((at, text.Length));
        for (var i = 0; i < pieces.Count;)
        {
            var take = i;
            for (var j = pieces.Count - 1; j > i; j--)
                if (known(text[pieces[i].Start..pieces[j].End])) { take = j; break; }
            var entry = Clean(text[pieces[i].Start..pieces[take].End]);
            if (entry.Length > 0 && !NoneRx().IsMatch(entry)) result.Add(entry);
            i = take + 1;
        }
        return result;
    }

    /// <summary>An entry as written, without the bold, a closing full stop, or quotes or backticks around the whole of
    /// it; backticks inside a heading (<c>Leaving check for `.darg`</c>) stay.</summary>
    public static string Clean(string s)
    {
        s = s.Trim();
        for (var changed = true; changed && s.Length > 0;)
        {
            var before = s;
            s = s.Trim();
            if (s.StartsWith("**") && s.EndsWith("**") && s.Length >= 4) s = s[2..^2];
            if (s.EndsWith('.') && !s.EndsWith("..")) s = s[..^1];
            if (s.Length >= 2 && (s[0], s[^1]) is ('`', '`') or ('"', '"') or ('\'', '\'') or ('“', '”') or ('*', '*') or ('_', '_')) s = s[1..^1];
            changed = s != before;
        }
        return s.Trim();
    }

    static string Norm(string s) => WsRx().Replace(Clean(s), " ");

    [GeneratedRegex(@"\s*(?:[,;]|\s+and\s+|\s+&\s+)\s*", RegexOptions.IgnoreCase)] private static partial Regex SepRx();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*-\d+$")] private static partial Regex IdRx();
    [GeneratedRegex(@"^(none|nothing|n/a|-|–|—)$", RegexOptions.IgnoreCase)] private static partial Regex NoneRx();
    [GeneratedRegex(@"\s+")] private static partial Regex WsRx();
}

/// <summary>Making plan cards (WOR-0177, WOR-0201): which of the rows asked for can be made and why the rest can't, the
/// order they're made in across plans, and the making. A row can't be made when it's under 3/3, shares its heading with
/// another row of its plan, names a blocker that is no card or is in two plans, waits on an unmade card that isn't being
/// made (one not ticked, or one in another plan), is in a loop of Blocked by, or waits on a row that can't be made.</summary>
public static class PlanMaking
{
    public sealed record Row(PlanDoc Plan, PlanCard Card);

    /// <summary>A row that can't be made and why, in words for the page (the card's heading is said by the page).</summary>
    public sealed record CantMake(string PlanFile, string Plan, string Card, string Reason)
    {
        public string Text => $"\"{Card}\"" + (Plan.Length > 0 ? $" ({Plan})" : "") + $": {Reason}";
    }

    public sealed record Check(IReadOnlyList<Row> Ordered, IReadOnlyList<CantMake> Cant)
    {
        /// <summary>The reason a row can't be made, or null when it can.</summary>
        public string? ReasonFor(PlanDoc plan, PlanCard card) =>
            Cant.FirstOrDefault(c => c.Card.Equals(card.Title, StringComparison.OrdinalIgnoreCase) && Same(c.PlanFile, plan.File))?.Reason;
    }

    static bool Same(string a, string b) =>
        a.Length > 0 && b.Length > 0 ? string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase) : a == b;

    /// <summary>Which of <paramref name="rows"/> can be made, in the order to make them (each after every row it waits
    /// on, across plans), and why each of the rest can't. Writes nothing.</summary>
    public static Check Plan(PlanScope scope, IReadOnlyList<Row> rows)
    {
        var several = rows.Select(r => r.Plan).Distinct().Count() > 1 || scope.Plans.Count > 1;
        string PlanName(PlanDoc p) => several ? PlanScope.NameOf(p) : "";
        var reason = new Dictionary<Row, string>();
        var key = new Dictionary<(PlanDoc, PlanCard), Row>();
        foreach (var r in rows) key[(r.Plan, r.Card)] = r;
        Row? RowOf(PlanRef x) => x.Plan is not null && x.Card is not null && key.TryGetValue((x.Plan, x.Card), out var r) ? r : null;

        var edges = new Dictionary<Row, List<Row>>();
        foreach (var r in rows)
        {
            var why = new List<string>();
            if (!r.Card.Ready)
                why.Add($"it is {r.Card.Score.Stars}/{TicketScore.Max} and doesn't say {string.Join(" or ", r.Card.Score.Missing)}");
            if (r.Plan.Cards.Count(c => c.Title.Equals(r.Card.Title, StringComparison.OrdinalIgnoreCase)) > 1)
                why.Add("another card in its plan has the same heading: give each its own");
            var waits = new List<Row>();
            foreach (var b in scope.BlockersOf(r.Plan, r.Card))
            {
                if (b.Problem.Length > 0) { why.Add("Blocked by " + b.Problem); continue; }
                if (b.External || b.Card!.Made) continue;
                if (RowOf(b) is { } w) { waits.Add(w); continue; }
                why.Add(b.Plan == r.Plan
                    ? $"it waits on \"{b.Card.Title}\", which has no id and isn't being made: tick it too"
                    : $"it waits on \"{b.Card.Title}\" in {PlanScope.NameOf(b.Plan!)}, which has no id yet: make that plan's cards first, or use Make all on the project plan");
            }
            edges[r] = waits;
            if (why.Count > 0) reason[r] = string.Join("; ", why);
        }

        // loops: each row in one is named with the loop it's in
        var state = new Dictionary<Row, int>();   // 1 visiting, 2 done
        var stack = new List<Row>();
        void Visit(Row r)
        {
            state[r] = 1;
            stack.Add(r);
            foreach (var w in edges[r])
            {
                if (!state.TryGetValue(w, out var s)) Visit(w);
                else if (s == 1)
                {
                    var loop = stack.Skip(stack.IndexOf(w)).ToList();
                    var text = "it is in a loop of Blocked by: " + string.Join(" → ", loop.Append(w).Select(x => $"\"{x.Card.Title}\"" + (PlanName(x.Plan) is { Length: > 0 } n && loop.Any(y => y.Plan != x.Plan) ? $" ({n})" : "")));
                    foreach (var x in loop) reason[x] = reason.TryGetValue(x, out var old) && !old.Contains("loop") ? old + "; " + text : text;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            state[r] = 2;
        }
        foreach (var r in rows) if (!state.ContainsKey(r)) Visit(r);

        // a row waiting on one that can't be made can't be made either; listed after the rows that are the cause
        var follows = new HashSet<Row>();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var r in rows.Where(r => !reason.ContainsKey(r)))
                if (edges[r].FirstOrDefault(reason.ContainsKey) is { } w)
                {
                    reason[r] = $"it waits on \"{w.Card.Title}\"" + (w.Plan != r.Plan ? $" in {PlanScope.NameOf(w.Plan)}" : "") + ", which can't be made";
                    follows.Add(r);
                    changed = true;
                }
        }

        var ordered = new List<Row>();
        var placed = new HashSet<Row>();
        void Place(Row r)
        {
            if (!placed.Add(r)) return;
            foreach (var w in edges[r]) Place(w);
            ordered.Add(r);
        }
        foreach (var r in rows.Where(r => !reason.ContainsKey(r))) Place(r);
        var cant = rows.Where(reason.ContainsKey).OrderBy(follows.Contains).Select(r => new CantMake(r.Plan.File, PlanName(r.Plan), r.Card.Title, reason[r])).ToList();
        return new Check(ordered, cant);
    }

    /// <summary>Make <paramref name="rows"/> (WOR-0201): each on its own plan's story board (the plan's <c>board:</c>, or one
    /// named for it, made with the project's columns and a prefix from its name), in Blocked by order across plans, each
    /// reference turned into the id, each id written into its plan's heading as soon as its card exists, and every plan
    /// that got cards set to agreed. Nothing is made when a row can't be made, unless <paramref name="skipBlocked"/> (then
    /// only the rows that can be), or when questions are open in a plan making cards, until <paramref name="confirmOpen"/>.</summary>
    public static PlanStore.MadeCards Make(PlanScope scope, IReadOnlyList<Row> rows, string boardsFolder, bool confirmOpen, bool skipBlocked, string nothingLeft)
    {
        var plans = rows.Select(r => r.Plan).Distinct().ToList();
        var several = plans.Count > 1;
        var open = plans.SelectMany(p => p.OpenQuestions.Select(q => several ? $"{PlanScope.NameOf(p)}: {q.Question}" : q.Question)).ToList();
        var single = plans.Count == 1 ? plans[0].Board : "";
        PlanStore.MadeCards Stop(string problem, IReadOnlyList<CantMake>? cant = null, bool confirm = false, int can = 0) =>
            new(single, [], problem, confirm, open, false) { Cant = cant ?? [], CanMake = can };

        if (rows.Count == 0) return Stop(nothingLeft);
        var check = Plan(scope, rows);
        var needConfirm = open.Count > 0 && !confirmOpen;
        if (check.Cant.Count > 0 && (!skipBlocked || check.Ordered.Count == 0))
        {
            var lead = check.Ordered.Count == 0
                ? (rows.Count == 1 ? "It can't be made: " : $"None of the {rows.Count} cards can be made: ")
                : $"{check.Cant.Count} of the {rows.Count} cards can't be made, so nothing was made: ";
            return Stop(lead + string.Join("; ", check.Cant.Select(c => c.Text)) + ".", check.Cant, needConfirm && check.Ordered.Count > 0, check.Ordered.Count);
        }
        if (needConfirm) return Stop("", check.Cant, confirm: true, can: check.Ordered.Count);

        // every board settled before a card is made, so a folder in the way stops it with nothing made
        var making = check.Ordered.Select(r => r.Plan).Distinct().ToList();
        var boards = new Dictionary<PlanDoc, string>();
        foreach (var p in making)
        {
            var name = p.Board.Length > 0 ? p.Board : (p.Title.Length > 0 ? p.Title : p.Slug);
            name = Regex.Replace(name, "[\\\\/:*?\"<>|]", " ").Trim().TrimStart('.').Trim();
            if (name.Length == 0) return Stop($"{PlanScope.NameOf(p)} has no name to give its board: set board: in its properties.", check.Cant);
            var folder = Path.Combine(boardsFolder, name);
            if (!File.Exists(Path.Combine(folder, "board.json")) && Directory.Exists(folder)) return Stop($"{folder} exists but isn't a board.", check.Cant);
            if (boards.Values.Contains(name, StringComparer.OrdinalIgnoreCase)) return Stop($"Two plans would make their cards on the board \"{name}\": give one a board: of its own.", check.Cant);
            boards[p] = name;
        }
        var created = new List<string>();
        foreach (var (p, name) in boards)
        {
            if (!File.Exists(Path.Combine(boardsFolder, name, "board.json")))
            {
                var columns = BoardStore.List(boardsFolder).Select(b => BoardStore.Load(Path.Combine(boardsFolder, b)).Columns.Select(c => c.Name).ToList())
                    .FirstOrDefault(c => c.Count > 0) ?? [.. BoardStore.DefaultColumns];
                BoardStore.CreateBoard(boardsFolder, name, columns, PlanStore.PrefixFor(p.Title.Length > 0 ? p.Title : name));
                created.Add(name);
            }
            if (!p.Board.Equals(name, StringComparison.Ordinal)) PlanStore.SetProperty(p.File, "board", name);
        }

        var ids = new Dictionary<(PlanDoc, PlanCard), string>();
        var made = new List<PlanStore.MadeCard>();
        foreach (var r in check.Ordered)
        {
            var folder = Path.Combine(boardsFolder, boards[r.Plan]);
            var card = BoardStore.Create(folder, r.Card.Title, r.Card.Ticket, null, "", CardTypes.Guess(r.Card.Title + "\n" + r.Card.Description, []));
            ids[(r.Plan, r.Card)] = card.Id;
            PlanStore.SetCardId(r.Plan.File, r.Card.Title, card.Id);   // at once, so a failure further on leaves the plan knowing what exists
            made.Add(new(card.Id, r.Card.Title, r.Plan.File, boards[r.Plan]));
            var waits = scope.BlockersOf(r.Plan, r.Card)
                .Select(b => b.Card is { Made: false } c ? ids[(b.Plan!, c)] : b.Id ?? b.Text).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (waits.Count > 0) BoardStore.SetBlockedBy(folder, card.Id, waits);
        }
        foreach (var p in making) if (p.Status != "agreed") PlanStore.SetProperty(p.File, "status", "agreed");
        return new PlanStore.MadeCards(making.Count == 1 ? boards[making[0]] : "", made.Select(m => (m.Id, m.Title)).ToList(), "", false, open, created.Count > 0)
        {
            Cant = check.Cant, CanMake = check.Ordered.Count, Detail = made, CreatedBoards = created,
        };
    }
}
