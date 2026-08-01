using System;
using System.IO;

namespace FileFormat.G9b;

/// <summary>Reads V9990 GFX9000 (.g9b) files from bytes, streams, or file paths.</summary>
public static class G9bReader {

  internal static readonly byte[] Magic = [0x47, 0x39, 0x42];

  public static G9bFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("G9B file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static G9bFile FromStream(Stream stream) {
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

  public static G9bFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < G9bFile.FixedHeaderSize + 1)
      throw new InvalidDataException("Too short to be a G9B file.");

    if (data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2]
        || data[3] != G9bFile.Version || data[4] != 0)
      throw new InvalidDataException("Not a G9B file.");

    var depth = data[5];
    if (depth is not (2 or 4 or 8 or 16))
      throw new InvalidDataException($"A G9B pixel is 2, 4, 8 or 16 bits, not {depth}.");

    var entries = data[7];
    var headerLength = G9bFile.FixedHeaderSize + entries * 3;
    var width = data[8] | (data[9] << 8);
    var height = data[10] | (data[11] << 8);
    if (width <= 0 || height <= 0)
      throw new InvalidDataException($"A G9B states no size: {width}x{height}.");

    // The packed form is a bitstream this reader does not carry, and reading its bytes as pixels
    // would give a picture rather than an error, which is worse than refusing.
    if (data[12] != 0)
      throw new InvalidDataException("A packed G9B is not read here yet.");

    var stride = (width * depth + 7) >> 3;
    if (data.Length < headerLength + stride * height)
      throw new InvalidDataException(
        $"{width}x{height} at {depth} bits needs {headerLength + stride * height} bytes; this file is {data.Length}.");

    return new() {
      Width = width,
      Height = height,
      Depth = depth,
      ColorMode = data[6],
      Palette = data.Slice(G9bFile.FixedHeaderSize, entries * 3).ToArray(),
      PixelData = data.Slice(headerLength, stride * height).ToArray(),
    };
  }

  public static G9bFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
