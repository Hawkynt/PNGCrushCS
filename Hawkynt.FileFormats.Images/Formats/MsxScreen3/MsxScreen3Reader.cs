using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxScreen3;

/// <summary>Reads MSX Screen 3 pictures from bytes, streams, or file paths.</summary>
public static class MsxScreen3Reader {

  public static MsxScreen3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxScreen3File FromStream(Stream stream) {
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

  public static MsxScreen3File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MsxScreen3File.MinimumFileSize || data[0] != MsxGraphics.BsaveMagic
        || MsxGraphics.ReadBsaveEndAddress(data) < MsxScreen3File.MinimumFileSize - MsxGraphics.BsaveHeaderSize - 1)
      throw new InvalidDataException($"Not an MSX Screen 3 picture: {data.Length} bytes.");

    return new() { Data = data.ToArray() };
  }

  public static MsxScreen3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
