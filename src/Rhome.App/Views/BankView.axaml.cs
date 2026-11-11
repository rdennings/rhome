using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Rhome.Core;

namespace Rhome.App.Views;

/// <summary>
/// The bank's boxes (issue #44): create, rename, reorder and delete boxes, and move Pokémon between slots and boxes by
/// tapping one, then where it goes. Everything is staged in <see cref="PendingChanges"/> until Save, in the
/// pending-changes sheet (issue #46).
/// </summary>
public partial class BankView : UserControl, IGoesBack
{
    readonly PendingChanges? pending;
    readonly PendingSheet? sheet;
    int openBox;
    Guid? picked;

    /// <summary>Raised when the user goes back to My saves, once nothing is pending (saved, discarded, or none).</summary>
    public event Action? Back;

    public BankView()
    {
        InitializeComponent();
        BackButton.Click += (_, _) => GoBack();
        BoxList.SelectionChanged += (_, _) =>
        {
            if ((BoxList.SelectedItem as Control)?.Tag is BankBox box && box.Id != openBox)
            {
                openBox = box.Id;
                NameBox.Text = box.Name;
                Redraw();
            }
        };
        AddButton.Click += (_, _) =>
        {
            var name = string.IsNullOrWhiteSpace(NameBox.Text) ? $"Box {pending!.Bank.Boxes.Boxes.Count + 1}" : NameBox.Text;
            var id = pending!.Bank.Boxes.NextId;
            if (Stage(new AddBoxChange(id, name)))
                openBox = id;
            Redraw();
        };
        RenameButton.Click += (_, _) => { Stage(new RenameBoxChange(openBox, NameBox.Text ?? "")); Redraw(); };
        UpButton.Click += (_, _) => { Stage(new MoveBoxChange(openBox, Position - 1)); Redraw(); };
        DownButton.Click += (_, _) => { Stage(new MoveBoxChange(openBox, Position + 1)); Redraw(); };
        DeleteButton.Click += (_, _) =>
        {
            var position = Position;
            if (Stage(new DeleteBoxChange(openBox)))
            {
                var boxes = pending!.Bank.Boxes.Boxes;
                openBox = boxes[Math.Min(position, boxes.Count) - 1].Id;
            }
            Redraw();
        };
        UndoButton.Click += (_, _) =>
        {
            var refusal = pending!.Undo(pending.Outcomes.Count - 1, DateTimeOffset.Now);
            Settle();
            Problem(refusal);
        };
        DiscardButton.Click += (_, _) => { pending!.Discard(); AppLog.Write("bank: discarded the pending changes"); Settle(); };
        SaveButton.Click += (_, _) => sheet?.Show(leaving: false);
    }

    public BankView(PendingChanges pending) : this()
    {
        this.pending = pending;
        sheet = new PendingSheet(pending, "bank: ");
        sheet.Changed += Settle;
        sheet.Left += () => Back?.Invoke();
        Root.Children.Add(sheet);
        openBox = pending.Bank.Boxes.Boxes[0].Id;
        NameBox.Text = pending.Bank.Boxes.Boxes[0].Name;
        if (pending.LoadProblem is { } problem)
        {
            AppLog.Write(problem);
            Problem(problem);
        }
        Redraw();
    }

    int Position => pending!.Bank.Boxes.Position(openBox);

    /// <summary>Back to My saves; with unsaved changes it asks first. With the sheet open, it closes the sheet.</summary>
    public void GoBack()
    {
        if (sheet is { IsVisible: true })
            sheet.Hide();
        else if (pending is null || pending.IsEmpty)
            Back?.Invoke();
        else
            sheet!.Show(leaving: true);
    }

    /// <summary>Stages a change; says why when it can't be done.</summary>
    bool Stage(Change change)
    {
        var refusal = pending!.Stage(change, DateTimeOffset.Now).Refusal;
        Problem(refusal);
        if (refusal is not null)
            AppLog.Write($"bank: {refusal}");
        return refusal is null;
    }

    void Problem(string? text)
    {
        ProblemText.Text = text;
        ProblemText.IsVisible = text is not null;
    }

    /// <summary>After undo, discard or save the bank may have other boxes: keep the open one if it's still there.</summary>
    void Settle()
    {
        if (pending!.Bank.Boxes.Box(openBox) is null)
            openBox = pending.Bank.Boxes.Boxes[0].Id;
        if (picked is { } id && pending.Bank.Pokemon.All(p => p.Id != id))
            picked = null;
        Problem(null);
        Redraw();
    }

    void Redraw()
    {
        if (pending is null)
            return;
        var bank = pending.Bank;
        var boxes = bank.Boxes.Boxes;
        var box = bank.Boxes.Box(openBox) ?? boxes[0];
        openBox = box.Id;
        var editable = pending.LoadProblem is null;

        var items = boxes.Select(b =>
        {
            var count = bank.Pokemon.Count(p => p.Slot.Box == b.Id);
            return new TextBlock { Text = count == 0 ? b.Name : $"{b.Name} ({count})", Tag = b };
        }).ToList();
        BoxList.ItemsSource = items;
        BoxList.SelectedItem = items.First(i => i.Tag == box);

        BoxTitle.Text = box.Name;
        HintText.Text = picked is { } id && bank.Pokemon.Find(p => p.Id == id) is { } holding
            ? $"Holding {Label(holding)}: tap a slot to put it there (a Pokémon already there swaps places), or tap it again to put it back."
            : "Tap a Pokémon to pick it up, then tap where it goes. Pick another box on the left to move it there.";

        SlotGrid.Children.Clear();
        for (var slot = 1; slot <= BankBox.SlotCount; slot++)
            SlotGrid.Children.Add(Slot(bank, new BankSlot(box.Id, slot), editable));

        AddButton.IsEnabled = RenameButton.IsEnabled = editable;
        UpButton.IsEnabled = editable && Position > 1;
        DownButton.IsEnabled = editable && Position < boxes.Count;
        DeleteButton.IsEnabled = editable && boxes.Count > 1;

        PendingText.Text = PendingSheet.Summary(pending);
        SaveButton.IsEnabled = UndoButton.IsEnabled = DiscardButton.IsEnabled = !pending.IsEmpty;
    }

    Control Slot(Bank bank, BankSlot slot, bool editable)
    {
        var pokemon = bank.At(slot);
        var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (pokemon is not null)
        {
            text.Children.Add(new TextBlock { Text = Label(pokemon), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center });
            var detail = pokemon.CheckedOut is { } away
                ? $"out in {bank.Saves.Find(s => s.Id == away.SaveId)?.DisplayName ?? "a game"}"
                : $"Lv{pokemon.Summary.Level}{(pokemon.Summary.IsShiny ? " ★" : "")}";
            text.Children.Add(new TextBlock { Text = detail, FontSize = 11, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center });
        }

        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(2),
            Opacity = pokemon is { IsInBank: false } ? 0.6 : 1,
            IsEnabled = editable && (pokemon is not null || picked is not null),
        };
        if (pokemon is not null && pokemon.Id == picked)
            button.Classes.Add("accent");
        button.Click += (_, _) => Tap(pokemon, slot);
        return button;
    }

    void Tap(BankPokemon? pokemon, BankSlot slot)
    {
        if (picked is not { } id)
        {
            picked = pokemon?.Id;
        }
        else
        {
            if (pokemon?.Id != id)
                Stage(new MoveInBankChange(id, slot));
            picked = null;
        }
        Redraw();
    }

    static string Label(BankPokemon pokemon) =>
        pokemon.Summary.IsNicknamed ? pokemon.Summary.Nickname : EnglishNames.Species(pokemon.Summary.Species);
}
