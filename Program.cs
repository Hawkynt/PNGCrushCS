// PngCrushCS.cs - A single-file C# implementation inspired by pngcrush
// Usage: PngCrushCS <input.png> <output.png>

using System.Diagnostics;
using System.IO.Compression;

// --- Argument Parsing ---
if (args.Length != 2) {
  Console.WriteLine("Usage: PngCrushCS <input.png> <output.png>");
  return 1;
}

var inputFile = args[0];
var outputFile = args[1];

ArgumentNullException.ThrowIfNull(inputFile);
ArgumentNullException.ThrowIfNull(outputFile);

if (!File.Exists(inputFile)) {
  Console.Error.WriteLine($"Error: Input file not found: {inputFile}");
  return 1;
}

// --- Main Logic ---
var stopwatch = Stopwatch.StartNew();
var originalFileSize = new FileInfo(inputFile).Length;

Console.WriteLine($"Processing: {inputFile}");

try {
  // 1. Read the original PNG
  var originalPngBytes = await File.ReadAllBytesAsync(inputFile);
  var chunks = PngParser.ReadPngChunks(originalPngBytes);

  // 2. Extract necessary info and data
  var ihdrChunk = chunks.First(c => c.Type == "IHDR");
  var ihdr = PngParser.ParseIhdr(ihdrChunk);

  if (ihdr.InterlaceMethod != 0) {
    Console.Error.WriteLine("Error: Interlaced PNGs are not supported by this tool.");
    return 1; // Or attempt to copy file as-is
  }

  // Combine IDAT data
  var compressedImageData = PngParser.GetCombinedIdatData(chunks);

  // 3. Decompress image data
  var decompressedData = PngOptimizer.DecompressZlib(compressedImageData);

  // 4. Unfilter image data
  var bytesPerPixel = PngOptimizer.CalculateBytesPerPixel(ihdr);
  var rawPixelData = PngOptimizer.Unfilter(decompressedData, ihdr.Width, ihdr.Height, bytesPerPixel);

  Console.WriteLine($"Original size: {originalFileSize:N0} bytes");
  Console.WriteLine($"IHDR: Width={ihdr.Width}, Height={ihdr.Height}, Depth={ihdr.BitDepth}, ColorType={ihdr.ColorType}");
  Console.WriteLine("Trying optimization strategies...");

  // Keep track of the best result found
  var bestPngData = originalPngBytes;
  var bestSize = originalFileSize;

  // --- Optimization Loop ---
  // Try different filter types and compression levels
  byte[] filterTypesToTry = [0, 1, 2, 3, 4]; // None, Sub, Up, Average, Paeth
  CompressionLevel[] levelsToTry = [CompressionLevel.Optimal, CompressionLevel.SmallestSize]; // Common levels

  foreach (var filterType in filterTypesToTry) {
    Console.Write($"  Filter type {filterType}: ");
    var filteredData = PngOptimizer.ApplyFilter(rawPixelData, ihdr.Width, ihdr.Height, bytesPerPixel, filterType);

    foreach (var level in levelsToTry) {
      Console.Write($"Level {level}...");
      var recompressedData = PngOptimizer.CompressZlib(filteredData, level);

      // Create new IDAT chunk(s) - simplifying to one large chunk
      var newIdatChunk = PngChunk.Create("IDAT", recompressedData);

      // Reconstruct the PNG file bytes
      var candidatePngBytes = PngOptimizer.RebuildPng(chunks, newIdatChunk);

      if (candidatePngBytes.Length < bestSize) {
        Console.Write($" Found smaller: {candidatePngBytes.Length:N0} bytes! ");
        bestSize = candidatePngBytes.Length;
        bestPngData = candidatePngBytes;
      } else {
        Console.Write(" No improvement. ");
      }
    }
    Console.WriteLine(); // Newline after processing levels for a filter
  }

  // 5. Write the best result
  if (bestSize < originalFileSize) {
    await File.WriteAllBytesAsync(outputFile, bestPngData);
    stopwatch.Stop();
    Console.WriteLine($"\nOptimization complete.");
    Console.WriteLine($"Original size: {originalFileSize:N0} bytes");
    Console.WriteLine($"Best size:     {bestSize:N0} bytes");
    Console.WriteLine($"Saved:         {originalFileSize - bestSize:N0} bytes ({(double)(originalFileSize - bestSize) / originalFileSize:P2})");
    Console.WriteLine($"Time taken:    {stopwatch.ElapsedMilliseconds} ms");
    return 0;
  }

  // If no improvement, write the original data to the output file
  await File.WriteAllBytesAsync(outputFile, originalPngBytes);
  stopwatch.Stop();
  Console.WriteLine("\nNo size reduction found. Original file copied.");
  Console.WriteLine($"Time taken:    {stopwatch.ElapsedMilliseconds} ms");
  return 0;
} catch (ArgumentException ex) {
  // Catch specific PNG format errors
  Console.Error.WriteLine($"\nError processing PNG: {ex.Message}");
  return 1;
} catch (IOException ex) {
  Console.Error.WriteLine($"\nFile I/O Error: {ex.Message}");
  return 1;
} catch (Exception ex) {
  Console.Error.WriteLine($"\nAn unexpected error occurred: {ex}");
  return 1;
}
