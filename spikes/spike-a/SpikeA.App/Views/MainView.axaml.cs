using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PKHeX.Core;
using SpikeA.Report;

namespace SpikeA.App.Views;

public partial class MainView : UserControl
{
    double? _firstFrameMs;

    public MainView()
    {
        InitializeComponent();
        OpenButton.Click += OnOpenClicked;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_firstFrameMs is not null)
            return;
        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ =>
        {
            _firstFrameMs ??= Host.MsSinceProcessStart();
            UpdateInfo();
            // Let the first frame finish, then report whatever is in the saves folder.
            Dispatcher.UIThread.Post(ReportSavesFolder, DispatcherPriority.Background);
        });
        UpdateInfo();
    }

    void UpdateInfo()
    {
        var pkhex = typeof(SaveFile).Assembly.GetName().Version;
        InfoText.Text =
            $"PKHeX.Core {pkhex} · .NET {Environment.Version} · {Host.Platform}\n" +
            $"process start → first frame: {(_firstFrameMs is { } ms ? $"{ms:F0} ms" : "…")}";
    }

    void ReportSavesFolder()
    {
        var sb = new StringBuilder();
        sb.Append($"startup_first_frame_ms: {_firstFrameMs:F0}\n");
        sb.Append($"pkhex: {typeof(SaveFile).Assembly.GetName().Version}\n");
        sb.Append($"platform: {Host.Platform}\n\n");

        var folder = Host.SavesFolder;
        if (folder is null || !Directory.Exists(folder))
        {
            sb.Append($"(no saves folder: {folder ?? "none"})\n");
        }
        else
        {
            var files = Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            if (files.Length == 0)
                sb.Append($"(saves folder is empty: {folder})\n");
            foreach (var path in files)
                AppendReport(sb, File.ReadAllBytes(path), Path.GetFileName(path));
        }

        ReportText.Text = sb.ToString();
        if (Host.ReportPath is { } reportPath)
        {
            try { File.WriteAllText(reportPath, sb.ToString()); }
            catch (Exception ex) { ReportText.Text += $"\n(could not write report: {ex.Message})"; }
        }
    }

    static void AppendReport(StringBuilder sb, byte[] data, string name)
    {
        try
        {
            var result = SaveReport.Build(data, name);
            sb.Append(result.Text);
            sb.Append($"load_ms: {result.LoadTime.TotalMilliseconds:F1}\n\n");
        }
        catch (Exception ex)
        {
            sb.Append($"File: {name}\nERROR {ex.GetType().Name}: {ex.Message}\n\n");
        }
    }

    async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open a save",
                AllowMultiple = true,
            });
            if (files.Count == 0)
                return;
            var sb = new StringBuilder();
            foreach (var file in files)
            {
                await using var stream = await file.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                AppendReport(sb, ms.ToArray(), file.Name);
            }
            ReportText.Text = sb.ToString();
        }
        catch (Exception ex)
        {
            ReportText.Text = $"ERROR {ex.GetType().Name}: {ex.Message}";
        }
    }
}
