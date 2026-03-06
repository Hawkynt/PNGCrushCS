using CommandLine;
using Crush.Core;
using FileFormat.Jpeg;
using Optimizer.Jpeg;

namespace Crush.Jpeg;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    var qualities = ParseQualities(opts.Qualities);

    var options = new JpegOptimizationOptions(
      AllowLossy: opts.AllowLossy,
      MinQuality: opts.MinQuality,
      Qualities: qualities,
      StripMetadata: opts.StripMetadata,
      MaxParallelTasks: opts.ParallelTasks
    );

    return await CrushRunner.RunAsync(
      "JpegCrush - JPEG Optimizer",
      opts.InputFile,
      opts.OutputFile,
      opts.Verbose,
      async (ct, progress) => {
        var optimizer = JpegOptimizer.FromFile(new FileInfo(opts.InputFile), options);
        return await optimizer.OptimizeAsync(ct, progress);
      },
      r => r.FileContents,
      r => r.IsLossy
        ? $"Mode: {r.Mode}, OptHuffman: {r.OptimizeHuffman}, Strip: {r.StripMetadata}\nQuality: {r.Quality}, Subsampling: {r.Subsampling}"
        : $"Mode: {r.Mode}, OptHuffman: {r.OptimizeHuffman}, Strip: {r.StripMetadata}",
      opts.Verbose ? () => {
        Console.WriteLine($"Allow lossy: {opts.AllowLossy}");
        if (opts.AllowLossy) {
          Console.WriteLine($"Min quality: {opts.MinQuality}");
          Console.WriteLine($"Qualities: {string.Join(", ", qualities)}");
        }
        Console.WriteLine($"Strip metadata: {opts.StripMetadata}");
        Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}");
      } : null
    );
  }

  private static List<int> ParseQualities(string input) {
    var result = new List<int>();
    foreach (var part in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
      if (int.TryParse(part, out var q) && q >= 1 && q <= 100)
        result.Add(q);

    return result.Count > 0 ? result : [75, 80, 85, 90, 95];
  }
}
