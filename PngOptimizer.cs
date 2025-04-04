// PngOptimizer.cs

using System;
using System.Buffers; // For ArrayPool potentially later if optimizing heavily
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920); // Use ArrayPool for buffer
        try {
          int bytesRead;
          while ((bytesRead = decompressor.Read(buffer, 0, buffer.Length)) > 0) {
            decompressedStream.Write(buffer, 0, bytesRead);
          }
        } finally {
          ArrayPool<byte>.Shared.Return(buffer); // Ensure buffer is returned
        }
      } // Decompressor disposed here

      byte[] result = decompressedStream.ToArray();

      // Sanity check: If input wasn't empty but output is, decompression likely failed silently or data was invalid.
      if (compressedData.Length > 0 && result.Length == 0) {
        throw new InvalidDataException("ZLib decompression resulted in zero bytes, input data may be corrupt or not a valid ZLib/Deflate stream.");
      }

      // Console.WriteLine($"DEBUG: Decompressed to {result.Length} bytes.");
      return result;
    } catch (InvalidDataException ex) // Catch specific ZLib/Deflate errors
      {
      Console.Error.WriteLine($"Error: ZLib decompression failed. Input data is likely corrupt or not valid ZLib/Deflate. Details: {ex.Message}");
      throw new ArgumentException("Failed to decompress PNG image data (IDAT). File might be corrupt.", nameof(compressedData), ex);
    } catch (Exception ex) // Catch other potential errors during stream operations
      {
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
    using (ZLibStream compressor = new(outputStream, level, leaveOpen: true)) {
      inputStream.CopyTo(compressor);
      // The compressor must be flushed/closed to finalize the stream.
      // The using statement handles disposal, which includes flushing.
    }
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
    int samplesPerPixel = ihdr.ColorType switch {
      0 => 1, // Grayscale
      2 => 3, // Truecolor (R, G, B)
      3 => 1, // Indexed-color (palette index) - filter operates on index bytes
      4 => 2, // Grayscale + Alpha
      6 => 4, // Truecolor + Alpha (R, G, B, A)
      _ => throw new NotSupportedException($"Unsupported PNG color type: {ihdr.ColorType}")
    };

    // PNG filters operate on bytes. Calculate how many bytes represent one pixel's worth of data.
    if (ihdr.BitDepth < 8) {
      // Sub-byte pixels are packed. The filter offset ('bpp') should be the number
      // of bytes needed to store one full pixel, typically rounded up.
      // E.g., 4-bit indexed (1 sample/pixel): (1 * 4 + 7) / 8 = 1 byte/pixel.
      // E.g., 2-bit grayscale (1 sample/pixel): (1 * 2 + 7) / 8 = 1 byte/pixel.
      // E.g., 1-bit grayscale (1 sample/pixel): (1 * 1 + 7) / 8 = 1 byte/pixel.
      int bitsPerPixel = samplesPerPixel * ihdr.BitDepth;
      return (bitsPerPixel + 7) / 8; // Ceiling division
    }
    if (ihdr.BitDepth == 8) {
      return samplesPerPixel; // Each sample is exactly one byte
    }
    if (ihdr.BitDepth == 16) {
      return samplesPerPixel * 2; // Each sample is two bytes
    }

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
    if (bytesPerPixel <= 0)
      throw new ArgumentOutOfRangeException(nameof(bytesPerPixel), "Bytes per pixel must be positive.");
    if (width < 0 || height < 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Width and height cannot be negative.");
    if (width == 0 || height == 0)
      return []; // Image has no pixels

    // Calculate stride (bytes per raw scanline) carefully to avoid overflow
    long strideL = (long)width * bytesPerPixel;
    if (strideL > int.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(width), $"Calculated stride ({strideL}) exceeds maximum integer size.");
    int stride = (int)strideL;

    // Expected length includes 1 filter byte per row + stride data bytes
    long expectedMinLengthL = (long)height * (1 + stride);
    if (expectedMinLengthL > int.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(height), "Expected data length exceeds maximum integer size.");
    int expectedMinLength = (int)expectedMinLengthL;

    if (decompressedData.Length < expectedMinLength)
      throw new ArgumentException($"Decompressed data length ({decompressedData.Length}) is less than minimum expected ({expectedMinLength}) for {width}x{height}x{bytesPerPixel}bpp (stride {stride}). Data is likely truncated or corrupt.");
    if (decompressedData.Length > expectedMinLength + height * 8) // Allow some reasonable padding/slack per row
      Console.WriteLine($"Warning: Decompressed data length ({decompressedData.Length}) is significantly larger than expected ({expectedMinLength}). Extra data may be ignored.");

    byte[] rawData = new byte[height * stride];
    byte[] prevRow = new byte[stride]; // Store the *reconstructed* previous row

    int inputOffset = 0;
    int outputOffset = 0;

    for (int y = 0; y < height; y++) {
      if (inputOffset >= decompressedData.Length)
        throw new ArgumentException($"Reached end of decompressed data prematurely at row {y}. Expected more data.");

      byte filterType = decompressedData[inputOffset++];

      // Ensure we don't read past the end of the decompressed data for the current row's filtered data
      int remainingBytes = decompressedData.Length - inputOffset;
      if (remainingBytes < stride)
        throw new ArgumentException($"Insufficient data for row {y}. Needed {stride} bytes for scanline, only {remainingBytes} available after filter byte. Input offset: {inputOffset}.");

      // Create temporary buffers for the current row's processing
      byte[] currentRowFiltered = new byte[stride];
      Array.Copy(decompressedData, inputOffset, currentRowFiltered, 0, stride);

      byte[] currentRowReconstructed = new byte[stride]; // Buffer to hold reconstructed data for this row

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
  public static byte[] ApplyFilter(byte[] rawPixelData, int width, int height, int bytesPerPixel, byte filterType) {
    ArgumentNullException.ThrowIfNull(rawPixelData);
    if (bytesPerPixel <= 0)
      throw new ArgumentOutOfRangeException(nameof(bytesPerPixel), "Bytes per pixel must be positive.");
    if (width < 0 || height < 0)
      throw new ArgumentOutOfRangeException(nameof(width), "Width and height cannot be negative.");
    if (width == 0 || height == 0)
      return []; // No pixels to filter

    long strideL = (long)width * bytesPerPixel;
    if (strideL > int.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(width), "Calculated stride exceeds maximum integer size.");
    int stride = (int)strideL;

    long expectedRawLengthL = (long)height * stride;
    if (expectedRawLengthL > int.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(height), "Expected raw data length exceeds maximum integer size.");
    int expectedRawLength = (int)expectedRawLengthL;

    if (rawPixelData.Length < expectedRawLength)
      throw new ArgumentException($"Raw pixel data length ({rawPixelData.Length}) is less than expected ({expectedRawLength}) for {width}x{height}x{bytesPerPixel}bpp (stride {stride}).");

    long filteredLengthL = (long)height * (1 + stride);
    if (filteredLengthL > int.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(height), "Filtered data length exceeds maximum integer size.");
    byte[] filteredData = new byte[(int)filteredLengthL]; // 1 filter byte + data per row

    byte[] prevRow = new byte[stride]; // Previous *raw* row data

    int inputOffset = 0;
    int outputOffset = 0;

    for (int y = 0; y < height; y++) {
      if (outputOffset >= filteredData.Length)
        throw new IndexOutOfRangeException("Output buffer overflow during filtering."); // Safety check

      filteredData[outputOffset++] = filterType; // Write filter type byte

      // Prepare buffers for the current row processing
      byte[] currentRowRaw = new byte[stride];
      Array.Copy(rawPixelData, inputOffset, currentRowRaw, 0, stride);

      byte[] currentRowFiltered = new byte[stride]; // Temporary buffer for filtered output

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

  private static void UnfilterRow(byte filterType, byte[] filteredRow, byte[] reconstructedRow, byte[] prevRow, int bpp) {
    int stride = filteredRow.Length;
    if (reconstructedRow.Length != stride || prevRow.Length != stride)
      throw new ArgumentException("Buffer lengths must match stride in UnfilterRow.");
    if (bpp <= 0) // Defend against invalid bpp
      throw new ArgumentOutOfRangeException(nameof(bpp), "Bytes per pixel must be positive for unfiltering.");

    switch (filterType) {
      case 0: // None
        Array.Copy(filteredRow, reconstructedRow, stride);
        break;
      case 1: // Sub
        for (int x = 0; x < stride; x++) {
          // Recon(x) = Filt(x) + Recon(x-bpp)
          byte reconA = (x >= bpp) ? reconstructedRow[x - bpp] : (byte)0;
          reconstructedRow[x] = (byte)(filteredRow[x] + reconA);
        }
        break;
      case 2: // Up
        for (int x = 0; x < stride; x++) {
          // Recon(x) = Filt(x) + Recon(Prior, x)
          byte reconB = prevRow[x];
          reconstructedRow[x] = (byte)(filteredRow[x] + reconB);
        }
        break;
      case 3: // Average
        for (int x = 0; x < stride; x++) {
          // Recon(x) = Filt(x) + floor((Recon(x-bpp) + Recon(Prior, x)) / 2)
          byte reconA = (x >= bpp) ? reconstructedRow[x - bpp] : (byte)0;
          byte reconB = prevRow[x];
          reconstructedRow[x] = (byte)(filteredRow[x] + (byte)((reconA + reconB) / 2));
        }
        break;
      case 4: // Paeth
        for (int x = 0; x < stride; x++) {
          // Recon(x) = Filt(x) + PaethPredictor(Recon(x-bpp), Recon(Prior, x), Recon(Prior, x-bpp))
          byte reconA = (x >= bpp) ? reconstructedRow[x - bpp] : (byte)0;
          byte reconB = prevRow[x];
          byte reconC = (x >= bpp) ? prevRow[x - bpp] : (byte)0;
          byte paethPredicted = PaethPredictor(reconA, reconB, reconC);
          reconstructedRow[x] = (byte)(filteredRow[x] + paethPredicted);
        }
        break;
      default:
        // Maybe treat as Filter=0 or throw? Throwing is safer.
        throw new ArgumentException($"Invalid PNG filter type encountered during unfiltering: {filterType}");
    }
  }

  private static void FilterRow(byte filterType, byte[] rawRow, byte[] filteredRow, byte[] prevRow, int bpp) {
    int stride = rawRow.Length;
    if (filteredRow.Length != stride || prevRow.Length != stride)
      throw new ArgumentException("Buffer lengths must match stride in FilterRow.");
    if (bpp <= 0) // Defend against invalid bpp
      throw new ArgumentOutOfRangeException(nameof(bpp), "Bytes per pixel must be positive for filtering.");


    switch (filterType) {
      case 0: // None
        Array.Copy(rawRow, filteredRow, stride);
        break;
      case 1: // Sub
        for (int x = 0; x < stride; x++) {
          // Filt(x) = Orig(x) - Orig(x-bpp)
          byte rawA = (x >= bpp) ? rawRow[x - bpp] : (byte)0;
          filteredRow[x] = (byte)(rawRow[x] - rawA);
        }
        break;
      case 2: // Up
        for (int x = 0; x < stride; x++) {
          // Filt(x) = Orig(x) - Orig(Prior, x)
          byte rawB = prevRow[x];
          filteredRow[x] = (byte)(rawRow[x] - rawB);
        }
        break;
      case 3: // Average
        for (int x = 0; x < stride; x++) {
          // Filt(x) = Orig(x) - floor((Orig(x-bpp) + Orig(Prior, x)) / 2)
          byte rawA = (x >= bpp) ? rawRow[x - bpp] : (byte)0;
          byte rawB = prevRow[x];
          filteredRow[x] = (byte)(rawRow[x] - (byte)((rawA + rawB) / 2));
        }
        break;
      case 4: // Paeth
        for (int x = 0; x < stride; x++) {
          // Filt(x) = Orig(x) - PaethPredictor(Orig(x-bpp), Orig(Prior, x), Orig(Prior, x-bpp))
          byte rawA = (x >= bpp) ? rawRow[x - bpp] : (byte)0;
          byte rawB = prevRow[x];
          byte rawC = (x >= bpp) ? prevRow[x - bpp] : (byte)0;
          byte paethPredicted = PaethPredictor(rawA, rawB, rawC);
          filteredRow[x] = (byte)(rawRow[x] - paethPredicted);
        }
        break;
      default:
        // This should not happen if called with valid filter types (0-4)
        throw new ArgumentException($"Invalid PNG filter type requested for filtering: {filterType}");
    }
  }


  // Paeth predictor function (as defined in PNG spec)
  private static byte PaethPredictor(byte a, byte b, byte c) // a = left, b = above, c = upper-left
  {
    int p = a + b - c; // Initial estimate
    int pa = Math.Abs(p - a); // Distances
    int pb = Math.Abs(p - b);
    int pc = Math.Abs(p - c);

    // Return nearest of a, b, c predictors
    if (pa <= pb && pa <= pc)
      return a;
    if (pb <= pc)
      return b;
    return c;
  }

  /// <summary>
  /// Reconstructs the PNG byte stream from chunks, replacing original IDAT chunks
  /// with a single new IDAT chunk.
  /// </summary>
  /// <param name="originalChunks">The list of chunks read from the original PNG.</param>
  /// <param name="newIdatChunk">The new IDAT chunk containing the optimized image data.</param>
  /// <returns>A byte array representing the complete new PNG file.</returns>
  public static byte[] RebuildPng(List<PngChunk> originalChunks, PngChunk newIdatChunk) {
    ArgumentNullException.ThrowIfNull(originalChunks);
    if (originalChunks.Count == 0)
      throw new ArgumentException("Original chunk list cannot be empty.", nameof(originalChunks));

    using MemoryStream ms = new();
    // Use ASCII for type writing, leaveOpen is good practice though MemoryStream owns it here
    using BinaryWriter writer = new(ms, Encoding.ASCII, leaveOpen: true);

    // Write PNG signature
    writer.Write(PngParser.PngSignature);

    // Find the position of the first IDAT chunk (or where it *should* go, typically before IEND)
    int firstIdatIndex = originalChunks.FindIndex(c => c.Type == "IDAT");
    int insertPosition = (firstIdatIndex != -1) ? firstIdatIndex : originalChunks.FindIndex(c => c.Type == "IEND");
    if (insertPosition == -1) // If no IEND found either, append (shouldn't happen in valid PNG)
    {
      insertPosition = originalChunks.Count;
      Console.WriteLine("Warning: No IDAT or IEND chunk found in original list to determine insertion point.");
    }


    // Write all chunks *before* the insertion point
    for (int i = 0; i < insertPosition; i++) {
      // Skip original IDATs if any appeared before the calculated insertion point (unlikely but possible)
      if (originalChunks[i].Type != "IDAT") {
        WriteChunk(writer, originalChunks[i]);
      }
    }

    // Write the new, single IDAT chunk
    WriteChunk(writer, newIdatChunk);

    bool iendWritten = false;
    // Write all chunks *after* the original IDAT sequence, skipping original IDATs
    for (int i = insertPosition; i < originalChunks.Count; i++) {
      PngChunk currentChunk = originalChunks[i];
      if (currentChunk.Type != "IDAT") // Skip all original IDAT chunks
      {
        WriteChunk(writer, currentChunk);
        if (currentChunk.Type == "IEND") {
          iendWritten = true;
        }
      }
    }

    // Ensure an IEND chunk is present at the very end if it wasn't written
    if (!iendWritten) {
      // Check if an IEND existed but wasn't last (should have been caught by loop)
      PngChunk? iend = originalChunks.Find(c => c.Type == "IEND");
      if (iend.HasValue) {
        // This implies the original chunk order was unusual, but we'll append it anyway
        Console.WriteLine("Warning: IEND chunk was present but not written in the main loop; appending.");
        WriteChunk(writer, iend.Value);
      } else {
        // If no IEND chunk was found in the original list, create a mandatory empty one.
        Console.WriteLine("Warning: Original IEND chunk missing; creating and appending a new empty IEND.");
        WriteChunk(writer, PngChunk.Create("IEND", []));
      }
    }

    return ms.ToArray();
  }

  // Helper to check if the file ends correctly (mostly for final validation)
  private static bool EndsWithIend(byte[] data) {
    if (data.Length < 12)
      return false; // Min size: 0 length + "IEND" + 4 CRC bytes
                    // Check last 8 bytes for Type ("IEND") and 4 CRC bytes (value doesn't matter here)
    return data[^8] == 'I' && data[^7] == 'E' && data[^6] == 'N' && data[^5] == 'D';
  }

  // Writes a single PNG chunk (Length, Type, Data, CRC) to the writer
  private static void WriteChunk(BinaryWriter writer, PngChunk chunk) {
    // Write Length (Big-Endian)
    PngParser.WriteUInt32BigEndian(writer, chunk.Length);

    // Write Type (ASCII Bytes)
    byte[] typeBytes = Encoding.ASCII.GetBytes(chunk.Type);
    writer.Write(typeBytes);

    // Write Data (if any)
    if (chunk.Length > 0) {
      writer.Write(chunk.Data);
    }

    // Calculate and Write CRC (Big-Endian) - always recalculate for safety
    uint crc = PngParser.CalculateCrc(typeBytes, chunk.Data);
    PngParser.WriteUInt32BigEndian(writer, crc);
  }
}