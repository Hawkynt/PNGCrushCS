using System;
using System.IO;
using System.Text;

namespace FileFormat.Pgx;

/// <summary>Reads PGX images from bytes, streams, or file paths.</summary>
public static class PgxReader {

  public static PgxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PgxFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  public static PgxFile FromSpan(ReadOnlySpan<byte> data) {
    var newline = data.IndexOf((byte)'\n');
    if (newline < 0 || !data[..PgxFile.Signature.Length].SequenceEqual("PG"u8))
      throw new InvalidDataException("Not a PGX image.");

    var header = Encoding.ASCII.GetString(data[..newline]).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // "PG", byte order, an optional sign, the depth, and the two dimensions.
    if (header.Length < 5)
      throw new InvalidDataException("A PGX header names a byte order, a depth and a size.");

    var isBigEndian = header[1] == "ML";
    var at = 2;
    var isSigned = false;
    if (header[at] is "+" or "-") {
      isSigned = header[at] == "-";
      ++at;
    }

    if (header.Length < at + 3
        || !int.TryParse(header[at], out var depth)
        || !int.TryParse(header[at + 1], out var width)
        || !int.TryParse(header[at + 2], out var height))
      throw new InvalidDataException("A PGX header does not name a depth and a size.");

    if (depth is < 1 or > 16 || width < 1 || height < 1)
      throw new InvalidDataException($"A PGX image is not {width}x{height} at {depth} bits.");

    var bytesPerSample = depth > 8 ? 2 : 1;
    var offset = newline + 1;
    var count = width * height;
    if (offset + count * bytesPerSample > data.Length)
      throw new InvalidDataException("A PGX image is shorter than its header says.");

    var samples = new byte[count];
    var maximum = (1 << depth) - 1;

    for (var i = 0; i < count; ++i) {
      int value;
      if (bytesPerSample == 1)
        value = data[offset + i];
      else {
        var a = data[offset + i * 2];
        var b = data[offset + i * 2 + 1];
        value = isBigEndian ? (a << 8) | b : (b << 8) | a;
      }

      // A signed sample is biased so the darkest is the smallest; unsigned needs only widening.
      if (isSigned)
        value += 1 << (depth - 1);

      value &= maximum;
      samples[i] = depth == 8 ? (byte)value : (byte)(value * 255 / maximum);
    }

    return new() {
      Width = width,
      Height = height,
      Depth = depth,
      IsSigned = isSigned,
      IsBigEndian = isBigEndian,
      Samples = samples,
    };
  }

  public static PgxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
