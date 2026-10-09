using System.Globalization;

namespace CodexDiscordPresence;

public enum SubagentWorkKind
{
    Unknown = 0,
    Thinking = 1,
    Editing = 2,
    Reading = 3,
    Researching = 4,
    RunningCommand = 5,
    Coordinating = 6
}

public sealed record SubagentActivitySummary
{
    internal const int MaxObservedWorkKindCount = 64;

    private SubagentActivitySummary(
        int activeCount,
        int thinkingCount,
        int editingCount,
        int readingCount,
        int researchingCount,
        int runningCommandCount,
        int coordinatingCount)
    {
        ActiveCount = activeCount;
        ThinkingCount = thinkingCount;
        EditingCount = editingCount;
        ReadingCount = readingCount;
        ResearchingCount = researchingCount;
        RunningCommandCount = runningCommandCount;
        CoordinatingCount = coordinatingCount;
    }

    public int ActiveCount { get; }
    public int ThinkingCount { get; }
    public int EditingCount { get; }
    public int ReadingCount { get; }
    public int ResearchingCount { get; }
    public int RunningCommandCount { get; }
    public int CoordinatingCount { get; }
    public int KnownCount =>
        ThinkingCount + EditingCount + ReadingCount + ResearchingCount + RunningCommandCount + CoordinatingCount;
    public int UnknownCount => Math.Max(0, ActiveCount - KnownCount);

    internal static SubagentActivitySummary? Create(
        int activeCount,
        IEnumerable<SubagentWorkKind>? observedWorkKinds)
    {
        if (activeCount <= 0)
        {
            return null;
        }

        var counts = new int[Enum.GetValues<SubagentWorkKind>().Length];
        foreach (var workKind in (observedWorkKinds ?? []).Take(MaxObservedWorkKindCount))
        {
            if (workKind is > SubagentWorkKind.Unknown and <= SubagentWorkKind.Coordinating)
            {
                counts[(int)workKind]++;
            }
        }

        if (counts.Sum() > activeCount)
        {
            Array.Clear(counts);
        }

        return new SubagentActivitySummary(
            activeCount,
            counts[(int)SubagentWorkKind.Thinking],
            counts[(int)SubagentWorkKind.Editing],
            counts[(int)SubagentWorkKind.Reading],
            counts[(int)SubagentWorkKind.Researching],
            counts[(int)SubagentWorkKind.RunningCommand],
            counts[(int)SubagentWorkKind.Coordinating]);
    }

    internal bool TryGetHomogeneousWorkKind(out SubagentWorkKind workKind)
    {
        if (KnownCount == ActiveCount)
        {
            foreach (var candidate in Enum.GetValues<SubagentWorkKind>().Skip(1))
            {
                if (GetCount(candidate) == ActiveCount)
                {
                    workKind = candidate;
                    return true;
                }
            }
        }

        workKind = SubagentWorkKind.Unknown;
        return false;
    }

    internal string FormatSmallImageText()
    {
        var activeCount = ActiveCount.ToString(CultureInfo.InvariantCulture);
        var noun = ActiveCount == 1 ? "subagent" : "subagents";
        if (KnownCount == 0) return $"{activeCount} {noun} active";
        if (TryGetHomogeneousWorkKind(out var workKind))
        {
            return $"{activeCount} {noun} · {GetLabel(workKind)}";
        }

        var details = Enum.GetValues<SubagentWorkKind>()
            .Where(kind => kind != SubagentWorkKind.Unknown)
            .Select(kind => (Kind: kind, Count: GetCount(kind)))
            .Where(item => item.Count > 0)
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Kind)
            .Select(item => $"{item.Count.ToString(CultureInfo.InvariantCulture)} {GetLabel(item.Kind)}")
            .ToList();
        if (UnknownCount > 0)
        {
            details.Add($"{UnknownCount.ToString(CultureInfo.InvariantCulture)} unspecified");
        }

        return details.Count == 0
            ? $"{activeCount} {noun} active"
            : $"{activeCount} {noun} active · {string.Join(", ", details)}";
    }

    private int GetCount(SubagentWorkKind workKind) => workKind switch
    {
        SubagentWorkKind.Thinking => ThinkingCount,
        SubagentWorkKind.Editing => EditingCount,
        SubagentWorkKind.Reading => ReadingCount,
        SubagentWorkKind.Researching => ResearchingCount,
        SubagentWorkKind.RunningCommand => RunningCommandCount,
        SubagentWorkKind.Coordinating => CoordinatingCount,
        _ => 0
    };

    private static string GetLabel(SubagentWorkKind workKind) => workKind switch
    {
        SubagentWorkKind.Thinking => "thinking",
        SubagentWorkKind.Editing => "editing",
        SubagentWorkKind.Reading => "reading",
        SubagentWorkKind.Researching => "researching",
        SubagentWorkKind.RunningCommand => "commands",
        SubagentWorkKind.Coordinating => "coordinating",
        _ => "active"
    };
}
