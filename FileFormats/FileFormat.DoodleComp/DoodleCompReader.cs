using System;
using System.IO;

namespace FileFormat.DoodleComp;

/// <summary>Reads Commodore 64 Doodle Compressed hires files from bytes, streams, or file paths.</summary>
public static class DoodleCompReader {

  public static DoodleCompFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("DoodleComp file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static DoodleCompFile FromStream(Stream stream) {
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

  public static DoodleCompFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < DoodleCompFile.MinimumFileSize)
      throw new InvalidDataException($"Data too small for a valid DoodleComp file (minimum {DoodleCompFile.MinimumFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += DoodleCompFile.LoadAddressSize;

    var decompressed = _Decompress(data, offset);
    if (decompressed.Length < DoodleCompFile.DecompressedDataSize)
      throw new InvalidDataException($"Decompressed data too small (expected {DoodleCompFile.DecompressedDataSize} bytes, got {decompressed.Length}).");

    var bitmapData = new byte[DoodleCompFile.BitmapDataSize];
    decompressed.AsSpan(0, DoodleCompFile.BitmapDataSize).CopyTo(bitmapData);

    var screenRam = new byte[DoodleCompFile.ScreenRamSize];
    decompressed.AsSpan(DoodleCompFile.BitmapDataSize, DoodleCompFile.ScreenRamSize).CopyTo(screenRam);

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
    };
  }

  public static DoodleCompFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static byte[] _Decompress(ReadOnlySpan<byte> data, int startOffset) {
    using var output = new MemoryStream();
    var i = startOffset;
    while (i < data.Length) {
      var current = data[i++];
      if (current == DoodleCompFile.RleEscapeByte) {
        if (i + 1 >= data.Length)
          break;

        var count = data[i++];
        var value = data[i++];
        for (var j = 0; j < count; ++j)
          output.WriteByte(value);
      } else
        output.WriteByte(current);
    }

    return output.ToArray();
  }
}
