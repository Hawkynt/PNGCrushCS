using System;
using System.IO;

namespace FileFormat.Zoom4;

/// <summary>Reads Atari 8-bit Zoom-4 graphics editor (.zm4) screens. from bytes, streams, or file paths.</summary>
public static class Zoom4Reader {

  public static Zoom4File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Zoom4File FromStream(Stream stream) {
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

  public static Zoom4File FromSpan(ReadOnlySpan<byte> data) {
    // The format carries no signature; its fixed size is the only thing identifying it.
    if (data.Length != Zoom4File.FileSize)
      throw new InvalidDataException($"This format is exactly {Zoom4File.FileSize} bytes, got {data.Length}.");

    var header = new byte[Zoom4File.HeaderSize];
    data[..Zoom4File.HeaderSize].CopyTo(header);

    var screen = new byte[Zoom4File.ScreenDataSize];
    data.Slice(Zoom4File.HeaderSize, Zoom4File.ScreenDataSize).CopyTo(screen);

    return new() { Header = header, ScreenData = screen };
  }

  public static Zoom4File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
