using System.Diagnostics;
using CommandLine;

namespace GifOptimizer;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    Console.WriteLine("GifCrush - GIF Optimizer");
    Console.WriteLine(new string('=', 40));

    var inputFile = new FileInfo(opts.InputFile);
    if (!inputFile.Exists) {
      Console.Error.WriteLine($"Input file not found: {inputFile.FullName}");
      return 1;
    }

    var outputDir = Path.GetDirectoryName(Path.GetFullPath(opts.OutputFile));
    if (outputDir != null && !Directory.Exists(outputDir)) {
      Console.Error.WriteLine($"Output directory not found: {outputDir}");
      return 1;
    }

    var originalSize = inputFile.Length;
    Console.WriteLine($"Input:  {inputFile.FullName} ({FormatFileSize(originalSize)})");

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

    if (opts.Verbose) {
      Console.WriteLine($"Strategies: {string.Join(", ", strategies)}");
      Console.WriteLine($"Optimize disposal: {opts.OptimizeDisposal}");
      Console.WriteLine($"Trim margins: {opts.TrimMargins}");
      Console.WriteLine($"Deferred clear: {opts.DeferredClear}");
      Console.WriteLine($"Frame differencing: {opts.FrameDiff}");
      Console.WriteLine($"Deduplicate frames: {opts.Deduplicate}");
      Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}");
    }

    Console.WriteLine("Optimizing...");
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => {
      e.Cancel = true;
      cts.Cancel();
    };
    var sw = Stopwatch.StartNew();

    var optimizer = GifOptimizer.FromFile(inputFile, options);
    var progressReporter = new Progress<GifOptimizationProgress>(p =>
      Console.Write(
        $"\r[{p.CombosCompleted}/{p.CombosTotal}] Best: {FormatFileSize(p.BestSizeSoFar)} | Phase: {p.Phase}    ")
    );
    var result = await optimizer.OptimizeAsync(cts.Token, progressReporter);
    Console.WriteLine();

    sw.Stop();

    var outputFile = new FileInfo(opts.OutputFile);
    File.WriteAllBytes(outputFile.FullName, result.FileContents);

    var newSize = result.CompressedSize;
    var reduction = originalSize > 0 ? (1.0 - (double)newSize / originalSize) * 100 : 0;

    Console.WriteLine($"Output: {outputFile.FullName} ({FormatFileSize(newSize)})");
    Console.WriteLine($"Reduction: {reduction:F1}% ({FormatFileSize(originalSize - newSize)} saved)");
    Console.WriteLine($"Strategy: {result.PaletteStrategy}, GCT: {result.UsedGlobalColorTable}");
    Console.WriteLine($"Frames: {result.FrameCount}");
    Console.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");

    return 0;
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

  private static string FormatFileSize(long bytes) {
    return bytes switch {
      < 1024 => $"{bytes} B",
      < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
      _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
  }
}
