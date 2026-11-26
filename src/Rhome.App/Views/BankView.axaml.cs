using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Rhome.App.Sprites;
using Rhome.Core;

namespace Rhome.App.Views;

/// <summary>
/// The bank's boxes (issue #44): create, rename, reorder and delete boxes, and move Pokémon between slots and boxes by
/// tapping one, then where it goes. Everything is staged in <see cref="PendingChanges"/> until Save, in the
/// pending-changes sheet (issue #46). Details opens the picked-up Pokémon's detail view (issue #47).
/// </summary>
public partial class BankView : UserControl, IGoesBack, IDualScreen
{
    readonly BottomPanel bottom = new();
    bool dual;
    readonly PendingChanges? pending;
    readonly PendingSheet? sheet;
    readonly PokemonSheet details = new();
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
        UndoButton.Click += (_, _) => UndoLast();
        SaveButton.Click += (_, _) => sheet?.Show(leaving: false);
        Selected.Click += (_, _) => ShowDetails();
        Selected.Hint = bottom.Selected.Hint = "Tap a Pokémon to pick it up. Its moves and where it's from show here; tap this card for everything.";

        // The bottom screen's panel, on a two-screen device (issue #49)
        bottom.Selected.Click += (_, _) => ShowDetails();
        bottom.Save.Click += (_, _) => sheet?.Show(leaving: false);
        bottom.Undo.Click += (_, _) => UndoLast();
        bottom.Discard.Click += (_, _) => { pending!.Discard(); AppLog.Write("bank: discarded the pending changes"); Settle(); };
    }

    public Control Bottom => bottom;

    /// <summary>With a second screen, the picked-up Pokémon, the pending list and Save go to the bottom screen.</summary>
    public void SetDual(bool dual)
    {
        this.dual = dual;
        Aside.IsVisible = !dual;
        Columns.ColumnDefinitions[3].Width = new GridLength(dual ? 0 : 10);
        Columns.ColumnDefinitions[4].Width = new GridLength(dual ? 0 : 210);
        UndoButton.IsVisible = SaveButton.IsVisible = !dual;
        Redraw();
    }

    void UndoLast()
    {
        var refusal = pending!.Undo(pending.Outcomes.Count - 1, DateTimeOffset.Now);
        Settle();
        Problem(refusal);
    }

    public BankView(PendingChanges pending) : this()
    {
        this.pending = pending;
        sheet = new PendingSheet(pending, "bank: ");
        sheet.Changed += Settle;
        sheet.Left += () => Back?.Invoke();
        Root.Children.Add(sheet);
        Root.Children.Add(details);
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

    /// <summary>Back to My saves; with unsaved changes it asks first. With a sheet open, it closes the sheet.</summary>
    public void GoBack()
    {
        if (details.IsVisible)
            details.Hide();
        else if (sheet is { IsVisible: true })
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
        BoxCount.Text = $"{bank.Pokemon.Count(p => p.Slot.Box == box.Id)} / {BankBox.SlotCount}";
        HintText.Text = picked is { } id && bank.Pokemon.Find(p => p.Id == id) is { } holding
            ? $"Holding {Label(holding)}{Away(bank, holding)}: tap a slot to put it there (a Pokémon already there swaps places), or tap it again to put it back."
            : "Tap a Pokémon to pick it up, then tap where it goes. Pick another box on the left to move it there.";

        var shownDetails = picked is { } shown && bank.Pokemon.Find(p => p.Id == shown) is { } pokemon
            ? PokemonDetails.ForBank(bank, pokemon, pending.SaveName)
            : null;
        Selected.Show(shownDetails);
        bottom.Selected.Show(shownDetails);

        SlotGrid.Children.Clear();
        for (var slot = 1; slot <= BankBox.SlotCount; slot++)
            SlotGrid.Children.Add(Slot(bank, new BankSlot(box.Id, slot), editable));

        AddButton.IsEnabled = RenameButton.IsEnabled = editable;
        UpButton.IsEnabled = editable && Position > 1;
        DownButton.IsEnabled = editable && Position < boxes.Count;
        DeleteButton.IsEnabled = editable && boxes.Count > 1;

        var lines = PendingView.Lines(pending);
        PendingLabel.Text = lines.Count == 0 ? "PENDING" : $"PENDING · {lines.Count}";
        PendingList.Fill(PendingPanel, lines, "Nothing yet. Changes show here and only happen when you save.");
        bottom.ShowPending(lines, "Nothing yet. Changes show here and only happen when you save.");
        PendingPill.IsVisible = !dual && lines.Count > 0;
        PendingCount.Text = $"{lines.Count} pending";
        SaveButton.IsEnabled = UndoButton.IsEnabled = !pending.IsEmpty;
    }

    Control Slot(Bank bank, BankSlot slot, bool editable)
    {
        var pokemon = bank.At(slot);
        Control? face = null;
        if (pokemon is not null)
        {
            var sprite = SpriteRequest.Of(pokemon);
            face = SlotFace.For(sprite, Label(pokemon), SlotFace.Line(sprite.IsEgg, pokemon.Summary.Level, pokemon.Summary.Gender), 10);
        }

        var state = pokemon is null ? SlotState.Normal
            : pokemon.Id == picked ? SlotState.Picked
            : pokemon.IsInBank ? SlotState.Normal : SlotState.Away;
        var button = SlotFace.Button(face, state);
        button.IsEnabled = editable && (pokemon is not null || picked is not null);
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

    void ShowDetails()
    {
        var bank = pending!.Bank;
        if (picked is { } id && bank.Pokemon.Find(p => p.Id == id) is { } pokemon)
            details.Show(PokemonDetails.ForBank(bank, pokemon, pending.SaveName));
    }

    /// <summary>Where a Pokémon that's out in a game is (its slot here is kept for it, and shows dimmed).</summary>
    static string Away(Bank bank, BankPokemon pokemon) => pokemon.CheckedOut is { } away
        ? $" (out in {bank.Saves.Find(s => s.Id == away.SaveId)?.DisplayName ?? "a game"})"
        : "";

    static string Label(BankPokemon pokemon) =>
        pokemon.Summary.IsNicknamed ? pokemon.Summary.Nickname : EnglishNames.Species(pokemon.Summary.Species);
}
