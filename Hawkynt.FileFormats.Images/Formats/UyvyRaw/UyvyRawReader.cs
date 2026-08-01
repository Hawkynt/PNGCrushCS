using System;
using System.IO;

namespace FileFormat.UyvyRaw;

/// <summary>Reads raw UYVY 4:2:2 streams from bytes, streams, or file paths.</summary>
public static class UyvyRawReader {

  public static UyvyRawFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Stream not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static UyvyRawFile FromStream(Stream stream) {
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

  public static UyvyRawFile FromSpan(ReadOnlySpan<byte> data) {
    var (width, height) = UyvyRawFile.SizeOf(data.Length);

    return new() { Width = width, Height = height, PixelData = data.ToArray() };
  }

  public static UyvyRawFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
