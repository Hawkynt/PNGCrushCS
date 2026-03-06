using FileFormat.Bmp;
using Optimizer.Bmp;
using CommandLine;
using Crush.Core;

namespace Crush.Bmp;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    var compressions = ParseCompressions(opts.Compressions);

    var options = new BmpOptimizationOptions(
      Compressions: compressions,
      AutoSelectColorMode: opts.AutoColorMode,
      MaxParallelTasks: opts.ParallelTasks
    );

    return await CrushRunner.RunAsync(
      "BmpCrush - BMP Optimizer",
      opts.InputFile,
      opts.OutputFile,
      opts.Verbose,
      async (ct, progress) => {
        var optimizer = BmpOptimizer.FromFile(new FileInfo(opts.InputFile), options);
        return await optimizer.OptimizeAsync(ct, progress);
      },
      r => r.FileContents,
      r => $"Color: {r.ColorMode}, Compression: {r.Compression}, RowOrder: {r.RowOrder}",
      opts.Verbose ? () => {
        Console.WriteLine($"Compressions: {string.Join(", ", compressions)}");
        Console.WriteLine($"Auto color mode: {opts.AutoColorMode}");
        Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}");
      } : null
    );
  }

  private static List<BmpCompression> ParseCompressions(string input) {
    var result = new List<BmpCompression>();
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      if (Enum.TryParse<BmpCompression>(part, true, out var value))
        result.Add(value);

    return result.Count > 0 ? result : [BmpCompression.None, BmpCompression.Rle8, BmpCompression.Rle4];
  }
}
