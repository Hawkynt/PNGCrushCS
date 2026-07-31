using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.AmosBank;

/// <summary>Reads AMOS memory banks from bytes, streams, or file paths.</summary>
public static class AmosBankReader {

  public static AmosBankFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Bank not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AmosBankFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static AmosBankFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 82 || Encoding.ASCII.GetString(data[..2]) != AmosBankFile.Signature)
      throw new InvalidDataException("Not an AMOS bank.");

    var kind = Encoding.ASCII.GetString(data.Slice(2, 2));

    return kind switch {
      "Sp" or "Ic" => _ReadSprites(data),
      "Bk" => _ReadScreen(data),
      _ => throw new InvalidDataException($"An AMOS bank holds sprites, icons or a screen, not '{kind}'."),
    };
  }

  /// <summary>Reads a bank of sprites or icons, which are laid out side by side.</summary>
  private static AmosBankFile _ReadSprites(ReadOnlySpan<byte> data) {
    var sprites = (data[4] << 8) | data[5];
    var at = 6;
    int width = 0, height = 0;

    for (var sprite = 0; sprite < sprites; ++sprite) {
      if (at + 10 >= data.Length || data[at + 4] != 0)
        throw new InvalidDataException($"Sprite {sprite} does not start where the bank says.");

      // The stored width counts sixteen-pixel words, since the hardware fetches them that way.
      var spriteWidth = ((data[at] << 8) | data[at + 1]) << 4;
      var spriteHeight = (data[at + 2] << 8) | data[at + 3];
      var planes = data[at + 5];
      if (planes == 0 || planes > 5)
        throw new InvalidDataException($"Sprite {sprite} has {planes} bitplanes.");

      width += spriteWidth;
      height = Math.Max(height, spriteHeight);
      if (width <= 0 || height > 134217728 / width)
        throw new InvalidDataException("An AMOS sprite bank is larger than any picture.");

      at += 10 + (spriteWidth >> 3) * spriteHeight * planes;
    }

    // The palette closes the bank, so its position is what confirms the sprites were walked right.
    if (at + 64 != data.Length)
      throw new InvalidDataException("An AMOS sprite bank's palette is not where its sprites end.");

    var palette = AmosBankFile.ReadPalette(data, at);
    var pixels = new byte[width * height];

    at = 6;
    var left = 0;
    for (var sprite = 0; sprite < sprites; ++sprite) {
      var spriteWidth = ((data[at] << 8) | data[at + 1]) << 4;
      var spriteHeight = (data[at + 2] << 8) | data[at + 3];
      var planes = data[at + 5];
      var stride = spriteWidth >> 3;
      var planeLength = spriteHeight * stride;

      for (var y = 0; y < spriteHeight; ++y)
      for (var x = 0; x < spriteWidth; ++x) {
        var index = 0;
        for (var plane = 0; plane < planes; ++plane) {
          var source = at + 10 + plane * planeLength + y * stride + (x >> 3);
          if (source < data.Length && ((data[source] >> (~x & 7)) & 1) != 0)
            index |= 1 << plane;
        }

        pixels[y * width + left + x] = (byte)index;
      }

      left += spriteWidth;
      at += 10 + planes * planeLength;
    }

    return new() { PixelData = pixels, Palette = palette, Width = width, Height = height };
  }

  /// <summary>Reads a packed screen.</summary>
  private static AmosBankFile _ReadScreen(ReadOnlySpan<byte> data) {
    if (data.Length < 135 || Encoding.ASCII.GetString(data.Slice(12, 7)) != "Pac.Pic"
        || data[110] != 6 || data[111] != 7 || data[112] != 25 || data[113] != 99 || data[124] != 0)
      throw new InvalidDataException("Not a packed AMOS screen.");

    // The width counts bytes, and must be even because the Amiga fetches bitplanes by the word.
    var width = (data[118] << 8) | data[119];
    if ((width & 1) != 0)
      throw new InvalidDataException($"A packed AMOS screen is {width} bytes across, which is odd.");

    var lumps = (data[120] << 8) | data[121];
    var lumpLines = (data[122] << 8) | data[123];
    var height = lumps * lumpLines;
    var planes = data[125];
    if (planes == 0 || planes > 5 || width == 0 || height == 0)
      throw new InvalidDataException($"A packed AMOS screen is not {width << 3}x{height} of {planes} planes.");

    var unpacked = new byte[planes * width * height];
    _Unpack(data, unpacked, width, height, lumps, lumpLines, planes);

    return new() {
      PixelData = PlanarConverter.NonInterleavedPlanarToChunky(unpacked, width << 3, height, planes),
      Palette = AmosBankFile.ReadPalette(data, 46),
      Width = width << 3,
      Height = height,
    };
  }

  /// <summary>
  /// Unpacks the three streams a packed screen holds, read in step.
  /// </summary>
  /// <remarks>
  /// One stream is bytes of picture. The second is a bit per output byte saying whether to take a
  /// new one or repeat the last. The third is a bit per byte of the second, saying whether that
  /// byte is fresh or the previous one shifted on — so the control stream is itself compressed,
  /// which is what pays for the arrangement on a screen of a hundred kilobytes.
  /// <para/>
  /// The screen is written in lumps of scanlines, and within a lump down each column, because that
  /// is the order the packer found its runs in.
  /// </remarks>
  private static void _Unpack(
    ReadOnlySpan<byte> data, Span<byte> unpacked, int width, int height, int lumps, int lumpLines, int planes) {
    var rleAt = 110 + _BigEndian(data, 126);
    var pointsAt = 110 + _BigEndian(data, 130);
    if (rleAt < 0 || rleAt >= data.Length || pointsAt < 0)
      throw new InvalidDataException("A packed AMOS screen's streams do not start inside the file.");

    var picAt = 135;
    var pic = data[134];
    var rleBits = (data[rleAt++] << 8) | 128;
    var pointsBits = 0;

    for (var plane = 0; plane < planes; ++plane)
    for (var lump = 0; lump < lumps; ++lump)
    for (var x = 0; x < width; ++x)
    for (var y = 0; y < lumpLines; ++y) {
      rleBits <<= 1;
      if ((rleBits & 255) == 0) {
        pointsBits <<= 1;
        if ((pointsBits & 255) == 0)
          pointsBits = (_At(data, ref pointsAt) << 1) | 1;

        // The marker bit says whether the control byte is fresh or the old one carried on.
        if (((pointsBits >> 8) & 1) != 0)
          rleBits = (_At(data, ref rleAt) << 1) | 1;
        else
          rleBits >>= 8;
      }

      if (((rleBits >> 8) & 1) != 0)
        pic = _At(data, ref picAt);

      unpacked[((plane * lumps + lump) * lumpLines + y) * width + x] = pic;
    }
  }

  private static byte _At(ReadOnlySpan<byte> data, ref int offset) {
    if (offset >= data.Length)
      throw new InvalidDataException("A packed AMOS screen ends before its picture does.");

    return data[offset++];
  }

  private static int _BigEndian(ReadOnlySpan<byte> data, int offset)
    => (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

  public static AmosBankFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
