using System;
using System.IO;

namespace FileFormat.IffAnim8;

/// <summary>Reads IFF ANIM8 (Long-word delta animation) files from bytes, streams, or file paths.</summary>
public static class IffAnim8Reader {

  public static IffAnim8File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ANIM8 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffAnim8File FromStream(Stream stream) {
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

  public static IffAnim8File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < IffAnim8File.MinFileSize)
      throw new InvalidDataException($"Invalid ANIM8 data: expected at least {IffAnim8File.MinFileSize} bytes, got {data.Length}.");

    var width = IffAnim8File.DefaultWidth;
    var height = IffAnim8File.DefaultHeight;

    _TryParseBmhd(data, out width, out height);

    var rawData = new byte[data.Length];
    data.AsSpan(0, data.Length).CopyTo(rawData);

    return new() {
      Width = width,
      Height = height,
      RawData = rawData,
    };
  }

  private static void _TryParseBmhd(byte[] data, out int width, out int height) {
    width = IffAnim8File.DefaultWidth;
    height = IffAnim8File.DefaultHeight;

    for (var i = 0; i < data.Length - 24; ++i) {
      if (data[i] != 0x42 || data[i + 1] != 0x4D || data[i + 2] != 0x48 || data[i + 3] != 0x44)
        continue;

      var offset = i + 8;
      if (offset + 4 > data.Length)
        return;

      width = (data[offset] << 8) | data[offset + 1];
      height = (data[offset + 2] << 8) | data[offset + 3];

      if (width <= 0 || height <= 0) {
        width = IffAnim8File.DefaultWidth;
        height = IffAnim8File.DefaultHeight;
      }

      return;
    }
  }
}
