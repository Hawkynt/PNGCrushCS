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
var bestStrategy = "Original";

Console.WriteLine($"Processing: {inputFile}");

try {
  // 1. Read the original PNG
  var originalPngBytes = await File.ReadAllBytesAsync(inputFile);
  var chunks = PngParser.ReadPngChunks(originalPngBytes);

  // 2. Extract necessary info and data
  var ihdrChunk = chunks.First(c => c.Type == "IHDR");
  var ihdr = PngParser.ParseIhdr(ihdrChunk);

  var isInterlaced = ihdr.InterlaceMethod == 1;
  if (isInterlaced)
    Console.WriteLine("Detected Adam7 interlacing. Output will be non-interlaced.");
  
  // Combine IDAT data
  var compressedImageData = PngParser.GetCombinedIdatData(chunks);

  // 3. Decompress image data
  var decompressedData = PngOptimizer.DecompressZlib(compressedImageData);

  // 4. Unfilter / De-interlace image data
  var bytesPerPixel = PngOptimizer.CalculateBytesPerPixel(ihdr);
  byte[] rawPixelData; // This will hold the final, non-interlaced pixel data

  if (isInterlaced) {
    Console.WriteLine("De-interlacing pixel data...");
    rawPixelData = Adam7.Deinterlace(decompressedData, ihdr.Width, ihdr.Height, bytesPerPixel);
  } else
    // Standard unfiltering for non-interlaced
    rawPixelData = PngOptimizer.Unfilter(decompressedData, ihdr.Width, ihdr.Height, bytesPerPixel);
  
  Console.WriteLine($"Original size: {originalFileSize:N0} bytes");
  Console.WriteLine($"IHDR: Width={ihdr.Width}, Height={ihdr.Height}, Depth={ihdr.BitDepth}, ColorType={ihdr.ColorType}, Interlaced={isInterlaced}");
  Console.WriteLine("Trying optimization strategies...");

  // --- Baseline ---
  var bestPngData = originalPngBytes;
  var bestSize = originalFileSize;

  // --- Optimization Loop ---
  // Try different filter types and compression levels
  RowFilterType[] filterTypesToTry = [RowFilterType.None, RowFilterType.Sub, RowFilterType.Up, RowFilterType.Average, RowFilterType.Paeth];

  var levelsToTry = new[] {
    CompressionLevel.NoCompression,
    CompressionLevel.Fastest,
    CompressionLevel.Optimal,
    CompressionLevel.SmallestSize
  };

  PngChunk? outputIhdrChunk = null; // Store the modified IHDR chunk if needed
  if (isInterlaced) {
    // Create a modified IHDR chunk with InterlaceMethod set to 0
    var ihdrDataBytes = ihdrChunk.Data.ToArray(); // Get a mutable copy
    ihdrDataBytes[12] = 0; // Set the Interlace method byte (index 12) to 0
    outputIhdrChunk = PngChunk.Create("IHDR", ihdrDataBytes); // Create new chunk with recalculated CRC
    Console.WriteLine("Will write output IHDR with Interlace=0.");
  }

  // Iterate through strategies
  foreach (var filterType in filterTypesToTry) {
    Console.Write($"  Filter type {filterType}: ");
    var filteredData = PngOptimizer.ApplyFilter(rawPixelData, ihdr.Width, ihdr.Height, bytesPerPixel, filterType);

    foreach (var level in levelsToTry) {
      var levelName = level switch {
        CompressionLevel.NoCompression => "None",
        CompressionLevel.Fastest => "Fastest",
        CompressionLevel.Optimal => "Optimal",
        CompressionLevel.SmallestSize => "Smallest",
        _ => level.ToString()
      };
      Console.Write($"Level {levelName}...");

      var recompressedData = PngOptimizer.CompressZlib(filteredData, level);

      // Create new IDAT chunk (single large chunk for simplicity)
      var newIdatChunk = PngChunk.Create("IDAT", recompressedData);

      // Reconstruct the PNG file bytes, passing the modified IHDR if input was interlaced
      var candidatePngBytes = PngOptimizer.RebuildPng(chunks, newIdatChunk, outputIhdrChunk); // Pass optional new IHDR

      if (candidatePngBytes.Length < bestSize) {
        var currentStrategy = $"Filter={filterType}, Level={levelName}";
        Console.Write($" Found smaller: {candidatePngBytes.Length:N0} bytes! ({currentStrategy})");
        bestSize = candidatePngBytes.Length;
        bestPngData = candidatePngBytes;
        bestStrategy = currentStrategy;
      } else
        Console.Write(" No improvement.");

    }
    Console.WriteLine(); // Newline after processing levels for a filter
  }

  // 5. Write the best result
  ArgumentNullException.ThrowIfNull(bestPngData); // Should always have original as fallback

  await File.WriteAllBytesAsync(outputFile, bestPngData);
  stopwatch.Stop();

  if (bestSize < originalFileSize) {
    Console.WriteLine($"\nOptimization complete.");
    Console.WriteLine($"Best strategy: {bestStrategy}");
    Console.WriteLine($"Original size: {originalFileSize:N0} bytes");
    Console.WriteLine($"Best size:     {bestSize:N0} bytes");
    Console.WriteLine($"Saved:         {originalFileSize - bestSize:N0} bytes ({(double)(originalFileSize - bestSize) / originalFileSize:P2})");
    Console.WriteLine($"Time taken:    {stopwatch.ElapsedMilliseconds} ms");
    return 0;
  }

  // If no improvement, write the original data (or potentially the non-interlaced version if input was interlaced but optimization didn't shrink)
  // Decide: Always write original, or write the non-interlaced version if it was generated?
  // Let's write the *best found data* even if it's not smaller, which might be the de-interlaced version recompressed with default strategy.
  // Write the best data found (might be original)
  Console.WriteLine("\nNo size reduction found. Smallest version saved (might be de-interlaced original).");
  Console.WriteLine($"Final size:    {bestSize:N0} bytes");
  Console.WriteLine($"Time taken:    {stopwatch.ElapsedMilliseconds} ms");
  return 0;
} catch (ArgumentException ex) { // Catch specific PNG format errors, file issues etc.
  Console.Error.WriteLine($"\nError processing PNG: {ex.Message}");
  if (ex.InnerException != null)
    Console.Error.WriteLine($"  Inner Exception: {ex.InnerException.Message}");
  // Debug.WriteLine(ex.ToString()); // Uncomment for full stack trace during debugging
  return 1;
} catch (IOException ex) {
  Console.Error.WriteLine($"\nFile I/O Error: {ex.Message}");
  return 1;
} catch (NotSupportedException ex) {
  Console.Error.WriteLine($"\nUnsupported PNG Feature: {ex.Message}");
  Console.Error.WriteLine("This tool may not support certain rare PNG color type/bit depth combinations or features.");
  return 1;
} catch (Exception ex) {
  Console.Error.WriteLine($"\nAn unexpected error occurred: {ex}");
  return 1;
}