using System.Buffers;
using System.IO.Compression;
using System.Text;

/// <summary>
/// Handles PNG optimization tasks like compression, filtering, and reconstruction.
/// </summary>
internal static class PngOptimizer {
  /// <summary>
  /// Decompresses ZLib data using a robust read loop.
  /// </summary>
  /// <param name="compressedData">The concatenated byte data from IDAT chunks.</param>
  /// <returns>The decompressed (filtered) pixel data.</returns>
  /// <exception cref="ArgumentNullException">Thrown if compressedData is null.</exception>
  /// <exception cref="ArgumentException">Thrown if decompression fails (e.g., corrupt data).</exception>
  public static byte[] DecompressZlib(byte[] compressedData) {
    ArgumentNullException.ThrowIfNull(compressedData);
    if (compressedData.Length == 0) {
      Console.WriteLine("Warning: Attempting to decompress empty IDAT data.");
      return []; // Return empty array if input is empty
    }

    // Debug: Log first few bytes if needed for diagnosing format issues
    // Console.WriteLine($"DEBUG: Decompressing {compressedData.Length} bytes. Header: {BitConverter.ToString(compressedData.Take(Math.Min(4, compressedData.Length)).ToArray())}");

    try {
      using MemoryStream compressedStream = new(compressedData);
      using MemoryStream decompressedStream = new();
      // Manual read loop is often more robust than CopyTo with ZLibStream
      using (ZLibStream decompressor = new(compressedStream, CompressionMode.Decompress, leaveOpen: true)) {
        var buffer = ArrayPool<byte>.Shared.Rent(81920); // Use ArrayPool for buffer
        try {
          int bytesRead;
          while ((bytesRead = decompressor.Read(buffer, 0, buffer.Length)) > 0)
            decompressedStream.Write(buffer, 0, bytesRead);

        } finally {
          ArrayPool<byte>.Shared.Return(buffer); // Ensure buffer is returned
        }
      } // Decompressor disposed here

      var result = decompressedStream.ToArray();
      
      // Sanity check: If input wasn't empty but output is, decompression likely failed silently or data was invalid.
      if (compressedData.Length > 0 && result.Length == 0)
        throw new InvalidDataException("ZLib decompression resulted in zero bytes, input data may be corrupt or not a valid ZLib/Deflate stream.");

      // Console.WriteLine($"DEBUG: Decompressed to {result.Length} bytes.");
      return result;
    } catch (InvalidDataException ex) { // Catch specific ZLib/Deflate errors
      Console.Error.WriteLine($"Error: ZLib decompression failed. Input data is likely corrupt or not valid ZLib/Deflate. Details: {ex.Message}");
      throw new ArgumentException("Failed to decompress PNG image data (IDAT). File might be corrupt.", nameof(compressedData), ex);
    } catch (Exception ex) { // Catch other potential errors during stream operations
      Console.Error.WriteLine($"Error: An unexpected error occurred during decompression: {ex}");
      throw; // Re-throw maintaining stack trace
    }
  }

  /// <summary>
  /// Compresses raw (filtered) pixel data using ZLib.
  /// </summary>
  /// <param name="data">The raw (filtered) pixel data.</param>
  /// <param name="level">The desired compression level.</param>
  /// <returns>The compressed data.</returns>
  public static byte[] CompressZlib(byte[] data, CompressionLevel level) {
    ArgumentNullException.ThrowIfNull(data);

    using MemoryStream inputStream = new(data);
    using MemoryStream outputStream = new();
    // Use ZLibStream for standard ZLib header/footer (RFC 1950)
    using (ZLibStream compressor = new(outputStream, level, leaveOpen: true))
      inputStream.CopyTo(compressor);
    
    // The compressor must be flushed/closed to finalize the stream.
    // The using statement handles disposal, which includes flushing.
    return outputStream.ToArray();
  }

  /// <summary>
  /// Calculates the number of bytes per complete pixel for filtering purposes.
  /// </summary>
  /// <remarks>
  /// For PNG filtering (Sub, Average, Paeth), the 'prior' pixel's byte corresponding
  /// to the current byte is needed. This calculation determines that offset ('bpp').
  /// It handles sub-byte pixels by determining how many bytes store a full pixel,
  /// often rounded up due to byte packing.
  /// </remarks>
  /// <param name="ihdr">The parsed IHDR data.</param>
  /// <returns>Bytes per pixel.</returns>
  /// <exception cref="NotSupportedException">If color type or bit depth is unsupported.</exception>
  public static int CalculateBytesPerPixel(IhdrData ihdr) {
    var samplesPerPixel = (ColorType)ihdr.ColorType switch {
      ColorType.Grasyscale => 1,
      ColorType.Truecolor => 3, 
      ColorType.IndexedColor => 1, 
      ColorType.GrayscaleAlpha => 2, 
      ColorType.TruecolorAlpha => 4, 
      _ => throw new NotSupportedException($"Unsupported PNG color type: {ihdr.ColorType}")
    };

    // PNG filters operate on bytes. Calculate how many bytes represent one pixel's worth of data.
    if (ihdr.BitDepth < 8) {
      // Sub-byte pixels are packed. The filter offset ('bpp') should be the number
      // of bytes needed to store one full pixel, typically rounded up.
      // E.g., 4-bit indexed (1 sample/pixel): (1 * 4 + 7) / 8 = 1 byte/pixel.
      // E.g., 2-bit grayscale (1 sample/pixel): (1 * 2 + 7) / 8 = 1 byte/pixel.
      // E.g., 1-bit grayscale (1 sample/pixel): (1 * 1 + 7) / 8 = 1 byte/pixel.
      var bitsPerPixel = samplesPerPixel * ihdr.BitDepth;
      return (bitsPerPixel + 7) / 8; // Ceiling division
    }

    if (ihdr.BitDepth == 8)
      return samplesPerPixel; // Each sample is exactly one byte

    if (ihdr.BitDepth == 16)
      return samplesPerPixel * 2; // Each sample is two bytes
    
    // Should be unreachable given the switch and bit depth checks
    throw new NotSupportedException($"Unsupported PNG bit depth: {ihdr.BitDepth}");
  }

  /// <summary>
  /// Reverses PNG filtering on decompressed data to get raw pixel data.
  /// </summary>
  /// <param name="decompressedData">The decompressed data stream (filter bytes + filtered pixel data).</param>
  /// <param name="width">Image width in pixels.</param>
  /// <param name="height">Image height in pixels.</param>
  /// <param name="bytesPerPixel">Bytes per pixel, calculated by <see cref="CalculateBytesPerPixel"/>.</param>
  /// <returns>The raw pixel data.</returns>
  /// <exception cref="ArgumentException">If input data is inconsistent with dimensions.</exception>
  /// <exception cref="ArgumentOutOfRangeException">If bytesPerPixel is invalid.</exception>
  public static byte[] Unfilter(byte[] decompressedData, int width, int height, int bytesPerPixel) {
    ArgumentNullException.ThrowIfNull(decompressedData);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerPixel);
    ArgumentOutOfRangeException.ThrowIfNegative(width);
    ArgumentOutOfRangeException.ThrowIfNegative(height);
    if (width == 0 || height == 0)
      return []; // Image has no pixels

    // Calculate stride (bytes per raw scanline) carefully to avoid overflow
    var strideL = (long)width * bytesPerPixel;
    ArgumentOutOfRangeException.ThrowIfGreaterThan(strideL,int.MaxValue);
    
    var stride = (int)strideL;

    // Expected length includes 1 filter byte per row + stride data bytes
    var expectedMinLengthL = (long)height * (1 + stride);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedMinLengthL, int.MaxValue);
    
    var expectedMinLength = (int)expectedMinLengthL;
    if (decompressedData.Length < expectedMinLength)
      throw new ArgumentException($"Decompressed data length ({decompressedData.Length}) is less than minimum expected ({expectedMinLength}) for non-interlaced {width}x{height}x{bytesPerPixel}bpp (stride {stride}). Data is likely truncated or corrupt.");

    if (decompressedData.Length > expectedMinLength + height * 8) // Allow some reasonable padding/slack per row
      Console.WriteLine($"Warning: Decompressed data length ({decompressedData.Length}) is significantly larger than expected ({expectedMinLength}). Extra data may be ignored.");

    var rawData = new byte[height * stride];
    var prevRow = new byte[stride]; // Store the *reconstructed* previous row
    var inputOffset = 0;
    var outputOffset = 0;

    for (var y = 0; y < height; ++y) {
      if (inputOffset >= decompressedData.Length)
        throw new ArgumentException($"Reached end of decompressed data prematurely at row {y}. Expected more data.");

      var filterType = decompressedData[inputOffset++];

      // Ensure we don't read past the end of the decompressed data for the current row's filtered data
      var remainingBytes = decompressedData.Length - inputOffset;
      if (remainingBytes < stride)
        throw new ArgumentException($"Insufficient data for row {y}. Needed {stride} bytes for scanline, only {remainingBytes} available after filter byte. Input offset: {inputOffset}.");

      // Create temporary buffers for the current row's processing
      var currentRowFiltered = new byte[stride];
      Array.Copy(decompressedData, inputOffset, currentRowFiltered, 0, stride);
      var currentRowReconstructed = new byte[stride]; // Buffer to hold reconstructed data for this row

      // Perform unfiltering into the temporary buffer
      UnfilterRow(filterType, currentRowFiltered, currentRowReconstructed, prevRow, bytesPerPixel);

      // Copy the fully reconstructed row to the final output buffer
      Array.Copy(currentRowReconstructed, 0, rawData, outputOffset, stride);

      // Update prevRow with the *just reconstructed* data for the next iteration
      Array.Copy(currentRowReconstructed, 0, prevRow, 0, stride);

      inputOffset += stride;
      outputOffset += stride;
    }

    return rawData;
  }

  /// <summary>
  /// Applies a specific PNG filter type to raw pixel data.
  /// </summary>
  /// <param name="rawPixelData">The raw, unfiltered pixel data.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="bytesPerPixel">Bytes per pixel.</param>
  /// <param name="filterType">The PNG filter type byte (0-4).</param>
  /// <returns>Filtered data stream (filter bytes + filtered pixel data).</returns>
  /// <exception cref="ArgumentException">If input data is inconsistent with dimensions.</exception>
  /// <exception cref="ArgumentOutOfRangeException">If bytesPerPixel is invalid.</exception>
  public static byte[] ApplyFilter(byte[] rawPixelData, int width, int height, int bytesPerPixel, RowFilterType filterType) {
    ArgumentNullException.ThrowIfNull(rawPixelData);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerPixel);
    ArgumentOutOfRangeException.ThrowIfNegative(width);
    ArgumentOutOfRangeException.ThrowIfNegative(height);

    if (width == 0 || height == 0)
      return []; // No pixels to filter

    var strideL = (long)width * bytesPerPixel;
    ArgumentOutOfRangeException.ThrowIfGreaterThan(strideL,int.MaxValue);
    
    var stride = (int)strideL;
    var expectedRawLengthL = (long)height * stride;
    ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedRawLengthL, int.MaxValue);
    
    var expectedRawLength = (int)expectedRawLengthL;
    if (rawPixelData.Length < expectedRawLength)
      throw new ArgumentException($"Raw pixel data length ({rawPixelData.Length}) is less than expected ({expectedRawLength}) for {width}x{height}x{bytesPerPixel}bpp (stride {stride}).");

    var filteredLengthL = (long)height * (1 + stride);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(filteredLengthL,int.MaxValue);

    var filteredData = new byte[(int)filteredLengthL]; // 1 filter byte + data per row
    var prevRow = new byte[stride]; // Previous *raw* row data
    var inputOffset = 0;
    var outputOffset = 0;

    for (var y = 0; y < height; ++y) {
      if (outputOffset >= filteredData.Length)
        throw new IndexOutOfRangeException("Output buffer overflow during filtering."); // Safety check

      filteredData[outputOffset++] = (byte)filterType; // Write filter type byte

      // Prepare buffers for the current row processing
      var currentRowRaw = new byte[stride];
      Array.Copy(rawPixelData, inputOffset, currentRowRaw, 0, stride);
      var currentRowFiltered = new byte[stride]; // Temporary buffer for filtered output

      // Perform filtering into the temporary buffer
      FilterRow(filterType, currentRowRaw, currentRowFiltered, prevRow, bytesPerPixel);

      // Copy the filtered data into the final output buffer
      if (outputOffset + stride > filteredData.Length)
        throw new IndexOutOfRangeException($"Attempting to write past end of filtered data buffer at row {y}."); // Safety check

      Array.Copy(currentRowFiltered, 0, filteredData, outputOffset, stride);

      // Update prevRow with the current *raw* data for the next iteration
      Array.Copy(currentRowRaw, 0, prevRow, 0, stride);

      inputOffset += stride;
      outputOffset += stride;
    }

    return filteredData;
  }


  // --- Row Filtering/Unfiltering Implementation ---
  internal static void UnfilterRow(byte filterType, byte[] filteredRow, byte[] reconstructedRow, byte[] prevRow, int bpp) {
    var stride = filteredRow.Length;
    ArgumentOutOfRangeException.ThrowIfNotEqual(reconstructedRow.Length,stride);
    ArgumentOutOfRangeException.ThrowIfNotEqual(prevRow.Length,stride);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bpp);
    
    switch ((RowFilterType)filterType) {
      case RowFilterType.None:
        Array.Copy(filteredRow, reconstructedRow, stride);
        break;
      case RowFilterType.Sub:
        for (var x = 0; x < stride; ++x) {
          var rA = x >= bpp ? reconstructedRow[x - bpp] : (byte)0; 
          reconstructedRow[x] = (byte)(filteredRow[x] + rA);
        }
        break;
      case RowFilterType.Up:
        for (var x = 0; x < stride; ++x) {
          var rB = prevRow[x]; 
          reconstructedRow[x] = (byte)(filteredRow[x] + rB);
        }
        break;
      case RowFilterType.Average:
        for (var x = 0; x < stride; ++x) {
          var rA = x >= bpp ? reconstructedRow[x - bpp] : (byte)0; 
          var rB = prevRow[x]; 
          reconstructedRow[x] = (byte)(filteredRow[x] + (byte)((rA + rB) / 2));
        }
        break;
      case RowFilterType.Paeth:
        for (var x = 0; x < stride; ++x) {
          var rA = x >= bpp ? reconstructedRow[x - bpp] : (byte)0; 
          var rB = prevRow[x]; 
          var rC = x >= bpp ? prevRow[x - bpp] : (byte)0; 
          var p = PaethPredictor(rA, rB, rC); 
          reconstructedRow[x] = (byte)(filteredRow[x] + p);
        }
        break;
      default:
        // Maybe treat as Filter=0 or throw? Throwing is safer.
        throw new ArgumentException($"Invalid PNG filter type encountered during unfiltering: {filterType}");
    }
  }

  internal static void FilterRow(RowFilterType filterType, byte[] rawRow, byte[] filteredRow, byte[] prevRow, int bpp) {
    var stride = rawRow.Length;
    ArgumentOutOfRangeException.ThrowIfNotEqual(filteredRow.Length, stride);
    ArgumentOutOfRangeException.ThrowIfNotEqual(prevRow.Length, stride);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bpp);
    
    switch (filterType) {
      case RowFilterType.None:
        Array.Copy(rawRow, filteredRow, stride);
        break;
      case RowFilterType.Sub:
        for (var x = 0; x < stride; ++x) {
          var rA = x >= bpp ? rawRow[x - bpp] : (byte)0; 
          filteredRow[x] = (byte)(rawRow[x] - rA);
        }
        break;
      case RowFilterType.Up:
        for (var x = 0; x < stride; ++x) {
          var rB = prevRow[x]; 
          filteredRow[x] = (byte)(rawRow[x] - rB);
        }
        break;
      case RowFilterType.Average:
        for (var x = 0; x < stride; ++x) {
          var rA = x >= bpp ? rawRow[x - bpp] : (byte)0; 
          var rB = prevRow[x]; 
          filteredRow[x] = (byte)(rawRow[x] - (byte)((rA + rB) / 2));
        }
        break;
      case RowFilterType.Paeth:
        for (var x = 0; x < stride; ++x) {
          var rA = x >= bpp ? rawRow[x - bpp] : (byte)0; 
          var rB = prevRow[x]; 
          var rC = x >= bpp ? prevRow[x - bpp] : (byte)0; 
          var p = PaethPredictor(rA, rB, rC); 
          filteredRow[x] = (byte)(rawRow[x] - p);
        }
        break;
      default:
        // This should not happen if called with valid filter types (0-4)
        throw new ArgumentException($"Invalid PNG filter type requested for filtering: {filterType}");
    }
  }

  // Paeth predictor function (as defined in PNG spec)
  internal static byte PaethPredictor(byte a, byte b, byte c) { // a = left, b = above, c = upper-left
    var p = a + b - c; // Initial estimate
    var pa = Math.Abs(p - a);// Distances
    var pb = Math.Abs(p - b);
    var pc = Math.Abs(p - c);

    // Return nearest of a, b, c predictors
    if (pa <= pb && pa <= pc)
      return a;
    if (pb <= pc)
      return b;
    return c;
  }

  /// <summary>
  /// Reconstructs the PNG byte stream from chunks, replacing original IDAT chunks
  /// with a single new IDAT chunk. Can optionally replace the IHDR chunk.
  /// </summary>
  /// <param name="originalChunks">The list of chunks read from the original PNG.</param>
  /// <param name="newIdatChunk">The new IDAT chunk containing the optimized image data.</param>
  /// <param name="newIhdrChunk">Optional: A new IHDR chunk to use instead of the original (e.g., to change interlace flag).</param>
  /// <returns>A byte array representing the complete new PNG file.</returns>
  public static byte[] RebuildPng(List<PngChunk> originalChunks, PngChunk newIdatChunk, PngChunk? newIhdrChunk = null) {
    ArgumentNullException.ThrowIfNull(originalChunks);
    ArgumentOutOfRangeException.ThrowIfZero(originalChunks.Count);
    
    using MemoryStream ms = new();
    // Use ASCII for type writing, leaveOpen is good practice though MemoryStream owns it here
    using BinaryWriter writer = new(ms, Encoding.ASCII, leaveOpen: true);

    // Write PNG signature
    writer.Write(PngParser.PngSignature);

    var ihdrWritten = false;
    var idatWritten = false;
    var iendWritten = false;

    // Iterate through original chunks, replacing IHDR and IDAT as needed
    foreach (var chunk in originalChunks) {
      switch (chunk.Type) {
        case "IHDR":
          // Write the new IHDR if provided, otherwise write the original
          WriteChunk(writer, newIhdrChunk ?? chunk);
          ihdrWritten = true;
          break;
        case "IDAT": {
          // Write the single new IDAT chunk *only once* when the first original IDAT is encountered
          if (!idatWritten) {
            WriteChunk(writer, newIdatChunk);
            idatWritten = true;
          }
          // Skip writing all original IDAT chunks
          break;
        }
        case "IEND":
          // Ensure IEND is written last, handle cases where it might appear early
          // We'll handle final IEND writing after the loop to guarantee it's last.
          continue; // Skip writing IEND in this loop
        default:
          // Write all other chunks as they are
          WriteChunk(writer, chunk);
          break;
      }
    }

    // --- Final Checks and IEND ---

    // If IHDR wasn't found (corrupt input?), add the new one if available. Very unlikely.
    if (!ihdrWritten && newIhdrChunk.HasValue) {
      Console.WriteLine("Warning: Original IHDR missing, writing provided new IHDR.");
      WriteChunk(writer, newIhdrChunk.Value);
    }

    // If no IDAT was encountered (corrupt input?), write the new one now.
    if (!idatWritten) {
      Console.WriteLine("Warning: Original IDAT missing, writing new IDAT before IEND.");
      WriteChunk(writer, newIdatChunk);
    }
    
    // Ensure IEND is the absolute last chunk
    PngChunk? finalIend = originalChunks.Find(c => c.Type == "IEND");
    if (finalIend.HasValue) {
      WriteChunk(writer, finalIend.Value);
      iendWritten = true;
    } else {
      // Create and write a mandatory empty IEND chunk if none was found
      Console.WriteLine("Warning: Original IEND chunk missing; creating and appending a new empty IEND.");
      WriteChunk(writer, PngChunk.Create("IEND", []));
      iendWritten = true; // Mark as written
    }

    return ms.ToArray();
  }

  private static void WriteChunk(BinaryWriter writer, PngChunk chunk) {
    PngParser.WriteUInt32BigEndian(writer, chunk.Length);
    var typeBytes = Encoding.ASCII.GetBytes(chunk.Type);
    writer.Write(typeBytes);
    if (chunk.Length > 0)
      writer.Write(chunk.Data);
    
    // Calculate and write CRC for the chunk
    var crc = PngParser.CalculateCrc(typeBytes, chunk.Data);
    PngParser.WriteUInt32BigEndian(writer, crc);
  }
}