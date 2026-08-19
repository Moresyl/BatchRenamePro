using BatchRenamePro.Core.Abstractions;
using BatchRenamePro.Core.Planning;
using BatchRenamePro.Core.Rules;
using BatchRenamePro.Core.Tokens;

namespace BatchRenamePro.Core.Tests;

[TestClass]
public sealed class RenamePlannerTests
{
    private static readonly PlanOptions Deterministic = new(ConflictPolicy.Block, Fixture.Timestamp);

    private static RenamePlan Plan(
        TemporaryDirectory directory,
        IReadOnlyList<string> names,
        IReadOnlyList<IRenameRule> rules,
        ConflictPolicy policy = ConflictPolicy.Block)
    {
        var sources = names
            .Select(name => new RenameSource(Path.Combine(directory.Path, name), false, Fixture.Facts))
            .ToArray();

        return new RenamePlanner().Build(sources, rules, Deterministic with { ConflictPolicy = policy });
    }

    private static MappingRule Mapping(params (string From, string To)[] moves) =>
        new(moves.ToDictionary(move => move.From, move => move.To, StringComparer.Ordinal));

    [TestMethod]
    public void Build_MarksItemsReadyAndProducesTheTargetPath()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");

        var plan = Plan(directory, ["a.txt"], [new PatternRule { Pattern = "b" }]);

        Assert.AreEqual(1, plan.ReadyCount);
        Assert.AreEqual("b.txt", plan.Items[0].ProposedName);
        Assert.AreEqual(Path.Combine(directory.Path, "b.txt"), plan.Items[0].TargetPath);
        Assert.IsTrue(plan.CanExecute);
    }

    [TestMethod]
    public void Build_TreatsAnIdenticalNameAsUnchanged()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");

        var plan = Plan(directory, ["a.txt"], [new PatternRule { Pattern = "{name}" }]);

        Assert.AreEqual(PlanItemStatus.Unchanged, plan.Items[0].Status);
        Assert.IsFalse(plan.CanExecute, "there is nothing to do");
    }

    [TestMethod]
    public void Build_TreatsACaseOnlyChangeAsARealRename()
    {
        // Windows paths are case-insensitive but case-preserving, so "a.txt" to "A.txt" is a rename
        // the user asked for and must not be swallowed as a no-op.
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");

        var plan = Plan(directory, ["a.txt"], [new CaseRule { Mode = CaseMode.Upper }]);

        Assert.AreEqual(PlanItemStatus.Ready, plan.Items[0].Status);
        Assert.AreEqual("A.txt", plan.Items[0].ProposedName);
    }

    [TestMethod]
    public void Build_FlagsAnItemThatIsNoLongerOnDisk()
    {
        using var directory = new TemporaryDirectory();

        var plan = Plan(directory, ["ghost.txt"], [new PatternRule { Pattern = "x" }]);

        Assert.AreEqual(PlanItemStatus.Missing, plan.Items[0].Status);
        Assert.IsFalse(plan.CanExecute);
    }

    [TestMethod]
    public void Build_StopsTheRunWhenARuleIsInvalid()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");

        var plan = Plan(directory, ["a.txt"], [new PatternRule { Pattern = "" }]);

        Assert.IsTrue(plan.HasBlockingDiagnostic);
        Assert.IsFalse(plan.CanExecute);
        Assert.AreEqual(PlanItemStatus.Invalid, plan.Items[0].Status);
    }

    [TestMethod]
    public void Build_FlagsANameWindowsWouldReject()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");

        var plan = Plan(directory, ["a.txt"], [new PatternRule { Pattern = "NUL", Scope = RenameScope.FullName }]);

        Assert.AreEqual(PlanItemStatus.Invalid, plan.Items[0].Status);
        Assert.AreEqual("name.reserved", plan.Items[0].StatusCode);
    }

    [TestMethod]
    public void Build_SkipsDisabledRules()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");

        var plan = Plan(directory, ["a.txt"], [new PatternRule { Pattern = "b", IsEnabled = false }]);

        Assert.AreEqual(PlanItemStatus.Unchanged, plan.Items[0].Status);
    }

    [TestMethod]
    public void Build_RunsRulesInOrderSoLaterRulesSeeEarlierOutput()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");

        var plan = Plan(directory, ["a.txt"],
        [
            new PatternRule { Pattern = "report" },
            new CaseRule { Mode = CaseMode.Upper },
            new InsertRule { Text = "_v2", Position = InsertPosition.Suffix }
        ]);

        Assert.AreEqual("REPORT_v2.txt", plan.Items[0].ProposedName);
    }

    [TestMethod]
    public void Build_BlocksWhenTwoItemsWouldShareAName()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");

        var plan = Plan(directory, ["a.txt", "b.txt"], [new PatternRule { Pattern = "same" }]);

        Assert.AreEqual(2, plan.ConflictCount);
        Assert.IsFalse(plan.CanExecute);
        Assert.AreEqual("item.duplicateTarget", plan.Items[0].StatusCode);
    }

    [TestMethod]
    public void Build_BlocksWhenTheNameIsHeldBySomethingOutsideTheBatch()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("taken.txt");

        var plan = Plan(directory, ["a.txt"], [new PatternRule { Pattern = "taken" }]);

        Assert.AreEqual(PlanItemStatus.Conflict, plan.Items[0].Status);
        Assert.AreEqual("item.existsOnDisk", plan.Items[0].StatusCode);
    }

    [TestMethod]
    public void Build_AllowsATargetHeldByAnItemThatIsItselfMovingAway()
    {
        // a -> b while b -> c. Nothing collides, because b vacates the name a is moving into.
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");

        var plan = Plan(directory, ["a.txt", "b.txt"], [Mapping(("a", "b"), ("b", "c"))]);

        Assert.AreEqual(2, plan.ReadyCount);
        Assert.AreEqual(0, plan.ConflictCount);
        Assert.IsTrue(plan.CanExecute);
    }

    [TestMethod]
    public void Build_AllowsAStraightSwap()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");

        var plan = Plan(directory, ["a.txt", "b.txt"], [Mapping(("a", "b"), ("b", "a"))]);

        Assert.AreEqual(2, plan.ReadyCount);
        Assert.IsTrue(plan.CanExecute);
    }

    [TestMethod]
    public void Build_PropagatesABlockedItemBackAlongTheChain()
    {
        // a -> b, b -> c, and c already exists outside the batch. b cannot move, so b never vacates
        // its name, so a cannot move either. A single detection pass would have left a marked Ready
        // and the failure would only have surfaced halfway through the batch.
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");
        directory.CreateFile("c.txt");

        var plan = Plan(directory, ["a.txt", "b.txt"], [Mapping(("a", "b"), ("b", "c"))]);

        Assert.AreEqual(0, plan.ReadyCount);
        Assert.AreEqual(2, plan.ConflictCount);
        Assert.IsFalse(plan.CanExecute);
    }

    [TestMethod]
    public void Build_DoesNotBlockAnItemMovingIntoANameFreedByAnUnrelatedRename()
    {
        // Only b is blocked here; a is moving somewhere nobody wants, so it stays Ready.
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");
        directory.CreateFile("c.txt");

        var plan = Plan(directory, ["a.txt", "b.txt"], [Mapping(("a", "z"), ("b", "c"))]);

        Assert.AreEqual(PlanItemStatus.Ready, plan.Items[0].Status);
        Assert.AreEqual(PlanItemStatus.Conflict, plan.Items[1].Status);
    }

    [TestMethod]
    public void Build_WithAutoNumber_FindsTheNextFreeName()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");

        var plan = Plan(directory, ["a.txt", "b.txt"], [new PatternRule { Pattern = "same" }], ConflictPolicy.AutoNumber);

        Assert.AreEqual(2, plan.ReadyCount);
        Assert.AreEqual("same.txt", plan.Items[0].ProposedName);
        Assert.AreEqual("same (2).txt", plan.Items[1].ProposedName);
        Assert.AreEqual(1, plan.AutoResolvedCount);
        Assert.IsTrue(plan.CanExecute);
    }

    [TestMethod]
    public void Build_WithAutoNumber_StepsPastNamesAlreadyOnDisk()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("taken.txt");

        var plan = Plan(directory, ["a.txt"], [new PatternRule { Pattern = "taken" }], ConflictPolicy.AutoNumber);

        Assert.AreEqual("taken (2).txt", plan.Items[0].ProposedName);
    }

    [TestMethod]
    public void Build_WithSkip_LeavesTheCollidingItemAloneAndRenamesTheRest()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");
        directory.CreateFile("taken.txt");

        var sources = new[]
        {
            new RenameSource(Path.Combine(directory.Path, "a.txt"), false, Fixture.Facts),
            new RenameSource(Path.Combine(directory.Path, "b.txt"), false, Fixture.Facts)
        };

        var rules = new IRenameRule[]
        {
            new ReplaceRule { Find = "a", ReplaceWith = "taken", Scope = RenameScope.BaseName, IgnoreCase = false }
        };

        var plan = new RenamePlanner().Build(sources, rules, Deterministic with { ConflictPolicy = ConflictPolicy.Skip });

        Assert.AreEqual(PlanItemStatus.Skipped, plan.Items[0].Status);
        Assert.AreEqual(plan.Items[0].SourcePath, plan.Items[0].TargetPath, "a skipped item must not move");
        Assert.AreEqual(PlanItemStatus.Unchanged, plan.Items[1].Status);
    }

    [TestMethod]
    public void Build_NumbersItemsByTheirPositionInTheSelection()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("x.txt");
        directory.CreateFile("y.txt");
        directory.CreateFile("z.txt");

        var rule = new NumberRule
        {
            Position = InsertPosition.Prefix,
            Separator = "-",
            Sequence = new SequenceSettings { Start = 1, Padding = 2 }
        };

        var plan = Plan(directory, ["x.txt", "y.txt", "z.txt"], [rule]);

        Assert.AreEqual("01-x.txt", plan.Items[0].ProposedName);
        Assert.AreEqual("02-y.txt", plan.Items[1].ProposedName);
        Assert.AreEqual("03-z.txt", plan.Items[2].ProposedName);
    }

    [TestMethod]
    public void Build_ReturnsAnEmptyPlanForAnEmptySelection()
    {
        var plan = new RenamePlanner().Build([], [new PatternRule()], Deterministic);

        Assert.IsEmpty(plan.Items);
        Assert.IsFalse(plan.CanExecute);
    }

    [TestMethod]
    public void Build_UsesTheSuppliedTimestampForEveryItem()
    {
        using var directory = new TemporaryDirectory();
        directory.CreateFile("a.txt");
        directory.CreateFile("b.txt");

        var plan = Plan(directory, ["a.txt", "b.txt"], [new PatternRule { Pattern = "{date}-{name}" }]);

        Assert.IsTrue(plan.Items.All(item => item.ProposedName.StartsWith("20260819", StringComparison.Ordinal)));
    }
}
