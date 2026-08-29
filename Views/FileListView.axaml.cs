using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CustomMcLauncher.ViewModels;

namespace CustomMcLauncher.Views;

public partial class FileListView : UserControl
{
    public FileListView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    public void FocusSearch() => SearchBox?.Focus();

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var paths = GetDroppedPaths(e);
        if (DataContext is FileListViewModel vm)
        {
            var (valid, _) = vm.EvaluateDropPaths(paths);
            vm.SetDragState(paths.Count > 0, valid > 0);
            e.DragEffects = valid > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }
        e.Handled = true;
    }

    private static List<string> GetDroppedPaths(DragEventArgs e)
    {
        var paths = new List<string>();
        if (e.DataTransfer == null) return paths;
        try
        {
            foreach (var p in e.DataTransfer.TryGetFiles() ?? System.Array.Empty<IStorageItem>())
            {
                try { paths.Add(p.TryGetLocalPath() ?? ""); }
                catch { }
            }
        }
        catch { }
        return paths;
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FileListViewModel vm)
            vm.SetDragState(false, false);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not FileListViewModel vm) return;

        var paths = GetDroppedPaths(e);
        vm.SetDragState(false, false);
        paths.RemoveAll(string.IsNullOrEmpty);
        if (paths.Count > 0)
            vm.ImportPaths(paths);
        e.Handled = true;
    }

    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: Models.InstanceFileEntry entry } && DataContext is FileListViewModel vm)
            vm.ShowDetailsCommand.Execute(entry);
        e.Handled = true;
    }
}