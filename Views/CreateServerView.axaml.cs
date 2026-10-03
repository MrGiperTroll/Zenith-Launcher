using Avalonia.Controls;
using Avalonia.Input;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class CreateServerView : UserControl
{
    public CreateServerView()
    {
        InitializeComponent();

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
                Avalonia.Threading.Dispatcher.UIThread.Post(() => nameInput.SelectAll());
            };

            nameInput.DoubleTapped += (s, e) =>
            {
                e.Handled = true;
                nameInput.Focus();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => nameInput.SelectAll());
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
}
