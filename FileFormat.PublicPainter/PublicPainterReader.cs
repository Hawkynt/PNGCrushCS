using System;
using System.IO;

namespace FileFormat.PublicPainter;

/// <summary>Reads Public Painter (.cmp) files from bytes, streams, or file paths.</summary>
public static class PublicPainterReader {

  public static PublicPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Public Painter file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PublicPainterFile FromStream(Stream stream) {
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

  public static PublicPainterFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static PublicPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 2)
      throw new InvalidDataException("Data too small for a valid Public Painter file.");

    var pixelData = PublicPainterCompressor.Decompress(data.AsSpan(), PublicPainterFile.DecompressedSize);

    return new PublicPainterFile {
      Width = PublicPainterFile.ImageWidth,
      Height = PublicPainterFile.ImageHeight,
      PixelData = pixelData,
    };
  }
}
