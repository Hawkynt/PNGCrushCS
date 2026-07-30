using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.InterPainter;

/// <summary>Reads InterPainter / ING 15 files from bytes, streams, or file paths.</summary>
public static class InterPainterReader {

  public static InterPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("InterPainter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterPainterFile FromStream(Stream stream) {
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

  public static InterPainterFile FromSpan(ReadOnlySpan<byte> data) {
    // An .ins file is these same bytes under the SFDN packer; unpack and there is one format left.
    if (SfdnDecompressor.IsSfdn(data)) {
      var unpacked = SfdnDecompressor.TryUnpack(data, InterPainterFile.FileSize)
        ?? throw new InvalidDataException("Not an InterPainter picture: the SFDN data does not unpack to a screen.");

      return FromSpan((ReadOnlySpan<byte>)unpacked);
    }

    // Some files carry a few trailing bytes of loader data, which readers ignore.
    if (data.Length < InterPainterFile.FileSize)
      throw new InvalidDataException($"An InterPainter file is at least {InterPainterFile.FileSize} bytes, got {data.Length}.");

    var first = new byte[InterPainterFile.FrameDataSize];
    data[..InterPainterFile.FrameDataSize].CopyTo(first);

    var second = new byte[InterPainterFile.FrameDataSize];
    data.Slice(InterPainterFile.SecondFrameOffset, InterPainterFile.FrameDataSize).CopyTo(second);

    var colors = new byte[InterPainterFile.ColorCount];
    data.Slice(InterPainterFile.ColorsOffset, InterPainterFile.ColorCount).CopyTo(colors);

    return new() { FirstFrame = first, SecondFrame = second, Colors = colors };
  }

  public static InterPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
