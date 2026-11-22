using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Darg.Desktop.Views;

/// <summary>The one Enter rule for every box that sends or submits something (WOR-0188):
/// <c>views:EnterSends.Command="{Binding SendCommand}"</c>. Enter runs the command; Shift+Enter is a new line; a
/// <c>\</c> typed just before the caret then Enter takes the backslash out and makes a new line instead of sending.
/// Enter in a blank box, or one whose command can't run, does nothing at all (no send, no new line). Tunnels, so it
/// sees the Enter before the TextBox takes it. Editors with nothing to send (a card's description, docs) don't use it.</summary>
public static class EnterSends
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<TextBox, ICommand?>("Command", typeof(EnterSends));

    public static ICommand? GetCommand(TextBox box) => box.GetValue(CommandProperty);
    public static void SetCommand(TextBox box, ICommand? value) => box.SetValue(CommandProperty, value);

    static EnterSends() => CommandProperty.Changed.AddClassHandler<TextBox>((box, e) =>
    {
        box.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        if (e.NewValue is not null) box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    });

    static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift) || sender is not TextBox box) return;
        e.Handled = true;
        var text = box.Text ?? "";
        if (Backslash(box, text)) return;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (GetCommand(box) is { } cmd && cmd.CanExecute(null)) cmd.Execute(null);
    }

    /// <summary>A <c>\</c> right before the caret (nothing selected): swap it for a new line.</summary>
    static bool Backslash(TextBox box, string text)
    {
        var at = box.CaretIndex;
        if (box.SelectionStart != box.SelectionEnd || at <= 0 || at > text.Length || text[at - 1] != '\\') return false;
        var newLine = string.IsNullOrEmpty(box.NewLine) ? "\n" : box.NewLine;
        box.Text = text[..(at - 1)] + newLine + text[at..];
        box.CaretIndex = at - 1 + newLine.Length;
        return true;
    }
}
