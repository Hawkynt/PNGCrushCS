using CommandLine;
using Crush.Core;
using FileFormat.Tga;
using Optimizer.Tga;

namespace Crush.Tga;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    var compressions = ParseCompressions(opts.Compressions);

    var options = new TgaOptimizationOptions(
      Compressions: compressions,
      AutoSelectColorMode: opts.AutoColorMode,
      MaxParallelTasks: opts.ParallelTasks
    );

    return await CrushRunner.RunAsync(
      "TgaCrush - TGA Optimizer",
      opts.InputFile,
      opts.OutputFile,
      opts.Verbose,
      async (ct, progress) => {
        var optimizer = TgaOptimizer.FromFile(new FileInfo(opts.InputFile), options);
        return await optimizer.OptimizeAsync(ct, progress);
      },
      r => r.FileContents,
      r => $"Color: {r.ColorMode}, Compression: {r.Compression}, Origin: {r.Origin}",
      opts.Verbose ? () => {
        Console.WriteLine($"Compressions: {string.Join(", ", compressions)}");
        Console.WriteLine($"Auto color mode: {opts.AutoColorMode}");
        Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}");
      } : null
    );
  }

  private static List<TgaCompression> ParseCompressions(string input) {
    var result = new List<TgaCompression>();
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      if (Enum.TryParse<TgaCompression>(part, true, out var value))
        result.Add(value);

    return result.Count > 0 ? result : [TgaCompression.None, TgaCompression.Rle];
  }
}
