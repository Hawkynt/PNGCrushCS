/// <summary>
/// Provides functionality to de-interlace Adam7 PNG data.
/// </summary>
internal static class Adam7 {
  // Adam7 pass parameters: starting row, starting col, row increment, col increment
  private static readonly int[] PassStartRow = [0, 0, 4, 0, 2, 0, 1];
  private static readonly int[] PassStartCol = [0, 4, 0, 2, 0, 1, 0];
  private static readonly int[] PassRowInc = [8, 8, 8, 4, 4, 2, 2];
  private static readonly int[] PassColInc = [8, 8, 4, 4, 2, 2, 1];

  /// <summary>
  /// De-interlaces Adam7 encoded PNG data and reconstructs the raw pixel buffer.
  /// This method also handles the unfiltering process internally as it processes pass rows.
  /// </summary>
  /// <param name="decompressedData">The raw byte stream from the concatenated IDAT chunks.</param>
  /// <param name="width">Image width.</param>
  /// <param name="height">Image height.</param>
  /// <param name="bytesPerPixel">Bytes per pixel.</param>
  /// <returns>Raw, de-interlaced pixel data (non-interlaced scanline order).</returns>
  /// <exception cref="ArgumentException">If data is insufficient or corrupt.</exception>
  public static byte[] Deinterlace(byte[] decompressedData, int width, int height, int bytesPerPixel) {
    ArgumentNullException.ThrowIfNull(decompressedData);
    if (width <= 0 || height <= 0)
      return []; // No pixels to deinterlace
    
    if (bytesPerPixel <= 0)
      throw new ArgumentOutOfRangeException(nameof(bytesPerPixel));

    var rawData = new byte[(long)width * height * bytesPerPixel];
    var inputOffset = 0;

    // Process each of the 7 Adam7 passes
    for (var pass = 0; pass < 7; pass++) {
      var startRow = PassStartRow[pass];
      var startCol = PassStartCol[pass];
      var rowInc = PassRowInc[pass];
      var colInc = PassColInc[pass];

      // Calculate the dimensions of the sub-image for this pass
      // Number of pixels horizontally in this pass's rows
      var passWidth = (width - startCol + colInc - 1) / colInc;
      // Number of pixels vertically (number of rows) in this pass
      var passHeight = (height - startRow + rowInc - 1) / rowInc;

      if (passWidth == 0 || passHeight == 0) {
        continue; // Skip passes that don't contain any pixels for this image size
      }

      // Calculate the stride (bytes per row) for *this pass*
      var passStride = passWidth * bytesPerPixel;
      if (passStride <= 0)
        continue; // Skip empty passes

      var prevPassRowReconstructed = new byte[passStride]; // Previous row *within this pass*

      // Process each row within the current pass
      for (var passY = 0; passY < passHeight; passY++) {
        // Check if enough data remains for filter byte + passStride
        if (inputOffset + 1 + passStride > decompressedData.Length) {
          throw new ArgumentException($"Insufficient data in Adam7 stream. Reached end prematurely in pass {pass}, row {passY}. Needed {1 + passStride} bytes, offset {inputOffset}, total {decompressedData.Length}.");
        }

        var filterType = decompressedData[inputOffset++];
        var currentPassRowFiltered = new byte[passStride];
        Array.Copy(decompressedData, inputOffset, currentPassRowFiltered, 0, passStride);
        inputOffset += passStride;

        var currentPassRowReconstructed = new byte[passStride]; // Temp buffer for this pass row

        // Unfilter the row *relative to the previous row in this pass*
        UnfilterRowInternal(filterType, currentPassRowFiltered, currentPassRowReconstructed, prevPassRowReconstructed, bytesPerPixel);

        // --- Place the reconstructed pixels into the final rawData buffer ---
        var actualY = startRow + passY * rowInc; // Row in the final image

        for (var passX = 0; passX < passWidth; passX++) {
          var actualX = startCol + passX * colInc; // Column in the final image

          // Calculate the starting index in the final rawData buffer for this pixel
          var targetOffset = (long)actualY * width * bytesPerPixel + (long)actualX * bytesPerPixel;
          // Calculate the starting index in the reconstructed pass row buffer
          var sourceOffset = passX * bytesPerPixel;

          // Copy the pixel (potentially multi-byte)
          if (targetOffset + bytesPerPixel > rawData.Length || sourceOffset + bytesPerPixel > currentPassRowReconstructed.Length) {
            throw new IndexOutOfRangeException($"Adam7 Deinterlace Error: Attempting to write pixel data out of bounds. Pass {pass}, PassXY({passX},{passY}), ActualXY({actualX},{actualY}), TargetOffset={targetOffset}, SourceOffset={sourceOffset}, BPP={bytesPerPixel}");
          }

          Array.Copy(currentPassRowReconstructed, sourceOffset, rawData, targetOffset, bytesPerPixel);
        }

        // Update the previous row buffer for the next iteration *of this pass*
        Array.Copy(currentPassRowReconstructed, prevPassRowReconstructed, passStride);
      }
    }

    // Optional: Check if we consumed roughly the expected amount of data
    // This is hard to predict exactly due to filtering/compression, but major discrepancies are bad.
    // if (inputOffset < decompressedData.Length - 100) Console.WriteLine($"Warning: Adam7 deinterlace finished with {decompressedData.Length - inputOffset} bytes remaining in input buffer.");

    return rawData;

    // The final output buffer, laid out in standard scanline order
  }

  // --- Internal Unfilter Row Logic ---
  // Duplicated/adapted from PngOptimizer to avoid circular dependency or forcing public access
  // Operates identically to PngOptimizer.UnfilterRow

  private static void UnfilterRowInternal(byte filterType, byte[] filteredRow, byte[] reconstructedRow, byte[] prevRow, int bpp) {
    var stride = filteredRow.Length;
    if (reconstructedRow.Length != stride || prevRow.Length != stride)
      throw new ArgumentException("Buffer lengths must match stride in UnfilterRowInternal.");
    if (bpp <= 0)
      throw new ArgumentOutOfRangeException(nameof(bpp), "Bytes per pixel must be positive for unfiltering.");

    switch (filterType) {
      case 0: // None
        Array.Copy(filteredRow, reconstructedRow, stride);
        break;
      case 1: // Sub
        for (var x = 0; x < stride; x++) {
          var reconA = (x >= bpp) ? reconstructedRow[x - bpp] : (byte)0;
          reconstructedRow[x] = (byte)(filteredRow[x] + reconA);
        }
        break;
      case 2: // Up
        for (var x = 0; x < stride; x++) {
          var reconB = prevRow[x];
          reconstructedRow[x] = (byte)(filteredRow[x] + reconB);
        }
        break;
      case 3: // Average
        for (var x = 0; x < stride; x++) {
          var reconA = (x >= bpp) ? reconstructedRow[x - bpp] : (byte)0;
          var reconB = prevRow[x];
          reconstructedRow[x] = (byte)(filteredRow[x] + (byte)((reconA + reconB) / 2));
        }
        break;
      case 4: // Paeth
        for (var x = 0; x < stride; x++) {
          var reconA = (x >= bpp) ? reconstructedRow[x - bpp] : (byte)0;
          var reconB = prevRow[x];
          var reconC = (x >= bpp) ? prevRow[x - bpp] : (byte)0;
          var paethPredicted = PaethPredictorInternal(reconA, reconB, reconC);
          reconstructedRow[x] = (byte)(filteredRow[x] + paethPredicted);
        }
        break;
      default:
        throw new ArgumentException($"Invalid PNG filter type encountered during unfiltering: {filterType}");
    }
  }

  private static byte PaethPredictorInternal(byte a, byte b, byte c) // a = left, b = above, c = upper-left
  {
    var p = a + b - c;
    var pa = Math.Abs(p - a);
    var pb = Math.Abs(p - b);
    var pc = Math.Abs(p - c);
    if (pa <= pb && pa <= pc)
      return a;
    if (pb <= pc)
      return b;
    return c;
  }
}