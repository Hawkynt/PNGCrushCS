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
// Make Main async to use await
// Use a helper async method to allow returning int from Main
return await ProcessPngAsync(inputFile, outputFile);

static async Task<int> ProcessPngAsync(string inputFile, string outputFile) {
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

    var isInterlaced = ihdr.InterlaceMethod == InterlaceMethod.Adam7Interlaced;
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
    // Use a tuple to hold best results to avoid potential issues with captured variables if bestPngData were directly updated
    (long Size, byte[] PngData, string Strategy) bestResult = (originalFileSize, originalPngBytes, "Original");

    // --- Optimization Loop ---
    RowFilterType[] filterTypesToTry = [RowFilterType.None, RowFilterType.Sub, RowFilterType.Up, RowFilterType.Average, RowFilterType.Paeth];

    var levelsToTry = new[] {
            CompressionLevel.NoCompression,
            CompressionLevel.Fastest,
            CompressionLevel.Optimal,
            CompressionLevel.SmallestSize
        };

    PngChunk? outputIhdrChunk = null; // Store the modified IHDR chunk if needed
    if (isInterlaced) {
      var ihdrDataBytes = ihdrChunk.Data.ToArray(); // Get a mutable copy
      const int INTERLACED_FLAG_INDEX = 12;
      ihdrDataBytes[INTERLACED_FLAG_INDEX] = (byte)InterlaceMethod.NonInterlaced;
      outputIhdrChunk = PngChunk.Create("IHDR", ihdrDataBytes);
      Console.WriteLine("Will write output IHDR with Interlace=0.");
    }

    // Iterate through filter types sequentially
    foreach (var filterType in filterTypesToTry) {
      Console.WriteLine($"  Filter type {filterType}: Processing levels in parallel...");
      var filteredData = PngOptimizer.ApplyFilter(rawPixelData, ihdr.Width, ihdr.Height, bytesPerPixel, filterType);

      // --- Parallel Processing for Compression Levels ---
      var compressionTasks = levelsToTry.Select(level => Task.Run(() => {
        var levelName = level switch {
          CompressionLevel.NoCompression => "None",
          CompressionLevel.Fastest => "Fastest",
          CompressionLevel.Optimal => "Optimal",
          CompressionLevel.SmallestSize => "Smallest",
          _ => level.ToString()
        };

        var recompressedData = PngOptimizer.CompressZlib(filteredData, level);

        // Create new IDAT chunk (single large chunk for simplicity)
        var newIdatChunk = PngChunk.Create("IDAT", recompressedData);

        // Reconstruct the PNG file bytes, passing the modified IHDR if input was interlaced
        // Important: Pass necessary data (chunks, newIdatChunk, outputIhdrChunk) into the task
        var candidatePngBytes = PngOptimizer.RebuildPng(chunks, newIdatChunk, outputIhdrChunk);
        var currentStrategy = $"Filter={filterType}, Level={levelName}";
        return (Size: (long)candidatePngBytes.Length, PngData: candidatePngBytes, Strategy: currentStrategy);
      }))
        .ToList();

      // Wait for all compression level tasks for the current filter type to complete
      var results = await Task.WhenAll(compressionTasks);

      // --- Process results sequentially after parallel execution ---
      var improvementFoundThisFilter = false;
      foreach (var result in results) {
        // Update the overall best result if this task found a smaller size
        // Accessing bestResult here is safe as it's done sequentially after await Task.WhenAll
        if (result.Size >= bestResult.Size)
          continue;

        Console.WriteLine($"    Found smaller: {result.Size:N0} bytes! ({result.Strategy})");
        bestResult = result; // Update the best result tuple
        improvementFoundThisFilter = true;
      }

      if (!improvementFoundThisFilter)
        Console.WriteLine("    No improvement found for this filter type.");
    } // End foreach filterType

    // 5. Write the best result
    ArgumentNullException.ThrowIfNull(bestResult.PngData); // Should always have original as fallback

    await File.WriteAllBytesAsync(outputFile, bestResult.PngData);
    stopwatch.Stop();

    if (bestResult.Size < originalFileSize) {
      Console.WriteLine($"\nOptimization complete.");
      Console.WriteLine($"Best strategy: {bestResult.Strategy}");
      Console.WriteLine($"Original size: {originalFileSize:N0} bytes");
      Console.WriteLine($"Best size:     {bestResult.Size:N0} bytes");
      Console.WriteLine($"Saved:         {originalFileSize - bestResult.Size:N0} bytes ({(double)(originalFileSize - bestResult.Size) / originalFileSize:P2})");
      Console.WriteLine($"Time taken:    {stopwatch.ElapsedMilliseconds} ms");
      return 0;
    }

    // If no improvement, write the best data found (might be original or de-interlaced)
    Console.WriteLine("\nNo size reduction found. Smallest version saved (might be de-interlaced original).");
    Console.WriteLine($"Final size:    {bestResult.Size:N0} bytes");
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
}
