using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using PKHeX.Core;
using Rhome.App.Sprites;
using Rhome.Core;

namespace Rhome.App.Views;

/// <summary>
/// The split view (issue #45): the opened game's party and boxes on the left, the bank on the right. Tap a Pokémon,
/// then where it goes, or drag it there. Each move is staged in <see cref="PendingChanges"/>, so the core's checks
/// (generation lock, structural check, a full party) refuse a move straight away and say why. Nothing is written until
/// Save, in the pending-changes sheet (issue #46). A Pokémon is only ever on one side: an import empties the game's
/// slot, a withdraw leaves a placeholder. Laid out as mockup A (issue #49): the picked-up Pokémon's card beside the
/// grids opens its detail view (issue #47), and the pending list says where each Pokémon goes; slots a Pokémon arrives
/// in or leaves are outlined until Save.
/// </summary>
public partial class TransferView : UserControl, IGoesBack, IDualScreen
{
    readonly PendingChanges? pending;
    readonly PendingSheet? sheet;
    readonly PokemonSheet details = new();
    readonly BottomPanel bottom = new();
    bool dual;
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
        UndoButton.Click += (_, _) => UndoLast();
        SaveButton.Click += (_, _) => sheet?.Show(leaving: false);
        Selected.Click += (_, _) => ShowDetails();
        Selected.Hint = bottom.Selected.Hint = "Tap a Pokémon to pick it up. Its moves and where it's from show here; tap this card for everything.";

        // The bottom screen's panel, on a two-screen device (issue #49)
        bottom.Selected.Click += (_, _) => ShowDetails();
        bottom.Save.Click += (_, _) => sheet?.Show(leaving: false);
        bottom.Undo.Click += (_, _) => UndoLast();
        bottom.Discard.Click += (_, _) => { pending!.Discard(); AppLog.Write("discarded the pending changes"); Settle(); };

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
        Root.Children.Add(details);
        Game = opened.Game;
        TitleText.Text = $"{SaveText.Title(opened.Game)} ↔ Bank";
        CopiesText.Text = opened.Game.HasSeveralCopies
            ? string.Join(" · ", opened.Game.Copies.Select(c => SaveText.Copy(opened.Game, c)))
            : SaveText.Copy(opened.Game, opened.Game.Loaded);
        GameLabel.Text = $"{EnglishNames.Game(SaveDetector.GameOf(Save)).ToUpperInvariant()} · PARTY";
        Redraw();
    }

    public Control Bottom => bottom;

    /// <summary>
    /// With a second screen, the picked-up Pokémon, the pending list and Save go to the bottom screen, and the top
    /// screen is the two box grids (mockup A, dual).
    /// </summary>
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
        Problem(pending!.Undo(pending.Outcomes.Count - 1, DateTimeOffset.Now));
        picked = null;
        asking = null;
        Redraw();
    }

    /// <summary>The game shown, to open it again.</summary>
    public FoundGame? Game { get; }

    SaveFile Save => pending!.Save(saveId);

    /// <summary>
    /// Back to My saves. With unsaved changes it asks first (save, discard, or stay), so changes never sit pending
    /// while the game is played and the save changes under them. With a sheet open, it closes the sheet.
    /// </summary>
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
        var (arriving, leaving) = PendingView.InSave(pending, saveId);
        var arrivingInBank = PendingView.ArrivingInBank(pending);

        saveBox = Math.Clamp(saveBox, 1, save.BoxCount);
        SaveBoxTitle.Text = SaveContents.BoxName(save, saveBox);
        SavePrev.IsEnabled = saveBox > 1;
        SaveNext.IsEnabled = saveBox < save.BoxCount;

        var boxes = bank.Boxes.Boxes;
        bankBox = Math.Clamp(bankBox, 1, boxes.Count);
        var box = boxes[bankBox - 1];
        BankBoxTitle.Text = box.Name;
        BankBoxCount.Text = $"{bankBox} of {boxes.Count}";
        BankPrev.IsEnabled = bankBox > 1;
        BankNext.IsEnabled = bankBox < boxes.Count;

        PartyGrid.Children.Clear();
        for (var i = 1; i <= 6; i++)
            PartyGrid.Children.Add(SaveSlotButton(new SaveSlot(null, i), arriving, leaving));
        SaveGrid.Children.Clear();
        var filled = 0;
        for (var i = 1; i <= save.BoxSlotCount; i++)
        {
            var slot = new SaveSlot(saveBox, i);
            filled += InSlot(slot) is null ? 0 : 1;
            SaveGrid.Children.Add(SaveSlotButton(slot, arriving, leaving));
        }
        SaveBoxCount.Text = $"{filled} / {save.BoxSlotCount}";
        BankGrid.Children.Clear();
        for (var i = 1; i <= BankBox.SlotCount; i++)
        {
            var slot = new BankSlot(box.Id, i);
            var pokemon = bank.At(slot);
            var state = pokemon is { IsInBank: false } ? SlotState.Away : arrivingInBank.Contains(slot) ? SlotState.Arriving : SlotState.Normal;
            BankGrid.Children.Add(SlotButton(new InBank(slot), pokemon is null ? null : BankFace(pokemon), state));
        }

        HintText.Text = picked is null
            ? "Tap a Pokémon, then tap where it goes, or drag it there. Nothing is written until Save."
            : $"Holding {PickedName()}. Tap where it goes (another box: use the arrows), or tap it again to put it down.";
        var shown = PickedDetails();
        Selected.Show(shown);
        bottom.Selected.Show(shown);

        ChoicePanel.Children.Clear();
        ChoiceCard.IsVisible = asking is not null;
        if (asking is not null)
            ShowChoice();

        var lines = PendingView.Lines(pending);
        PendingLabel.Text = lines.Count == 0 ? "PENDING" : $"PENDING · {lines.Count}";
        PendingList.Fill(PendingPanel, lines, "Nothing yet. Moves show here and only happen when you save.");
        bottom.ShowPending(lines, "Nothing yet. Moves show here and only happen when you save.");
        PendingPill.IsVisible = !dual && lines.Count > 0;
        PendingCount.Text = $"{lines.Count} pending";
        SaveButton.IsEnabled = UndoButton.IsEnabled = !pending.IsEmpty;
    }

    Button SaveSlotButton(SaveSlot slot, IReadOnlySet<SaveSlot> arriving, IReadOnlySet<SaveSlot> leaving)
    {
        var pk = InSlot(slot);
        var state = arriving.Contains(slot) ? SlotState.Arriving : pk is null && leaving.Contains(slot) ? SlotState.Leaving : SlotState.Normal;
        return SlotButton(new InSave(slot), pk is null ? null : SlotFace.For(SpriteRequest.Of(pk), Label(pk), Detail(pk), 10), state);
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

    void ShowDetails()
    {
        if (PickedDetails() is { } shown)
            details.Show(shown);
    }

    PokemonDetails? PickedDetails() => picked switch
    {
        InSave s when InSlot(s.Slot) is not null => PokemonDetails.ForSave(pending!.Bank, Save, saveId, s.Slot, pending.SaveName(saveId), pending.SaveName),
        InBank b when pending!.Bank.At(b.Slot) is { } pokemon => PokemonDetails.ForBank(pending.Bank, pokemon, pending.SaveName),
        _ => null,
    };

    static Control BankFace(BankPokemon pokemon)
    {
        var sprite = SpriteRequest.Of(pokemon);
        return SlotFace.For(sprite, BankLabel(pokemon), SlotFace.Line(sprite.IsEgg, pokemon.Summary.Level, pokemon.Summary.Gender), 10);
    }

    /// <summary>The picked-up Pokémon's name, and where it is when it's a bank Pokémon that's out in a game.</summary>
    string PickedName() => picked switch
    {
        InSave s when InSlot(s.Slot) is { } pk => Label(pk),
        InBank b when pending!.Bank.At(b.Slot) is { } pokemon => pokemon.CheckedOut is { } away
            ? $"{BankLabel(pokemon)} (out in {pending.SaveName(away.SaveId)})"
            : BankLabel(pokemon),
        _ => "a Pokémon",
    };

    Button SlotButton(Place place, Control? face, SlotState state)
    {
        var button = SlotFace.Button(face, place == picked ? SlotState.Picked : state);
        button.Tag = place;
        button.IsEnabled = pending!.LoadProblem is null;
        button.Click += (_, _) => Tap(place);
        return button;
    }

    static string Label(PKM pk) => pk.IsEgg ? "Egg" : pk.IsNicknamed ? pk.Nickname : EnglishNames.Species(pk.Species);

    static string Detail(PKM pk) => SlotFace.Line(pk.IsEgg, pk.CurrentLevel, pk.Gender);

    static string BankLabel(BankPokemon pokemon) =>
        pokemon.Summary.IsNicknamed ? pokemon.Summary.Nickname : EnglishNames.Species(pokemon.Summary.Species);
}
