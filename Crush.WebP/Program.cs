using CommandLine;
using Crush.Core;

namespace Optimizer.WebP;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    var options = new WebPOptimizationOptions(
      MaxParallelTasks: opts.ParallelTasks,
      StripMetadata: opts.StripMetadata
    );

    return await CrushRunner.RunAsync(
      "WebPCrush - WebP Optimizer",
      opts.InputFile,
      opts.OutputFile,
      opts.Verbose,
      async (ct, progress) => {
        var optimizer = WebPOptimizer.FromFile(new FileInfo(opts.InputFile), options);
        return await optimizer.OptimizeAsync(ct, progress);
      },
      r => r.FileContents,
      r => $"MetadataStripped: {r.MetadataStripped}",
      opts.Verbose ? () => {
        Console.WriteLine($"Strip metadata: {opts.StripMetadata}");
        Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}");
      } : null
    );
  }
}
