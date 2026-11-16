using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Darg.Desktop.Views;

/// <summary>Enter runs a command, Shift+Enter is a new line, for a TextBox in a template with no code-behind
/// (<c>views:EnterSends.Command="{Binding SendCommand}"</c>). Tunnels, so it sees the Enter before the TextBox
/// takes it, as the agent and Bench boxes do.</summary>
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
        if (GetCommand(box) is { } cmd && cmd.CanExecute(null)) cmd.Execute(null);
    }
}
