using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Fantasia.Client;
using Fantasia.UI;

namespace Fantasia;

/// Entry point: login screen, host/join, dedicated server mode and the automated smoke test.
public partial class Main : Node
{
	public static Main I { get; private set; }

	Control login;
	LineEdit nameEdit, passEdit, hostEdit, portEdit;
	Label status;
	Button hostBtn, joinBtn;
	GameWorld game;
	public SettingsWindow SettingsUi { get; private set; }
	string autotestDir;
	string[] only;
	bool dedicated;

	public override void _EnterTree() => I = this;

	public override void _Ready()
	{
		Settings.Load();
		var args = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();
		int port = GameConst.DefaultPort;
		foreach (var a in args)
			if (a.StartsWith("--port=") && int.TryParse(a[7..], out var p)) port = p;
		bool dev = args.Contains("--dev");

		AddChild(new Music { Name = "Music" });
		AddChild(new Sfx { Name = "Sfx" });
		AddChild(new IconCache { Name = "IconCache" });
		Settings.Apply(GetTree());
		GetTree().Root.SizeChanged += () => { if (Settings.UiScale <= 0) Settings.Apply(GetTree()); };
		SettingsUi = SettingsWindow.Create();
		AddChild(SettingsUi);

		Net.I.MessageReceived += OnServerMessage;
		Net.I.ConnectionLost += why => { ReturnToLogin(); status.Text = why; };

		if (args.Contains("--server"))
		{
			dedicated = true;
			var err = Net.I.Host(port, true);
			if (err != Error.Ok) { GD.PrintErr($"Could not start server: {err}"); GetTree().Quit(1); return; }
			Net.I.Server.DevMode = dev;
			GD.Print("[Main] Dedicated server running. Ctrl+C to stop.");
			return;
		}

		if (args.Contains("--datacheck"))
		{
			int bad = 0;
			void Need(string id, string where) { if (ItemDb.Get(id) == null) { GD.PrintErr($"[DataCheck] missing item '{id}' in {where}"); bad++; } }
			foreach (var r in RecipeDb.All) { Need(r.Out, r.Id); foreach (var (i, _) in r.In) Need(i, r.Id); if (r.Unlock != null && RecipeDb.GetUnlock(r.Unlock) == null) { GD.PrintErr($"[DataCheck] bad unlock {r.Unlock}"); bad++; } }
			foreach (var n in NpcDb.All.Values) foreach (var d in n.Drops) Need(d.Item, "drops of " + n.Id);
			foreach (var sh in ShopDb.All.Values) foreach (var i in sh.Stock) Need(i, "shop " + sh.Id);
			foreach (var q in QuestDb.All)
			{
				if (NpcDb.Get(q.Giver) == null) { GD.PrintErr($"[DataCheck] quest {q.Id} giver {q.Giver} missing"); bad++; }
				foreach (var (i, _) in q.StartItems.Concat(q.ItemReward)) Need(i, "quest " + q.Id);
				foreach (var st in q.Stages) { foreach (var (i, _) in st.Items) Need(i, "quest " + q.Id); foreach (var npc in st.Npcs) if (NpcDb.Get(npc) == null) { GD.PrintErr($"[DataCheck] quest npc {npc}"); bad++; } }
				foreach (var u in q.Unlocks) if (RecipeDb.GetUnlock(u) == null) { GD.PrintErr($"[DataCheck] quest unlock {u}"); bad++; }
			}
			// Cross-references in the data files.
			void Bad(string msg) { GD.PrintErr($"[DataCheck] {msg}"); bad++; }
			foreach (var d in ItemDb.All.Values)
			{
				if (d.Teaches != null && RecipeDb.GetUnlock(d.Teaches) == null) Bad($"item {d.Id} teaches unknown unlock {d.Teaches}");
				if (d.ProvidesRune != null) Need(d.ProvidesRune, "staff " + d.Id);
			}
			foreach (var n in NpcDb.All.Values)
				if (n.ShopId != null && !ShopDb.All.ContainsKey(n.ShopId)) Bad($"npc {n.Id} has unknown shop {n.ShopId}");
			foreach (var sp in SpellDb.List) foreach (var rc in sp.Runes ?? System.Array.Empty<RuneCost>()) Need(rc.rune, "spell " + sp.Id);
			foreach (var c in FarmDb.Crops) { Need(c.Seed, "crop " + c.Id); Need(c.Produce, "crop " + c.Id); }
			foreach (var u in RecipeDb.Unlocks) Need("scroll_" + u.Key, "unlock " + u.Key);
			void Unique<T>(string file, System.Func<T, string> key)
			{
				foreach (var g in GameData.Load<System.Collections.Generic.List<T>>(file).GroupBy(key).Where(g => g.Count() > 1)) Bad($"{file}: id '{g.Key}' defined {g.Count()} times");
			}
			Unique<ItemDef>("items.json", d => d.Id); Unique<NpcDef>("npcs.json", d => d.Id); Unique<ResourceDef>("resources.json", d => d.Id);
			foreach (var g in RecipeDb.File.Recipes.GroupBy(r => r.Id).Where(g => g.Count() > 1)) Bad($"recipes.json: recipe '{g.Key}' defined {g.Count()} times");
			var world = MapGenerator.World;
			foreach (var a in world.Overworld.Spawns.Keys) if (!OverworldBuilder.SpawnAnchors.Contains(a)) Bad($"world.json: unknown spawn anchor '{a}' (use one of {string.Join(", ", OverworldBuilder.SpawnAnchors)})");
			foreach (var r in world.Overworld.QuarryRocks.Concat(world.Overworld.CircleRocks)) if (ResourceDb.Get(r.Res) == null) Bad($"world.json: unknown rock {r.Res}");
			foreach (var f in world.Overworld.FishingGroups) if (ResourceDb.Get(f.Res) == null) Bad($"world.json: unknown fishing spot {f.Res}");
			foreach (var dg in world.Dungeons)
			{
				foreach (var npc in dg.Monsters.Concat(dg.Boss.Select(b => b.Npc))) if (NpcDb.Get(npc) == null) Bad($"world.json: dungeon {dg.Id} npc {npc} unknown");
				foreach (var o in dg.Ores) if (ResourceDb.Get(o) == null) Bad($"world.json: dungeon {dg.Id} ore {o} unknown");
			}
			foreach (var (icon, o) in Client.EquipTuning.T.Outfits) if (!Client.Assets.HasModel(o.Model)) Bad($"equipment.json: outfit for {icon} uses missing model {o.Model}");
			foreach (var id in MapGenerator.MapIds)
			{
				var m = MapGenerator.Get(id);
				foreach (var sp in m.Spawns) if (NpcDb.Get(sp.NpcId) == null) Bad($"{id}: spawn of unknown npc {sp.NpcId}");
				var counts = new System.Collections.Generic.Dictionary<string, int>();
				foreach (var o in m.Objects)
				{
					string key = o.Res ?? (o.Station != null ? "station:" + o.Station : o.Kind == ObjKind.FarmPatch ? "patch" : null);
					if (key == null) continue;
					counts[key] = (counts.TryGetValue(key, out var cnt) ? cnt : 0) + 1;
					if (o.Res != null) foreach (var y in ResourceDb.Get(o.Res).Yields) Need(y.item, o.Res);
					bool reach = false;
					for (int x = o.X - 1; x <= o.X + o.W; x++) for (int z = o.Z - 1; z <= o.Z + o.D; z++) if (!o.Covers(x, z) && m.Walkable(x, z)) reach = true;
					if (!reach) { GD.PrintErr($"[DataCheck] {id}: {o.Name} at {o.X},{o.Z} unreachable"); bad++; }
				}
				GD.Print($"[DataCheck] {id}: " + string.Join(", ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
				foreach (var sp in m.Spawns) if (!m.Walkable(sp.X, sp.Z)) GD.PrintErr($"[DataCheck] {id}: spawn {sp.NpcId} blocked at {sp.X},{sp.Z}");
			}
			GD.Print($"[DataCheck] items={ItemDb.All.Count} recipes={RecipeDb.All.Count} quests={QuestDb.All.Count} problems={bad}");
			GetTree().Quit();
			return;
		}
		if (args.Contains("--handcheck"))
		{
			foreach (var key in new[] { "char_player", "npc_goblin", "npc_guard", "npc_skeleton", "npc_zombie", "npc_dark_wizard", "npc_barbarian", "npc_bandit", "npc_warlord", "npc_merchant", "npc_king" })
			{
				var r = RiggedHumanoid.TryCreate(key, 1.8f, Colors.White);
				var sk = r == null ? null : Assets.Find<Skeleton3D>(r);
				GD.Print($"[HandCheck] {key}: fingers L={sk?.FindBone("LeftFingers")} R={sk?.FindBone("RightFingers")} thumbs L={sk?.FindBone("LeftThumb")} R={sk?.FindBone("RightThumb")}");
				r?.Free();
			}
			GetTree().Quit();
			return;
		}
		var at = args.FirstOrDefault(a => a.StartsWith("--animtest="));
		if (at != null) { _ = AnimTest(at[11..]); return; }
		var fxt = args.FirstOrDefault(a => a.StartsWith("--fxtest="));
		if (fxt != null) { _ = FxTest(fxt[9..]); return; }
		var pf = args.FirstOrDefault(a => a.StartsWith("--propfacing="));
		if (pf != null) { _ = PropFacing(pf[13..]); return; }
		var strip = args.FirstOrDefault(a => a.StartsWith("--animstrip="));
		if (strip != null) { _ = AnimStrip(strip[12..]); return; }
		var pose = args.FirstOrDefault(a => a.StartsWith("--posetest="));
		if (pose != null) { _ = PoseTest(pose[11..]); return; }

		BuildLogin();
		var uishot = args.FirstOrDefault(a => a.StartsWith("--uishot="));
		if (uishot != null) _ = UiShots(uishot[9..]);
		var join = args.FirstOrDefault(a => a.StartsWith("--jointest="));
		if (join != null) { _ = JoinTest(join[11..]); return; }
		var export = args.FirstOrDefault(a => a.StartsWith("--exportdata="));
		if (export != null) { ExportData(export[13..]); return; }
		if (args.Contains("--worldhash")) { WorldHash(); return; }
		var skill = args.FirstOrDefault(a => a.StartsWith("--skilltest"));
		if (skill != null)
		{
			autotestDir = skill.Contains('=') ? skill[(skill.IndexOf('=') + 1)..] : ProjectSettings.GlobalizePath("user://skilltest");
			var o = args.FirstOrDefault(a => a.StartsWith("--only="));
			if (o != null) only = o[7..].Split(',');
			_ = SkillTest(port);
			return;
		}
		var auto = args.FirstOrDefault(a => a.StartsWith("--autotest"));
		if (auto != null)
		{
			autotestDir = auto.Contains('=') ? auto[(auto.IndexOf('=') + 1)..] : ProjectSettings.GlobalizePath("user://autotest");
			var o = args.FirstOrDefault(a => a.StartsWith("--only="));
			if (o != null) only = o[7..].Split(',');
			_ = AutoTest(port);
		}
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest) { Net.I?.Shutdown(); Settings.Save(); }
	}

	// ================= login screen =================

	/// `--uishot=<dir>`: screenshots of the title screen and the settings window.
	async Task UiShots(string dir)
	{
		autotestDir = dir;
		await Wait(1.5);
		await Shot("ui_login");
		SettingsUi.Open();
		await Wait(0.5);
		await Shot("ui_settings");
		GetTree().Quit();
	}

	/// The title / login screen (Scenes/UI/LoginScreen.tscn).
	void BuildLogin()
	{
		login = GD.Load<PackedScene>("res://Scenes/UI/LoginScreen.tscn").Instantiate<Control>();
		AddChild(login);
		nameEdit = login.GetNode<LineEdit>("%NameEdit");
		passEdit = login.GetNode<LineEdit>("%PassEdit");
		hostEdit = login.GetNode<LineEdit>("%HostEdit");
		portEdit = login.GetNode<LineEdit>("%PortEdit");
		hostBtn = login.GetNode<Button>("%HostButton");
		joinBtn = login.GetNode<Button>("%JoinButton");
		status = login.GetNode<Label>("%Status");
		nameEdit.Text = Settings.LastName;
		hostEdit.Text = Settings.LastHost;
		portEdit.Text = Settings.LastPort.ToString();
		hostBtn.Pressed += OnHost;
		joinBtn.Pressed += OnJoin;
		login.GetNode<Button>("%SettingsButton").Pressed += () => SettingsUi.Open();
		passEdit.TextSubmitted += _ => OnHost();
		Music.I?.Play("title");
	}

	void SetBusy(bool busy)
	{
		hostBtn.Disabled = busy;
		joinBtn.Disabled = busy;
	}

	int PortValue() => int.TryParse(portEdit.Text, out var p) && p > 0 && p < 65536 ? p : GameConst.DefaultPort;

	void Remember()
	{
		Settings.LastName = nameEdit.Text.Trim();
		Settings.LastHost = hostEdit.Text.Trim();
		Settings.LastPort = PortValue();
		Settings.Save();
	}

	void OnHost()
	{
		Remember();
		var err = Net.I.Host(PortValue(), false);
		if (err != Error.Ok) { status.Text = $"Could not host on port {PortValue()}: {err}"; return; }
		SetBusy(true);
		status.Text = "Creating world...";
		Net.I.LoginLocal(nameEdit.Text.Trim(), passEdit.Text);
	}

	void OnJoin()
	{
		Remember();
		var err = Net.I.Join(hostEdit.Text.Trim(), PortValue(), nameEdit.Text.Trim(), passEdit.Text);
		if (err != Error.Ok) { status.Text = $"Could not connect: {err}"; return; }
		SetBusy(true);
		status.Text = "Connecting...";
	}

	void OnServerMessage(ServerMsg m)
	{
		if (dedicated) return;
		if (m.T == "login_fail")
		{
			status.Text = m.S;
			Net.I.Shutdown();
			SetBusy(false);
		}
		else if (m.T == "login_ok" && game == null)
		{
			login.Visible = false;
			game = new GameWorld { Name = "Game", MyName = m.S };
			game.MyId = m.A;
			AddChild(game);
		}
	}

	public void Logout()
	{
		Net.I.Shutdown();
		ReturnToLogin();
		status.Text = "You have logged out.";
	}

	void ReturnToLogin()
	{
		if (dedicated) return;
		game?.QueueFree();
		game = null;
		if (login != null) login.Visible = true;
		SetBusy(false);
		Music.I?.Play("title");
	}

	// ================= automated smoke test =================

	async Task Wait(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);

	async Task Shot(string name)
	{
		if (only != null && !only.Any(name.Contains)) return;
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		System.IO.Directory.CreateDirectory(autotestDir);
		var img = GetViewport().GetTexture().GetImage();
		var path = System.IO.Path.Combine(autotestDir, name + ".png");
		img.SavePng(path);
		GD.Print($"[AutoTest] Saved {path}");
	}

	void Dev(string cmd) => Net.I.Send(new ClientMsg { T = C2S.Chat, S = cmd });

	async Task AutoTest(int port)
	{
		await Wait(0.5);
		nameEdit.Text = "Tester";
		passEdit.Text = "tester";
		var err = Net.I.Host(port, false);
		if (err != Error.Ok) { GD.PrintErr($"[AutoTest] host failed {err}"); GetTree().Quit(1); return; }
		Net.I.Server.DevMode = true;
		Net.I.LoginLocal("Tester", "tester");
		await Wait(4);
		var steps = new (string cmd, string shot, double wait)[]
		{
			("::tele 80 106", "01_town_square", 4),
			("::tele 96 90", "02_treasury_inside", 3),
			("::tele 79 88", "02b_castle_throne_room", 3),
			("::tele 66 111", "02c_inn_inside", 3),
			("::tele 65 102", "02d_forge_inside", 3),
			("::tele 80 94", "02e_castle_outside", 3),
			("::tele 66 97", "02f_west_street", 3),
			("::tele 80 79", "02g_behind_castle_cutaway", 3),
			("::tele 71 130", "02h_raider_hut", 3),
			("::tele 44 100", "03_farm", 3),
			("::tele 60 45", "04_forest", 3),
			("::tele 29 74", "05_goblin_camp", 3),
			("::tele 124 97", "06_bridge", 3),
			("::tele 140 107", "07_graveyard", 3),
			("::tele 134 58", "08_stone_circle", 3),
			("::tele 60 14", "09_wildlands", 3),
		};
		foreach (var (cmd, shot, wait) in steps)
		{
			if (only != null && !only.Any(shot.Contains)) continue;
			Dev(cmd);
			await Wait(wait);
			await Shot(shot);
			if (shot == "01_town_square") await MenuShot("guide", "01b_context_menu");
		}
		// Combat test near goblins (sturdy enough to survive the whole camp)
		Dev("::lvl vitality 50"); Dev("::lvl fortitude 40"); Dev("::lvl archery 20"); Dev("::lvl sorcery 20");
		Dev("::tele 29 74");
		await Wait(2);
		AttackNearest("goblin");
		await Wait(7);
		await Shot("10_combat_melee");
		Dev("::item shortbow 1"); Dev("::item bronze_arrow 100");
		await Wait(1);
		EquipByName("shortbow"); await Wait(0.7); EquipByName("bronze_arrow");
		await Wait(1);
		AttackNearest("goblin");
		await Wait(5);
		await Shot("11_combat_ranged");
		EquipByName("gale_staff");
		await Wait(1);
		Net.I.Send(new ClientMsg { T = C2S.Spell, S = "gale_dart" });
		await Wait(1);
		AttackNearest("goblin");
		await Wait(5);
		await Shot("12_combat_magic");
		Dev("::tele goblin_caves");
		await Wait(4);
		await Shot("13_goblin_caves");
		Dev("::tele crypt");
		await Wait(4);
		await Shot("14_crypt");
		Dev("::boss");
		await Wait(4);
		await Shot("15_crypt_boss");
		Dev("::tele overworld 80 104");
		await Wait(3);
		game?.Hud.OpenBank();
		await Wait(1.5);
		await Shot("16_bank_ui");
		game?.Hud.CloseWindows(false);
		game?.Hud.ToggleWorldMap();
		await Wait(1);
		await Shot("17_world_map");
		game?.Hud.ToggleWorldMap();
		game?.Hud.ShowTab(4);
		await Wait(0.8);
		await Shot("18_spellbook");
		SettingsUi.Open();
		await Wait(0.8);
		await Shot("19_settings");
		SettingsUi.Close();
		GD.Print("[AutoTest] Done.");
		Net.I.Shutdown();
		GetTree().Quit();
	}

	/// Debug: renders rigged characters holding a weapon with several candidate grip rotations.
	async Task PoseTest(string path)
	{
		var root = new Node3D();
		AddChild(root);
		root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0) });
		root.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.3f, 0.35f, 0.4f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.6f } });
		var sets = new (float yaw, string[] items)[]
		{
			(25, new[] { "azurite_sword", "azurite_heater", "azurite_helm" }),
			(110, new[] { "azurite_sword", "azurite_heater", "azurite_helm" }),
			(-60, new[] { "bronze_sword", "wooden_shield", "leather_hood", "leather_jerkin" }),
			(25, new[] { "ember_staff", "apprentice_hat", "apprentice_robe", "apprentice_skirt" }),
			(25, new[] { "moonsilk_hat", "moonsilk_robe", "moonsilk_skirt", "silk_gloves" }),
			(205, new[] { "cobalt_helm", "red_cape", "cobalt_chestplate", "cobalt_legguards" }),
			(160, new[] { "mantle_of_heroes", "leather_gloves", "leather_boots", "steel_sabre" }),
			(25, new[] { "ruby_pendant", "ring_of_the_warlord", "iron_warhammer", "steel_heater" }),
		};
		// --sets=a,b;c,d overrides the gear sets; --models=k1,k2 cycles character models across them.
		var setsArg = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--sets="));
		if (setsArg != null) sets = setsArg[7..].Split(';').Select(s => (25f, s.Split(',', StringSplitOptions.RemoveEmptyEntries))).ToArray();
		var modelsArg = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--models="));
		var models = modelsArg != null ? modelsArg[9..].Split(',') : new[] { "char_player" };
		for (int i = 0; i < sets.Length; i++)
		{
			var mk = models[i % models.Length];
			var npc = NpcDb.All.Values.FirstOrDefault(n => n.Model == mk);
			var r = RiggedHumanoid.TryCreate(mk, npc?.Height ?? 1.8f, npc?.Tint ?? Colors.White);
			if (r == null) { GD.PrintErr("no rig"); GetTree().Quit(1); return; }
			r.Position = new Vector3(i * (OS.GetCmdlineUserArgs().Contains("--heads") ? 4f : 1.3f) - 4.55f, 0, 0);
			var yawArg = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--yaw="));
			r.RotationDegrees = new Vector3(0, yawArg != null ? float.Parse(yawArg[6..]) : sets[i].yaw, 0);
			root.AddChild(r);
			var e2 = new string[11];
			foreach (var id in sets[i].items) { var d = ItemDb.Get(id); if (d != null) e2[(int)d.Slot] = id; }
			r.SetEquipment(e2);
			root.AddChild(new Label3D { Text = $"{i}", Position = new Vector3(i * 1.3f - 4.55f, 2.1f, 0), FontSize = 48, PixelSize = 0.004f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
		}
		var zoomArg = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--focus="));
		if (OS.GetCmdlineUserArgs().Contains("--heads"))
		{
			// One close-up per character, front and side: <path>_<i>f.png / _<i>s.png.
			var cam = new Camera3D { Current = true, Fov = 30 };
			root.AddChild(cam);
			await Wait(1.5);
			for (int i = 0; i < sets.Length; i++)
			{
				var r = root.GetChildren().OfType<RiggedHumanoid>().ElementAt(i);
				float h = r.Height;
				var head = r.GlobalPosition + Vector3.Up * h * 0.9f;
				var fwd = r.GlobalTransform.Basis.Z.Normalized();
				foreach (var (tag, dir) in new[] { ("f", fwd), ("s", fwd.Rotated(Vector3.Up, Mathf.Pi / 2)) })
				{
					cam.LookAtFromPosition(head + dir * 1.3f, head, Vector3.Up);
					await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
					await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
					GetViewport().GetTexture().GetImage().SavePng(path.Replace(".png", $"_{i}{tag}.png"));
				}
			}
			GD.Print($"[PoseTest] heads {path}");
			GetTree().Quit();
			return;
		}
		else if (zoomArg != null) { int fi = int.Parse(zoomArg[8..]); root.AddChild(new Camera3D { Position = new Vector3(fi * 1.3f - 4.55f, 1.3f, 2.2f), Current = true, Fov = 50 }); }
		else root.AddChild(new Camera3D { Position = new Vector3(0, 1.3f, 6.8f), Current = true, Fov = 60 });
		await Wait(1.5);
		autotestDir = System.IO.Path.GetDirectoryName(path);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		GetViewport().GetTexture().GetImage().SavePng(path);
		GD.Print($"[PoseTest] {path}");
		GetTree().Quit();
	}

	/// Headless multiplayer check: join host:port:name, walk, report what we see, quit.
	async Task JoinTest(string spec)
	{
		var parts = spec.Split(':');
		var err = Net.I.Join(parts[0], int.Parse(parts[1]), parts[2], "secret123");
		GD.Print($"[JoinTest] {parts[2]} connecting: {err}");
		for (int i = 0; i < 40 && game?.Me == null; i++) await Wait(0.25);
		if (game?.Me == null) { GD.PrintErr($"[JoinTest] {parts[2]} never spawned"); GetTree().Quit(2); return; }
		var start = game.Me.Tile;
		GD.Print($"[JoinTest] {parts[2]} spawned at {start} on {game.Map.Id}");
		await Wait(3);
		var target = new Tile(start.X + 3, start.Z - 4);
		Net.I.Send(new ClientMsg { T = C2S.Walk, A = target.X, B = target.Z });
		Net.I.Send(new ClientMsg { T = C2S.Chat, S = $"Hello from {parts[2]}!" });
		await Wait(4);
		var others = game.Views.Values.Where(v => v.IsPlayer && !v.IsMe).Select(v => $"{v.DisplayName}@{v.Tile}").ToList();
		GD.Print($"[JoinTest] {parts[2]} now at {game.Me.Tile} (asked {target}); sees players: [{string.Join(", ", others)}]; npcs: {game.Views.Values.Count(v => !v.IsPlayer)}");
		// Try to cheat: walk into a wall tile and request an item via dev command. Both must be refused.
		Net.I.Send(new ClientMsg { T = C2S.Chat, S = "::item rune_platebody 1" });
		Net.I.Send(new ClientMsg { T = C2S.Buy, S = "azurite_chestplate", A = 1 });
		await Wait(2);
		bool cheated = game.State?.Inv.Any(x => x?.Id == "azurite_chestplate") ?? false;
		GD.Print($"[JoinTest] {parts[2]} cheat succeeded: {cheated}");
		await Wait(2);
		Net.I.Shutdown();
		GetTree().Quit();
	}

	/// Debug: characters frozen mid-animation with weapons, to check grips and hands.
	/// Renders wall furniture from all four local sides (rows: camera sees local +Z, -X, -Z, +X).
	async Task PropFacing(string path)
	{
		var root = new Node3D();
		AddChild(root);
		root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 20, 0) });
		root.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.3f, 0.35f, 0.4f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.6f } });
		var keysArg = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--keys="));
		var keys = keysArg != null ? keysArg[7..].Split(',') : new[] { "bookshelf", "wardrobe", "shelf_potions", "weapon_rack", "fireplace", "counter", "armor_stand", "throne_royal" };
		var rows = new[] { ("+Z", 0f), ("-X", 90f), ("-Z", 180f), ("+X", -90f) };
		for (int i = 0; i < keys.Length; i++)
			for (int j = 0; j < rows.Length; j++)
			{
				var m = Assets.Model(keys[i], 1.6f);
				if (m == null) continue;
				m.Position = new Vector3(i * 2.2f - 7.7f, -j * 2.3f + 3.5f, 0);
				m.RotationDegrees = new Vector3(0, rows[j].Item2, 0);
				root.AddChild(m);
				if (i == 0) root.AddChild(new Label3D { Text = rows[j].Item1, Position = new Vector3(-9.3f, -j * 2.3f + 4.3f, 0), FontSize = 60, PixelSize = 0.006f });
				if (j == 0) root.AddChild(new Label3D { Text = keys[i], Position = new Vector3(i * 2.2f - 7.7f, 5.4f, 0), FontSize = 40, PixelSize = 0.005f });
			}
		root.AddChild(new Camera3D { Position = new Vector3(0, 0.8f, 14f), Current = true, Fov = 55 });
		await Wait(1.0);
		GetViewport().GetTexture().GetImage().SavePng(path);
		GetTree().Quit();
	}

	/// Film strip of one clip: 8 frames across its length (--clip=name, --model=key, --yaw=deg).
	async Task AnimStrip(string path)
	{
		string Arg(string k, string d) { var a = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith(k + "=")); return a != null ? a[(k.Length + 1)..] : d; }
		string clip = Arg("--clip", "idle"), key = Arg("--model", "char_player");
		float yaw = float.Parse(Arg("--yaw", "30"));
		var root = new Node3D();
		AddChild(root);
		root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0) });
		root.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.3f, 0.35f, 0.4f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.6f } });
		var rs = new System.Collections.Generic.List<RiggedHumanoid>();
		for (int i = 0; i < 8; i++)
		{
			var r = RiggedHumanoid.TryCreate(key, 1.8f, Colors.White);
			r.Position = new Vector3(i * 1.3f - 4.55f, 0, 0);
			r.RotationDegrees = new Vector3(0, yaw, 0);
			root.AddChild(r);
			var eq = new string[11];
			var w = Arg("--weapon", "");
			if (w != "") eq[(int)EquipSlot.Weapon] = w;
			r.SetEquipment(eq);
			rs.Add(r);
		}
		root.AddChild(new Camera3D { Position = new Vector3(0, 1.1f, 6.2f), Current = true, Fov = 60 });
		await Wait(1.0);
		for (int i = 0; i < rs.Count; i++) rs[i].PoseAt(clip, i / 8f);
		await Wait(0.5);
		GetViewport().GetTexture().GetImage().SavePng(path);
		GetTree().Quit();
	}

	/// Spell impacts in several colours, frozen mid-burst.
	async Task FxTest(string path)
	{
		var root = new Node3D();
		AddChild(root);
		root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0) });
		root.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.12f, 0.14f, 0.18f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.4f, GlowEnabled = true } });
		var cols = new[] { "#e0f0ff", "#4080ff", "#b07030", "#ff5020", "#a040ff" };
		for (int i = 0; i < cols.Length; i++)
			root.AddChild(new Client.SpellImpact { Color = Color.FromHtml(cols[i]), Position = new Vector3(i * 1.8f - 3.6f, 1f, 0) });
		root.AddChild(new Camera3D { Position = new Vector3(0, 1.3f, 5.5f), Current = true, Fov = 60 });
		for (int f = 0; f < 3; f++) await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		await Wait(0.12);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		GetViewport().GetTexture().GetImage().SavePng(path);
		GetTree().Quit();
	}

	async Task AnimTest(string path)
	{
		var root = new Node3D();
		AddChild(root);
		root.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0) });
		root.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.3f, 0.35f, 0.4f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.6f } });
		var variant = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--grip="));
		Vector3? grip = variant != null ? (Vector3?)GD.StrToVar("Vector3" + variant[7..]).AsVector3() : null;
		var cases = new (string clip, float t, string weapon, bool? bowRight, string label)[]
		{
			("idle", 0.3f, "steel_sword", null, "longsword"),
			("idle", 0.3f, "iron_sabre", null, "sabre"),
			("idle", 0.3f, "bronze_dagger", null, "dagger"),
			("idle", 0.3f, "verdite_greatsword", null, "greatsword"),
			("idle", 0.3f, "iron_warhammer", null, "warhammer"),
			("idle", 0.3f, "ember_staff", null, "staff"),
		};
		for (int i = 0; i < cases.Length; i++)
		{
			var c = cases[i];
			var r = RiggedHumanoid.TryCreate("char_player", 1.8f, Colors.White);
			r.BowRightOverride = c.bowRight;
			r.WeaponRotOverride = grip;
			r.Position = new Vector3(i * 1.7f - 4.25f, 0, 0);
			r.RotationDegrees = new Vector3(0, 60, 0);
			root.AddChild(r);
			var eq = new string[11];
			eq[(int)EquipSlot.Weapon] = c.weapon;
			r.SetEquipment(eq);
			root.AddChild(new Label3D { Text = c.label, Position = new Vector3(i * 1.7f - 4.25f, 2.2f, 0), FontSize = 40, PixelSize = 0.004f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled });
		}
		var closeArg = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--close"));
		bool close = closeArg != null;
		int closeIdx = closeArg != null && closeArg.Contains('=') ? int.Parse(closeArg[(closeArg.IndexOf('=') + 1)..]) : 0;
		var camNode = new Camera3D { Position = new Vector3(0, 1.3f, 5.5f), Current = true, Fov = 65 };
		root.AddChild(camNode);
		await Wait(1.0);
		int k = 0;
		foreach (var n in root.GetChildren())
			if (n is RiggedHumanoid rh) { rh.PoseAt(cases[k].clip, cases[k].t); k++; }
		await Wait(0.5);
		k = 0;
		foreach (var n in root.GetChildren())
			if (n is RiggedHumanoid rh) GD.Print($"[Hold] {cases[k++].label}: {rh.DebugWeaponDir()}");
		if (close)
		{
			// Frame the first character's right hand.
			var first = root.GetChildren().OfType<RiggedHumanoid>().ElementAt(closeIdx);
			var skel = Assets.Find<Skeleton3D>(first);
			int hb = skel.FindBone("RightHand");
			var hand = skel.GlobalTransform * skel.GetBoneGlobalPose(hb).Origin;
			camNode.Fov = 25;
			camNode.Fov = 45;
			camNode.GlobalPosition = hand + new Vector3(0.4f, 0.5f, 1.8f);
			camNode.LookAt(hand, Vector3.Up);
		}
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		GetViewport().GetTexture().GetImage().SavePng(path);
		GetTree().Quit();
	}

	/// Right-clicks an NPC through the real picking path and screenshots the menu.
	async Task MenuShot(string npcId, string name)
	{
		var v = game?.Views.Values.FirstOrDefault(x => x.DefId == npcId);
		if (v == null) { GD.Print($"[AutoTest] no {npcId} for menu"); return; }
		var pos = game.Rig.Cam.UnprojectPosition(v.GlobalPosition + Vector3.Up * 1.0f);
		var opts = game.OptionsAt(pos);
		GD.Print($"[AutoTest] options at {npcId}: " + string.Join(" | ", opts.Select(o => $"{o.Verb} {o.Target}{o.Suffix}")));
		game.Hud.ShowContextMenu(pos, opts);
		Input.WarpMouse(pos + new Vector2(0, 12));
		await Wait(0.5);
		await Shot(name);
		game.Hud.ShowContextMenu(pos, new System.Collections.Generic.List<MenuOption>());
		Input.WarpMouse(new Vector2(20, 450));
	}

	void AttackNearest(string npcId)
	{
		var me = game?.Me;
		if (me == null) return;
		var target = game.Views.Values.Where(v => !v.IsPlayer && v.DefId == npcId && !v.Dead)
			.OrderBy(v => v.Tile.Chebyshev(me.Tile)).FirstOrDefault();
		if (target != null) Net.I.Send(new ClientMsg { T = C2S.Npc, A = target.Id, S = "Attack" });
		else GD.Print($"[AutoTest] no {npcId} nearby");
	}

	void EquipByName(string id)
	{
		var st = game?.State;
		if (st == null) return;
		for (int i = 0; i < st.Inv.Length; i++)
			if (st.Inv[i]?.Id == id) { Net.I.Send(new ClientMsg { T = C2S.Inv, A = i, S = "Equip" }); return; }
	}
}
