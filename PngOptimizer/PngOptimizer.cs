using System.Buffers;
using System.IO.Hashing;
using System.Linq;

namespace PngOptimizer;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO.Compression;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;

using System;

/// <summary>Main class for PNG optimization</summary>
public sealed class PngOptimizer {
  private const byte COMPRESSION_METHOD_DEFLATE = (byte)0;
  private const byte FILTER_METHOD_ADAPTIVE = (byte)0;
  private readonly int _imageWidth;
  private readonly int _imageHeight;
  private readonly PngOptimizationOptions _options;
  private readonly ImageStats _imageStats;
  private readonly byte[] _imagePixelData;

  /// <summary>Constructor for the PNG optimizer</summary>
  public PngOptimizer(Bitmap image, PngOptimizationOptions? options = null) {
    this._options = options ?? new();

    // Speichere die Bildgröße einmal
    this._imageWidth = image.Width;
    this._imageHeight = image.Height;

    // Analysiere das Bild und extrahiere alle Pixeldaten direkt am Anfang
    (this._imageStats, this._imagePixelData) = this.ExtractImageData(image);
  }

  /// <summary>Extracts image data and analyzes it in a single pass</summary>
  private unsafe (ImageStats, byte[]) ExtractImageData(Bitmap image) {
    var uniqueColors = new HashSet<uint>();
    var hasAlpha = false;
    var isGrayscale = true;

    // Berechne Größe des Pixel-Arrays
    var width = image.Width;
    var height = image.Height;
    const int BYTES_PER_PIXEL=4;// 4 Bytes pro Pixel (BGRA)
    var pixelDataSize = width * height * BYTES_PER_PIXEL; 
    var result = new byte[pixelDataSize];

    // Lock the bitmap and extract all pixel data at once
    var bmpData = image.LockBits(
      new Rectangle(0, 0, width, height),
      ImageLockMode.ReadOnly,
      PixelFormat.Format32bppArgb);

    try {
      var stride = bmpData.Stride;
      var scan0 = bmpData.Scan0;

      fixed (byte* resultPtr = result) {
        var rowOffset = (byte*)(void*)scan0;
        var destPtr = resultPtr;

        for (var y = 0; y < height; ++y,rowOffset+=stride) {
          var currentRowOffset = rowOffset;
          for (var x = 0; x < width; ++x, currentRowOffset += BYTES_PER_PIXEL, destPtr += BYTES_PER_PIXEL) {

            var pixelValue = *(uint*)currentRowOffset;
            *(uint*)destPtr = pixelValue;

            var b = (byte)pixelValue;
            var g = (byte)(pixelValue >> 8);
            var r = (byte)(pixelValue >> 16);
            var a = (byte)(pixelValue >> 24);

            uniqueColors.Add(pixelValue);
            hasAlpha |= a < 255;
            isGrayscale &= r == g && g == b;
          }
        }
      }
    } finally {
      image.UnlockBits(bmpData);
    }

    var stats = new ImageStats(
      uniqueColors.Count,
      hasAlpha,
      isGrayscale
    );

    Console.WriteLine($"Image analysis: {uniqueColors.Count} unique colors, " +
                     $"Alpha: {hasAlpha}, Grayscale: {isGrayscale}");

    return (stats, result);
  }

  /// <summary>Optimize the PNG image and return the best result</summary>
  public async ValueTask<OptimizationResult> OptimizeAsync() {
    var stopwatch = Stopwatch.StartNew();
    Console.WriteLine("Starting PNG optimization...");

    // Generate all combinations to try
    var combinations = this.GenerateOptimizationCombinations();
    Console.WriteLine($"Testing {combinations.Count} optimization combinations");

    // Run all combinations in parallel
    // TODO: create all tasks first, then inside the task mess with the semaphore
    using var semaphore = new SemaphoreSlim(this._options.MaxParallelTasks);
    var tasks = new List<Task<OptimizationResult>>(combinations.Count);

    foreach (var combo in combinations) {
      await semaphore.WaitAsync();
      tasks.Add(Task.Run(async () => {
        try {
          return await this.TestCombinationAsync(combo);
        } finally {
          semaphore.Release();
        }
      }));
    }

    // Wait for all tasks to complete
    var results = await Task.WhenAll(tasks);

    // Find the best result
    var bestResult = results
      .OrderBy(r => r.CompressedSize)
      .First();

    stopwatch.Stop();
    Console.WriteLine($"Optimization completed in {stopwatch.Elapsed.TotalSeconds:F1} seconds");
    Console.WriteLine($"Best result: {bestResult}");

    return bestResult;
  }

  /// <summary>Generate all combinations of parameters to try based on the options</summary>
  private List<OptimizationCombo> GenerateOptimizationCombinations() {
    
    // Determine color modes to try
    var colorModesToTry = new List<(ColorMode colorMode, int bitDepth)>();

    if (this._options.AutoSelectColorMode) {
      // Add color modes based on image analysis
      var (uniqueColors, hasAlpha, isGrayscale) = this._imageStats;

      if (isGrayscale) {
        if (hasAlpha) {
          colorModesToTry.Add((ColorMode.GrayscaleAlpha, 8));
        } else {
          colorModesToTry.Add((ColorMode.Grayscale, 8));
          // Try lower bit depths if few enough colors
          if (uniqueColors <= 16)
            colorModesToTry.Add((ColorMode.Grayscale, 4));
          if (uniqueColors <= 4)
            colorModesToTry.Add((ColorMode.Grayscale, 2));
          if (uniqueColors <= 2)
            colorModesToTry.Add((ColorMode.Grayscale, 1));
        }
      } else {
        colorModesToTry.Add(hasAlpha ? (ColorMode.RGBA, 8) : (ColorMode.RGB, 8));

        // Try palette if few enough colors
        if (uniqueColors <= this._options.MaxPaletteColors) {
          colorModesToTry.Add((ColorMode.Palette, 8));
          if (uniqueColors <= 16)
            colorModesToTry.Add((ColorMode.Palette, 4));
          if (uniqueColors <= 4)
            colorModesToTry.Add((ColorMode.Palette, 2));
          if (uniqueColors <= 2)
            colorModesToTry.Add((ColorMode.Palette, 1));
        }
      }
    } else {
      // Just use RGB or RGBA by default
      colorModesToTry.Add(this._imageStats.HasAlpha
        ? (ColorMode.RGBA, 8)
        : (ColorMode.RGB, 8));
    }

    // Determine interlace methods to try
    var interlaceMethods = new List<InterlaceMethod> { InterlaceMethod.None };
    if (this._options.TryInterlacing)
      interlaceMethods.Add(InterlaceMethod.Adam7);

    // Build all combinations
    return (
      from colorModeInfo in colorModesToTry
      from interlaceMethod in interlaceMethods
      from filterStrategy in this._options.FilterStrategies
      where colorModeInfo.colorMode != ColorMode.Palette || colorModeInfo.bitDepth >= 8 || filterStrategy == FilterStrategy.SingleFilter
      from deflateMethod in this._options.DeflateMethods
      select new OptimizationCombo(
        colorModeInfo.colorMode,
        colorModeInfo.bitDepth,
        interlaceMethod,
        filterStrategy,
        deflateMethod)
    ).ToList();
  }

  /// <summary>Test a specific combination of parameters</summary>
  private async ValueTask<OptimizationResult> TestCombinationAsync(OptimizationCombo combo) {
    var stopwatch = Stopwatch.StartNew();

    // Convert the image to the right format
    var imageData = this.ConvertPixelDataToByteArray(combo.ColorMode, combo.BitDepth, out var palette);

    // Optimize filters based on the strategy
    FilterType[] filters;
    byte[][] filteredData;

    if (combo.FilterStrategy == FilterStrategy.PartitionOptimized && this._options.TryPartitioning) {
      var partitioner = new ImagePartitioner(
        imageData, 
        this._imageWidth, 
        this._imageHeight,
        GetBytesPerPixel(combo.ColorMode, combo.BitDepth),
        combo.ColorMode == ColorMode.Palette,
        combo.ColorMode is ColorMode.Grayscale or ColorMode.GrayscaleAlpha,
        combo.BitDepth
      );

      (filters, filteredData) = partitioner.OptimizePartitions();
    } else {
      var filterOptimizer = new PngFilterOptimizer(
        this._imageWidth, 
        this._imageHeight, 
        GetBytesPerPixel(combo.ColorMode, combo.BitDepth), 
        combo.ColorMode is ColorMode.Grayscale or ColorMode.GrayscaleAlpha, 
        combo.ColorMode == ColorMode.Palette, 
        combo.BitDepth, 
        imageData
      );

      filters = filterOptimizer.OptimizeFilters(combo.FilterStrategy);
      filteredData = filterOptimizer.ApplyFilters(filters);
    }

    // Count filter transitions
    var filterTransitions = CountFilterTransitions(filteredData);

    // Apply deflate compression
    var bytes = await this.CompressDataAsync(
      filteredData, 
      combo.DeflateMethod,
      combo.InterlaceMethod, 
      combo.ColorMode,
      combo.BitDepth, 
      palette
    );

    stopwatch.Stop();

    return new OptimizationResult {
      ColorMode = combo.ColorMode,
      BitDepth = combo.BitDepth,
      InterlaceMethod = combo.InterlaceMethod,
      FilterStrategy = combo.FilterStrategy,
      DeflateMethod = combo.DeflateMethod,
      CompressedSize = bytes.Length,
      FileContents=bytes,
      FilteredData = filteredData,
      Filters = filters,
      FilterTransitions = filterTransitions,
      ProcessingTime = stopwatch.Elapsed
    };
  }

  /// <summary>Convert pixel data to the appropriate byte array format</summary>
  private byte[][] ConvertPixelDataToByteArray(ColorMode colorMode, int bitDepth, out byte[]? palette) {

    palette = null;
    var width = this._imageWidth;
    var height = this._imageHeight;
    var bytesPerPixel = GetBytesPerPixel(colorMode, bitDepth);

    // Calculate bytes per scanline with proper bit-level math
    var bitsPerPixel = bitDepth * bytesPerPixel;
    var bytesPerScanline = (width * bitsPerPixel + 7) >> 3; // Divide by 8 with ceiling

    // Create result array
    var result = new byte[height][];
    for (var y = 0; y < height; ++y)
      result[y] = new byte[bytesPerScanline];

    // For palette mode, we need to generate a palette
    if (colorMode == ColorMode.Palette) {
      // Benutze die gespeicherten Pixeldaten um eine Palette zu erstellen
      this.QuantizePixelData(width, height, 1 << bitDepth, out palette, result);
    } else {
      // Direct format conversion from pixel data
      var sourceRowOffset = 0;
      var stride = width * 4; /* ARGB */
      for (var y = 0; y < height; ++y,sourceRowOffset+=stride) {
        var scanline = result[y];
        var scanOffset = sourceRowOffset;

        for (var x = 0; x < width; ++x, scanOffset+=4) {
          var b = this._imagePixelData[scanOffset];
          var g = this._imagePixelData[scanOffset + 1];
          var r = this._imagePixelData[scanOffset + 2];
          var a = this._imagePixelData[scanOffset + 3];

          var destIdx = x * bytesPerPixel;

          switch (colorMode) {
            case ColorMode.Grayscale:
              // Convert to grayscale using standard formula
              scanline[destIdx] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
              break;

            case ColorMode.GrayscaleAlpha:
              scanline[destIdx] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
              scanline[destIdx + 1] = a;
              break;

            case ColorMode.RGB:
              scanline[destIdx] = r;
              scanline[destIdx + 1] = g;
              scanline[destIdx + 2] = b;
              break;

            case ColorMode.RGBA:
              scanline[destIdx] = r;
              scanline[destIdx + 1] = g;
              scanline[destIdx + 2] = b;
              scanline[destIdx + 3] = a;
              break;
          }
        }
      }
    }

    return result;
  }

  /// <summary>Quantize the pixel data for palette mode</summary>
  private void QuantizePixelData(int width, int height, int maxColors, out byte[] palette, byte[][] result) {
    // Für eine vollständige Implementierung würde hier ein Quantisierungsalgorithmus verwendet
    // Hier vereinfacht: Sammle die einzigartigen Farben und erstelle eine Palette

    var uniqueColors = new Dictionary<int, int>(); // ColorValue -> PaletteIndex
    palette = new byte[maxColors * 3]; // RGB triplets

    var paletteIndex = 0;

    // Erste Phase: Palette erstellen (maximal maxColors Einträge)
    for (var y = 0; y < height && uniqueColors.Count < maxColors; ++y) {
      for (var x = 0; x < width && uniqueColors.Count < maxColors; ++x) {
        var pixelIndex = (y * width + x) * 4;

        var b = this._imagePixelData[pixelIndex];
        var g = this._imagePixelData[pixelIndex + 1];
        var r = this._imagePixelData[pixelIndex + 2];

        // Ignoriere Alpha für die Palette
        var colorValue = (r << 16) | (g << 8) | b;

        if (!uniqueColors.ContainsKey(colorValue)) {
          // Füge zur Palette hinzu
          palette[paletteIndex * 3] = r;
          palette[paletteIndex * 3 + 1] = g;
          palette[paletteIndex * 3 + 2] = b;

          uniqueColors[colorValue] = paletteIndex++;
        }
      }
    }

    // Zweite Phase: Indizes zuweisen
    var bitDepth = GetBitDepthForColors(maxColors);
    var pixelsPerByte = 8 / bitDepth;

    for (var y = 0; y < height; ++y) {
      var scanline = result[y];

      if (bitDepth == 8) {
        // 8-bit: Ein Index pro Byte
        for (var x = 0; x < width; ++x) {
          var pixelIndex = (y * width + x) * 4;

          var b = this._imagePixelData[pixelIndex];
          var g = this._imagePixelData[pixelIndex + 1];
          var r = this._imagePixelData[pixelIndex + 2];

          var colorValue = (r << 16) | (g << 8) | b;

          // Finde den nächsten ähnlichen Farbindex, falls die exakte Farbe nicht in der Palette ist
          var paletteIdx = this.FindClosestColor(uniqueColors, colorValue, palette);
          scanline[x] = (byte)paletteIdx;
        }
      } else {
        // 1, 2 oder 4 bit: Mehrere Indizes pro Byte packen
        for (var x = 0; x < width; x += pixelsPerByte) {
          byte packed = 0;

          for (var bit = 0; bit < pixelsPerByte && x + bit < width; ++bit) {
            var pixelIndex = (y * width + (x + bit)) * 4;

            var b = this._imagePixelData[pixelIndex];
            var g = this._imagePixelData[pixelIndex + 1];
            var r = this._imagePixelData[pixelIndex + 2];

            var colorValue = (r << 16) | (g << 8) | b;
            var paletteIdx = this.FindClosestColor(uniqueColors, colorValue, palette);

            // Packe die Bits in ein Byte
            var mask = (1 << bitDepth) - 1;
            packed |= (byte)((paletteIdx & mask) << (8 - bitDepth * (bit + 1)));
          }

          scanline[x / pixelsPerByte] = packed;
        }
      }
    }
  }

  /// <summary>Find the closest color in the palette</summary>
  private int FindClosestColor(Dictionary<int, int> paletteMap, int colorValue, byte[] palette) {
    // Schnellster Fall: Exakte Farbe ist in der Palette
    if (paletteMap.TryGetValue(colorValue, out var index))
      return index;

    // Extrahiere RGB Komponenten
    var r = (colorValue >> 16) & 0xFF;
    var g = (colorValue >> 8) & 0xFF;
    var b = colorValue & 0xFF;

    // Suche nach der ähnlichsten Farbe (einfache Implementierung)
    var minDistance = int.MaxValue;
    var closestIndex = 0;

    for (var i = 0; i < paletteMap.Count; i++) {
      var pr = palette[i * 3];
      var pg = palette[i * 3 + 1];
      var pb = palette[i * 3 + 2];

      // Einfache Abstandsmessung im RGB-Raum
      var distance = (pr - r) * (pr - r) + (pg - g) * (pg - g) + (pb - b) * (pb - b);

      if (distance < minDistance) {
        minDistance = distance;
        closestIndex = i;
      }
    }

    return closestIndex;
  }

  /// <summary>Get the bit depth needed for a given number of colors</summary>
  private static int GetBitDepthForColors(int colorCount) {
    return colorCount switch {
      <= 2 => 1,
      <= 4 => 2,
      <= 16 => 4,
      _ => 8
    };
  }

  /// <summary>Get bytes per pixel for a given color mode and bit depth</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int GetBytesPerPixel(ColorMode colorMode, int bitDepth) => colorMode switch {
    ColorMode.Grayscale => 1,
    ColorMode.GrayscaleAlpha => 2,
    ColorMode.RGB => 3,
    ColorMode.RGBA => 4,
    ColorMode.Palette => 1,
    _ => throw new ArgumentException("Invalid color mode")
  } * bitDepth / 8;

  /// <summary>Count the number of filter type transitions between consecutive scanlines</summary>
  private static int CountFilterTransitions(byte[][] filteredDatra) {
    var transitions = 0;

    // first byte in each scanline is the filter type
    for (var i = 1; i < filteredDatra.Length; ++i)
      if (filteredDatra[i][0] != filteredDatra[i - 1][0])
        ++transitions;

    return transitions;
  }

  /// <summary>Compress the filtered data using the specified method</summary>
  private async ValueTask<byte[]> CompressDataAsync(
    byte[][] filteredData,
    DeflateMethod deflateMethod,
    InterlaceMethod interlaceMethod,
    ColorMode colorMode,
    int bitDepth,
    byte[]? palette) {

    // In a real implementation, this would create a proper PNG file
    // For estimation, we'll just compress the raw filtered data

    using var ms = new MemoryStream();

    // Write a minimal PNG header
    const byte CR = 13;
    const byte LF = 10;
    const byte SUB = 26;
    ReadOnlyMemory<byte> pngSignature = new byte[] { 137, (byte)'P', (byte)'N', (byte)'G', CR, LF, SUB, LF };
    await ms.WriteAsync(pngSignature);

    // IHDR chunk
    using (var ihdrData = new MemoryStream()) {
      await using (var bw = new BinaryWriter(ihdrData)) {
        bw.Write(IPAddress.HostToNetworkOrder(this._imageWidth));
        bw.Write(IPAddress.HostToNetworkOrder(this._imageHeight));
        bw.Write((byte)bitDepth);
        bw.Write((byte)colorMode);
        bw.Write(COMPRESSION_METHOD_DEFLATE);
        bw.Write(FILTER_METHOD_ADAPTIVE);
        bw.Write((byte)interlaceMethod);
      }

      await WriteChunkAsync(ms, "IHDR", ihdrData.ToArray());
    }

    // PLTE chunk for palette images
    if (colorMode == ColorMode.Palette && palette != null)
      await WriteChunkAsync(ms, "PLTE", palette);

    // IDAT chunk
    using (var idatData = new MemoryStream()) {
      // Write all filtered scanlines
      await using (var deflateStream = GetDeflateStream(idatData, deflateMethod))
        foreach (var scanline in filteredData)
          await deflateStream.WriteAsync(scanline);

      await WriteChunkAsync(ms, "IDAT", idatData.ToArray());
    }

    // IEND chunk
    await WriteChunkAsync(ms, "IEND", Array.Empty<byte>());

    return ms.ToArray();
  }

  /// <summary>Get the appropriate deflate stream based on the compression method</summary>
  private static Stream GetDeflateStream(Stream baseStream, DeflateMethod method) {
    var level = method switch {
      DeflateMethod.Fastest => CompressionLevel.NoCompression,
      DeflateMethod.Fast => CompressionLevel.Fastest,
      DeflateMethod.Default => CompressionLevel.Optimal,
      DeflateMethod.Maximum => CompressionLevel.Optimal,
      DeflateMethod.Ultra => CompressionLevel.SmallestSize,
      _ => CompressionLevel.Optimal
    };

    return new ZLibStream(baseStream, level, true);
  }

  /// <summary>Write a PNG chunk</summary>
  private static async ValueTask WriteChunkAsync(Stream stream, string type, ReadOnlyMemory<byte> data) {
    await using var bw = new BinaryWriter(stream, System.Text.Encoding.ASCII, true);
    bw.Write(IPAddress.HostToNetworkOrder(data.Length));
    var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
    await stream.WriteAsync(typeBytes);
    await stream.WriteAsync(data);
    var crc = CalculateCrc(typeBytes, data.Span);
    bw.Write(IPAddress.HostToNetworkOrder((int)crc));
  }

  /// <summary>Calculate CRC for PNG chunk</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static uint CalculateCrc(ReadOnlySpan<byte> typeBytes, ReadOnlySpan<byte> data) {
    // CRC includes the chunk type bytes and the chunk data bytes
    var totalLength = typeBytes.Length + data.Length;
    var token = ArrayPool<byte>.Shared.Rent(totalLength);
    try {
      var bytesToCrc = token.AsSpan(0, totalLength);
      typeBytes.CopyTo(bytesToCrc);
      data.CopyTo(bytesToCrc[typeBytes.Length..]);
      return Crc32.HashToUInt32(bytesToCrc);
    } finally {
      ArrayPool<byte>.Shared.Return(token);
    }
  }
}
