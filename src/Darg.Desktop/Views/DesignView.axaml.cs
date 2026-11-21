using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Darg.Desktop.Views;

/// <summary>The Design section (WOR-0175): the plan tree and the picked plan's page.</summary>
public partial class DesignView : UserControl
{
    /// <summary>An answered question's button: Change, or Keep while its options are showing again.</summary>
    public static readonly IValueConverter ChangeLabel = new FuncValueConverter<bool, string>(changing => changing ? "Keep" : "Change");

    StickToBottom? _follow;

    public DesignView()
    {
        InitializeComponent();
        InitializeComponentState();
    }

    /// <summary>The planner's chat follows its newest line (run again by HotAvalonia after a XAML reload).</summary>
    void InitializeComponentState() => _follow = new StickToBottom(ChatScroll);
}
