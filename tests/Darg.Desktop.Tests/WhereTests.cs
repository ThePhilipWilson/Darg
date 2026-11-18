using Avalonia.Headless.XUnit;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Tests;

/// <summary>WOR-0142: the line Fettle is sent names an open card's column and file, so it needn't hunt for them.</summary>
public class WhereTests
{
    [AvaloniaFact]
    public async Task An_open_card_is_named_with_its_column_and_file()
    {
        using var app = DargApp.Make();
        var card = app.Card("fix the logo", "QA Ready");
        await app.Start();
        await app.Api("show", new { project = "proj", section = "Board", card = card.Id });

        var where = await app.Api("where");
        var file = Path.Combine(app.BoardFolder, "items", card.File).Replace('\\', '/');
        Assert.Contains($"card {card.Id} (fix the logo) open, in column QA Ready, file {file}", where);
        Assert.Equal("", app.Crashes());
    }
}
