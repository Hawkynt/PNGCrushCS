using System;
using System.IO;

namespace FileFormat.Graphics9Plus;

/// <summary>Reads Atari 8-bit Graphics 9+ (.gr9p) screens. from bytes, streams, or file paths.</summary>
public static class Graphics9PlusReader {

  public static Graphics9PlusFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Graphics9PlusFile FromStream(Stream stream) {
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

  public static Graphics9PlusFile FromSpan(ReadOnlySpan<byte> data) {
    // The format carries no signature; its fixed size is the only thing identifying it.
    if (data.Length != Graphics9PlusFile.FileSize)
      throw new InvalidDataException($"This format is exactly {Graphics9PlusFile.FileSize} bytes, got {data.Length}.");

    var header = new byte[Graphics9PlusFile.HeaderSize];
    data[..Graphics9PlusFile.HeaderSize].CopyTo(header);

    var screen = new byte[Graphics9PlusFile.ScreenDataSize];
    data.Slice(Graphics9PlusFile.HeaderSize, Graphics9PlusFile.ScreenDataSize).CopyTo(screen);

    return new() { Header = header, ScreenData = screen };
  }

  public static Graphics9PlusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
