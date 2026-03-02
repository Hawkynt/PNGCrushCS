using System.Diagnostics;
using CommandLine;

namespace TiffOptimizer;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    Console.WriteLine("TiffCrush - TIFF Optimizer");
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

    var compressions = ParseCompressions(opts.Compressions);
    var predictors = ParsePredictors(opts.Predictors);

    var tileSizes = ParseTileSizes(opts.TileSizes);

    var options = new TiffOptimizationOptions(
      compressions,
      predictors,
      AutoSelectColorMode: opts.AutoColorMode,
      DynamicStripSizing: opts.DynamicStripSizing,
      TryTiles: opts.TryTiles,
      TileSizes: tileSizes,
      MaxParallelTasks: opts.ParallelTasks
    );

    if (opts.Verbose) {
      Console.WriteLine($"Compressions: {string.Join(", ", compressions)}");
      Console.WriteLine($"Predictors: {string.Join(", ", predictors)}");
      Console.WriteLine($"Auto color mode: {opts.AutoColorMode}");
      Console.WriteLine($"Dynamic strip sizing: {opts.DynamicStripSizing}");
      Console.WriteLine($"Try tiles: {opts.TryTiles}");
      if (opts.TryTiles)
        Console.WriteLine($"Tile sizes: {string.Join(", ", tileSizes)}");
      Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}");
    }

    Console.WriteLine("Optimizing...");
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => {
      e.Cancel = true;
      cts.Cancel();
    };
    var sw = Stopwatch.StartNew();

    var optimizer = TiffOptimizer.FromFile(inputFile, options);
    var progressReporter = new Progress<TiffOptimizationProgress>(p =>
      Console.Write(
        $"\r[{p.CombosCompleted}/{p.CombosTotal}] Best: {FormatFileSize(p.BestSizeSoFar)} | Phase: {p.Phase}    ")
    );
    var result = await optimizer.OptimizeAsync(cts.Token, progressReporter);
    Console.WriteLine();

    sw.Stop();

    var outputFile = new FileInfo(opts.OutputFile);
    await File.WriteAllBytesAsync(outputFile.FullName, result.FileContents, cts.Token);

    var newSize = result.CompressedSize;
    var reduction = originalSize > 0 ? (1.0 - (double)newSize / originalSize) * 100 : 0;

    Console.WriteLine($"Output: {outputFile.FullName} ({FormatFileSize(newSize)})");
    Console.WriteLine($"Reduction: {reduction:F1}% ({FormatFileSize(originalSize - newSize)} saved)");
    Console.WriteLine($"Compression: {result.Compression}, Predictor: {result.Predictor}");
    if (result.IsTiled)
      Console.WriteLine($"Color: {result.ColorMode}, Tile: {result.TileWidth}x{result.TileHeight}");
    else
      Console.WriteLine($"Color: {result.ColorMode}, StripRows: {result.StripRowCount}");

    Console.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");

    return 0;
  }

  private static List<TiffCompression> ParseCompressions(string input) {
    var result = new List<TiffCompression>();
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      if (Enum.TryParse<TiffCompression>(part, true, out var value))
        result.Add(value);

    return result.Count > 0 ? result : [TiffCompression.None, TiffCompression.Deflate];
  }

  private static List<TiffPredictor> ParsePredictors(string input) {
    var result = new List<TiffPredictor>();
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      if (Enum.TryParse<TiffPredictor>(part, true, out var value))
        result.Add(value);

    return result.Count > 0 ? result : [TiffPredictor.None];
  }

  private static List<int> ParseTileSizes(string input) {
    var result = new List<int>();
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      if (int.TryParse(part, out var size) && size >= 16 && size % 16 == 0)
        result.Add(size);

    return result.Count > 0 ? result : [64, 128, 256];
  }

  private static string FormatFileSize(long bytes) {
    return bytes switch {
      < 1024 => $"{bytes} B",
      < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
      _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
  }
}
