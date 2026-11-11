using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Rhome.Core;

namespace Rhome.App.Views;

/// <summary>
/// The pending-changes sheet (issue #46, PLAN.md §2 "Pending changes → Save"): every staged change and what it does
/// (what a withdraw drops or replaces, what the bank keeps, the game's bookkeeping), the moveset choice for a withdraw
/// into a game the Pokémon has been in before, undo for any one change, and Save. Shown over the split view and the
/// bank. When the user leaves with unsaved changes it opens as the question "save before leaving?".
/// </summary>
public sealed class PendingSheet : Border
{
    readonly PendingChanges pending;
    readonly string logPrefix;
    readonly TextBlock title = new() { FontSize = 20, FontWeight = FontWeight.SemiBold };
    readonly TextBlock intro = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    readonly TextBlock problem = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, IsVisible = false };
    readonly StackPanel list = new() { Spacing = 8 };
    readonly Button save = new() { Classes = { "accent" }, Margin = new Thickness(0, 0, 8, 8) };
    readonly Button discard = new() { Margin = new Thickness(0, 0, 8, 8) };
    readonly Button close = new() { Margin = new Thickness(0, 0, 8, 8) };
    bool leaving;

    /// <param name="logPrefix">Put before each log line, to say which screen it came from.</param>
    public PendingSheet(PendingChanges pending, string logPrefix = "")
    {
        this.pending = pending;
        this.logPrefix = logPrefix;
        IsVisible = false;
        Background = new SolidColorBrush(Colors.Black, 0.5);

        save.Click += (_, _) => SaveAll();
        discard.Click += (_, _) =>
        {
            if (!pending.IsEmpty)
            {
                pending.Discard();
                AppLog.Write($"{logPrefix}discarded the pending changes");
            }
            Finish();
        };
        close.Click += (_, _) => Hide();

        var card = new Border
        {
            MaxWidth = 760,
            Margin = new Thickness(16),
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new DockPanel
            {
                Children =
                {
                    Top(title, 4),
                    Top(intro, 8),
                    Top(problem, 8),
                    Bottom(new WrapPanel { Margin = new Thickness(0, 12, 0, 0), Children = { save, discard, close } }),
                    new ScrollViewer { Content = list },
                },
            },
        };
        card[!BackgroundProperty] = new DynamicResourceExtension("SystemRegionBrush");
        Child = card;
    }

    /// <summary>Raised after anything changed what's pending (undo, a moveset, discard or save): redraw behind.</summary>
    public event Action? Changed;

    /// <summary>Raised when the user chose to leave, once the changes are saved or discarded.</summary>
    public event Action? Left;

    /// <summary>Shown as the question asked before leaving with unsaved changes, rather than to review them.</summary>
    public bool IsLeaving => IsVisible && leaving;

    /// <param name="leaving">True when the user is leaving the screen: save or discard first, or stay.</param>
    public void Show(bool leaving)
    {
        this.leaving = leaving;
        Problem(null);
        Redraw();
        IsVisible = true;
    }

    public void Hide()
    {
        IsVisible = false;
        Problem(null);
    }

    /// <summary>The compact list for the side panel: one line per change and its notes.</summary>
    public static string Summary(PendingChanges pending) =>
        pending.IsEmpty
            ? "Nothing yet. Changes show here and only happen when you tap Save."
            : string.Join("\n", pending.Outcomes.SelectMany((o, i) =>
                new[] { $"{i + 1}. {o.Description}" }
                    .Concat(o.Notes.Select(n => "    " + n.Message))
                    .Concat(o.Movesets is null ? [] : ["    Two movesets to choose from: see Review."])));

    void SaveAll()
    {
        if (pending.IsEmpty)
            return;
        try
        {
            var result = pending.Save(DateTimeOffset.Now);
            if (!result.Written)
            {
                AppLog.Write($"{logPrefix}not saved: {result.Refusal}");
                Problem($"Nothing was saved: {result.Refusal}");
                return;
            }
            foreach (var line in result.Lines)
                AppLog.Write(logPrefix + line);
            AppLog.Write($"{logPrefix}saved: {Count(result.Outcomes.Count)} written, each file backed up first");
            Finish();
        }
        catch (ChangesetWriteException ex)
        {
            // Its message says what was put back, and what wasn't.
            AppLog.Write($"{logPrefix}saving failed: {ex.Message}");
            Problem(ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"{logPrefix}saving failed: {ex.Message}");
            Problem($"Nothing was saved: {ex.Message}");
        }
    }

    /// <summary>Saved or discarded: close, and go on leaving if that's what the user was doing.</summary>
    void Finish()
    {
        var wasLeaving = leaving;
        Hide();
        Changed?.Invoke();
        if (wasLeaving)
            Left?.Invoke();
    }

    void Problem(string? text)
    {
        problem.Text = text;
        problem.IsVisible = text is not null;
    }

    void Redraw()
    {
        var empty = pending.IsEmpty;
        var count = pending.Outcomes.Count;
        title.Text = leaving ? "Save before leaving?" : "Pending changes";
        intro.Text = empty
            ? "Nothing is pending."
            : (leaving ? $"You have {Count(count)} that {(count == 1 ? "isn't" : "aren't")} saved yet. " : "") +
              "Nothing is written until you tap Save. Then rhome backs up the bank and each game it changes, and writes " +
              "everything at once: if anything goes wrong, nothing is half-written.";

        save.Content = leaving ? "Save and leave" : "Save";
        save.IsEnabled = !empty;
        discard.Content = leaving ? empty ? "Leave" : "Discard and leave" : "Discard all";
        discard.IsEnabled = leaving || !empty;
        close.Content = leaving ? "Stay" : "Keep editing";

        list.Children.Clear();
        for (var i = 0; i < count; i++)
            list.Children.Add(Item(i, pending.Outcomes[i]));
    }

    Control Item(int index, ChangeOutcome outcome)
    {
        var undo = new Button { Content = "Undo", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0, 0, 0) };
        undo.Click += (_, _) => After(pending.Undo(index, DateTimeOffset.Now), $"undid: {outcome.Description}");

        var body = new StackPanel { Spacing = 2 };
        body.Children.Add(new TextBlock { Text = $"{index + 1}. {outcome.Description}", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var note in outcome.Notes)
            body.Children.Add(Line($"• {note.Message}"));
        if (outcome.Kept is { } kept)
            body.Children.Add(Line(kept, opacity: 0.7, italic: true));
        else if (outcome.Notes.Count > 0)
            body.Children.Add(Line("This isn't kept anywhere else.", foreground: Brushes.DarkOrange));
        foreach (var line in outcome.Bookkeeping)
            body.Children.Add(Line($"• {line}", opacity: 0.7));
        if (outcome.Movesets is { LastInThisGame: { } last } options && outcome.Change is WithdrawChange withdraw)
        {
            body.Children.Add(Line("Which moves does it take?", opacity: 0.9, top: 6));
            body.Children.Add(MovesButton(index, MovesetChoice.Newest, "Its moves now", options.Newest, withdraw.Moves));
            var gen = pending.Save(withdraw.SaveId).Generation;
            body.Children.Add(MovesButton(index, MovesetChoice.LastInThisGame, $"Its moves last time in a Gen {gen} game", last, withdraw.Moves));
        }

        var row = new DockPanel();
        DockPanel.SetDock(undo, Dock.Right);
        row.Children.Add(undo);
        row.Children.Add(body);
        return new Border
        {
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Colors.Gray, 0.4),
            Child = row,
        };
    }

    Button MovesButton(int index, MovesetChoice choice, string label, Moveset moves, MovesetChoice chosen)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { Text = $"{(choice == chosen ? "✓ " : "")}{label}: {Names(moves)}", TextWrapping = TextWrapping.Wrap },
        };
        if (choice == chosen)
            button.Classes.Add("accent");
        else
            button.Click += (_, _) => After(pending.ChooseMoves(index, choice, DateTimeOffset.Now), $"moves: {char.ToLowerInvariant(label[0])}{label[1..]}");
        return button;
    }

    void After(string? refusal, string done)
    {
        if (refusal is null)
            AppLog.Write(logPrefix + done);
        else
            AppLog.Write($"{logPrefix}{refusal}");
        Redraw();
        Problem(refusal);
        Changed?.Invoke();
    }

    static string Names(Moveset moves)
    {
        var names = new[] { moves.Move1, moves.Move2, moves.Move3, moves.Move4 }.Where(m => m != 0).Select(m => EnglishNames.Move(m)).ToList();
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    static string Count(int n) => n == 1 ? "1 change" : $"{n} changes";

    static TextBlock Line(string text, double opacity = 0.85, bool italic = false, IBrush? foreground = null, double top = 0)
    {
        var line = new TextBlock
        {
            Text = text,
            Opacity = opacity,
            TextWrapping = TextWrapping.Wrap,
            FontStyle = italic ? FontStyle.Italic : FontStyle.Normal,
            Margin = new Thickness(12, top, 0, 0),
        };
        if (foreground is not null)
            line.Foreground = foreground;
        return line;
    }

    static Control Top(Control control, double bottom)
    {
        control.Margin = new Thickness(0, 0, 0, bottom);
        DockPanel.SetDock(control, Dock.Top);
        return control;
    }

    static Control Bottom(Control control)
    {
        DockPanel.SetDock(control, Dock.Bottom);
        return control;
    }
}
