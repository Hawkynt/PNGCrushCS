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

  public static HireslaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < HireslaceFile.ExpectedFileSize)
      throw new InvalidDataException($"Hireslace file must be at least {HireslaceFile.ExpectedFileSize} bytes, got {data.Length}.");

    var offset = 0;

    // Load address (2 bytes LE)
    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += HireslaceFile.LoadAddressSize;

    // Frame 1: bitmap (8000) + screen (1000)
    var bitmap1 = new byte[HireslaceFile.BitmapDataSize];
    data.AsSpan(offset, HireslaceFile.BitmapDataSize).CopyTo(bitmap1);
    offset += HireslaceFile.BitmapDataSize;

    var screen1 = new byte[HireslaceFile.ScreenDataSize];
    data.AsSpan(offset, HireslaceFile.ScreenDataSize).CopyTo(screen1);
    offset += HireslaceFile.ScreenDataSize;

    // Frame 2: bitmap (8000) + screen (1000)
    var bitmap2 = new byte[HireslaceFile.BitmapDataSize];
    data.AsSpan(offset, HireslaceFile.BitmapDataSize).CopyTo(bitmap2);
    offset += HireslaceFile.BitmapDataSize;

    var screen2 = new byte[HireslaceFile.ScreenDataSize];
    data.AsSpan(offset, HireslaceFile.ScreenDataSize).CopyTo(screen2);

    return new HireslaceFile {
      LoadAddress = loadAddress,
      Bitmap1 = bitmap1,
      Screen1 = screen1,
      Bitmap2 = bitmap2,
      Screen2 = screen2,
    };
  }
}
