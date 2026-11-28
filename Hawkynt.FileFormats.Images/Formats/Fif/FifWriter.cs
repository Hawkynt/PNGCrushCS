using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Fif;

/// <summary>Writes FTC 3.1 fractal images by a fractal search of the picture.</summary>
/// <remarks>
/// <para>
/// The file is a single-region, colour, bit-coded FTC 3.1 stream laid out as Iterated's own
/// generation-2 encoder laid out its files: 4×4 ranges, a domain grid of step 2, eight symmetries
/// and one type-6 quantizer of nine bits (contrast ¾, 512 brightness offsets). Every range block
/// carries its own transform (class 0).
/// </para>
/// <para>
/// The search: the picture is converted to the codec's Y and U/V planes (U and V at half size, packed
/// beside each other under Y), and for every range block the domains within ±<see cref="SearchRadius"/>
/// pixels — 8×8 areas of the same plane, seen at half size — are tried in all eight orientations.
/// With the contrast fixed, the best brightness follows from the means, so the error of each
/// candidate is the residual after removing them; the winner's brightness index is then chosen by
/// trying its neighbours through the real affine table. The decoder's attractor is not the picture,
/// so this is lossy, as fractal coding is.
/// </para>
/// </remarks>
public static class FifWriter {

  /// <summary>How far from a range block, in pixels, domains are searched.</summary>
  public const int SearchRadius = 8;

  private const int _QUANTIZER_BITS = 9;
  private const int _QUANTIZER_KIND = 6;
  private const int _VERSION = 31;

  public static byte[] ToBytes(FifFile file) {
    if (file.Width is < 1 or > 0xFFF8 || file.Height is < 1 or > 0xFFF8)
      throw new ArgumentException($"A fractal image is 1 to 65528 pixels each way, not {file.Width}x{file.Height}.", nameof(file));
    if (file.PixelData == null || file.PixelData.Length < file.Width * file.Height * 3)
      throw new ArgumentException("Pixel data is smaller than the picture it describes.", nameof(file));

    var canvasWidth = Math.Max(16, (file.Width + 7) & ~7);
    var canvasHeight = Math.Max(16, (file.Height + 7) & ~7);
    var packed = _ToPacked(file.PixelData, file.Width, file.Height, canvasWidth, canvasHeight);
    var segment = _EncodeRegion(packed, canvasWidth, canvasHeight);
    return _Assemble(file.Width, file.Height, canvasWidth, canvasHeight, segment);
  }

  // ============================================================================================
  // Colour
  // ============================================================================================

  /// <summary>Converts to the packed Y + (U|V) layout, extending the picture to the coded canvas by repeating its edges.</summary>
  private static byte[] _ToPacked(byte[] rgb, int width, int height, int canvasWidth, int canvasHeight) {
    var y = new int[canvasWidth * canvasHeight];
    var u = new int[canvasWidth * canvasHeight];
    var v = new int[canvasWidth * canvasHeight];
    var red = _RedTable();
    for (var row = 0; row < canvasHeight; ++row)
    for (var column = 0; column < canvasWidth; ++column) {
      var at = (Math.Min(row, height - 1) * width + Math.Min(column, width - 1)) * 3;
      int r = rgb[at], g = rgb[at + 1], b = rgb[at + 2];
      var luma = (299 * r + 587 * g + 114 * b + 500) / 1000;
      // B = Y - 256 + 2U and R = Y + table[V]; G then follows from the codec's own equation.
      var uValue = Math.Clamp((b - luma + 256 + 1) / 2, 0, 255);
      var vValue = _InvertRed(red, r - luma);
      var index = row * canvasWidth + column;
      y[index] = luma;
      u[index] = uValue;
      v[index] = vValue;
    }

    var packed = new byte[canvasWidth * canvasHeight * 3 / 2];
    for (var i = 0; i < canvasWidth * canvasHeight; ++i)
      packed[i] = (byte)y[i];

    var chromaWidth = canvasWidth / 2;
    for (var row = 0; row < canvasHeight / 2; ++row)
    for (var column = 0; column < chromaWidth; ++column) {
      var a = 2 * row * canvasWidth + 2 * column;
      var b = a + canvasWidth;
      var target = canvasWidth * canvasHeight + row * canvasWidth + column;
      packed[target] = (byte)((u[a] + u[a + 1] + u[b] + u[b + 1] + 2) / 4);
      packed[target + chromaWidth] = (byte)((v[a] + v[a + 1] + v[b] + v[b + 1] + 2) / 4);
    }

    return packed;
  }

  private static int[] _RedTable() {
    var table = new int[256];
    var value = -0x804;
    for (var i = 0; i < 128; ++i, value += 0x10)
      table[i] = FifTransforms.CDiv(value, 10);
    value = 5;
    for (var i = 128; i < 256; ++i, value += 0x10)
      table[i] = FifTransforms.CDiv(value, 10);

    return table;
  }

  private static int _InvertRed(int[] table, int difference) {
    var best = 0;
    var bestError = int.MaxValue;
    for (var i = 0; i < 256; ++i) {
      var error = Math.Abs(table[i] - difference);
      if (error < bestError) {
        best = i;
        bestError = error;
      }
    }

    return best;
  }

  // ============================================================================================
  // Search
  // ============================================================================================

  private sealed class BitWriter {
    private readonly List<byte> _bytes = [];
    private int _bit;

    public void Write(int value, int count) {
      for (var i = 0; i < count; ++i) {
        if (_bit == 0)
          _bytes.Add(0);
        if (((value >> i) & 1) != 0)
          _bytes[^1] |= (byte)(1 << _bit);
        _bit = (_bit + 1) & 7;
      }
    }

    public void Run(int length) {
      if (length == 1) {
        Write(0, 1);
        return;
      }

      var m = length - 1;
      var k = 0;
      for (var t = m; t > 0; t >>= 1)
        ++k;
      for (var i = 0; i < k; ++i)
        Write(1, 1);
      Write(0, 1);
      Write(m - (1 << (k - 1)), k - 1);
    }

    public void Align() => _bit = 0;

    public byte[] ToArray() => _bytes.ToArray();
  }

  private static byte[] _EncodeRegion(byte[] packed, int width, int height) {
    var quantizer = FifTransforms.QuantizerValues(_QUANTIZER_KIND, _QUANTIZER_BITS, 1, 0, _VERSION);
    var packedHeight = height * 3 / 2;
    var halfWidth = width / 2;
    var half = new int[halfWidth * (packedHeight / 2)];
    for (var row = 0; row < packedHeight / 2; ++row)
    for (var column = 0; column < halfWidth; ++column) {
      var a = 2 * row * width + 2 * column;
      half[row * halfWidth + column] = (packed[a] + packed[a + 1] + packed[a + width] + packed[a + width + 1] + 2) >> 2;
    }

    var writer = new BitWriter();
    _EncodePlane(writer, packed, half, quantizer, width, 0, height, height, 0, 0);
    writer.Align();
    // The chroma plane's domain grid continues below the luminance plane's.
    _EncodePlane(writer, packed, half, quantizer, width, height, height / 2, height / 2, height, (height + 1) / 2);
    return writer.ToArray();
  }

  /// <summary>Codes one plane: rows <paramref name="top"/>.. of the packed canvas, domains within the same rows.</summary>
  private static void _EncodePlane(BitWriter writer, byte[] packed, int[] half, int[] quantizer, int width, int top, int height,
    int domainRows, int domainTop, int domainHeightPrefix) {
    var rangeWidth = width / 4;
    var rangeHeight = height / 4;
    var domainWidth = (width + 1) / 2;
    var domainHeight = domainHeightPrefix + (height + 1) / 2;
    var domainBits = FifTransforms.BitLength(domainWidth * domainHeight);
    var (order, _, _) = FifTransforms.TileOrder(rangeWidth, rangeHeight);
    var count = 0;
    foreach (var logical in order)
      if (logical >= 0)
        ++count;

    // Every candidate survives the class-2, class-6, class-1 and class-5 masks, and the last mask
    // gives them all class 0.
    for (var mask = 0; mask < 4; ++mask) {
      writer.Write(1, 1);
      writer.Run(count);
    }

    writer.Write(0, 1);
    writer.Run(count);

    var halfWidth = width / 2;
    var range = new int[16];
    var domain = new int[16];
    foreach (var logical in order) {
      if (logical < 0)
        continue;

      var blockX = logical % rangeWidth * 4;
      var blockY = logical / rangeWidth * 4;
      var rangeSum = 0;
      for (var i = 0; i < 16; ++i) {
        range[i] = packed[(top + blockY + i / 4) * width + blockX + i % 4];
        rangeSum += range[i];
      }

      // Domains are 8×8 at full size, inside this plane, on the step-2 grid near the block.
      long bestError = long.MaxValue;
      int bestX = 0, bestY = domainTop, bestSymmetry = 0, bestDomainSum = 0;
      var minimumY = domainTop;
      var maximumY = domainTop + domainRows - 8;
      for (var dy = Math.Max(minimumY, top + blockY - SearchRadius) & ~1; dy <= Math.Min(maximumY, top + blockY + SearchRadius); dy += 2)
      for (var dx = Math.Max(0, blockX - SearchRadius) & ~1; dx <= Math.Min(width - 8, blockX + SearchRadius); dx += 2) {
        var origin = dy / 2 * halfWidth + dx / 2;
        var domainSum = 0;
        for (var i = 0; i < 16; ++i) {
          domain[i] = half[origin + i / 4 * halfWidth + i % 4];
          domainSum += domain[i];
        }

        for (var symmetry = 0; symmetry < 8; ++symmetry) {
          // Residual of range - 3/4 domain after both means are removed, in units of 1/64.
          long error = 0;
          for (var i = 0; i < 16; ++i) {
            var (sy, sx) = FifTransforms.Transform(symmetry, i / 4, i % 4, 4);
            var residual = 64 * range[i] - 48 * domain[sy * 4 + sx] - (4 * rangeSum - 3 * domainSum);
            error += (long)residual * residual;
            if (error >= bestError)
              break;
          }

          if (error < bestError) {
            (bestError, bestX, bestY, bestSymmetry, bestDomainSum) = (error, dx, dy, symmetry, domainSum);
          }
        }
      }

      // Brightness: start from the means and try the neighbouring indices through the real table.
      var origin2 = bestY / 2 * halfWidth + bestX / 2;
      for (var i = 0; i < 16; ++i)
        domain[i] = half[origin2 + i / 4 * halfWidth + i % 4];

      var targetOffset = (rangeSum - 0.75 * bestDomainSum) / 16.0;
      var estimate = (int)Math.Round((targetOffset - 0.5) * 64 / 6 / (8 * 4 / 3.0) + 256);
      int bestIndex = 0;
      long bestIndexError = long.MaxValue;
      for (var k = Math.Max(0, estimate - 6); k <= Math.Min(quantizer.Length - 1, estimate + 6); ++k) {
        long error = 0;
        for (var i = 0; i < 16; ++i) {
          var (sy, sx) = FifTransforms.Transform(bestSymmetry, i / 4, i % 4, 4);
          var difference = FifTransforms.Affine(quantizer[k], domain[sy * 4 + sx], _QUANTIZER_KIND) - range[i];
          error += difference * difference;
        }

        if (error < bestIndexError) {
          bestIndexError = error;
          bestIndex = k;
        }
      }

      var domainIndex = bestY / 2 * domainWidth + bestX / 2;
      writer.Write(bestIndex, _QUANTIZER_BITS);
      writer.Write(domainIndex, domainBits);
      writer.Write(bestSymmetry, 3);
    }
  }

  // ============================================================================================
  // Container
  // ============================================================================================

  private static byte[] _Assemble(int outputWidth, int outputHeight, int canvasWidth, int canvasHeight, byte[] segment) {
    var directory = new byte[14 + 8 + 1];
    BinaryPrimitives.WriteUInt32LittleEndian(directory, (uint)Math.Max(segment.Length * 2, 0x10000));
    BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(9), 256000);
    directory[13] = 1;
    BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(18), (ushort)canvasWidth);
    BinaryPrimitives.WriteUInt16LittleEndian(directory.AsSpan(20), (ushort)canvasHeight);

    // Range 4, domain 4, step 2, 3 symmetry bits; one 9-bit type-6 quantizer, step 1, base 0.
    byte[] descriptor = [
      4, 0, 4, 0, 2, 0, 3, _QUANTIZER_BITS, 1, 0, 0, 1, 0, _QUANTIZER_KIND, 0,
      8, 0, 3, 0, 0, 0, 1, 0, 3, 9, 1, 0, 0,
    ];

    var mainSize = 48 + directory.Length + descriptor.Length;
    var payloadSize = 4 + segment.Length;
    using var output = new MemoryStream();
    var header = new byte[48];
    "FTC\0"u8.CopyTo(header);
    header[4] = 2;
    header[5] = 3;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 0x0102);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)canvasWidth);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)canvasHeight);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 24);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), (ushort)mainSize);
    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)payloadSize);
    header[28] = 3;
    header[29] = 1;
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(30), (ushort)(directory.Length + 20));
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(36), (ushort)outputWidth);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(38), (ushort)outputHeight);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(40), (ushort)outputWidth);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(42), (ushort)outputHeight);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(46), (ushort)directory.Length);
    output.Write(header);
    output.Write(directory);
    output.Write(descriptor);
    Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)segment.Length);
    output.Write(length);
    output.Write(segment);
    return output.ToArray();
  }
}
