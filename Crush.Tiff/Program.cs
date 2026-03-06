using CommandLine;
using Crush.Core;
using FileFormat.Tiff;
using Optimizer.Tiff;

namespace Crush.Tiff;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
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

    return await CrushRunner.RunAsync(
      "TiffCrush - TIFF Optimizer",
      opts.InputFile,
      opts.OutputFile,
      opts.Verbose,
      async (ct, progress) => {
        var optimizer = TiffOptimizer.FromFile(new FileInfo(opts.InputFile), options);
        return await optimizer.OptimizeAsync(ct, progress);
      },
      r => r.FileContents,
      r => r.IsTiled
        ? $"Compression: {r.Compression}, Predictor: {r.Predictor}\nColor: {r.ColorMode}, Tile: {r.TileWidth}x{r.TileHeight}"
        : $"Compression: {r.Compression}, Predictor: {r.Predictor}\nColor: {r.ColorMode}, StripRows: {r.StripRowCount}",
      opts.Verbose ? () => {
        Console.WriteLine($"Compressions: {string.Join(", ", compressions)}");
        Console.WriteLine($"Predictors: {string.Join(", ", predictors)}");
        Console.WriteLine($"Auto color mode: {opts.AutoColorMode}");
        Console.WriteLine($"Dynamic strip sizing: {opts.DynamicStripSizing}");
        Console.WriteLine($"Try tiles: {opts.TryTiles}");
        if (opts.TryTiles)
          Console.WriteLine($"Tile sizes: {string.Join(", ", tileSizes)}");
        Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}");
      } : null
    );
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
}
