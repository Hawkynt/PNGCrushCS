using System;
using System.IO;

namespace FileFormat.AtariGrayscale9;

/// <summary>Reads Atari 8-bit Graphics 9 greyscale (.bg9/.g09) screens. from bytes, streams, or file paths.</summary>
public static class AtariGrayscale9Reader {

  public static AtariGrayscale9File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("File not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariGrayscale9File FromStream(Stream stream) {
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

  public static AtariGrayscale9File FromSpan(ReadOnlySpan<byte> data) {
    // The format carries no signature; its fixed size is the only thing identifying it.
    if (data.Length != AtariGrayscale9File.FileSize)
      throw new InvalidDataException($"This format is exactly {AtariGrayscale9File.FileSize} bytes, got {data.Length}.");

    var header = new byte[AtariGrayscale9File.HeaderSize];
    data[..AtariGrayscale9File.HeaderSize].CopyTo(header);

    var screen = new byte[AtariGrayscale9File.ScreenDataSize];
    data.Slice(AtariGrayscale9File.HeaderSize, AtariGrayscale9File.ScreenDataSize).CopyTo(screen);

    return new() { Header = header, ScreenData = screen };
  }

  public static AtariGrayscale9File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
