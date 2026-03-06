using Optimizer.Gif;
using CommandLine;
using Crush.Core;

namespace Crush.Gif;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    var strategies = ParseStrategies(opts.PaletteStrategies);
    if (opts.CompressionPalette && !strategies.Contains(PaletteReorderStrategy.CompressionOptimized))
      strategies.Add(PaletteReorderStrategy.CompressionOptimized);

    var options = new GifOptimizationOptions(
      strategies,
      OptimizeDisposal: opts.OptimizeDisposal,
      TrimMargins: opts.TrimMargins,
      TryDeferredClear: opts.DeferredClear,
      DeduplicateFrames: opts.Deduplicate,
      TryFrameDifferencing: opts.FrameDiff,
      MaxParallelTasks: opts.ParallelTasks
    );

    return await CrushRunner.RunAsync(
      "GifCrush - GIF Optimizer",
      opts.InputFile,
      opts.OutputFile,
      opts.Verbose,
      async (ct, progress) => {
        var optimizer = GifOptimizer.FromFile(new FileInfo(opts.InputFile), options);
        return await optimizer.OptimizeAsync(ct, progress);
      },
      r => r.FileContents,
      r => $"Strategy: {r.PaletteStrategy}, GCT: {r.UsedGlobalColorTable}\nFrames: {r.FrameCount}",
      opts.Verbose ? () => {
        Console.WriteLine($"Strategies: {string.Join(", ", strategies)}");
        Console.WriteLine($"Optimize disposal: {opts.OptimizeDisposal}");
        Console.WriteLine($"Trim margins: {opts.TrimMargins}");
        Console.WriteLine($"Deferred clear: {opts.DeferredClear}");
        Console.WriteLine($"Frame differencing: {opts.FrameDiff}");
        Console.WriteLine($"Deduplicate frames: {opts.Deduplicate}");
        Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}");
      } : null
    );
  }

  private static List<PaletteReorderStrategy> ParseStrategies(string input) {
    var strategies = new List<PaletteReorderStrategy>();
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      if (Enum.TryParse<PaletteReorderStrategy>(part, true, out var strategy))
        strategies.Add(strategy);

    return strategies.Count > 0
      ? strategies
      : [PaletteReorderStrategy.Original, PaletteReorderStrategy.FrequencySorted];
  }
}
