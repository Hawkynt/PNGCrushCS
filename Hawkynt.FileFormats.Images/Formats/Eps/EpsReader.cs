using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using FileFormat.Tiff;

namespace FileFormat.Eps;

/// <summary>Reads EPS files with embedded TIFF preview from bytes, streams, or file paths.</summary>
public static class EpsReader {

  public static EpsFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EPS file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EpsFile FromStream(Stream stream) {
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

  public static EpsFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < 4)
      throw new InvalidDataException("Data too small for a valid EPS file.");

    // Only the DOS-binary flavour was accepted — the one that opens with a 0xC5D0D3C6 preamble
    // pointing at an embedded TIFF preview. That is the rarer kind by a wide margin: an ordinary EPS
    // is plain PostScript beginning "%!PS", and every one of them was rejected as having invalid
    // magic. Those carry their preview as ASCII hex in a %%BeginPreview block instead, which is what
    // the EPSI ("EPS Interchange") flavour is.
    if (data.Length >= EpsHeader.StructSize) {
      var header = EpsHeader.ReadFrom(data);
      if (header.Magic == EpsHeader.ExpectedMagic)
        return _ParseFromHeader(header, data);
    }

    if (data.StartsWith("%!PS"u8))
      return _ParsePostScript(data);

    throw new InvalidDataException("Invalid EPS magic bytes.");
  }

  public static EpsFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static EpsFile _ParseFromHeader(EpsHeader header, ReadOnlySpan<byte> data) {
    var tiffOffset = header.TiffOffset;
    var tiffLength = header.TiffLength;

    if (tiffOffset == 0 || tiffLength == 0)
      throw new InvalidDataException("EPS file has no embedded TIFF preview.");

    if (tiffOffset + tiffLength > (uint)data.Length)
      throw new InvalidDataException("TIFF preview extends beyond end of file.");

    var tiffData = new byte[tiffLength];
    data.Slice((int)tiffOffset, (int)tiffLength).CopyTo(tiffData.AsSpan(0));

    var tiff = TiffReader.FromBytes(tiffData);
    var raw = TiffFile.ToRawImage(tiff);

    // Convert to RGB24 if needed
    var rgb24 = raw.ToRgb24();

    return new EpsFile {
      Width = raw.Width,
      Height = raw.Height,
      PixelData = rgb24,
    };
  }

  /// <summary>
  /// A plain PostScript EPS, whose raster preview — where it has one — is ASCII hex in a
  /// %%BeginPreview block.
  /// </summary>
  /// <remarks>
  /// The header line reads "%%BeginPreview: width height depth lines", the samples that follow are
  /// hex nibbles behind a leading "%" on every line, and each row is padded to a whole byte. Depth is
  /// grey levels a sample, so 1 gives a bitmap and 8 a greyscale image. There is no fallback to the
  /// PostScript itself: drawing that needs an interpreter, and a file with no preview says so rather
  /// than pretending.
  /// </remarks>
  private static EpsFile _ParsePostScript(ReadOnlySpan<byte> data) {
    var text = Encoding.ASCII.GetString(data);
    var begin = text.IndexOf("%%BeginPreview:", StringComparison.Ordinal);
    if (begin < 0)
      throw new InvalidDataException("EPS file has no preview image; rendering PostScript is out of scope.");

    var lineEnd = text.IndexOf('\n', begin);
    if (lineEnd < 0)
      throw new InvalidDataException("EPS preview header is truncated.");

    var parts = text[(begin + "%%BeginPreview:".Length)..lineEnd]
      .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 3
        || !int.TryParse(parts[0], out var width)
        || !int.TryParse(parts[1], out var height)
        || !int.TryParse(parts[2], out var depth))
      throw new InvalidDataException("EPS preview header is malformed.");

    if (width <= 0 || height <= 0 || depth is not (1 or 2 or 4 or 8))
      throw new InvalidDataException($"EPS preview is {width}x{height} at {depth} bits, which is not readable.");

    var end = text.IndexOf("%%EndPreview", begin, StringComparison.Ordinal);
    if (end < 0)
      end = text.Length;

    var samples = _ReadHexNibbles(text.AsSpan((lineEnd + 1)..end));
    var stride = ((width * depth) + 7) / 8;
    var pixels = new byte[width * height * 3];
    var maximum = (1 << depth) - 1;

    for (var y = 0; y < height; ++y) {
      for (var x = 0; x < width; ++x) {
        var bitOffset = (x * depth);
        var at = (y * stride) + (bitOffset / 8);
        var sample = at < samples.Length
          ? (samples[at] >> (8 - depth - (bitOffset % 8))) & maximum
          : 0;

        var grey = (byte)(sample * 255 / maximum);
        var target = (((y * width) + x) * 3);
        pixels[target] = grey;
        pixels[target + 1] = grey;
        pixels[target + 2] = grey;
      }
    }

    return new EpsFile {
      Width = width,
      Height = height,
      PixelData = pixels,
    };
  }

  /// <summary>The hex bytes of a preview block, ignoring the "%" that opens each line.</summary>
  private static byte[] _ReadHexNibbles(ReadOnlySpan<char> body) {
    var bytes = new List<byte>(body.Length / 2);
    var high = -1;
    foreach (var c in body) {
      var value = c switch {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
      };

      if (value < 0)
        continue;

      if (high < 0) {
        high = value;
      } else {
        bytes.Add((byte)((high << 4) | value));
        high = -1;
      }
    }

    return bytes.ToArray();
  }
}
