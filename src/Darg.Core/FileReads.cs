namespace Darg.Core;

/// <summary>Where a section's folder reads are seen happening (WOR-0231): the stores that read a whole folder
/// for a view (<see cref="NoteStore.List"/>, <see cref="NoteStore.TicketStatuses"/>,
/// <see cref="ArtifactStore.List"/>) say so here, with what and which folder, on the thread doing the reading.
/// Unheard, as it is outside the tests, it costs a null check; a test listens to learn whether a reload read on
/// the UI thread (filtering by folder, since tests run side by side).</summary>
public static class FileReads
{
    public static event Action<string, string>? Reading;

    public static void Note(string what, string folder) => Reading?.Invoke(what, folder);
}
