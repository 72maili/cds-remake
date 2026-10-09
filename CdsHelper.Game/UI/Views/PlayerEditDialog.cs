using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CdsHelper.Game.Engine.Sea;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 에디터 창 — 제독의 능력치 · 기능 · 언어, 함대의 배 · 선원, 부하 넷을 손으로 고친다.
/// </summary>
/// <remarks>
/// 원본에 없는 창이다. 세이브 파일을 직접 고치지 않고 <b>놀고 있는 판의 <see cref="Player"/></b> 를 고친다 —
/// 세이브 형식이 바뀌어도 이 창은 따라갈 필요가 없고, 고친 값은 다음에 저장할 때 그대로 적힌다.
///
/// 칸에 적은 값은 「적용」을 눌러야 들어간다. 「닫기」만 누르면 아무것도 안 바뀐다.
/// 값은 게임이 받는 폭으로 잘린다 — 배 값은 선체 한계(<see cref="Hull.HpCeiling"/> 따위)를,
/// 선원은 정원(<see cref="Player.MaxCrew"/>)을 넘지 못한다.
///
/// 게임 규칙(기능 합 제한 · 부관과 통역의 언어 조건)은 보지 않는다 — 시험하려고 여는 창이다.
/// </remarks>
public sealed class PlayerEditDialog : GameWindow
{
    private readonly Engine.Game _game;
    private readonly Player _player;

    private readonly TextBox[] _abilities = new TextBox[Ability.Names.Length];
    private readonly TextBox[] _skills = new TextBox[Skill.Names.Length];
    private readonly TextBox[] _tongues = new TextBox[Skill.Languages.Length];

    // ── 배 ──
    private readonly List<ShipDraft> _drafts = [];
    private int _flagship;
    private readonly List<Hull> _hulls;
    private readonly ListBox _shipList = new() { Width = 230, Height = 300 };
    private readonly ComboBox _addHull = Combo(150);
    private readonly TextBox _shipName = Num(0, 150, digitsOnly: false);
    private readonly ComboBox _hull = Combo(150);
    private readonly TextBox _hp = Num(0), _maxHp = Num(0), _speedNow = Num(0), _speed = Num(0);
    private readonly TextBox _capacity = Num(0), _tonnage = Num(0), _needCrew = Num(0);
    private readonly TextBox _turrets = Num(0), _guns = Num(0);
    private readonly ComboBox _gun = Combo(110);
    private readonly ComboBox[] _sails = [Combo(80), Combo(80), Combo(80)];
    private readonly ComboBox _figurehead = Combo(150);
    private readonly TextBlock _maxHpTop = Hint(), _speedTop = Hint(), _capacityTop = Hint(),
                               _tonnageTop = Hint(), _needCrewTop = Hint(), _turretsTop = Hint();
    private readonly StackPanel _shipForm = new() { Margin = new Thickness(14, 0, 0, 0) };
    /// <summary>폼을 채우는 동안 — 선체 칸이 바뀌어도 값을 새로 깔지 않는다.</summary>
    private bool _loading;
    /// <summary>폼이 들고 있는 배. 없으면 -1.</summary>
    private int _shown = -1;

    // ── 선원 ──
    private readonly TextBox _crew = Num(0, 90);
    private readonly TextBlock _crewRange = Hint();

    // ── 부하 ──
    private readonly List<string> _people;
    private readonly TavernRoster? _roster;
    private readonly ComboBox[] _mateNames = new ComboBox[Player.MaxMates];
    /// <summary>부하 자리마다 능력 다섯(체력 · 지력 · 무력 · 매력 · 운) 다음 기능 일곱이다.</summary>
    private readonly TextBox[][] _mateFields = new TextBox[Player.MaxMates][];
    private static readonly string[] MateSkillNames = ["검술", "사격술", "포술", "항해술", "운용술", "의학", "과학"];

    private const string Empty = "(비움)";

    private PlayerEditDialog(Engine.Game game)
    {
        _game = game;
        _player = game.Player;

        Title = "에디터";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;

        // 조선소 선체에 해전에서 빼앗는 선체(코구 · 다우 …)까지 — 세이브를 되살릴 때 찾는 차례와 같다.
        _hulls = [.. Hull.All.Concat(Enumerable.Range(0, Hull.Table.Length).Select(Hull.FromTable))
                         .DistinctBy(h => h.Name)];
        foreach (var ship in _player.Ships)
            if (!_hulls.Any(h => h.Name == ship.Hull.Name)) _hulls.Add(ship.Hull);

        // 부하로 앉힐 수 있는 사람 — 술집 · 여관에 앉는 인물 모두다. 지금 나와 있는지는 안 본다.
        if (game.World is { } world)
        {
            _roster = TavernRoster.From(world.People, _ => true);
            _people = [.. world.People
                .Where(r => r.Building is PersonTable.Tavern or PersonTable.Inn && r.Name is { Length: > 0 } and not "???")
                .Select(r => r.Name).Distinct().Order()];
        }
        else _people = [];
        foreach (var mate in _player.Mates)
            if (mate.Length > 0 && !_people.Contains(mate)) _people.Add(mate);

        var pages = new List<(string, FrameworkElement)>
        {
            ("능력치", AbilityPage()),
            ("기능", LevelPage(Skill.Names, _skills, 4, "기능 열셋 — 0~3. 지력으로 정해지는 합계 제한은 보지 않습니다.")),
            ("언어", LevelPage(Skill.Languages, _tongues, 3, "언어 열넷 — 0~3. 게임의 「특기」 창은 기능과 언어를 함께 보여 줍니다.")),
            ("배", ShipPage()),
            ("선원", CrewPage()),
            ("항해사", MatePage()),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 12),
        };
        buttons.Children.Add(GameUi.PushButton("적용", Apply, 96));
        buttons.Children.Add(new Border { Width = 12 });
        buttons.Children.Add(GameUi.PushButton("닫기", Close, 96));

        var title = GameUi.TitleBar("에디터", Close);
        GameUi.EnableDrag(this, title);

        var stack = new StackPanel();
        stack.Children.Add(title);
        stack.Children.Add(Tabs([.. pages]));
        foreach (var (_, page) in pages) stack.Children.Add(page);
        stack.Children.Add(buttons);

        Content = new Border
        {
            Background = GameUi.Back,
            BorderBrush = GameUi.Edge,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(4),
            Child = stack,
        };

        LoadAll();
        KeyDown += (_, e) => { if (e.Key is Key.Escape) Close(); };
    }

    public static void Show(Window owner, Engine.Game game) =>
        new PlayerEditDialog(game) { Owner = owner }.ShowDialog();

    // ── 판들 ─────────────────────────────────────────────────────────────────

    private FrameworkElement AbilityPage()
    {
        var page = Page();
        page.Children.Add(Note($"보이는 값 1~{Ability.Max}. 작위가 얹는 보너스는 따로 붙습니다(여기 값에는 안 들어갑니다)."));
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < _abilities.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            _abilities[i] = Num(0);
            Place(grid, Label(Ability.Names[i], 70), i, 0);
            Place(grid, _abilities[i], i, 1);
            // 신앙심은 게임 화면에 안 뜬다(Ability.Shown).
            if (i >= Ability.Shown) Place(grid, Hint("화면에는 안 뜨는 값"), i, 2);
        }
        page.Children.Add(grid);
        return page;
    }

    private static FrameworkElement LevelPage(string[] names, TextBox[] boxes, int columns, string note)
    {
        var page = Page();
        page.Children.Add(Note(note));
        var grid = new Grid();
        for (int c = 0; c < columns * 2; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int rows = (names.Length + columns - 1) / columns;
        for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition());
        for (int i = 0; i < names.Length; i++)
        {
            boxes[i] = Num(0, 44);
            Place(grid, Label(names[i], 130), i / columns, i % columns * 2);
            Place(grid, boxes[i], i / columns, i % columns * 2 + 1);
        }
        page.Children.Add(grid);
        return page;
    }

    private FrameworkElement ShipPage()
    {
        var page = Page();
        page.Children.Add(Note($"함대 최대 {Player.MaxShips}척. 값은 선체 한계까지만 들어갑니다. 선체를 바꾸면 그 선체 기본값으로 다시 깝니다."));

        foreach (var h in _hulls) _hull.Items.Add(h.Name);
        foreach (var h in _hulls) _addHull.Items.Add(h.Name);
        _addHull.SelectedIndex = 0;
        _gun.Items.Add("없음");
        foreach (var c in Cannon.All) _gun.Items.Add(c.Name);
        foreach (var box in _sails)
            foreach (var s in Ship.SailNames) box.Items.Add(s);
        _figurehead.Items.Add("없음");
        for (int i = 0; i < Ship.FigureheadCount; i++)
            _figurehead.Items.Add(Figureheads.ShortName(i) is { Length: > 0 } n ? n : $"{i}번");

        _shipList.SelectionChanged += (_, _) => ShowShip(_shipList.SelectedIndex);
        _hull.SelectionChanged += (_, _) =>
        {
            if (_loading || _shown < 0 || _hull.SelectedIndex < 0) return;
            var picked = _hulls[_hull.SelectedIndex];
            var d = _drafts[_shown];
            if (picked.Name == d.Hull.Name) return;
            d.Hull = picked;
            d.Stats = Ship.Stats.Of(picked) with { Lent = d.Stats.Lent };
            d.Hp = picked.Hp;
            FillShip(d);
            RefreshShipList();
        };

        var left = new StackPanel();
        left.Children.Add(_shipList);
        var addLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        addLine.Children.Add(_addHull);
        addLine.Children.Add(GameUi.PushButton("추가", AddShip, 72));
        left.Children.Add(addLine);
        var editLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        editLine.Children.Add(GameUi.PushButton("기함으로", MakeFlagship, 110));
        editLine.Children.Add(GameUi.PushButton("빼기", RemoveShip, 110));
        left.Children.Add(editLine);

        var form = new Grid();
        for (int c = 0; c < 3; c++) form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sailLine = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var box in _sails) sailLine.Children.Add(box);
        var gunLine = new StackPanel { Orientation = Orientation.Horizontal };
        gunLine.Children.Add(_gun);
        gunLine.Children.Add(_guns);
        gunLine.Children.Add(Hint("문"));
        (string, FrameworkElement, FrameworkElement?)[] rows =
        [
            ("이름", _shipName, null),
            ("선체", _hull, null),
            ("내구", _hp, Hint("지금 값")),
            ("최대 내구", _maxHp, _maxHpTop),
            ("추진력", _speedNow, Hint("지금 값")),
            ("최대 추진력", _speed, _speedTop),
            ("적재용량", _capacity, _capacityTop),
            ("적재중량", _tonnage, _tonnageTop),
            ("필요승원", _needCrew, _needCrewTop),
            ("포탑", _turrets, _turretsTop),
            ("대포", gunLine, null),
            ("돛", sailLine, null),
            ("선수상", _figurehead, null),
        ];
        for (int r = 0; r < rows.Length; r++)
        {
            form.RowDefinitions.Add(new RowDefinition());
            Place(form, Label(rows[r].Item1, 100), r, 0);
            Place(form, rows[r].Item2, r, 1);
            if (rows[r].Item3 is { } extra) Place(form, extra, r, 2);
        }
        _shipForm.Children.Add(form);

        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(left);
        body.Children.Add(_shipForm);
        page.Children.Add(body);
        return page;
    }

    private FrameworkElement CrewPage()
    {
        var page = Page();
        page.Children.Add(Note("정원은 배마다 선체 필요승원의 5배입니다. 「배」 탭에서 고친 함대로 셉니다."));
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(Label("선원", 70));
        line.Children.Add(_crew);
        line.Children.Add(Hint("명"));
        line.Children.Add(GameUi.PushButton("가득", () => { StoreShip(); _crew.Text = CrewSpan().Max.ToString(); }, 72));
        page.Children.Add(line);
        page.Children.Add(_crewRange);
        _crewRange.Margin = new Thickness(0, 8, 0, 0);
        page.IsVisibleChanged += (_, _) => { if (page.IsVisible) { StoreShip(); SyncCrewRange(); } };
        return page;
    }

    private FrameworkElement MatePage()
    {
        var page = Page();
        page.Children.Add(Note("자리마다 사람을 고르고 능력을 고칩니다. 능력은 보이는 값 1~100, 기능은 0~3. 언어 조건은 보지 않습니다."));
        var grid = new Grid();
        int columns = 2 + 5 + MateSkillNames.Length;
        for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        string[] heads = ["자리", "사람", "체력", "지력", "무력", "매력", "운", .. MateSkillNames];
        for (int c = 0; c < heads.Length; c++)
        {
            var head = Label(heads[c].Length > 2 && c >= 7 ? heads[c][..2] : heads[c], double.NaN);
            head.HorizontalAlignment = HorizontalAlignment.Center;
            head.FontSize = 13;
            Place(grid, head, 0, c);
        }

        for (int slot = 0; slot < Player.MaxMates; slot++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            int row = slot + 1;
            Place(grid, Label(Player.MateRoles[slot], 64), row, 0);

            var names = Combo(170);
            names.IsEditable = true;
            names.IsTextSearchEnabled = true;
            names.Items.Add(Empty);
            foreach (var p in _people) names.Items.Add(p);
            _mateNames[slot] = names;
            Place(grid, names, row, 1);

            var fields = new TextBox[5 + MateSkillNames.Length];
            for (int f = 0; f < fields.Length; f++)
            {
                fields[f] = Num(0, f < 5 ? 46 : 36);
                Place(grid, fields[f], row, 2 + f);
            }
            _mateFields[slot] = fields;

            int at = slot;
            names.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                FillMate(at, names.SelectedItem as string);
            };
        }
        page.Children.Add(grid);
        return page;
    }

    // ── 채우기 ───────────────────────────────────────────────────────────────

    /// <summary>모든 칸을 지금 판의 값으로 다시 적는다.</summary>
    private void LoadAll()
    {
        for (int i = 0; i < _abilities.Length; i++)
            _abilities[i].Text = Ability.Display(i < _player.Abilities.Length ? _player.Abilities[i] : Ability.Base).ToString();
        for (int i = 0; i < _skills.Length; i++)
            _skills[i].Text = _player.LevelOf(Skill.Names[i]).ToString();
        for (int i = 0; i < _tongues.Length; i++)
            _tongues[i].Text = _player.TongueOf(Skill.Languages[i]).ToString();

        _drafts.Clear();
        _drafts.AddRange(_player.Ships.Select(ShipDraft.Of));
        _flagship = _player.Flagship;
        _shown = -1;
        RefreshShipList();
        // 고르는 줄이 그대로면 SelectionChanged 가 안 와서 폼이 옛 값을 든 채 남는다 — 손으로 채운다.
        _loading = true;
        _shipList.SelectedIndex = _drafts.Count > 0 ? 0 : -1;
        _loading = false;
        ShowShip(_shipList.SelectedIndex);

        _crew.Text = _player.Crew.ToString();
        SyncCrewRange();

        _loading = true;
        for (int slot = 0; slot < Player.MaxMates; slot++)
        {
            string name = _player.MateAt(slot);
            _mateNames[slot].SelectedItem = name.Length > 0 ? name : Empty;
            FillMate(slot, name);
        }
        _loading = false;
    }

    private void RefreshShipList()
    {
        int keep = _shipList.SelectedIndex;
        _loading = true;
        _shipList.Items.Clear();
        for (int i = 0; i < _drafts.Count; i++)
        {
            var d = _drafts[i];
            _shipList.Items.Add($"{(i == _flagship ? "★" : "  ")} {i + 1}. {d.Name} ({d.Hull.Name})");
        }
        _shipList.SelectedIndex = Math.Min(keep, _drafts.Count - 1);
        _loading = false;
    }

    private void ShowShip(int index)
    {
        if (_loading) return;
        StoreShip();
        _shown = index >= 0 && index < _drafts.Count ? index : -1;
        _shipForm.IsEnabled = _shown >= 0;
        if (_shown >= 0) FillShip(_drafts[_shown]);
    }

    private void FillShip(ShipDraft d)
    {
        _loading = true;
        var h = d.Hull;
        var s = d.Stats;
        _shipName.Text = d.Name;
        _hull.SelectedIndex = _hulls.FindIndex(x => x.Name == h.Name);
        _hp.Text = d.Hp.ToString();
        _maxHp.Text = s.MaxHp.ToString();
        _speedNow.Text = (s.SpeedNow ?? s.Speed).ToString();
        _speed.Text = s.Speed.ToString();
        _capacity.Text = s.Capacity.ToString();
        _tonnage.Text = s.Tonnage.ToString();
        _needCrew.Text = s.Crew.ToString();
        _turrets.Text = s.Turrets.ToString();
        _gun.SelectedIndex = s.Gun + 1;
        _guns.Text = s.Guns.ToString();
        int masts = Math.Clamp(h.MaxMasts, 1, Ship.MastSlots);
        for (int i = 0; i < _sails.Length; i++)
        {
            _sails[i].SelectedIndex = s.Sails != null && i < s.Sails.Count ? s.Sails[i] : Ship.NoSail;
            _sails[i].IsEnabled = i < masts;
        }
        _figurehead.SelectedIndex = (s.Figurehead ?? -1) + 1;

        _maxHpTop.Text = $"한계 {h.HpCeiling}";
        _speedTop.Text = $"한계 {h.SpeedCeiling}";
        _capacityTop.Text = $"한계 {h.CapacityCeiling}";
        _tonnageTop.Text = $"한계 {h.TonnageCeiling}";
        _needCrewTop.Text = $"한계 {h.Crew * Ship.RefitCeiling}";
        _turretsTop.Text = $"한계 {Math.Max(0, h.GunsCeiling)}";
        _loading = false;
    }

    /// <summary>폼에 적힌 값을 들고 있는 배에 담는다 — 선체 한계로 자른다.</summary>
    private void StoreShip()
    {
        if (_shown < 0 || _shown >= _drafts.Count) return;
        var d = _drafts[_shown];
        var h = d.Hull;
        var s = d.Stats;

        int maxHp = Read(_maxHp, 1, Math.Max(1, h.HpCeiling), s.MaxHp);
        int speed = Read(_speed, 1, Math.Max(1, h.SpeedCeiling), s.Speed);
        int turrets = Read(_turrets, 0, Math.Max(0, h.GunsCeiling), s.Turrets);
        int gun = _gun.SelectedIndex - 1;
        int guns = gun < 0 ? 0 : Read(_guns, 0, turrets, s.Guns);
        if (guns == 0) gun = -1;
        int masts = Math.Clamp(h.MaxMasts, 1, Ship.MastSlots);
        var sails = new int[Ship.MastSlots];
        for (int i = 0; i < sails.Length; i++)
            sails[i] = i < masts && _sails[i].SelectedIndex >= 0 ? _sails[i].SelectedIndex : Ship.NoSail;

        d.Name = _shipName.Text.Trim() is { Length: > 0 } name ? name : h.Name;
        d.Hp = Read(_hp, 0, maxHp, d.Hp);
        d.Stats = s with
        {
            MaxHp = maxHp,
            Speed = speed,
            SpeedNow = Read(_speedNow, 0, speed, s.SpeedNow ?? speed),
            Capacity = Read(_capacity, 1, Math.Max(1, h.CapacityCeiling), s.Capacity),
            Tonnage = Read(_tonnage, 1, Math.Max(1, h.TonnageCeiling), s.Tonnage),
            Crew = Read(_needCrew, 1, Math.Max(1, h.Crew * Ship.RefitCeiling), s.Crew),
            Turrets = turrets,
            Gun = gun,
            Guns = guns,
            Sails = sails,
            Figurehead = _figurehead.SelectedIndex - 1,
        };
    }

    private void FillMate(int slot, string? name)
    {
        var fields = _mateFields[slot];
        Player.MateInfo? who = string.IsNullOrEmpty(name) || name == Empty ? null : MateOf(name);
        foreach (var f in fields) f.IsEnabled = who != null;
        if (who is not { } w)
        {
            foreach (var f in fields) f.Text = "";
            return;
        }
        int[] values = [Ability.Display(w.Body), Ability.Display(w.Mind), Ability.Display(w.Might),
                        Ability.Display(w.Charm), Ability.Display(w.Luck),
                        w.Sword, w.Shooting, w.Gunnery, w.Sailing, w.Handling, w.Medicine, w.Science];
        for (int i = 0; i < fields.Length; i++) fields[i].Text = values[i].ToString();
    }

    /// <summary>그 사람의 신상 — 적어 둔 것을 먼저, 없으면 인물 표에서 베낀다.</summary>
    private Player.MateInfo? MateOf(string name) =>
        _player.MateInfoOf(name)
        ?? (_roster?.Find(name) is { } person ? Tavern.MateInfoOf(person) : null);

    private (int Min, int Max) CrewSpan() =>
        (_drafts.Sum(d => d.Stats.Crew), _drafts.Sum(d => d.Hull.Crew * 5));

    private void SyncCrewRange()
    {
        var (min, max) = CrewSpan();
        _crewRange.Text = $"필요 {min}명 · 정원 {max}명 (정원을 넘는 값은 정원으로 잘립니다)";
    }

    // ── 배 단추 ─────────────────────────────────────────────────────────────

    private void AddShip()
    {
        if (_drafts.Count >= Player.MaxShips)
        {
            NoticeDialog.Show(this, $"함대에는 {Player.MaxShips}척까지만 둘 수 있습니다");
            return;
        }
        if (_addHull.SelectedIndex < 0) return;
        StoreShip();
        var hull = _hulls[_addHull.SelectedIndex];
        var taken = _drafts.Select(d => d.Name)
                           .Concat(_player.Docked.Values.SelectMany(l => l).Select(s => s.Name));
        _drafts.Add(ShipDraft.New(hull, ShipNames.Suggest(taken)));
        if (_drafts.Count == 1) _flagship = 0;
        _shown = -1;
        RefreshShipList();
        _shipList.SelectedIndex = _drafts.Count - 1;
    }

    private void RemoveShip()
    {
        int at = _shipList.SelectedIndex;
        if (at < 0) return;
        if (_drafts.Count <= 1)
        {
            NoticeDialog.Show(this, "마지막 한 척은 뺄 수 없습니다");
            return;
        }
        _shown = -1;   // 빼는 배에는 폼 값을 안 담는다
        _drafts.RemoveAt(at);
        if (_flagship > at) _flagship--;
        else if (_flagship == at) _flagship = 0;
        RefreshShipList();
        _shipList.SelectedIndex = Math.Min(at, _drafts.Count - 1);
        ShowShip(_shipList.SelectedIndex);
    }

    private void MakeFlagship()
    {
        if (_shipList.SelectedIndex < 0) return;
        StoreShip();
        _flagship = _shipList.SelectedIndex;
        RefreshShipList();
    }

    // ── 적용 ────────────────────────────────────────────────────────────────

    private void Apply()
    {
        StoreShip();

        // 부하는 먼저 따져 본다 — 같은 사람이 두 자리에 앉으면 아무것도 안 바꾼다.
        var picked = new string[Player.MaxMates];
        for (int slot = 0; slot < picked.Length; slot++)
        {
            string text = (_mateNames[slot].SelectedItem as string ?? _mateNames[slot].Text ?? "").Trim();
            picked[slot] = text == Empty ? "" : text;
            if (picked[slot].Length > 0 && MateOf(picked[slot]) == null)
            {
                NoticeDialog.Show(this, $"{Player.MateRoles[slot]} 자리의 「{picked[slot]}」을(를) 인물 표에서 찾지 못했습니다");
                return;
            }
        }
        var twice = picked.Where(n => n.Length > 0).GroupBy(n => n).FirstOrDefault(g => g.Count() > 1);
        if (twice != null)
        {
            NoticeDialog.Show(this, $"「{twice.Key}」이(가) 두 자리에 앉아 있습니다");
            return;
        }

        // 능력치 · 기능 · 언어
        var abilities = new int[_abilities.Length];
        for (int i = 0; i < abilities.Length; i++)
        {
            int was = Ability.Display(i < _player.Abilities.Length ? _player.Abilities[i] : Ability.Base);
            abilities[i] = Ability.Store(Read(_abilities[i], 1, Ability.Max, was));
        }
        _player.SetAbilities(abilities);
        for (int i = 0; i < _skills.Length; i++)
            _player.SetSkill(Skill.Names[i], Read(_skills[i], 0, Skill.MaxLevel, _player.LevelOf(Skill.Names[i])));
        for (int i = 0; i < _tongues.Length; i++)
            _player.SetTongue(Skill.Languages[i], Read(_tongues[i], 0, Skill.MaxLevel, _player.TongueOf(Skill.Languages[i])));

        // 배 — 세이브를 되살리는 길(RestoreFleet)로 함대를 통째로 다시 세운다. 맡겨 둔 배는 그대로 넘긴다.
        // RestoreFleet 이 맡겨 둔 배를 먼저 비우므로 넘길 것은 지금 다 베껴 둔다.
        var docked = _player.Docked.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        _player.RestoreFleet(
            [.. _drafts.Select(d => d.Hull.Name)], _flagship,
            [.. docked.Select(kv => KeyValuePair.Create(kv.Key, kv.Value.Select(s => s.Hull.Name).ToList()))],
            [.. _drafts.Select(d => d.Hp)],
            docked.ToDictionary(kv => kv.Key, kv => kv.Value.Select(s => s.Hp).ToList()),
            [.. _drafts.Select(d => d.Stats)],
            docked.ToDictionary(kv => kv.Key, kv => kv.Value.Select(s => s.Snapshot()).ToList()),
            [.. _drafts.Select(d => d.Name)],
            docked.ToDictionary(kv => kv.Key, kv => kv.Value.Select(s => s.Name).ToList()));

        // 선원 — 배를 고친 뒤라야 정원이 맞는다.
        _player.SetCrew(Read(_crew, 0, int.MaxValue, _player.Crew));

        // 부하
        for (int slot = 0; slot < picked.Length; slot++)
        {
            string name = picked[slot];
            if (name.Length == 0)
            {
                _player.SetMate(slot, "");
                continue;
            }
            var who = MateOf(name)!.Value;
            var f = _mateFields[slot];
            int Stat(int i, int stored) => Ability.Store(Read(f[i], 1, Ability.Max, Ability.Display(stored)));
            int Level(int i, int was) => Read(f[i], 0, Skill.MaxLevel, was);
            // 자리를 바꿔 고른 사람이면 칸이 그 사람 값으로 새로 채워졌으므로 그대로 읽으면 된다.
            _player.RememberMate(who with
            {
                Body = Stat(0, who.Body), Mind = Stat(1, who.Mind), Might = Stat(2, who.Might),
                Charm = Stat(3, who.Charm), Luck = Stat(4, who.Luck),
                Sword = Level(5, who.Sword), Shooting = Level(6, who.Shooting), Gunnery = Level(7, who.Gunnery),
                Sailing = Level(8, who.Sailing), Handling = Level(9, who.Handling),
                Medicine = Level(10, who.Medicine), Science = Level(11, who.Science),
            });
            _player.SetMate(slot, name);
        }

        LoadAll();
        NoticeDialog.Show(this, "적용했습니다. 저장하면 세이브에 남습니다.", "에디터");
    }

    // ── 조각 ────────────────────────────────────────────────────────────────

    /// <summary>폼이 고치는 배 한 척 — 「적용」 전까지는 판에 안 들어간다.</summary>
    private sealed class ShipDraft
    {
        public required Hull Hull { get; set; }
        public required string Name { get; set; }
        public int Hp { get; set; }
        public required Ship.Stats Stats { get; set; }

        public static ShipDraft Of(Ship s) => new() { Hull = s.Hull, Name = s.Name, Hp = s.Hp, Stats = s.Snapshot() };

        public static ShipDraft New(Hull h, string name) =>
            new() { Hull = h, Name = name, Hp = h.Hp, Stats = Ship.Stats.Of(h) };
    }

    /// <summary>탭 머리 — <see cref="DevDialog"/> 와 같은 모양이다.</summary>
    private static FrameworkElement Tabs(params (string Text, FrameworkElement Page)[] pages)
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 0) };
        var heads = new List<(Border Head, FrameworkElement Page)>();

        void Select(FrameworkElement page)
        {
            foreach (var (head, p) in heads)
            {
                bool on = ReferenceEquals(p, page);
                p.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                head.Background = on ? new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
                ((TextBlock)head.Child).Opacity = on ? 1 : 0.6;
            }
        }

        foreach (var (text, page) in pages)
        {
            var head = new Border
            {
                BorderBrush = GameUi.Edge,
                BorderThickness = new Thickness(1, 1, 1, 0),
                Padding = new Thickness(16, 4, 16, 4),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, Foreground = GameUi.Text, FontWeight = FontWeights.Bold, FontSize = 15 },
            };
            head.MouseLeftButtonDown += (_, _) => Select(page);
            heads.Add((head, page));
            bar.Children.Add(head);
        }
        Select(pages[0].Page);
        return bar;
    }

    private static StackPanel Page() => new() { Margin = new Thickness(12, 10, 12, 4), MinWidth = 640, MinHeight = 200 };

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static TextBlock Label(string text, double width) => new()
    {
        Text = text,
        Width = width,
        Foreground = GameUi.Text,
        FontWeight = FontWeights.Bold,
        FontSize = 14,
        Margin = new Thickness(0, 3, 6, 3),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = GameUi.Text,
        Opacity = 0.75,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 760,
        Margin = new Thickness(0, 0, 0, 8),
    };

    private static TextBlock Hint(string text = "") => new()
    {
        Text = text,
        Foreground = GameUi.Text,
        Opacity = 0.7,
        FontSize = 13,
        Margin = new Thickness(4, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static ComboBox Combo(double width) => new()
    {
        Width = width,
        Margin = new Thickness(0, 2, 6, 2),
        FontSize = 13,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    /// <summary>수 칸. <paramref name="digitsOnly"/> 면 숫자만 받는다.</summary>
    private static TextBox Num(int value, double width = 70, bool digitsOnly = true)
    {
        var box = new TextBox
        {
            Text = value.ToString(),
            Width = width,
            Margin = new Thickness(0, 2, 6, 2),
            Padding = new Thickness(4, 1, 4, 1),
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            TextAlignment = digitsOnly ? TextAlignment.Right : TextAlignment.Left,
            Background = GameUi.ItemFill,
            Foreground = Brushes.Black,
            BorderBrush = GameUi.ItemEdge,
            BorderThickness = new Thickness(2),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (digitsOnly) box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        return box;
    }

    /// <summary>칸에 적힌 수를 폭 안으로 자른다. 수가 아니면 <paramref name="fallback"/>.</summary>
    private static int Read(TextBox box, int min, int max, int fallback) =>
        Math.Clamp(int.TryParse(box.Text.Replace(",", "").Trim(), out int v) ? v : fallback, min, Math.Max(min, max));
}
