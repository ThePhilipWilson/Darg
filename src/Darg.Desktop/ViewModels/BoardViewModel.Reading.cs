using Avalonia.Threading;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>Reading the boards off the UI thread (WOR-0227). A reload asked for by anything but the person's own click (a
/// board file changing, an agent starting, ending or changing state) is one pass for the whole section: every board of the
/// project read once on a pool thread (the rows' boards among them), then applied on the UI thread in one go, and
/// <see cref="Reloaded"/> raised once. What the agents list asks after it (each card's board, the done cards, whether a
/// card is still being worked) is answered from that read (<see cref="ProjectBoards"/>), so nothing reads a card file on
/// the UI thread then. Asked for again while a pass reads, one more pass follows it, however many times it was asked.</summary>
public partial class BoardViewModel
{
    /// <summary>The root's: every board of the project as the last pass read it; null before the first pass lands.</summary>
    ProjectBoards? _all;
    /// <summary>The root's: a pass is reading, and another was asked for meanwhile.</summary>
    bool _reading, _readAgain;
    /// <summary>A row's: bumped each time the row reads its board on the UI thread (<see cref="Reload"/>), so a pass that
    /// read before that doesn't put the older board back.</summary>
    int _gen;
    /// <summary>The root's: a redraw from memory is posted (<see cref="AgentGone"/>), so a burst of them is one.</summary>
    bool _redrawPosted;

    /// <summary>Whether the boards have been read since the section was made (what the agents list asks is answered from it).</summary>
    public bool BoardsRead => Root._all is not null;

    /// <summary>Every board of the project as last read: the last pass's, else read now and kept (only before the first pass
    /// lands; the agents list waits for <see cref="BoardsRead"/> instead).</summary>
    ProjectBoards AllBoards() => Root._all ??= _project.Boards is null ? ProjectBoards.Empty : ProjectBoards.Read(_project.Boards);

    /// <summary>How many passes have landed (for the tests: several agents ending at once make one or two, not one each).</summary>
    public int Passes { get; private set; }

    /// <summary>Read every board off the UI thread and apply them all at once (see the class summary). Cheap to call often:
    /// calls while a pass reads make one more pass, not one each.</summary>
    public void ReloadLater()
    {
        var root = Root;
        if (root._reading) { root._readAgain = true; return; }
        root._reading = true;
        root._readAgain = false;
        var rows = root.Rows.Where(r => r.Folder is not null).Select(r => new RowRead(r, r.Folder!, r._gen)).ToList();
        var boards = _project.Boards;
        var stamps = new Dictionary<string, (DateTime, long)>(StringComparer.OrdinalIgnoreCase);
        _ = Task.Run(() => Read(rows, boards, stamps))
            .ContinueWith(t => Dispatcher.UIThread.Post(() => root.Landed(rows, t, stamps)), TaskScheduler.Default);
    }

    sealed class RowRead(BoardViewModel row, string folder, int gen)
    {
        public BoardViewModel Row { get; } = row;
        public string Folder { get; } = folder;
        public int Gen { get; } = gen;
        public Board? Board { get; set; }
        public string? Error { get; set; }
    }

    /// <summary>The pass's reading, on a pool thread: each row's board as <see cref="Reload"/> reads it (its attention column
    /// made, column changes stamped Moved), then every other board. Each board.json's stamp is taken first, before any is
    /// read, for the blocker lookup (WOR-0229: a board changed after its stamp is read again before work starts).</summary>
    static ProjectBoards Read(List<RowRead> rows, string? boards, Dictionary<string, (DateTime, long)> stamps)
    {
        // no StallWatch section here: its sections are the UI thread's, and one on a pool thread would be blamed for a stall
        foreach (var name in BoardStore.List(boards)) stamps[name] = StampOf(Path.Combine(boards!, name));
        var loaded = new Dictionary<string, Board>();
        foreach (var r in rows)
        {
            try { r.Board = r.Row.ReadBoard(r.Folder); loaded[ProjectBoards.Key(r.Folder)] = r.Board; }
            catch (Exception e) { r.Error = e.Message; }
        }
        return ProjectBoards.Read(boards, loaded);
    }

    /// <summary>A row's board from disk, as a reload reads it. The Moved stamps are kept per row, so two reads of one row
    /// (the person's click and a pass) take turns.</summary>
    Board ReadBoard(string folder)
    {
        BoardStore.EnsureAttentionColumn(folder);
        var board = BoardStore.Load(folder);
        // a column change made outside Move (an agent editing the card) is stamped Moved, then read again
        lock (_stamps) if (_stamps.Read(folder, board).Count > 0) board = BoardStore.Load(folder);
        return board;
    }

    /// <summary>The pass has read: on the UI thread, every row still showing the board it read takes it, the lists are
    /// drawn, and <see cref="Reloaded"/> is raised once.</summary>
    void Landed(List<RowRead> rows, Task<ProjectBoards> read, IReadOnlyDictionary<string, (DateTime, long)> stamps)
    {
        _reading = false;
        if (_disposed) return;
        using (StallWatch.Shared.Section("Board.Reload"))
        {
            if (read.IsFaulted) Status = $"Couldn't read the boards: {read.Exception?.GetBaseException().Message}";
            else _all = read.Result;
            foreach (var r in rows)
            {
                if (!Rows.Contains(r.Row) || r.Row.Folder != r.Folder || r.Row._gen != r.Gen) continue; // moved on, or read since
                if (r.Row._held is not null) { r.Row._reloadWaiting = true; continue; } // a header being edited: once it's done
                if (r.Board is null) { if (r.Error is not null) r.Row.Status = $"Couldn't read the board: {r.Error}"; continue; }
                r.Row._board = r.Board;
                r.Row.NoticeAttention();
            }
            // the blocker lookup takes the pass's boards, but a row read by a click since keeps that newer read (WOR-0229)
            if (!read.IsFaulted) BlockersLanded(read.Result, stamps, rows.Where(r => r.Row._gen != r.Gen).Select(r => Path.GetFileName(r.Folder)));
            SyncLabels();
            ApplyAll();
            Passes++;
            if (!read.IsFaulted) Reloaded?.Invoke();
            Editor?.RefreshSendBack();
        }
        if (_readAgain) ReloadLater();
    }

    /// <summary>Redraw every row from the boards already read (no file read), once for however many ask before it runs.</summary>
    void RedrawLater()
    {
        var root = Root;
        if (root._redrawPosted) return;
        root._redrawPosted = true;
        Dispatcher.UIThread.Post(() => { root._redrawPosted = false; if (!root._disposed) root.ApplyAll(); });
    }

    bool _disposed;
}
