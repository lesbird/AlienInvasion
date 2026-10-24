using System.Diagnostics;

namespace AlienInvasion.Engine;

/// <summary>Frame profiler for hunting hitches (--perf). Times named sections of each frame and prints
/// any frame that runs long, with its section breakdown and whether a garbage collection happened.</summary>
public static class Perf
{
    public static bool On;
    public static float SpikeMs = 25;
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static readonly Dictionary<string, double> sections = new();
    static readonly Dictionary<string, double> totals = new();
    static readonly List<double> frameMs = new();
    static double frameStart = -1;
    static int gc0, gc1, gc2, spikes;
    public static string Context = "";
    /// <summary>Set by --perfsync: waits for the GPU at each section end so GPU time lands in the section that caused it.</summary>
    public static Action Sync;

    static double Now => clock.Elapsed.TotalMilliseconds;

    public static double Begin() => On ? Now : 0;

    public static void End(string name, double start)
    {
        if (!On) return;
        Sync?.Invoke();
        double ms = Now - start;
        sections[name] = sections.GetValueOrDefault(name) + ms;
        totals[name] = totals.GetValueOrDefault(name) + ms;
    }

    /// <summary>Call once at the top of each frame: closes the previous frame and reports it if it was slow.</summary>
    public static void Frame()
    {
        if (!On) return;
        double now = Now;
        if (frameStart >= 0)
        {
            double ms = now - frameStart;
            frameMs.Add(ms);
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            if (ms > SpikeMs && frameMs.Count > 30)
            {
                spikes++;
                double known = sections.Values.Sum();
                var parts = string.Join(" ", sections.OrderByDescending(s => s.Value).Where(s => s.Value >= 0.5).Select(s => $"{s.Key}={s.Value:F1}"));
                string gc = g2 > gc2 ? " GC2" : g1 > gc1 ? " GC1" : g0 > gc0 ? " GC0" : "";
                Console.WriteLine($"PERF spike {ms,6:F1}ms [{Context}] {parts} other={ms - known:F1}{gc}");
            }
            gc0 = g0; gc1 = g1; gc2 = g2;
        }
        sections.Clear();
        frameStart = now;
    }

    public static void Report()
    {
        if (!On || frameMs.Count == 0) return;
        var s = frameMs.Skip(30).OrderBy(x => x).ToList();
        if (s.Count == 0) return;
        double P(double q) => s[Math.Min(s.Count - 1, (int)(q * s.Count))];
        Console.WriteLine($"PERF frames={s.Count} avg={s.Average():F2}ms p50={P(0.5):F2} p95={P(0.95):F2} p99={P(0.99):F2} max={s[^1]:F1} spikes={spikes} " +
                          $"gc0={GC.CollectionCount(0)} gc1={GC.CollectionCount(1)} gc2={GC.CollectionCount(2)} alloc={GC.GetTotalAllocatedBytes() / 1048576}MB");
        Console.WriteLine("PERF per-frame avg: " + string.Join(" ", totals.OrderByDescending(t => t.Value).Select(t => $"{t.Key}={t.Value / frameMs.Count:F2}")));
    }
}
