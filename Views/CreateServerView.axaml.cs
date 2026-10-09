using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class CreateServerView : UserControl
{
    private CreateServerViewModel? _subscribedVm;

    public CreateServerView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => ResubscribeConsole();
        ResubscribeConsole();

        var nameInput = this.FindControl<TextBox>("ServerNameInput");

        if (nameInput != null)
        {
            string? preRenameName = null;

            nameInput.GotFocus += (s, e) =>
            {
                if (DataContext is CreateServerViewModel vm)
                {
                    preRenameName = vm.SelectedServerName;
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(() => nameInput.SelectAll(), Avalonia.Threading.DispatcherPriority.Background);
            };

            nameInput.DoubleTapped += (s, e) =>
            {
                e.Handled = true;
                nameInput.Focus();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => nameInput.SelectAll(), Avalonia.Threading.DispatcherPriority.Background);
            };

            nameInput.KeyDown += (s, e) =>
            {
                if (DataContext is not CreateServerViewModel vm) return;
                if (e.Key == Key.Enter)
                {
                    var text = nameInput.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(text) && text != vm.SelectedServerName)
                    {
                        if (!vm.CommitRenameDirect(text))
                        {
                            nameInput.Text = vm.SelectedServerName;
                        }
                    }
                    this.Focus();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    nameInput.Text = preRenameName ?? vm.SelectedServerName;
                    this.Focus();
                    e.Handled = true;
                }
            };

            nameInput.LostFocus += (s, e) =>
            {
                if (DataContext is CreateServerViewModel vm)
                {
                    var text = nameInput.Text?.Trim() ?? "";
                    if (!string.IsNullOrWhiteSpace(text) && text != vm.SelectedServerName)
                    {
                        if (!vm.CommitRenameDirect(text))
                        {
                            nameInput.Text = vm.SelectedServerName;
                        }
                    }
                }
            };
        }
    }

    private void ResubscribeConsole()
    {
        if (_subscribedVm != null)
            _subscribedVm.ConsoleUpdated -= OnConsoleUpdated;
        _subscribedVm = DataContext as CreateServerViewModel;
        if (_subscribedVm != null)
            _subscribedVm.ConsoleUpdated += OnConsoleUpdated;
    }

    private void OnConsoleUpdated()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var box = this.FindControl<TextBox>("ConsoleBox");
            if (box != null)
                box.CaretIndex = box.Text?.Length ?? 0;
        }, DispatcherPriority.Background);
    }

    private void OnConsoleInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                if (sender is TextBox tb)
                {
                    var caret = tb.CaretIndex;
                    var text = tb.Text ?? "";
                    tb.Text = text.Insert(caret, "\n");
                    tb.CaretIndex = caret + 1;
                    e.Handled = true;
                }
            }
            else
            {
                if (DataContext is CreateServerViewModel vm)
                {
                    vm.SendConsoleCommand();
                    e.Handled = true;
                }
            }
        }
    }
}
