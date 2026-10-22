using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Filee.App.Services;
using Filee.Core.Localization;
using Microsoft.Extensions.Logging;

namespace Filee.App.Views.Pages;

public partial class HomePage : UserControl
{
    /// <summary>
    /// The first drag over the drop zone of this run is logged: a log without it after "drops don't work" means the
    /// drag never reached Filee (Windows refuses drops from Explorer into a Filee running as administrator).
    /// </summary>
    private static int _dragEnterLogged;

    public HomePage()
    {
        InitializeComponent();
        DropZone.DoubleTapped += OnDropZoneDoubleTapped;
        DragDrop.AddDragEnterHandler(DropZone, (_, e) =>
        {
            DropZone.Classes.Add("over");
            var files = DraggedFiles.Offered(e.DataTransfer);
            e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
            if (Interlocked.Exchange(ref _dragEnterLogged, 1) == 0)
                AppHost.Get<ILogger<HomePage>>().LogInformation("First drag over the drop zone (files: {Files})", files);
        });
        DragDrop.AddDragOverHandler(DropZone, (_, e) =>
            e.DragEffects = DraggedFiles.Offered(e.DataTransfer) ? DragDropEffects.Copy : DragDropEffects.None);
        DragDrop.AddDragLeaveHandler(DropZone, (_, _) => DropZone.Classes.Remove("over"));
        DragDrop.AddDropHandler(DropZone, OnDrop);
    }

    private async void OnDropZoneDoubleTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
        if (TopLevel.GetTopLevel(this) is not { } top)
            return;

        var loc = AppHost.Get<ILocalizer>();
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            Title = loc["home.drop_title"],
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();

        if (paths.Count == 0)
            return;

        var clickPos = e.GetPosition(this);
        var targetPos = DropZone.Bounds.Contains(clickPos)
            ? clickPos
            : new Point(DropZone.Bounds.Width / 2, DropZone.Bounds.Height / 2);
        var screenPoint = this.PointToScreen(targetPos);
        AppHost.Get<RadialController>().ShowForFiles(screenPoint.X, screenPoint.Y, paths);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("over");
        var dragged = DraggedFiles.Read(e.DataTransfer, forDrop: true);
        if (dragged.Files.Count == 0)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        var screenPoint = this.PointToScreen(e.GetPosition(this));
        AppHost.Get<RadialController>().ShowForFiles(screenPoint.X, screenPoint.Y, dragged.Files, web: dragged.Web);
    }
}
