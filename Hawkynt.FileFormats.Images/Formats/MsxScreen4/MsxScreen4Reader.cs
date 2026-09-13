using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MsxScreen4;

/// <summary>Reads MSX Screen 4 pictures from bytes, streams, or file paths.</summary>
public static class MsxScreen4Reader {

  public static MsxScreen4File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MsxScreen4File FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static MsxScreen4File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < MsxScreen4File.MinimumFileSize || data[0] != MsxGraphics.BsaveMagic
        || MsxGraphics.ReadBsaveEndAddress(data) < MsxScreen4File.VramSize - 1)
      throw new InvalidDataException($"Not an MSX Screen 4 picture: {data.Length} bytes.");

    return new() { Data = data.ToArray() };
  }

  public static MsxScreen4File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
