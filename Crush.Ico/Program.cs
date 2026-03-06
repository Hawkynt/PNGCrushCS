using CommandLine;
using Crush.Core;
using Optimizer.Ico;

namespace Crush.Ico;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    var options = new IcoOptimizationOptions(MaxParallelTasks: opts.ParallelTasks);

    return await CrushRunner.RunAsync(
      "IcoCrush - ICO Optimizer",
      opts.InputFile,
      opts.OutputFile,
      opts.Verbose,
      async (ct, progress) => {
        var optimizer = IcoOptimizer.FromFile(new FileInfo(opts.InputFile), options);
        return await optimizer.OptimizeAsync(ct, progress);
      },
      r => r.FileContents,
      r => $"Formats: [{string.Join(", ", r.EntryFormats)}]",
      opts.Verbose ? () => Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}") : null
    );
  }
}
