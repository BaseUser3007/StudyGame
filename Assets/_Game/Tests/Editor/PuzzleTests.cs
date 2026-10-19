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

    // ─── Helpers ──────────────────────────────────────────────

    private static bool Equal(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }
}
