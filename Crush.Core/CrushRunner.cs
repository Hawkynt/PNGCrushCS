using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Crush.Core;

/// <summary>Shared optimization runner for all crush CLI tools.</summary>
public static class CrushRunner {

  public static async Task<int> RunAsync<TResult>(
    string toolName,
    string inputPath,
    string outputPath,
    bool verbose,
    Func<CancellationToken, IProgress<OptimizationProgress>, ValueTask<TResult>> optimize,
    Func<TResult, byte[]> getFileContents,
    Func<TResult, string>? formatResult = null,
    Action? beforeOptimize = null
  ) {
    Console.WriteLine($"{toolName}");
    Console.WriteLine(new string('=', 40));

    var inputFile = new FileInfo(inputPath);
    if (!inputFile.Exists) {
      Console.Error.WriteLine($"Input file not found: {inputFile.FullName}");
      return 1;
    }

    var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (outputDir != null && !Directory.Exists(outputDir)) {
      Console.Error.WriteLine($"Output directory not found: {outputDir}");
      return 1;
    }

    var originalSize = inputFile.Length;
    Console.WriteLine($"Input:  {inputFile.FullName} ({FileFormatting.FormatFileSize(originalSize)})");

    beforeOptimize?.Invoke();

    Console.WriteLine("Optimizing...");
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => {
      e.Cancel = true;
      cts.Cancel();
    };

    var sw = Stopwatch.StartNew();
    var progressReporter = new Progress<OptimizationProgress>(p =>
      Console.Write(
        $"\r[{p.CombosCompleted}/{p.CombosTotal}] Best: {FileFormatting.FormatFileSize(p.BestSizeSoFar)} | Phase: {p.Phase}    ")
    );

    var result = await optimize(cts.Token, progressReporter);
    Console.WriteLine();
    sw.Stop();

    var fileContents = getFileContents(result);
    var outputFile = new FileInfo(outputPath);
    await File.WriteAllBytesAsync(outputFile.FullName, fileContents, cts.Token);

    var newSize = (long)fileContents.Length;
    var reduction = originalSize > 0 ? (1.0 - (double)newSize / originalSize) * 100 : 0;

    Console.WriteLine($"Output: {outputFile.FullName} ({FileFormatting.FormatFileSize(newSize)})");
    Console.WriteLine($"Reduction: {reduction:F1}% ({FileFormatting.FormatFileSize(originalSize - newSize)} saved)");

    if (formatResult != null)
      Console.WriteLine(formatResult(result));

    Console.WriteLine($"Time: {sw.Elapsed.TotalSeconds:F1}s");

    return 0;
  }
}
