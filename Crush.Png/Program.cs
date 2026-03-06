using System.Drawing;
using CommandLine;
using Crush.Core;
using FileFormat.Png;
using Optimizer.Png;

namespace Crush.Png;

/// <summary>Command line program for PNG optimization</summary>
public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(RunOptimization, _ => Task.FromResult(1));
  }

  private static async Task<int> RunOptimization(CommandLineOptions opts) {
    try {
      using var inputBitmap = _LoadPngFile(opts.InputFile);
      var originalPngBytes = opts.PreserveChunks ? File.ReadAllBytes(opts.InputFile) : null;
      var pngOptions = _CreatePngOptions(opts);
      var optimizer = new PngOptimizer(inputBitmap, originalPngBytes, pngOptions);

      return await CrushRunner.RunAsync(
        "PNG Optimizer v1.1 - Advanced PNG compression tool",
        opts.InputFile,
        opts.OutputFile,
        opts.Verbose,
        async (ct, progress) => await optimizer.OptimizeAsync(ct, progress),
        r => r.FileContents,
        r => opts.Verbose ? _FormatVerboseResult(r) : null,
        () => {
          Console.WriteLine($"Dimensions: {inputBitmap.Width}x{inputBitmap.Height}");
          if (opts.Verbose) {
            Console.WriteLine($"Options: AutoColorMode={opts.AutoColorMode}, " +
                              $"TryInterlacing={opts.TryInterlacing}, " +
                              $"TryPartitioning={opts.TryPartitioning}");
          }
        }
      );
    } catch (Exception ex) {
      Console.Error.WriteLine($"Error: {ex.Message}");
      if (opts.Verbose)
        Console.Error.WriteLine(ex.StackTrace);

#if DEBUG
      throw;
#else
      return 1;
#endif
    }
  }

  private static string? _FormatVerboseResult(OptimizationResult r) {
    var lines = new List<string> {
      "",
      "Optimization details:",
      $"  Color mode: {r.ColorMode}, Bit depth: {r.BitDepth}",
      $"  Interlace: {r.InterlaceMethod}",
      $"  Filter strategy: {r.FilterStrategy}",
      $"  Filter transitions: {r.FilterTransitions}",
      $"  Deflate method: {r.DeflateMethod}"
    };

    _AppendFilterUsageStats(lines, r.Filters);

    return string.Join("\n", lines);
  }

  private static void _AppendFilterUsageStats(List<string> lines, PngFilterType[] filters) {
    var filterCounts = new Dictionary<PngFilterType, int>();
    foreach (var filter in filters) {
      filterCounts.TryAdd(filter, 0);
      ++filterCounts[filter];
    }

    lines.Add("");
    lines.Add("Filter usage statistics:");
    foreach (var kvp in filterCounts.OrderByDescending(kv => kv.Value)) {
      var percentage = kvp.Value / (double)filters.Length * 100;
      lines.Add($"  {kvp.Key}: {kvp.Value} scanlines ({percentage:F1}%)");
    }
  }

  private static Bitmap _LoadPngFile(string filePath) {
    try {
      return new Bitmap(filePath);
    } catch (Exception ex) {
      throw new Exception($"Failed to load PNG file: {ex.Message}", ex);
    }
  }

  private static PngOptimizationOptions _CreatePngOptions(CommandLineOptions options) {
    var filterStrategies = _ParseFilterStrategies(options.FilterStrategies);
    var deflateMethods = _ParseDeflateMethods(options.DeflateMethods);

    return new PngOptimizationOptions {
      AutoSelectColorMode = options.AutoColorMode,
      TryInterlacing = options.TryInterlacing,
      TryPartitioning = options.TryPartitioning,
      AllowLossyPalette = options.LossyPalette,
      UseDithering = options.UseDithering,
      IsHighQualityQuantization = options.HighQualityQuantize,
      QuantizerNames = _ParseNames(options.Quantizers),
      DithererNames = _ParseNames(options.Ditherers),
      FilterStrategies = filterStrategies,
      DeflateMethods = deflateMethods,
      PreserveAncillaryChunks = options.PreserveChunks,
      MaxParallelTasks = options.ParallelTasks <= 0 ? Environment.ProcessorCount : options.ParallelTasks
    };
  }

  private static List<string> _ParseNames(string input) =>
    input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

  private static List<FilterStrategy> _ParseFilterStrategies(string input) {
    var result = new List<FilterStrategy>();

    foreach (var strategy in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
      if (Enum.TryParse<FilterStrategy>(strategy.Trim(), out var filterStrategy))
        result.Add(filterStrategy);

    if (result.Count != 0)
      return result;

    var defaultValue = _GetDefaultValueFromAttribute(nameof(CommandLineOptions.FilterStrategies));
    foreach (var strategy in defaultValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
      if (Enum.TryParse<FilterStrategy>(strategy.Trim(), out var filterStrategy))
        result.Add(filterStrategy);

    return result;
  }

  private static List<DeflateMethod> _ParseDeflateMethods(string input) {
    var result = new List<DeflateMethod>();

    foreach (var method in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
      if (Enum.TryParse<DeflateMethod>(method.Trim(), out var deflateMethod))
        result.Add(deflateMethod);

    if (result.Count != 0)
      return result;

    var defaultValue = _GetDefaultValueFromAttribute(nameof(CommandLineOptions.DeflateMethods));
    foreach (var method in defaultValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
      if (Enum.TryParse<DeflateMethod>(method.Trim(), out var deflateMethod))
        result.Add(deflateMethod);

    return result;
  }

  private static string _GetDefaultValueFromAttribute(string propertyName) {
    var property = typeof(CommandLineOptions).GetProperty(propertyName);
    if (property == null)
      throw new InvalidOperationException($"Property '{propertyName}' not found.");

    var optionAttribute = property.GetCustomAttributes(typeof(CommandLine.OptionAttribute), false)
      .Cast<CommandLine.OptionAttribute>()
      .FirstOrDefault();
    if (optionAttribute == null)
      throw new InvalidOperationException($"Option attribute not found on '{propertyName}'.");

    return optionAttribute.Default?.ToString() ?? string.Empty;
  }
}
