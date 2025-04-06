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
    Console.WriteLine("Trying optimization strategies in parallel...");

    // --- Baseline ---
    // Use a tuple to hold best results to avoid potential race conditions if bestPngData were directly updated in parallel tasks
    // Initialize with the original (or de-interlaced if applicable) data
    (long Size, byte[]? PngData, string Strategy) bestResult;

    PngChunk? outputIhdrChunk = null; // Store the modified IHDR chunk if needed
    if (isInterlaced) {
      var ihdrDataBytes = ihdrChunk.Data.ToArray(); // Get a mutable copy
      const int INTERLACED_FLAG_INDEX = 12;
      ihdrDataBytes[INTERLACED_FLAG_INDEX] = (byte)InterlaceMethod.NonInterlaced;
      outputIhdrChunk = PngChunk.Create("IHDR", ihdrDataBytes);
      Console.WriteLine("Will write output IHDR with Interlace=0.");
      // If interlaced, rebuild the PNG with the non-interlaced header and non-interlaced raw data
      // Recompress with default settings as a baseline for the de-interlaced version
      var filteredBaseline = PngOptimizer.ApplyFilter(rawPixelData, ihdr.Width, ihdr.Height, bytesPerPixel, RowFilterType.Sub); // Use Sub as a reasonable default filter
      var compressedBaseline = PngOptimizer.CompressZlib(filteredBaseline, CompressionLevel.Optimal);
      var baselineIdat = PngChunk.Create("IDAT", compressedBaseline);
      var deinterlacedBaselineBytes = PngOptimizer.RebuildPng(chunks, baselineIdat, outputIhdrChunk);
      bestResult = (deinterlacedBaselineBytes.Length, deinterlacedBaselineBytes, "De-interlaced Baseline (Sub/Optimal)");
      Console.WriteLine($"De-interlaced baseline size: {bestResult.Size:N0} bytes");
    } else {
      bestResult = (originalFileSize, originalPngBytes, "Original"); // Non-interlaced baseline is the original
    }


    // --- Optimization Loop (Parallelized) ---
    RowFilterType[] filterTypesToTry = [RowFilterType.None, RowFilterType.Sub, RowFilterType.Up, RowFilterType.Average, RowFilterType.Paeth];
    var levelsToTry = new[] {
            CompressionLevel.NoCompression,
            CompressionLevel.Fastest,
            CompressionLevel.Optimal,
            CompressionLevel.SmallestSize
        };

    // Create tasks for each filter type strategy
    var filterStrategyTasks = new List<Task<(long Size, byte[]? PngData, string Strategy)?>>();
    foreach (var filterType in filterTypesToTry) {
      // Capture loop variable for the lambda
      var currentFilterType = filterType;

      var task = Task.Run(TryRowFilter);
      filterStrategyTasks.Add(task);
      continue;

      // Create a task for processing this filter type across all compression levels
      async Task<(long Size, byte[]? PngData, string Strategy)?>? TryRowFilter() {
        var filteredData = PngOptimizer.ApplyFilter(rawPixelData, ihdr.Width, ihdr.Height, bytesPerPixel, currentFilterType);

        // --- Parallel Processing for Compression Levels (within the filter task) ---
        var compressionTasks = levelsToTry.Select(level => Task.Run(() => TryCompressionLevel(level))).ToArray();

        // Wait for all compression level tasks for *this filter type* to complete
        var levelResults = await Task.WhenAll(compressionTasks);

        // --- Find the best result *for this filter type* ---
        (long Size, byte[]? PngData, string Strategy)? bestForThisFilter = null;
        foreach (var result in levelResults) {
          // Ensure PngData is not null (check for errors)
          if (result.PngData == null)
            continue;

          if (bestForThisFilter == null || result.Size < bestForThisFilter.Value.Size) {
            bestForThisFilter = result;
          }
        }

        // Console.WriteLine($"  Finished Task for Filter: {currentFilterType}, Best Size: {bestForThisFilter?.Size ?? -1}"); // Debug logging
        return bestForThisFilter; // Return the best result found for this filter type

        (long Size, byte[]? PngData, string Strategy) TryCompressionLevel(CompressionLevel compressionLevel) {
          var levelName = compressionLevel switch {
            CompressionLevel.NoCompression => "None",
            CompressionLevel.Fastest => "Fastest",
            CompressionLevel.Optimal => "Optimal",
            CompressionLevel.SmallestSize => "Smallest",
            _ => compressionLevel.ToString()
          };

          try {
            var recompressedData = PngOptimizer.CompressZlib(filteredData, compressionLevel);
            var newIdatChunk = PngChunk.Create("IDAT", recompressedData);
            // Important: Pass captured variables (chunks, outputIhdrChunk)
            var candidatePngBytes = PngOptimizer.RebuildPng(chunks, newIdatChunk, outputIhdrChunk);
            var currentStrategy = $"Filter={currentFilterType}, Level={levelName}";
            return (Size: candidatePngBytes.Length, PngData: candidatePngBytes, Strategy: currentStrategy);
          } catch (Exception ex) {
            // Handle potential errors during compression/rebuild for a specific level
            Console.Error.WriteLine($"    Error in strategy Filter={currentFilterType}, Level={levelName}: {ex.Message}");
            return (Size: long.MaxValue, PngData: null, Strategy: $"Filter={currentFilterType}, Level={levelName} (Error)"); // Indicate error
          }
        }
      }
    } // End foreach filterType

    // Wait for all filter type strategy tasks to complete
    var allFilterResults = await Task.WhenAll(filterStrategyTasks);

    // --- Process results sequentially after parallel execution ---
    Console.WriteLine("All strategies processed. Determining best overall result...");
    
    // Keep track of the best result found during parallel execution
    (long Size, byte[]? PngData, string Strategy)? bestParallelResult = null;

    foreach (var filterResult in allFilterResults) {
      if (filterResult == null)
        continue; // Skip if a filter task had no valid results

      // Update the best result found *among the parallel tasks*
      if (bestParallelResult == null || filterResult.Value.Size < bestParallelResult.Value.Size)
        bestParallelResult = filterResult;
    }

    // Compare the best result from parallel tasks with the initial baseline
    if (bestParallelResult != null && bestParallelResult.Value.Size < bestResult.Size) {
      Console.WriteLine($"    Found smaller: {bestParallelResult.Value.Size:N0} bytes! ({bestParallelResult.Value.Strategy})");
      bestResult = bestParallelResult.Value; // Update overall best
    } else {
      Console.WriteLine("    No improvement found compared to baseline.");
    }
    
    // 5. Write the best result
    ArgumentNullException.ThrowIfNull(bestResult.PngData); // Should always have baseline as fallback

    await File.WriteAllBytesAsync(outputFile, bestResult.PngData);
    stopwatch.Stop();

    if (bestResult.Size < originalFileSize) { // Always compare final size vs original file size for the report
      Console.WriteLine($"\nOptimization complete.");
      Console.WriteLine($"Best strategy: {bestResult.Strategy}");
      Console.WriteLine($"Original size: {originalFileSize:N0} bytes");
      Console.WriteLine($"Best size:     {bestResult.Size:N0} bytes");
      Console.WriteLine($"Saved:         {originalFileSize - bestResult.Size:N0} bytes ({(double)(originalFileSize - bestResult.Size) / originalFileSize:P2})");
      Console.WriteLine($"Time taken:    {stopwatch.ElapsedMilliseconds} ms");
      return 0;
    }

    // If no improvement compared to original file size
    Console.WriteLine("\nNo size reduction found compared to the original file.");
    Console.WriteLine($"Best strategy found: {bestResult.Strategy}"); // Still show the best strategy tried
    Console.WriteLine($"Final size:    {bestResult.Size:N0} bytes"); // Which might be larger if de-interlaced
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
