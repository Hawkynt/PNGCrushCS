using System;
using System.IO;

namespace FileFormat.Hireslace;

/// <summary>Reads C64 Hireslace Editor (.hle) files from bytes, streams, or file paths.</summary>
public static class HireslaceReader {

  public static HireslaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hireslace Editor file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HireslaceFile FromStream(Stream stream) {
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

  public static HireslaceFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < HireslaceFile.ExpectedFileSize)
      throw new InvalidDataException($"Hireslace file must be at least {HireslaceFile.ExpectedFileSize} bytes, got {data.Length}.");

    // Load address (2 bytes LE), then four 8 KB slots: bitmap1, screen1, screen2, bitmap2.
    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var bitmap1 = new byte[HireslaceFile.BitmapDataSize];
    data.Slice(HireslaceFile.Bitmap1Offset, HireslaceFile.BitmapDataSize).CopyTo(bitmap1);

    var screen1 = new byte[HireslaceFile.ScreenDataSize];
    data.Slice(HireslaceFile.Screen1Offset, HireslaceFile.ScreenDataSize).CopyTo(screen1);

    var screen2 = new byte[HireslaceFile.ScreenDataSize];
    data.Slice(HireslaceFile.Screen2Offset, HireslaceFile.ScreenDataSize).CopyTo(screen2);

    var bitmap2 = new byte[HireslaceFile.BitmapDataSize];
    data.Slice(HireslaceFile.Bitmap2Offset, HireslaceFile.BitmapDataSize).CopyTo(bitmap2);

    return new HireslaceFile {
      LoadAddress = loadAddress,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
    };
    }

  public static HireslaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
