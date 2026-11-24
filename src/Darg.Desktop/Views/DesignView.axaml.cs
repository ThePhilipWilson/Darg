using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Darg.Desktop.ViewModels;

namespace Darg.Desktop.Views;

/// <summary>The Design section (WOR-0175): the plan tree and the picked plan's page.</summary>
public partial class DesignView : UserControl
{
    /// <summary>An answered question's button: Change, or Cancel while its options are showing again (Keep is the other button, which writes the person's name on an agent's answer).</summary>
    public static readonly IValueConverter ChangeLabel = new FuncValueConverter<bool, string>(changing => changing ? "Cancel" : "Change");

    StickToBottom? _follow;
    DesignViewModel? _vm;

    public DesignView()
    {
        InitializeComponent();
        InitializeComponentState();
    }

    /// <summary>The planner's chat follows its newest line (run again by HotAvalonia after a XAML reload).</summary>
    void InitializeComponentState() => _follow = new StickToBottom(ChatScroll);

    // a Needs you row (WOR-0200) scrolls the page to its question, or the chat to the planner's ask
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) { _vm.QuestionShown -= ShowQuestion; _vm.ChatShown -= ShowChat; }
        _vm = DataContext as DesignViewModel;
        if (_vm is not null) { _vm.QuestionShown += ShowQuestion; _vm.ChatShown += ShowChat; }
    }

    void ShowQuestion(PlanQuestionRow question) =>
        // after layout, so a page just switched to has its questions drawn
        Dispatcher.UIThread.Post(() =>
        {
            UpdateLayout();
            var box = PageParts.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("question") && b.DataContext == question);
            box?.BringIntoView();
        }, DispatcherPriority.Loaded);

    void ShowChat() => _follow?.Reset();
}
