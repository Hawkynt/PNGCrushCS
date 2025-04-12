namespace PngOptimizer;

using System;
using System.Buffers;
using System.IO.Hashing;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO.Compression;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;

/// <summary>Main class for PNG optimization</summary>
public sealed partial class PngOptimizer {
  private readonly int _imageWidth;
  private readonly int _imageHeight;
  private readonly PngOptimizationOptions _options;
  private readonly ImageStats _imageStats;
  private readonly ArgbPixel[] _imageBitmapPixelData;

  /// <summary>Constructor for the PNG optimizer</summary>
  public PngOptimizer(Bitmap image, PngOptimizationOptions? options = null) {
    this._options = options ?? new();
    this._imageWidth = image.Width;
    this._imageHeight = image.Height;
    (this._imageStats, this._imageBitmapPixelData) = ExtractImageData(image);

    return;

    unsafe (ImageStats, ArgbPixel[]) ExtractImageData(Bitmap image) {
      var uniqueColors = new HashSet<uint>();
      var hasAlpha = false;
      var isGrayscale = true;

      // Berechne Größe des Pixel-Arrays
      var width = image.Width;
      var height = image.Height;
      const int BYTES_PER_PIXEL = 4; // 4 Bytes pro Pixel (BGRA)
      var pixelDataSize = width * height * BYTES_PER_PIXEL;
      var result = new ArgbPixel[pixelDataSize];

      var bmpData = image.LockBits(
        new Rectangle(0, 0, width, height),
        ImageLockMode.ReadOnly,
        PixelFormat.Format32bppArgb);

      try {
        var stride = bmpData.Stride;
        var scan0 = bmpData.Scan0;

        fixed (ArgbPixel* resultPtr = result) {
          var rowOffset = (byte*)(void*)scan0;
          var destPtr = resultPtr;

          for (var y = 0; y < height; ++y, rowOffset += stride) {
            var currentRowOffset = rowOffset;
            for (var x = 0; x < width; ++x, currentRowOffset += BYTES_PER_PIXEL, ++destPtr) {

              var pixelValue = *(ArgbPixel*)currentRowOffset;
              uniqueColors.Add(*(uint*)currentRowOffset);
              
              *destPtr = pixelValue;
              hasAlpha |= pixelValue.A < 255;
              isGrayscale &= pixelValue.R == pixelValue.G && pixelValue.G == pixelValue.B;
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
  }

  /// <summary>Optimize the PNG image and return the best result</summary>
  public async ValueTask<OptimizationResult> OptimizeAsync() {
    var stopwatch = Stopwatch.StartNew();
    Console.WriteLine("Starting PNG optimization...");

    var combinations = GenerateOptimizationCombinations();
    Console.WriteLine($"Testing {combinations.Length} optimization combinations");

    using var semaphore = new SemaphoreSlim(this._options.MaxParallelTasks);
    var tasks = combinations.Select(async combo => {
      await semaphore.WaitAsync();
      try {
        return TestCombination(combo);
      } finally {
        semaphore.Release();
      }
    }).ToArray();

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

    OptimizationCombo[] GenerateOptimizationCombinations() {

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

      // Build all combinations
      return (
        from colorModeInfo in colorModesToTry
        from interlaceMethod in this._options.TryInterlacing? new[]{InterlaceMethod.None,InterlaceMethod.Adam7} : [InterlaceMethod.None]
        from filterStrategy in this._options.FilterStrategies
        where colorModeInfo.colorMode != ColorMode.Palette || colorModeInfo.bitDepth >= 8 || filterStrategy == FilterStrategy.SingleFilter
        from deflateMethod in this._options.DeflateMethods
        select new OptimizationCombo(
          colorModeInfo.colorMode,
          colorModeInfo.bitDepth,
          interlaceMethod,
          filterStrategy,
          deflateMethod)
      ).ToArray();
    }

    OptimizationResult TestCombination(OptimizationCombo combo) {
      var stopwatch = Stopwatch.StartNew();

      // Convert the image to the right format
      var imageData = ConvertPixelDataToByteArray(combo.ColorMode, combo.BitDepth, out var palette);

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

      var filterTransitions = CountFilterTransitions(filteredData);
      var bytes = this.CompressData(
        filteredData,
        combo.DeflateMethod,
        combo.InterlaceMethod,
        combo.ColorMode,
        combo.BitDepth,
        palette
      );

      stopwatch.Stop();

      return new OptimizationResult(
        combo.ColorMode,
        combo.BitDepth,
        combo.InterlaceMethod,
        combo.FilterStrategy,
        combo.DeflateMethod,
        bytes.Length,
        filteredData,
        filters,
        filterTransitions,
        stopwatch.Elapsed,
        bytes
      );

      byte[][] ConvertPixelDataToByteArray(ColorMode colorMode, int bitDepth, out byte[]? palette) {

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

        if (colorMode == ColorMode.Palette)
          QuantizePixelData(width, height, 1 << bitDepth, out palette, result);
        else {
          // Direct format conversion from pixel data
          var sourceOffset = 0;
          for (var y = 0; y < height; ++y) {
            var scanline = result[y];
            for (var x = 0; x < width; ++x, ++sourceOffset) {
              var pixel = this._imageBitmapPixelData[sourceOffset];
              var b = pixel.B;
              var g = pixel.G;
              var r = pixel.R;
              var a = pixel.A;

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

        void QuantizePixelData(int width, int height, int maxColors, out byte[] palette, byte[][] result) {
          // Für eine vollständige Implementierung würde hier ein Quantisierungsalgorithmus verwendet
          // Hier vereinfacht: Sammle die einzigartigen Farben und erstelle eine Palette

          var uniqueColors = new Dictionary<int, int>(); // ColorValue -> PaletteIndex
          palette = new byte[maxColors * 3]; // RGB triplets

          var paletteIndex = 0;

          // Erste Phase: Palette erstellen (maximal maxColors Einträge)
          var pixelIndex = 0;
          for (var y = 0; y < height && uniqueColors.Count < maxColors; ++y)
          for (var x = 0; x < width && uniqueColors.Count < maxColors; ++x) {
            var pixel = this._imageBitmapPixelData[pixelIndex++];
            var b = pixel.B;
            var g = pixel.G;
            var r = pixel.R;

            // Ignoriere Alpha für die Palette
            var colorValue = (r << 16) | (g << 8) | b;
            if (uniqueColors.ContainsKey(colorValue))
              continue;

            // Füge zur Palette hinzu
            palette[paletteIndex * 3] = r;
            palette[paletteIndex * 3 + 1] = g;
            palette[paletteIndex * 3 + 2] = b;
            uniqueColors[colorValue] = paletteIndex++;
          }

          // Zweite Phase: Indizes zuweisen
          var bitDepth = GetBitDepthForColors(maxColors);
          var pixelsPerByte = 8 / bitDepth;
          pixelIndex = 0;
          for (var y = 0; y < height; ++y) {
            var scanline = result[y];

            if (bitDepth == 8) {
              // 8-bit: Ein Index pro Byte
              for (var x = 0; x < width; ++x) {
                var pixel = this._imageBitmapPixelData[pixelIndex++];
                var b = pixel.B;
                var g = pixel.G;
                var r = pixel.R;
                var colorValue = (r << 16) | (g << 8) | b;
                var paletteIdx = FindClosestColor(uniqueColors, colorValue, palette);

                scanline[x] = (byte)paletteIdx;
              }
            } else {
              // 1, 2 oder 4 bit: Mehrere Indizes pro Byte packen
              for (var x = 0; x < width; x += pixelsPerByte) {
                byte packed = 0;

                for (var bit = 0; bit < pixelsPerByte && x + bit < width; ++bit) {
                  var pixel = this._imageBitmapPixelData[pixelIndex++];
                  var b = pixel.B;
                  var g = pixel.G;
                  var r = pixel.R;
                  var colorValue = (r << 16) | (g << 8) | b;
                  var paletteIdx = FindClosestColor(uniqueColors, colorValue, palette);

                  var mask = (1 << bitDepth) - 1;
                  packed |= (byte)((paletteIdx & mask) << (8 - bitDepth * (bit + 1)));
                }

                scanline[x / pixelsPerByte] = packed;
              }
            }
          }

          return;

          static int FindClosestColor(Dictionary<int, int> paletteMap, int colorValue, byte[] palette) {
            if (paletteMap.TryGetValue(colorValue, out var index))
              return index;

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
              var distance = (pr - r).Squared() + (pg - g).Squared() + (pb - b).Squared();
              if (distance >= minDistance)
                continue;

              minDistance = distance;
              closestIndex = i;
            }

            return closestIndex;
          }

          static int GetBitDepthForColors(int colorCount) {
            return colorCount switch {
              <= 2 => 1,
              <= 4 => 2,
              <= 16 => 4,
              _ => 8
            };
          }
        }
      }
    }
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
  private byte[] CompressData(
    byte[][] filteredData,
    DeflateMethod deflateMethod,
    InterlaceMethod interlaceMethod,
    ColorMode colorMode,
    int bitDepth,
    byte[]? palette) {

    const byte COMPRESSION_METHOD_DEFLATE = 0;
    const byte FILTER_METHOD_ADAPTIVE = 0;

    // In a real implementation, this would create a proper PNG file
    // For estimation, we'll just compress the raw filtered data

    using var ms = new MemoryStream();

    // PNG-Header direkt schreiben, ohne ReadOnlyMemory zu allokieren
    const byte CR = 13, LF = 10, SUB = 26;
    ms.WriteByte(137);
    ms.WriteByte((byte)'P');
    ms.WriteByte((byte)'N');
    ms.WriteByte((byte)'G');
    ms.WriteByte(CR);
    ms.WriteByte(LF);
    ms.WriteByte(SUB);
    ms.WriteByte(LF);

    // IHDR chunk
    var ihdr = ArrayPool<byte>.Shared.Rent(13);
    try {
      var ihdrData = ihdr.AsSpan(0, 13);
      ihdrData[0] = (byte)(this._imageWidth >> 24);
      ihdrData[1] = (byte)(this._imageWidth >> 16);
      ihdrData[2] = (byte)(this._imageWidth >> 8);
      ihdrData[3] = (byte)this._imageWidth;
      ihdrData[4] = (byte)(this._imageHeight >> 24);
      ihdrData[5] = (byte)(this._imageHeight >> 16);
      ihdrData[6] = (byte)(this._imageHeight >> 8);
      ihdrData[7] = (byte)this._imageHeight;
      ihdrData[8] = (byte)bitDepth;
      ihdrData[9] = (byte)colorMode;
      ihdrData[10] = COMPRESSION_METHOD_DEFLATE;
      ihdrData[11] = FILTER_METHOD_ADAPTIVE;
      ihdrData[12] = (byte)interlaceMethod;
      WriteChunk(ms, "IHDR", ihdrData.ToArray());
    } finally {
      ArrayPool<byte>.Shared.Return(ihdr);
    }

    // PLTE chunk for palette images
    if (colorMode == ColorMode.Palette && palette != null)
      WriteChunk(ms, "PLTE", palette);

    // IDAT chunk
    using (var idatData = new PooledMemoryStream(1024 + filteredData.Sum(f => f.Length))) {
      using (var deflateStream = CreateZlibStream(idatData.Stream, deflateMethod))
        foreach (var scanline in filteredData)
          deflateStream.Write(scanline);

      WriteChunk(ms, "IDAT", idatData.AsSpan());
    }

    // IEND chunk
    WriteChunk(ms, "IEND", ReadOnlySpan<byte>.Empty);

    return ms.ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static Stream CreateZlibStream(Stream baseStream, DeflateMethod method) {
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

    static void WriteChunk(Stream stream, string type, ReadOnlySpan<byte> data) {
      using var bw = new BinaryWriter(stream, System.Text.Encoding.ASCII, true);
      bw.Write(IPAddress.HostToNetworkOrder(data.Length));
      var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
      stream.Write(typeBytes);
      stream.Write(data);
      var crc = CalculateCrc(typeBytes, data);
      bw.Write(IPAddress.HostToNetworkOrder((int)crc));
      return;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      static uint CalculateCrc(ReadOnlySpan<byte> typeBytes, ReadOnlySpan<byte> data) {
        var crc = new Crc32();
        crc.Append(typeBytes);
        crc.Append(data);
        return crc.GetCurrentHashAsUInt32();
      }
    }
  }

}
