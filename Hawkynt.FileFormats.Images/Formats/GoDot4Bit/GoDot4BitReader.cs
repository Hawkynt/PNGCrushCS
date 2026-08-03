using System;
using System.IO;

namespace FileFormat.GoDot4Bit;

/// <summary>Reads Commodore 64 GoDot 4-bit files from bytes, streams, or file paths.</summary>
public static class GoDot4BitReader {

  public static GoDot4BitFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("GoDot 4-bit file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GoDot4BitFile FromStream(Stream stream) {
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

  public static GoDot4BitFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  /// <summary>
  /// Reads a GoDot picture or clip.
  /// </summary>
  /// <remarks>
  /// The packing: 0xAD introduces a run — a count and then the byte to repeat, a count of nought
  /// meaning 256. Everything else stands for itself. All six samples consume their file to the last
  /// byte and expand to exactly the pixels their size takes, or one byte more where the last run
  /// overshoots.
  /// </remarks>
  public static GoDot4BitFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException($"Data too small for a valid GoDot file (got {data.Length} bytes).");

    var isClip = data[..4].SequenceEqual(GoDot4BitFile.ClipMagic);
    if (!isClip && !data[..4].SequenceEqual(GoDot4BitFile.ScreenMagic))
      throw new InvalidDataException("Not a GoDot file: it opens with neither GOD0 nor GOD1.");

    int width, height, dataStart;
    if (isClip) {
      if (data.Length < 8)
        throw new InvalidDataException("A GoDot clip states its size in the four bytes after its signature; this file is shorter than that.");

      // Two bytes of where the clip was cut from, then its width and height in character cells.
      width = data[6] * 8;
      height = data[7] * 8;
      dataStart = 8;
    } else {
      width = GoDot4BitFile.ScreenWidth;
      height = GoDot4BitFile.ScreenHeight;
      dataStart = 4;
    }

    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A GoDot picture of {width}x{height} is no size.");

    var wanted = width * height / 2;
    var pixelData = new byte[wanted];
    var written = 0;
    var pos = dataStart;

    while (pos < data.Length && written < wanted) {
      var value = data[pos];
      if (value != GoDot4BitFile.RunEscape || pos + 2 >= data.Length) {
        pixelData[written++] = value;
        ++pos;
        continue;
      }

      // A count of nought stands for a full 256.
      var run = Math.Min(data[pos + 1] == 0 ? 256 : data[pos + 1], wanted - written);
      pixelData.AsSpan(written, run).Fill(data[pos + 2]);
      written += run;
      pos += 3;
    }

    if (written < wanted)
      throw new InvalidDataException($"A GoDot picture of {width}x{height} holds {wanted} bytes; this one ran out after {written}.");

    return new() {
      Width = width,
      Height = height,
      IsClip = isClip,
      PixelData = pixelData,
    };
  }
}
