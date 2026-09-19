using Avalonia.Controls;
using Avalonia.Input;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class CreateServerView : UserControl
{
    public CreateServerView()
    {
        InitializeComponent();

        var nameBlock = this.FindControl<TextBlock>("ServerNameBlock");
        var editBox = this.FindControl<TextBox>("EditServerNameBox");

        if (nameBlock != null)
        {
            nameBlock.DoubleTapped += (s, e) =>
            {
                if (DataContext is CreateServerViewModel vm && vm.CanEditServer)
                {
                    vm.StartRenameServerCommand.Execute(null);
                    editBox?.Focus();
                    editBox?.SelectAll();
                }
            };
        }

        if (editBox != null)
        {
            editBox.KeyDown += (s, e) =>
            {
                if (DataContext is not CreateServerViewModel vm) return;
                if (e.Key == Key.Enter)
                {
                    vm.CommitRenameServerCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    vm.CancelRenameServerCommand.Execute(null);
                    e.Handled = true;
                }
            };

            editBox.LostFocus += (s, e) =>
            {
                if (DataContext is CreateServerViewModel vm && vm.IsEditingServerName)
                {
                    vm.CommitRenameServerCommand.Execute(null);
                }
            };
        }
    }
}
