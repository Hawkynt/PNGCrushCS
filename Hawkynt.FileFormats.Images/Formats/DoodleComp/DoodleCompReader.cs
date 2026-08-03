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
      throw new InvalidDataException($"A compressed Doodle holds {DoodleCompFile.DecompressedDataSize} bytes of screen and bitmap; this one expands to {decompressed.Length}.");

    // The screen comes first and sits in a kilobyte, then the bitmap — which is where each lands in
    // the machine, the file loading at 0x5C00 and the bitmap at 0x6000. This took the bitmap first
    // and the screen after it, so the colours belonged to the wrong cells and were themselves read
    // out of the middle of the bitmap.
    var screenRam = new byte[DoodleCompFile.ScreenRamSize];
    decompressed.AsSpan(0, DoodleCompFile.ScreenRamSize).CopyTo(screenRam);

    var bitmapData = new byte[DoodleCompFile.BitmapDataSize];
    decompressed.AsSpan(DoodleCompFile.ScreenRamPaddedSize, DoodleCompFile.BitmapDataSize).CopyTo(bitmapData);

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

        // The value comes before the count. These were read the other way round, which expanded the
        // sample to 23233 bytes rather than the 9024 a screen and a bitmap take.
        var value = data[i++];
        var count = data[i++];
        for (var j = 0; j < count; ++j)
          output.WriteByte(value);
      } else
        output.WriteByte(current);
    }

    return output.ToArray();
  }
}
