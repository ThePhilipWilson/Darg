using CommunityToolkit.Mvvm.Input;
using Darg.Core;

namespace Darg.Desktop.ViewModels;

/// <summary>A story's panel lists the cards it holds (WOR-0217), each a link that opens the card, so a story reads as a
/// container rather than a card with a type on it.</summary>
public partial class CardEditor
{
    /// <summary>The card is a story (its type as read when the panel opened).</summary>
    public bool IsStoryCard { get; private set; }
    /// <summary>The cards it holds (<see cref="StoryCards.Of"/>), each a link; one no board has is a warning, not dropped.</summary>
    public IReadOnlyList<BlockerLink> StoryLinks { get; private set; } = [];
    public bool HasStoryLinks => StoryLinks.Count > 0;
    /// <summary>The heading over the list: how many cards it holds, or that it names none yet.</summary>
    public string StoryHeading => StoryLinks.Count switch
    {
        0 => "Contains no cards yet (a Cards: line in the description names them)",
        1 => "Contains 1 card",
        var n => $"Contains {n} cards",
    };

    void InitStory()
    {
        if (_card is null || !_card.Card.IsStory) return;
        IsStoryCard = true;
        IReadOnlyList<BoardCard> all;
        try { all = _board.AllProjectCards(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { all = []; }
        StoryLinks = StoryCards.Of(_card.Card, all).Select(m => new BlockerLink(m.Id, m.Text, !m.Found, m.Card?.Done == true,
            new RelayCommand(() => _board.Root.OpenById(m.Card?.Card.Id ?? m.Id)))).ToList();
    }
}
