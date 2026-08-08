using System;
using System.IO;

namespace FileFormat.PcPaint;

/// <summary>Reads PC Paint / Pictor pages from bytes, streams, or file paths.</summary>
public static class PcPaintReader {

  public static PcPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("PC Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PcPaintFile FromStream(Stream stream) {
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

  public static PcPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PcPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < PcPaintFile.HeaderSize)
      throw new InvalidDataException("Data too small for a valid PC Paint file.");

    var magic = (ushort)(data[0] | (data[1] << 8));
    if (magic != PcPaintFile.Magic)
      throw new InvalidDataException($"Invalid PC Paint magic bytes (expected 0x{PcPaintFile.Magic:X4}, got 0x{magic:X4}).");

    var width = (ushort)(data[2] | (data[3] << 8));
    var height = (ushort)(data[4] | (data[5] << 8));

    if (width == 0)
      throw new InvalidDataException("PC Paint width must be greater than zero.");
    if (height == 0)
      throw new InvalidDataException("PC Paint height must be greater than zero.");

    var xOffset = (ushort)(data[6] | (data[7] << 8));
    var yOffset = (ushort)(data[8] | (data[9] << 8));

    // The low nibble is the depth of one plane and the high nibble the planes past the first.
    var planeInfo = data[10];
    var bitsPerPixel = planeInfo & 0x0F;
    var extraPlanes = (planeInfo >> 4) & 0x0F;

    if (bitsPerPixel is not (1 or 2 or 4 or 8))
      throw new InvalidDataException($"A Pictor page states {bitsPerPixel} bits per pixel; 1, 2, 4 and 8 are the depths it has.");
    if (extraPlanes != 0)
      throw new InvalidDataException($"A Pictor page in {extraPlanes + 1} planes is not read: how the planes interleave is described two ways and there is nothing here to tell them apart.");

    if (data[11] != PcPaintFile.VersionTwoFlag)
      throw new InvalidDataException("A Pictor page from before version 2 is not read: it carries no palette block and where its picture begins is not settled.");

    var videoMode = data[12];
    var paletteType = (ushort)(data[13] | (data[14] << 8));
    var paletteBytes = (ushort)(data[15] | (data[16] << 8));

    var at = PcPaintFile.HeaderSize;
    if (at + paletteBytes > data.Length)
      throw new InvalidDataException($"A Pictor page states {paletteBytes} bytes of palette, which the file does not hold.");

    var stored = data.Slice(at, paletteBytes);
    at += paletteBytes;

    var palette = _ExpandPalette(paletteType, stored, bitsPerPixel);

    if (at + 2 > data.Length)
      throw new InvalidDataException("A Pictor page states no count of the blocks it is stored in.");

    var blockCount = data[at] | (data[at + 1] << 8);
    at += 2;

    var stride = (width * bitsPerPixel + 7) / 8;
    var expected = stride * height;

    var packed = blockCount == 0
      ? _ReadUncompressed(data, at, expected)
      : _ReadBlocks(data, at, blockCount, expected);

    // The offsets are a lower-left corner, and the rows follow from that: the first row stored is
    // the bottom one. The one sample reads as a legible line of text this way and as the same line
    // upside down the other.
    var pixels = new byte[width * height];
    for (var row = 0; row < height; ++row) {
      var source = packed.AsSpan((height - 1 - row) * stride, stride);
      _Unpack(source, pixels.AsSpan(row * width, width), bitsPerPixel);
    }

    return new() {
      Width = width,
      Height = height,
      XOffset = xOffset,
      YOffset = yOffset,
      BitsPerPixel = (byte)bitsPerPixel,
      VideoMode = videoMode,
      PaletteType = paletteType,
      Palette = palette,
      PixelData = pixels,
    };
  }

  /// <summary>Takes the picture as it stands where the header says nothing was compressed.</summary>
  private static byte[] _ReadUncompressed(ReadOnlySpan<byte> data, int at, int expected) {
    if (data.Length - at < expected)
      throw new InvalidDataException($"An uncompressed Pictor page of {expected} bytes has {data.Length - at} left to read.");

    return data.Slice(at, expected).ToArray();
  }

  /// <summary>
  /// Reads the blocks the picture is stored in. Each states its own size, the length it comes to
  /// when unpacked, and which byte introduces a run — and all three have to agree with what reading
  /// it actually produces, which is what says the format was read rather than guessed.
  /// </summary>
  private static byte[] _ReadBlocks(ReadOnlySpan<byte> data, int at, int blockCount, int expected) {
    var output = new byte[expected];
    var written = 0;

    for (var index = 0; index < blockCount; ++index) {
      if (at + PcPaintFile.BlockHeaderSize > data.Length)
        throw new InvalidDataException($"Block {index} of a Pictor page begins past the end of the file.");

      var blockSize = data[at] | (data[at + 1] << 8);
      var runLength = data[at + 2] | (data[at + 3] << 8);
      var marker = data[at + 4];

      var end = at + blockSize;
      if (blockSize < PcPaintFile.BlockHeaderSize || end > data.Length)
        throw new InvalidDataException($"Block {index} of a Pictor page states {blockSize} bytes, which the file does not hold.");
      if (written + runLength > expected)
        throw new InvalidDataException($"Block {index} of a Pictor page unpacks to more than the {expected} bytes the picture is.");

      var produced = 0;
      var cursor = at + PcPaintFile.BlockHeaderSize;

      while (cursor < end && produced < runLength) {
        var value = data[cursor++];

        if (value != marker) {
          output[written + produced++] = value;
          continue;
        }

        if (cursor >= end)
          throw new InvalidDataException($"Block {index} of a Pictor page ends on a run marker.");

        int count = data[cursor++];
        if (count == 0) {
          if (cursor + 1 >= end)
            throw new InvalidDataException($"Block {index} of a Pictor page ends inside a long run.");

          count = data[cursor] | (data[cursor + 1] << 8);
          cursor += 2;
        }

        if (cursor >= end)
          throw new InvalidDataException($"Block {index} of a Pictor page states a run of {count} with nothing to repeat.");

        var repeated = data[cursor++];
        if (produced + count > runLength)
          throw new InvalidDataException($"A run in block {index} of a Pictor page overruns the {runLength} bytes it says it unpacks to.");

        for (var i = 0; i < count; ++i)
          output[written + produced++] = repeated;
      }

      if (produced != runLength)
        throw new InvalidDataException($"Block {index} of a Pictor page unpacks to {produced} bytes where it states {runLength}.");

      written += produced;
      at = end;
    }

    if (written != expected)
      throw new InvalidDataException($"A Pictor page's blocks unpack to {written} bytes where the picture is {expected}.");

    return output;
  }

  /// <summary>Spreads a packed row out to one index a byte.</summary>
  private static void _Unpack(ReadOnlySpan<byte> source, Span<byte> target, int bitsPerPixel) {
    if (bitsPerPixel == 8) {
      source[..target.Length].CopyTo(target);
      return;
    }

    var perByte = 8 / bitsPerPixel;
    var mask = (1 << bitsPerPixel) - 1;

    for (var x = 0; x < target.Length; ++x) {
      var packed = source[x / perByte];
      var shift = 8 - bitsPerPixel - x % perByte * bitsPerPixel;
      target[x] = (byte)((packed >> shift) & mask);
    }
  }

  /// <summary>
  /// Turns the palette block into RGB triplets. The VGA and EGA blocks say their colours outright;
  /// the CGA block is two hardware register values and is not read as a palette here.
  /// </summary>
  /// <remarks>
  /// The two CGA bytes are what the program wrote to the colour-select register, and no published
  /// decoder of this format interprets them — the reference one hard-codes black, blue, red and
  /// bright white and ignores what the file says. With one CGA sample and nothing that renders the
  /// format to check an interpretation against, inventing one would be the worse of the two errors,
  /// so the reference colours stand and the file's two bytes are left alone.
  /// </remarks>
  private static byte[] _ExpandPalette(int paletteType, ReadOnlySpan<byte> stored, int bitsPerPixel) {
    switch (paletteType) {
      case PcPaintFile.PaletteVga when stored.Length >= PcPaintFile.VgaPaletteBytes: {
        // The digital-to-analogue converter takes six bits a channel. Repeating the top two into the
        // bottom is what maps them onto eight without darkening white.
        var palette = new byte[PcPaintFile.VgaPaletteBytes];
        for (var i = 0; i < palette.Length; ++i) {
          var value = stored[i] & 0x3F;
          palette[i] = (byte)((value << 2) | (value >> 4));
        }

        return palette;
      }

      case PcPaintFile.PalettePcJr or PcPaintFile.PaletteEga when stored.Length >= PcPaintFile.EgaPaletteBytes: {
        var palette = new byte[PcPaintFile.EgaPaletteBytes * 3];
        for (var i = 0; i < PcPaintFile.EgaPaletteBytes; ++i) {
          var register = stored[i];
          // Two bits a channel, the high one and the low one of each, in the order red green blue.
          var red = ((register >> 2) & 1) << 1 | ((register >> 5) & 1);
          var green = ((register >> 1) & 1) << 1 | ((register >> 4) & 1);
          var blue = (register & 1) << 1 | ((register >> 3) & 1);
          palette[i * 3] = (byte)(red * 85);
          palette[i * 3 + 1] = (byte)(green * 85);
          palette[i * 3 + 2] = (byte)(blue * 85);
        }

        return palette;
      }

      case PcPaintFile.PaletteCga:
        return [0, 0, 0, 0, 0, 170, 170, 0, 0, 255, 255, 255];

      default: {
        var count = 1 << bitsPerPixel;
        var palette = new byte[count * 3];
        for (var i = 0; i < count; ++i) {
          var level = (byte)(i * 255 / (count - 1));
          palette[i * 3] = level;
          palette[i * 3 + 1] = level;
          palette[i * 3 + 2] = level;
        }

        return palette;
      }
    }
  }
}
