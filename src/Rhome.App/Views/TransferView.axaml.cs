using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using PKHeX.Core;
using Rhome.Core;

namespace Rhome.App.Views;

/// <summary>
/// The split view (issue #45): the opened game's party and boxes on the left, the bank on the right. Tap a Pokémon,
/// then where it goes, or drag it there. Each move is staged in <see cref="PendingChanges"/>, so the core's checks
/// (generation lock, structural check, a full party) refuse a move straight away and say why. Nothing is written until
/// Save, in the pending-changes sheet (issue #46). A Pokémon is only ever on one side: an import empties the game's
/// slot, a withdraw leaves a placeholder.
/// </summary>
public partial class TransferView : UserControl, IGoesBack
{
    readonly PendingChanges? pending;
    readonly PendingSheet? sheet;
    readonly Guid saveId;
    int saveBox = 1;
    int bankBox = 1;
    Place? picked;
    Place? dragFrom;
    ImportChange? asking;
    IReadOnlyList<BankPokemon> candidates = [];

    /// <summary>A slot on either side.</summary>
    abstract record Place;

    sealed record InSave(SaveSlot Slot) : Place;

    sealed record InBank(BankSlot Slot) : Place;

    /// <summary>Raised when the user goes back to My saves, once nothing is pending (saved, discarded, or none).</summary>
    public event Action? Back;

    public TransferView()
    {
        InitializeComponent();
        BackButton.Click += (_, _) => GoBack();
        SavePrev.Click += (_, _) => { saveBox--; Redraw(); };
        SaveNext.Click += (_, _) => { saveBox++; Redraw(); };
        BankPrev.Click += (_, _) => { bankBox--; Redraw(); };
        BankNext.Click += (_, _) => { bankBox++; Redraw(); };
        UndoButton.Click += (_, _) => { Problem(pending!.Undo(pending.Outcomes.Count - 1, DateTimeOffset.Now)); picked = null; asking = null; Redraw(); };
        DiscardButton.Click += (_, _) => { pending!.Discard(); AppLog.Write("discarded the pending changes"); Settle(); };
        SaveButton.Click += (_, _) => sheet?.Show(leaving: false);

        // Drag: press on a Pokémon, let go over another slot. A press and release on one slot is a tap (Click).
        AddHandler(PointerPressedEvent, (_, e) => dragFrom = PlaceOf(e.Source as Visual) is { } p && HasPokemon(p) ? p : null,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public TransferView(PendingChanges pending, Guid saveId, OpenedSave opened) : this()
    {
        this.pending = pending;
        this.saveId = saveId;
        sheet = new PendingSheet(pending);
        sheet.Changed += Settle;
        sheet.Left += () => Back?.Invoke();
        Root.Children.Add(sheet);
        Game = opened.Game;
        TitleText.Text = $"{SaveText.Title(opened.Game)} ↔ Bank";
        CopiesText.Text = opened.Game.HasSeveralCopies
            ? string.Join("\n", opened.Game.Copies.Select(c => SaveText.Copy(opened.Game, c)))
            : SaveText.Copy(opened.Game, opened.Game.Loaded);
        Redraw();
    }

    /// <summary>The game shown, to open it again.</summary>
    public FoundGame? Game { get; }

    SaveFile Save => pending!.Save(saveId);

    /// <summary>
    /// Back to My saves. With unsaved changes it asks first (save, discard, or stay), so changes never sit pending
    /// while the game is played and the save changes under them. With the sheet open, it closes the sheet.
    /// </summary>
    public void GoBack()
    {
        if (sheet is { IsVisible: true })
            sheet.Hide();
        else if (pending is null || pending.IsEmpty)
            Back?.Invoke();
        else
            sheet!.Show(leaving: true);
    }

    void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var from = dragFrom;
        dragFrom = null;
        if (from is null || this.InputHitTest(e.GetPosition(this)) is not Visual hit || PlaceOf(hit) is not { } to || to == from)
            return;
        e.Handled = true;
        picked = null;
        Move(from, to);
        Redraw();
    }

    static Place? PlaceOf(Visual? visual) =>
        visual?.GetSelfAndVisualAncestors().OfType<Control>().Select(c => c.Tag).OfType<Place>().FirstOrDefault();

    void Tap(Place place)
    {
        if (picked is null)
            picked = HasPokemon(place) ? place : null;
        else if (picked == place)
            picked = null;
        else
        {
            var from = picked;
            picked = null;
            Move(from, place);
        }
        Redraw();
    }

    /// <summary>Stages the move from one slot to another: an import, a withdraw, or a move inside the bank.</summary>
    void Move(Place from, Place to)
    {
        var bank = pending!.Bank;
        switch (from, to)
        {
            case (InSave f, InBank t):
                Stage(new ImportChange(saveId, f.Slot, t.Slot, NewId: Guid.NewGuid()));
                break;
            case (InBank f, InSave t) when bank.At(f.Slot) is { } pokemon:
                // Into the party, a Pokémon goes after the last one, as in the games.
                var target = t.Slot.IsParty ? new SaveSlot(null, Save.PartyCount + 1) : t.Slot;
                Stage(new WithdrawChange(pokemon.Id, saveId, target));
                break;
            case (InBank f, InBank t) when bank.At(f.Slot) is { } pokemon:
                Stage(new MoveInBankChange(pokemon.Id, t.Slot));
                break;
            case (InSave, InSave):
                Problem("rhome moves Pokémon between a game and the bank. Rearrange the game's own boxes in the game.");
                break;
        }
    }

    void Stage(Change change)
    {
        var result = pending!.Stage(change, DateTimeOffset.Now);
        asking = null;
        if (result.Candidates.Count > 0 && change is ImportChange import)
        {
            asking = import;
            candidates = result.Candidates;
            Problem(null);
            return;
        }
        Problem(result.Refusal);
        if (result.Refusal is not null)
            AppLog.Write($"not moved: {result.Refusal}");
    }

    void Answer(ImportAs how)
    {
        if (asking is not { } import)
            return;
        Stage(import with { How = how });
        Redraw();
    }

    void Problem(string? text)
    {
        ProblemText.Text = text;
        ProblemText.IsVisible = text is not null;
    }

    void Settle()
    {
        picked = null;
        asking = null;
        Problem(null);
        Redraw();
    }

    bool HasPokemon(Place place) => place switch
    {
        InSave s => InSlot(s.Slot) is not null,
        InBank b => pending!.Bank.At(b.Slot) is not null,
        _ => false,
    };

    PKM? InSlot(SaveSlot slot)
    {
        var save = Save;
        if (slot.IsParty)
            return slot.Slot <= save.PartyCount ? save.GetPartySlotAtIndex(slot.Slot - 1) : null;
        var pk = save.GetBoxSlotAtIndex((slot.Box!.Value - 1) * save.BoxSlotCount + slot.Slot - 1);
        return pk.Species == 0 ? null : pk;
    }

    void Redraw()
    {
        if (pending is null)
            return;
        var save = Save;
        var bank = pending.Bank;

        saveBox = Math.Clamp(saveBox, 1, save.BoxCount);
        SaveBoxTitle.Text = $"{SaveContents.BoxName(save, saveBox)} ({saveBox}/{save.BoxCount})";
        SavePrev.IsEnabled = saveBox > 1;
        SaveNext.IsEnabled = saveBox < save.BoxCount;

        var boxes = bank.Boxes.Boxes;
        bankBox = Math.Clamp(bankBox, 1, boxes.Count);
        var box = boxes[bankBox - 1];
        BankBoxTitle.Text = $"Bank · {box.Name} ({bankBox}/{boxes.Count})";
        BankPrev.IsEnabled = bankBox > 1;
        BankNext.IsEnabled = bankBox < boxes.Count;

        PartyGrid.Children.Clear();
        for (var i = 1; i <= 6; i++)
        {
            var slot = new SaveSlot(null, i);
            PartyGrid.Children.Add(SlotButton(new InSave(slot), InSlot(slot) is { } pk ? (Label(pk), Detail(pk), 1) : null));
        }
        SaveGrid.Children.Clear();
        for (var i = 1; i <= save.BoxSlotCount; i++)
        {
            var slot = new SaveSlot(saveBox, i);
            SaveGrid.Children.Add(SlotButton(new InSave(slot), InSlot(slot) is { } pk ? (Label(pk), Detail(pk), 1) : null));
        }
        BankGrid.Children.Clear();
        for (var i = 1; i <= BankBox.SlotCount; i++)
        {
            var slot = new BankSlot(box.Id, i);
            BankGrid.Children.Add(SlotButton(new InBank(slot), bank.At(slot) is { } p ? BankText(p) : null));
        }

        HintText.Text = picked is null
            ? "Tap a Pokémon, then tap where it goes, or drag it there. Nothing is written until Save."
            : "Now tap where it goes (another box: use the arrows), or tap it again to put it down.";

        ChoicePanel.Children.Clear();
        ChoicePanel.IsVisible = asking is not null;
        if (asking is not null)
            ShowChoice();

        PendingText.Text = PendingSheet.Summary(pending);
        SaveButton.IsEnabled = UndoButton.IsEnabled = DiscardButton.IsEnabled = !pending.IsEmpty;
    }

    /// <summary>An import that might be a Pokémon the bank knows: which one is it, or is it a new one?</summary>
    void ShowChoice()
    {
        var bank = pending!.Bank;
        ChoicePanel.Children.Add(new TextBlock
        {
            Text = "This might be a Pokémon that's already in the bank. Which one is it?",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold,
        });
        foreach (var candidate in candidates)
        {
            var where = candidate.CheckedOut is { } away ? $"out in {pending.SaveName(away.SaveId)}" : bank.Boxes.Describe(candidate.Slot);
            var button = new Button { Content = $"{BankLabel(candidate)} Lv{candidate.Summary.Level}, {where}" };
            var id = candidate.Id;
            button.Click += (_, _) => Answer(ImportAs.Return(id));
            ChoicePanel.Children.Add(button);
        }
        var asNew = new Button { Content = "A new one" };
        asNew.Click += (_, _) => Answer(ImportAs.New);
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => { asking = null; Redraw(); };
        ChoicePanel.Children.Add(new WrapPanel { Children = { asNew, cancel } });
    }

    (string Name, string Detail, double Opacity) BankText(BankPokemon pokemon)
    {
        var detail = pokemon.CheckedOut is { } away
            ? $"out in {pending!.SaveName(away.SaveId)}"
            : $"Lv{pokemon.Summary.Level}{(pokemon.Summary.IsShiny ? " ★" : "")}";
        return (BankLabel(pokemon), detail, pokemon.IsInBank ? 1 : 0.6);
    }

    Button SlotButton(Place place, (string Name, string Detail, double Opacity)? pokemon)
    {
        var text = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (pokemon is { } p)
        {
            text.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeight.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center });
            text.Children.Add(new TextBlock { Text = p.Detail, FontSize = 10, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center });
        }
        var button = new Button
        {
            Content = text,
            Tag = place,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2),
            Margin = new Thickness(2),
            Opacity = pokemon?.Opacity ?? 1,
            IsEnabled = pending!.LoadProblem is null,
        };
        if (place == picked)
            button.Classes.Add("accent");
        button.Click += (_, _) => Tap(place);
        return button;
    }

    static string Label(PKM pk) => pk.IsEgg ? "Egg" : pk.IsNicknamed ? pk.Nickname : EnglishNames.Species(pk.Species);

    static string Detail(PKM pk) => pk.IsEgg ? "" : $"Lv{pk.CurrentLevel}{(pk.IsShiny ? " ★" : "")}";

    static string BankLabel(BankPokemon pokemon) =>
        pokemon.Summary.IsNicknamed ? pokemon.Summary.Nickname : EnglishNames.Species(pokemon.Summary.Species);
}
