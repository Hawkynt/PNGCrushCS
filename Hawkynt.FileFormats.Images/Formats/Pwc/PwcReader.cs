using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Pwc;

/// <summary>Reads PWC files from bytes, streams, or file paths.</summary>
public static class PwcReader {

  /// <summary>The largest picture this reader will allocate; the format's own limit is 65535×65535.</summary>
  public const long MaxPixels = 1L << 28;

  public static PwcFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PWC file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PwcFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static PwcFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PwcFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PwcFile.HeaderSize)
      throw new InvalidDataException("Data too small for a PWC header.");

    if (BinaryPrimitives.ReadUInt32LittleEndian(data) != PwcFile.Magic)
      throw new InvalidDataException("Invalid PWC signature (expected \"4yVa\").");

    var height = data[4] | data[5] << 8;
    var width = data[6] | data[7] << 8;
    var depth = (int)data[8];
    var uses = data[9];
    if (width == 0 || height == 0)
      throw new InvalidDataException("PWC picture dimensions must be non-zero.");

    if (depth is not (1 or 4 or 8))
      throw new InvalidDataException($"PWC has no model for {depth}-bit pictures; only depths 1, 4 and 8 are defined.");

    if ((long)width * height > MaxPixels)
      throw new InvalidDataException($"PWC picture of {width}x{height} exceeds the {MaxPixels}-pixel limit of this reader.");

    var offset = PwcFile.HeaderSize;
    var storesPalette = (uses & 1) != 0;
    byte[] palette;
    if (storesPalette) {
      if (data.Length < offset + 2)
        throw new InvalidDataException("PWC palette count is truncated.");

      var entries = data[offset] | data[offset + 1] << 8;
      offset += 2;
      if (entries > 256)
        throw new InvalidDataException($"PWC palette of {entries} entries exceeds 256.");

      if (depth == 4 && entries > 16)
        throw new InvalidDataException($"PWC 4-bit picture cannot index a palette of {entries} entries.");

      if (data.Length < offset + entries * 3)
        throw new InvalidDataException("PWC palette is truncated.");

      // Stored blue, green, red.
      palette = new byte[entries * 3];
      for (var i = 0; i < entries; ++i) {
        palette[i * 3] = data[offset + i * 3 + 2];
        palette[i * 3 + 1] = data[offset + i * 3 + 1];
        palette[i * 3 + 2] = data[offset + i * 3];
      }

      offset += entries * 3;
    } else
      palette = LinearPalette(depth);

    if (data.Length < offset + 4)
      throw new InvalidDataException("PWC option word is truncated.");

    var flags = (PwcCodingFlags)data[offset];
    var maxLps = data[offset + 1];
    offset += 4;

    var colors = storesPalette ? palette.Length / 3 : 1 << depth;
    var payload = data[offset..].ToArray();
    var coded = PwcCodec.Decode(payload, 0, width, height, depth, colors, flags, maxLps);

    var bottomUp = (uses & 8) != 0;
    var pixels = bottomUp ? FlipRows(coded, width, height) : coded;

    return new() {
      Width = width,
      Height = height,
      BitsPerPixel = depth,
      PixelData = pixels,
      Palette = palette,
      StoresPalette = storesPalette,
      CodingFlags = flags,
      MaxLps = maxLps,
      BottomUp = bottomUp,
    };
  }

  /// <summary>The palette a file without one is decoded against: a grey ramp, and white-on-black for one bit.</summary>
  public static byte[] LinearPalette(int depth) {
    var entries = 1 << depth;
    var palette = new byte[entries * 3];
    for (var i = 0; i < entries; ++i) {
      var level = depth switch {
        8 => i,
        4 => 8 + 16 * i,
        _ => i == 0 ? 255 : 0,
      };
      palette.AsSpan(i * 3, 3).Fill((byte)level);
    }

    return palette;
  }

  internal static byte[] FlipRows(byte[] pixels, int width, int height) {
    var flipped = new byte[pixels.Length];
    for (var y = 0; y < height; ++y)
      pixels.AsSpan(y * width, width).CopyTo(flipped.AsSpan((height - 1 - y) * width));

    return flipped;
  }
}
