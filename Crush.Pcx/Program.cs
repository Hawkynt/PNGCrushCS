using CommandLine;
using Crush.Core;
using Optimizer.Pcx;

namespace Crush.Pcx;

public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    var options = new PcxOptimizationOptions(
      AutoSelectColorMode: opts.AutoColorMode,
      MaxParallelTasks: opts.ParallelTasks
    );

    return await CrushRunner.RunAsync(
      "PcxCrush - PCX Optimizer",
      opts.InputFile,
      opts.OutputFile,
      opts.Verbose,
      async (ct, progress) => {
        var optimizer = PcxOptimizer.FromFile(new FileInfo(opts.InputFile), options);
        return await optimizer.OptimizeAsync(ct, progress);
      },
      r => r.FileContents,
      r => $"Color: {r.ColorMode}, Planes: {r.PlaneConfig}, Palette: {r.PaletteOrder}",
      opts.Verbose ? () => {
        Console.WriteLine($"Auto color mode: {opts.AutoColorMode}");
        Console.WriteLine($"Max parallel tasks: {options.MaxParallelTasks}");
      } : null
    );
  }
}
