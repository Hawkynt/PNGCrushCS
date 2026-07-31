using System;
using System.IO;

namespace FileFormat.HiresC64;

/// <summary>Reads Commodore 64 bare hires bitmap files from bytes, streams, or file paths.</summary>
public static class HiresC64Reader {

  public static HiresC64File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hires C64 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HiresC64File FromStream(Stream stream) {
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

  public static HiresC64File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != HiresC64File.ExpectedFileSize)
      throw new InvalidDataException(
        $"Invalid hires C64 file size (expected {HiresC64File.ExpectedFileSize} bytes, got {data.Length}).");

    var bitmap = new byte[HiresC64File.BitmapDataSize];
    data.Slice(HiresC64File.BitmapOffset, HiresC64File.BitmapDataSize).CopyTo(bitmap.AsSpan(0));

    return new() {
      LoadAddress = (ushort)(data[0] | (data[1] << 8)),
      BitmapData = bitmap,
    };
  }

  public static HiresC64File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
