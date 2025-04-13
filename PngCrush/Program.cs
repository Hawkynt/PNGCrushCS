using System.Drawing;
using System.Diagnostics;
using CommandLine;

namespace PngOptimizer;

/// <summary>Command line program for PNG optimization</summary>
public static partial class Program {
  public static async Task<int> Main(string[] args) {
    return await Parser.Default.ParseArguments<CommandLineOptions>(args)
      .MapResult(
        async opts => await RunOptimization(opts),
        errs => Task.FromResult(1));

    static async Task<int> RunOptimization(CommandLineOptions options) {
      try {
        PrintHeader();

        // Validate input file
        if (!File.Exists(options.InputFile)) {
          Console.Error.WriteLine($"Error: Input file '{options.InputFile}' does not exist.");
          return 1;
        }

        if (options.Verbose) {
          Console.WriteLine($"Input file: {options.InputFile}");
          Console.WriteLine($"Output file: {options.OutputFile}");
          Console.WriteLine($"Options: AutoColorMode={options.AutoColorMode}, " +
                            $"TryInterlacing={options.TryInterlacing}, " +
                            $"TryPartitioning={options.TryPartitioning}");
        }

        // Load input file
        using var inputBitmap = LoadPngFile(options.InputFile);
        var originalFileSize = new FileInfo(options.InputFile).Length;

        Console.WriteLine($"Input: {Path.GetFileName(options.InputFile)} ({FormatFileSize(originalFileSize)})");
        Console.WriteLine($"Dimensions: {inputBitmap.Width}x{inputBitmap.Height}");

        // Set up optimization options
        var pngOptions = CreatePngOptions(options);

        // Create optimizer
        var optimizer = new PngOptimizer(inputBitmap, pngOptions);

        // Run optimization
        var stopwatch = Stopwatch.StartNew();
        var result = await optimizer.OptimizeAsync();
        stopwatch.Stop();

        // Save the result
        await new FileInfo(options.OutputFile).WriteAllBytesAsync(result.FileContents);

        // Display results
        var newFileSize = new FileInfo(options.OutputFile).Length;
        var savingsPercent = Math.Round((1 - newFileSize / (double)originalFileSize) * 100, 2);

        Console.WriteLine($"Output: {Path.GetFileName(options.OutputFile)} ({FormatFileSize(newFileSize)})");
        Console.WriteLine($"Compression savings: {savingsPercent}% (reduced by {FormatFileSize(originalFileSize - newFileSize)})");
        Console.WriteLine($"Total time: {stopwatch.Elapsed.TotalSeconds:F1} seconds");

        if (options.Verbose) {
          Console.WriteLine("\nOptimization details:");
          Console.WriteLine($"  Color mode: {result.ColorMode}, Bit depth: {result.BitDepth}");
          Console.WriteLine($"  Interlace: {result.InterlaceMethod}");
          Console.WriteLine($"  Filter strategy: {result.FilterStrategy}");
          Console.WriteLine($"  Filter transitions: {result.FilterTransitions}");
          Console.WriteLine($"  Deflate method: {result.DeflateMethod}");

          // Display filter usage statistics
          DisplayFilterUsageStats(result.Filters);
        }

        return 0;
      } catch (Exception ex) {
        Console.Error.WriteLine($"Error: {ex.Message}");
        if (options.Verbose)
          Console.Error.WriteLine(ex.StackTrace);

#if DEBUG
        throw;
#else
      return 1;
#endif

      }

      static Bitmap LoadPngFile(string filePath) {
        try {
          return new Bitmap(filePath);
        } catch (Exception ex) {
          throw new Exception($"Failed to load PNG file: {ex.Message}", ex);
        }
      }

      static PngOptimizationOptions CreatePngOptions(CommandLineOptions options) {
        var filterStrategies = ParseFilterStrategies(options.FilterStrategies);
        var deflateMethods = ParseDeflateMethods(options.DeflateMethods);

        return new PngOptimizationOptions {
          AutoSelectColorMode = options.AutoColorMode,
          TryInterlacing = options.TryInterlacing,
          TryPartitioning = options.TryPartitioning,
          FilterStrategies = filterStrategies,
          DeflateMethods = deflateMethods,
          MaxParallelTasks = options.ParallelTasks <= 0 ? Environment.ProcessorCount : options.ParallelTasks
        };

        static List<FilterStrategy> ParseFilterStrategies(string input) {
          var result = new List<FilterStrategy>();

          foreach (var strategy in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (Enum.TryParse<FilterStrategy>(strategy.Trim(), out var filterStrategy))
              result.Add(filterStrategy);

          if (result.Count != 0)
            return result;

          // Use default from the Option attribute via reflection
          var defaultValue = GetDefaultValueFromAttribute(nameof(CommandLineOptions.FilterStrategies));
          foreach (var strategy in defaultValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (Enum.TryParse<FilterStrategy>(strategy.Trim(), out var filterStrategy))
              result.Add(filterStrategy);

          return result;
        }

        static List<DeflateMethod> ParseDeflateMethods(string input) {
          var result = new List<DeflateMethod>();

          foreach (var method in input.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (Enum.TryParse<DeflateMethod>(method.Trim(), out var deflateMethod))
              result.Add(deflateMethod);

          if (result.Count != 0)
            return result;

          // Use default from the Option attribute via reflection
          var defaultValue = GetDefaultValueFromAttribute(nameof(CommandLineOptions.DeflateMethods));
          foreach (var method in defaultValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (Enum.TryParse<DeflateMethod>(method.Trim(), out var deflateMethod))
              result.Add(deflateMethod);

          return result;
        }

        static string GetDefaultValueFromAttribute(string propertyName) {
          var property = typeof(CommandLineOptions).GetProperty(propertyName);
          if (property == null)
            throw new InvalidOperationException($"Property '{propertyName}' not found.");

          var optionAttribute = property.GetCustomAttributes(typeof(OptionAttribute), false)
            .Cast<OptionAttribute>()
            .FirstOrDefault();
          if (optionAttribute == null)
            throw new InvalidOperationException($"Option attribute not found on '{propertyName}'.");

          return optionAttribute.Default?.ToString() ?? string.Empty;
        }
      }

      static void DisplayFilterUsageStats(FilterType[] filters) {
        var filterCounts = new Dictionary<FilterType, int>();

        foreach (var filter in filters) {
          filterCounts.TryAdd(filter, 0);
          ++filterCounts[filter];
        }

        Console.WriteLine("\nFilter usage statistics:");
        foreach (var kvp in filterCounts.OrderByDescending(kv => kv.Value)) {
          var percentage = (kvp.Value / (double)filters.Length) * 100;
          Console.WriteLine($"  {kvp.Key}: {kvp.Value} scanlines ({percentage:F1}%)");
        }
      }

      static void PrintHeader() {
        Console.WriteLine("PNG Optimizer v1.1 - Advanced PNG compression tool");
        Console.WriteLine("Copyright © Hawkynt 2025 - All rights reserved");
        Console.WriteLine("------------------------------------------------");
      }

      static string FormatFileSize(long byteCount) {
        string[] sizes = ["B", "KB", "MB", "GB"];
        var order = 0;
        double size = byteCount;
        while (size >= 1024 && order < sizes.Length - 1) {
          order++;
          size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
      }

    }
  }
}