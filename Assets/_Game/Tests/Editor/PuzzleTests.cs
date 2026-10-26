using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Головоломки этапа 6: задачи на код (вход, ответы, смена входа по запускам), лабиринт (детерминизм,
/// связность, длина пути), награды и решение через PuzzleManager.
/// </summary>
public class PuzzleTests
{
    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        foreach (Object obj in _created)
            if (obj != null) Object.DestroyImmediate(obj);
        _created.Clear();
    }

    // ─── CodeTask ─────────────────────────────────────────────

    [Test]
    public void CodeTask_Answers_MatchKind()
    {
        int[] input = { 5, 12, 3, 20, 11, 1, 10, 7, 15, 2, 9, 18 };

        Assert.AreEqual(113, CodeTask.Create(CodeTaskKind.Sum, input).Answer);
        Assert.AreEqual(20, CodeTask.Create(CodeTaskKind.Max, input).Answer);
        // Больше 10: 12, 20, 11, 15, 18
        Assert.AreEqual(5, CodeTask.Create(CodeTaskKind.Count, input).Answer);
        Assert.AreEqual(10, CodeTask.Create(CodeTaskKind.Count, input).Limit);
        Assert.AreEqual(0, CodeTask.Create(CodeTaskKind.Sum, input).Limit);
    }

    [Test]
    public void CodeTask_IsCorrect_ComparesNumbers()
    {
        CodeTask task = CodeTask.Create(CodeTaskKind.Sum, new[] { 1, 2, 3 });

        Assert.IsTrue(task.IsCorrect(6));
        Assert.IsTrue(task.IsCorrect(6.0000000001));
        Assert.IsFalse(task.IsCorrect(7));
        Assert.IsFalse(task.IsCorrect(double.NaN));
        Assert.IsFalse(task.IsCorrect(double.PositiveInfinity));
    }

    [TestCase(CodeTaskKind.Sum, 1, 50)]
    [TestCase(CodeTaskKind.Max, 1, 99)]
    [TestCase(CodeTaskKind.Count, 1, 99)]
    public void CodeTask_Generate_NumbersInRange(CodeTaskKind kind, int min, int max)
    {
        var random = new System.Random(42);
        for (int i = 0; i < 50; i++)
        {
            CodeTask task = CodeTask.Generate(kind, random);
            Assert.AreEqual(CodeTask.GetInputLength(kind), task.Input.Count);
            foreach (int n in task.Input)
                Assert.That(n, Is.InRange(min, max));
            Assert.IsFalse(string.IsNullOrEmpty(task.Text));
        }
    }

    [Test]
    public void CodeTask_Count_RandomLimit_WideAnswerRange()
    {
        // Ответ равномерно 0..CountInputLength, limit случайный — ответ не угадать по limit
        var random = new System.Random(7);
        var answers = new HashSet<int>();
        var limits = new HashSet<int>();
        int sum = 0;
        const int Samples = 400;

        for (int i = 0; i < Samples; i++)
        {
            CodeTask task = CodeTask.Generate(CodeTaskKind.Count, random);
            Assert.That(task.Limit, Is.InRange(CodeTask.CountLimitMin, CodeTask.CountLimitMax));
            Assert.That(task.Answer, Is.InRange(0, CodeTask.CountInputLength));
            answers.Add(task.Answer);
            limits.Add(task.Limit);
            sum += task.Answer;
        }

        Assert.GreaterOrEqual(answers.Count, CodeTask.CountInputLength - 1);
        Assert.Greater(limits.Count, 10);
        double half = CodeTask.CountInputLength / 2.0;
        Assert.That(sum / (double)Samples, Is.InRange(half - 2.0, half + 2.0));
    }

    [Test]
    public void CodeTaskSource_SameRun_SameInput_NewRun_NewInput()
    {
        var source = new CodeTaskSource(CodeTaskKind.Sum, seed: 123);

        CodeTask first = source.GetTask(1);
        Assert.AreSame(first, source.GetTask(1));

        // Вход меняется от запуска к запуску (хотя бы один из следующих запусков отличается)
        bool changed = false;
        for (int run = 2; run < 6 && !changed; run++)
            changed = !Equal(first.Input, source.GetTask(run).Input);
        Assert.IsTrue(changed);

        // Ответ проверяется по задаче своего запуска
        CodeTask current = source.GetTask(7);
        Assert.IsTrue(source.Check(7, current.Answer));
    }

    [Test]
    public void PickKinds_AreDistinct()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            List<CodeTaskKind> kinds = PuzzleGenerator.PickKinds(new System.Random(seed), 2);
            Assert.AreEqual(2, kinds.Count);
            Assert.AreNotEqual(kinds[0], kinds[1]);
        }
    }

    // ─── MazeLayout ───────────────────────────────────────────

    [Test]
    public void Maze_SameSeed_SameLayout()
    {
        MazeLayout a = MazeLayout.Generate(new System.Random(7));
        MazeLayout b = MazeLayout.Generate(new System.Random(7));

        for (int x = 0; x < MazeLayout.Size; x++)
            for (int y = 0; y < MazeLayout.Size; y++)
                Assert.AreEqual(a.IsWall(x, y), b.IsWall(x, y), $"tile {x},{y}");
        Assert.AreEqual(a.Entrance, b.Entrance);
    }

    [Test]
    public void Maze_DifferentSeeds_GiveDifferentLayouts()
    {
        MazeLayout first = MazeLayout.Generate(new System.Random(1));
        bool differs = false;
        for (int seed = 2; seed < 20 && !differs; seed++)
        {
            MazeLayout other = MazeLayout.Generate(new System.Random(seed));
            differs = other.Entrance != first.Entrance;
            for (int x = 0; x < MazeLayout.Size && !differs; x++)
                for (int y = 0; y < MazeLayout.Size && !differs; y++)
                    differs = other.IsWall(x, y) != first.IsWall(x, y);
        }
        Assert.IsTrue(differs);
    }

    [Test]
    public void Maze_IsPerfect_Connected_WithOneEntrance()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            MazeLayout maze = MazeLayout.Generate(new System.Random(seed));

            // Идеальный лабиринт 3×3: 9 клеток + 8 проходов + вход
            Assert.AreEqual(18, maze.CountOpen(), $"seed {seed}");
            // Все открытые тайлы достижимы от входа, центр открыт
            Assert.AreEqual(maze.CountOpen(), maze.CountReachableFromEntrance(), $"seed {seed}");
            Assert.IsFalse(maze.IsWall(maze.Center));
            Assert.That(maze.PathLength, Is.GreaterThanOrEqualTo(7), $"seed {seed}");

            // Во внешнем кольце ровно один проход — вход в середине стороны
            int openings = 0;
            for (int i = 0; i < MazeLayout.Size; i++)
            {
                if (!maze.IsWall(i, 0)) openings++;
                if (!maze.IsWall(i, MazeLayout.Size - 1)) openings++;
                if (i > 0 && i < MazeLayout.Size - 1)
                {
                    if (!maze.IsWall(0, i)) openings++;
                    if (!maze.IsWall(MazeLayout.Size - 1, i)) openings++;
                }
            }
            Assert.AreEqual(1, openings, $"seed {seed}");
            Assert.IsTrue(maze.Entrance.x == MazeLayout.Size / 2 || maze.Entrance.y == MazeLayout.Size / 2);

            // Тайл перед входом — снаружи, рядом со входом
            Assert.IsFalse(MazeLayout.InBounds(maze.Approach.x, maze.Approach.y));
            Assert.AreEqual(1, Mathf.Abs(maze.Approach.x - maze.Entrance.x) + Mathf.Abs(maze.Approach.y - maze.Entrance.y));
        }
    }

    // ─── Награды ──────────────────────────────────────────────

    private sealed class RewardSetup
    {
        public PlayerConfig Config;
        public RunModifiers Modifiers;
        public PlayerEnergy Energy;
        public PlayerHealth Health;
        public DayCycle Day;
        public PlayerInventory Inventory;
        public PuzzleConfig PuzzleConfig;
        public PuzzleRewards Rewards;
    }

    private RewardSetup CreateRewards()
    {
        var s = new RewardSetup { Config = ScriptableObject.CreateInstance<PlayerConfig>() };
        s.Modifiers = RunModifiers.Create(s.Config, new MetaProgress());
        s.Energy = new PlayerEnergy(s.Modifiers.BaseEnergyLimit);
        s.Health = new PlayerHealth(s.Modifiers.MaxHealth);
        s.Day = new DayCycle(s.Config, s.Modifiers, s.Energy, null, s.Health);
        s.Inventory = new PlayerInventory(4);
        s.PuzzleConfig = ScriptableObject.CreateInstance<PuzzleConfig>();
        s.Rewards = new PuzzleRewards(s.PuzzleConfig, s.Modifiers, s.Energy, s.Health, s.Day, s.Inventory, null);
        _created.Add(s.Config);
        _created.Add(s.PuzzleConfig);
        return s;
    }

    [Test]
    public void Reward_Sum_RaisesEnergyLimitAndEnergy()
    {
        RewardSetup s = CreateRewards();
        float baseLimit = s.Modifiers.BaseEnergyLimit;
        s.Energy.TrySpend(30f);

        s.Rewards.ApplyCodeTask(CodeTaskKind.Sum);

        Assert.AreEqual(baseLimit + 10f, s.Modifiers.BaseEnergyLimit, 0.001f);
        Assert.AreEqual(baseLimit + 10f, s.Energy.Max, 0.001f);
        Assert.AreEqual(baseLimit - 30f + 10f, s.Energy.Current, 0.001f);

        // На следующий день максимум считается от нового лимита
        s.Day.TryEndDay();
        Assert.AreEqual(baseLimit + 10f, s.Energy.Max, 0.001f);
    }

    [Test]
    public void Reward_Max_RaisesMaxHealthAndHeals()
    {
        RewardSetup s = CreateRewards();
        float max = s.Health.Max;
        s.Health.TakeDamage(40f);

        s.Rewards.ApplyCodeTask(CodeTaskKind.Max);

        Assert.AreEqual(max + 20f, s.Modifiers.MaxHealth, 0.001f);
        Assert.AreEqual(max + 20f, s.Health.Max, 0.001f);
        Assert.AreEqual(s.Health.Max, s.Health.Current, 0.001f);
    }

    [Test]
    public void Reward_Count_DelaysThreat_OrGivesEnergyAfterIt()
    {
        RewardSetup s = CreateRewards();
        int threatDay = s.Day.ThreatDay;

        s.Rewards.ApplyCodeTask(CodeTaskKind.Count);
        Assert.AreEqual(threatDay + 1, s.Day.ThreatDay);

        // Угроза наступила — вместо отсрочки энергия, как у sum
        while (!s.Day.IsThreatActive) s.Day.TryEndDay();
        float limit = s.Modifiers.BaseEnergyLimit;
        int delayedDay = s.Day.ThreatDay;

        s.Rewards.ApplyCodeTask(CodeTaskKind.Count);
        Assert.AreEqual(delayedDay, s.Day.ThreatDay);
        Assert.AreEqual(limit + 10f, s.Modifiers.BaseEnergyLimit, 0.001f);
    }

    [Test]
    public void Reward_Maze_PutsCrystalsIntoInventory()
    {
        RewardSetup s = CreateRewards();
        MaterialResource crystal = TestHelpers.Material("crystal");
        _created.Add(crystal);
        s.PuzzleConfig.crystalItem = crystal;

        s.Rewards.ApplyMaze(Vector2.zero);

        Assert.AreEqual(2, s.Inventory.GetCount(crystal));
    }

    // ─── PuzzleManager ────────────────────────────────────────

    [Test]
    public void Manager_SolveCodeTask_ChecksReachAnswerAndSolvesOnce()
    {
        RewardSetup s = CreateRewards();
        Vector2 player = new Vector2(10f, 0f);
        var manager = new PuzzleManager(() => player, 1.5f, s.Rewards);

        var go = new GameObject("Obelisk");
        _created.Add(go);
        CodeTaskPuzzle obelisk = go.AddComponent<CodeTaskPuzzle>();
        obelisk.Setup(1, Vector2.zero);
        obelisk.Initialize(CodeTaskKind.Sum, 5, Rect.zero);
        manager.Register(obelisk);

        var solved = new List<GameEvent>();
        void OnEvent(GameEvent e) { if (e.Type == GameEventType.PuzzleSolved) solved.Add(e); }
        GameEventBus.OnGameEvent += OnEvent;
        try
        {
            int answer = obelisk.GetTask().Answer;
            Assert.AreEqual(PuzzleSolveResult.TooFar, manager.TrySolveCodeTask(obelisk, answer));

            player = new Vector2(1f, 0.5f);
            Assert.AreEqual(PuzzleSolveResult.Wrong, manager.TrySolveCodeTask(obelisk, answer + 1));
            Assert.IsFalse(obelisk.IsSolved);
            // Одна попытка за запуск: даже верный ответ в том же запуске не принимается
            Assert.AreEqual(PuzzleSolveResult.NoAttemptsLeft, manager.TrySolveCodeTask(obelisk, answer));

            // Новый запуск — новый вход и новая попытка
            GameEventBus.PublishRunStarted();
            answer = obelisk.GetTask().Answer;
            Assert.AreEqual(PuzzleSolveResult.Correct, manager.TrySolveCodeTask(obelisk, answer));
            Assert.IsTrue(obelisk.IsSolved);
            Assert.AreEqual(PuzzleSolveResult.AlreadySolved, manager.TrySolveCodeTask(obelisk, answer));

            Assert.AreEqual(1, solved.Count);
            Assert.AreEqual(PuzzleTypes.CodeTask, solved[0].Id);
            Assert.AreEqual("sum", solved[0].Tag);

            Assert.AreSame(obelisk, manager.GetById(1));
            Assert.AreSame(obelisk, manager.GetPuzzleAt(Vector2Int.zero));
            Assert.AreEqual(1, manager.GetPuzzlesInRadius(player, 2f, PuzzleTypes.CodeTask).Count);
            Assert.AreEqual(0, manager.GetPuzzlesInRadius(player, 2f, PuzzleTypes.Maze).Count);
        }
        finally
        {
            GameEventBus.OnGameEvent -= OnEvent;
            manager.Dispose();
        }
    }

    // ─── Плиты (T16) ──────────────────────────────────────────

    [Test]
    public void PlatesLayout_SameSeed_SameLayout()
    {
        PlatesLayout a = PlatesLayout.Generate(new System.Random(11));
        PlatesLayout b = PlatesLayout.Generate(new System.Random(11));

        Assert.AreEqual(a.Count, b.Count);
        Assert.IsTrue(Equal(a.Numbers, b.Numbers));
        for (int i = 0; i < a.Count; i++)
            Assert.AreEqual(a.Tiles[i], b.Tiles[i]);
    }

    [Test]
    public void PlatesLayout_UniqueNumbers_ValidTiles_Reachable()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            PlatesLayout layout = PlatesLayout.Generate(new System.Random(seed));

            Assert.That(layout.Count, Is.InRange(PlatesLayout.MinPlates, PlatesLayout.MaxPlates), $"seed {seed}");
            CollectionAssert.AllItemsAreUnique(layout.Numbers, $"seed {seed}");
            foreach (int n in layout.Numbers)
                Assert.That(n, Is.InRange(1, PlatesLayout.MaxNumber));

            for (int i = 0; i < layout.Count; i++)
            {
                Assert.IsTrue(PlatesLayout.InBounds(layout.Tiles[i]));
                // Плиты не касаются друг друга даже углами
                for (int j = i + 1; j < layout.Count; j++)
                {
                    Vector2Int d = layout.Tiles[i] - layout.Tiles[j];
                    Assert.IsTrue(Mathf.Abs(d.x) > 1 || Mathf.Abs(d.y) > 1, $"seed {seed}: plates {i} and {j} touch");
                    // По строке плиты можно пройти от края, не задев другие
                    Assert.IsTrue(d.x != 0 && d.y != 0, $"seed {seed}: plates {i} and {j} share a row or column");
                }
            }
            Assert.IsTrue(PlatesLayout.AllReachable(layout.Tiles), $"seed {seed}");
        }
    }

    [Test]
    public void PlatesLayout_DifferentSeeds_GiveDifferentLayouts()
    {
        PlatesLayout first = PlatesLayout.Generate(new System.Random(1));
        bool anyDifferent = false;
        for (int seed = 2; seed < 20 && !anyDifferent; seed++)
            anyDifferent = !Equal(first.Numbers, PlatesLayout.Generate(new System.Random(seed)).Numbers);
        Assert.IsTrue(anyDifferent);
    }

    [Test]
    public void PlatesSequence_AscendingOrder_WrongPlateResets()
    {
        // Индексы: 0 → 17, 1 → 3, 2 → 25, 3 → 9; порядок нажатия: 1, 3, 0, 2
        var sequence = new PlatesSequence(new[] { 17, 3, 25, 9 });
        Assert.AreEqual(3, sequence.NextNumber);

        Assert.AreEqual(PlateStepResult.Pressed, sequence.Step(1));
        Assert.AreEqual(PlateStepResult.AlreadyPressed, sequence.Step(1), "walking over a pressed plate is fine");
        Assert.AreEqual(PlateStepResult.Pressed, sequence.Step(3));
        Assert.AreEqual(2, sequence.PressedCount);

        // 25 раньше 17 — сброс всех
        Assert.AreEqual(PlateStepResult.Reset, sequence.Step(2));
        Assert.AreEqual(0, sequence.PressedCount);
        Assert.IsFalse(sequence.IsPressed(1));
        Assert.AreEqual(3, sequence.NextNumber);

        Assert.AreEqual(PlateStepResult.NotAPlate, sequence.Step(-1));
        Assert.AreEqual(PlateStepResult.Pressed, sequence.Step(1));
        Assert.AreEqual(PlateStepResult.Pressed, sequence.Step(3));
        Assert.AreEqual(PlateStepResult.Pressed, sequence.Step(0));
        Assert.AreEqual(PlateStepResult.Completed, sequence.Step(2));
        Assert.IsTrue(sequence.IsCompleted);
        Assert.AreEqual(-1, sequence.NextNumber);
        Assert.AreEqual(PlateStepResult.AlreadySolved, sequence.Step(0));
    }

    [Test]
    public void PlatesSequence_DuplicateNumbers_Rejected()
    {
        Assert.Throws<System.ArgumentException>(() => new PlatesSequence(new[] { 1, 5, 1 }));
    }

    [Test]
    public void Manager_Plates_WrongStepResets_OrderSolvesOnce_GivesCrystal()
    {
        RewardSetup s = CreateRewards();
        MaterialResource crystal = TestHelpers.Material("crystal");
        _created.Add(crystal);
        s.PuzzleConfig.crystalItem = crystal;
        var manager = new PuzzleManager(() => Vector2.zero, 1.5f, s.Rewards);

        PlatesPuzzle plates = CreatePlates(manager, new Vector2Int(10, 10), seed: 3);
        List<Vector2Int> order = PlateTilesInOrder(plates);

        var solved = new List<GameEvent>();
        void OnEvent(GameEvent e) { if (e.Type == GameEventType.PuzzleSolved) solved.Add(e); }
        GameEventBus.OnGameEvent += OnEvent;
        try
        {
            // Не на плиту — ничего; вторая по порядку первой — сброс
            Assert.IsFalse(manager.StepOnPlates(plates, plates.Origin + new Vector2Int(-3, -3)));
            Assert.IsTrue(manager.StepOnPlates(plates, order[0]));
            Assert.AreEqual(1, plates.PressedCount);
            Assert.IsTrue(manager.StepOnPlates(plates, order[2]));
            Assert.AreEqual(0, plates.PressedCount, "wrong plate releases all");

            // Шаги через шину, как при движении игрока
            foreach (Vector2Int tile in order)
                PlayerEventBus.PublishPlayerMoved(tile);

            Assert.IsTrue(plates.IsSolved);
            Assert.AreEqual(1, solved.Count);
            Assert.AreEqual(PuzzleTypes.Plates, solved[0].Id);
            Assert.AreEqual(1, s.Inventory.GetCount(crystal), "reward: crystal ×1 by default");

            // Решена один раз за забег
            foreach (Vector2Int tile in order)
                PlayerEventBus.PublishPlayerMoved(tile);
            Assert.AreEqual(1, solved.Count);
            Assert.AreEqual(1, s.Inventory.GetCount(crystal));
            Assert.IsTrue(plates.TryGetPlate(order[0], out _, out bool pressed) && pressed);
        }
        finally
        {
            GameEventBus.OnGameEvent -= OnEvent;
            manager.Dispose();
        }
    }

    [Test]
    public void Generator_Plates_DeterministicBySeed_OnlyWhenUnlocked()
    {
        PlatesPuzzle a = GeneratePlates(seed: 42, unlocked: true);
        PlatesPuzzle b = GeneratePlates(seed: 42, unlocked: true);

        Assert.IsNotNull(a);
        Assert.AreEqual(a.Origin, b.Origin);
        Assert.IsTrue(Equal(a.Layout.Numbers, b.Layout.Numbers));
        Assert.AreEqual(PuzzleTypes.Plates, a.TypeId);
        Assert.GreaterOrEqual(Vector2.Distance(a.Position, Vector2.zero), 15f, "far from spawn");
        // Без префаба — плиты-квадраты из кода
        Assert.AreEqual(a.PlateCount, a.GetComponentsInChildren<SpriteRenderer>().Length);

        Assert.IsNull(GeneratePlates(seed: 42, unlocked: false));
    }

    // ─── Helpers ──────────────────────────────────────────────

    private PlatesPuzzle CreatePlates(PuzzleManager manager, Vector2Int origin, int seed)
    {
        var go = new GameObject("Plates");
        _created.Add(go);
        PlatesPuzzle plates = go.AddComponent<PlatesPuzzle>();
        plates.Setup(7, origin + PlatesLayout.Center);
        plates.Build(PlatesLayout.Generate(new System.Random(seed)), origin, GrassMap.Instance, null);
        manager.Register(plates);
        return plates;
    }

    /// <summary>Тайлы плит на карте по возрастанию номеров.</summary>
    private static List<Vector2Int> PlateTilesInOrder(PlatesPuzzle plates)
    {
        var indices = new List<int>();
        for (int i = 0; i < plates.Layout.Count; i++) indices.Add(i);
        indices.Sort((x, y) => plates.Layout.Numbers[x].CompareTo(plates.Layout.Numbers[y]));

        var tiles = new List<Vector2Int>();
        foreach (int i in indices) tiles.Add(plates.Origin + plates.Layout.Tiles[i]);
        return tiles;
    }

    private PlatesPuzzle GeneratePlates(int seed, bool unlocked)
    {
        var config = ScriptableObject.CreateInstance<PuzzleConfig>();
        config.mazeCount = 0;
        config.codeTaskCount = 0;
        config.platesCount = 1;
        _created.Add(config);

        var manager = new PuzzleManager(() => Vector2.zero, 1.5f, null);
        try
        {
            PuzzleGenerator.Generate(config, new GrassMap(seed), null, null, null, Vector2Int.zero,
                id => unlocked && id == PuzzleTypes.Plates, manager, 0);

            // Контейнер "Puzzles" создаётся всегда — убираем после теста
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "Puzzles" && !_created.Contains(root))
                    _created.Add(root);
            return manager.Puzzles.Count > 0 ? manager.Puzzles[0] as PlatesPuzzle : null;
        }
        finally
        {
            manager.Dispose();
        }
    }

    /// <summary>Карта 64×64 из травы; тайл (x, y) — мировая точка (x, y).</summary>
    private sealed class GrassMap : IMapDataProvider
    {
        public static readonly GrassMap Instance = new GrassMap(1);

        private readonly int _seed;
        public GrassMap(int seed) { _seed = seed; }

        public int MapWidth => 64;
        public int MapHeight => 64;
        public int Seed => _seed;
        public bool IsWalkable(int x, int y) => IsInBounds(x, y);
        public bool IsInBounds(int x, int y) => x >= 0 && y >= 0 && x < MapWidth && y < MapHeight;
        public TileType GetTileType(int x, int y) => TileType.Grass;
        public Biome GetBiome(int x, int y) => null;
        public Vector3 TileIndexToWorld(int x, int y) => new Vector3(x, y, 0f);
        public Vector2Int WorldToTileIndex(Vector3 worldPos) => MapPosition.ToTile(worldPos);
        public Vector3 MapToWorld(Vector2 mapPos) => new Vector3(mapPos.x, mapPos.y, 0f);
    }

    private static bool Equal(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
