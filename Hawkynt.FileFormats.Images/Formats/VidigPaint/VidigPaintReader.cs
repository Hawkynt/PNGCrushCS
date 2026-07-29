using System;
using System.IO;

namespace FileFormat.VidigPaint;

/// <summary>Reads Atari 8-bit Vidig Paint (.rap) screens. from bytes, streams, or file paths.</summary>
public static class VidigPaintReader {

  public static VidigPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static VidigPaintFile FromStream(Stream stream) {
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

  public static VidigPaintFile FromSpan(ReadOnlySpan<byte> data) {
    // The format carries no signature; its fixed size is the only thing identifying it.
    if (data.Length != VidigPaintFile.FileSize)
      throw new InvalidDataException($"This format is exactly {VidigPaintFile.FileSize} bytes, got {data.Length}.");

    var header = new byte[VidigPaintFile.HeaderSize];
    data[..VidigPaintFile.HeaderSize].CopyTo(header);

    var screen = new byte[VidigPaintFile.ScreenDataSize];
    data.Slice(VidigPaintFile.HeaderSize, VidigPaintFile.ScreenDataSize).CopyTo(screen);

    return new() { Header = header, ScreenData = screen, BackgroundColor = data[VidigPaintFile.BackgroundColorOffset] };
  }

  public static VidigPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
