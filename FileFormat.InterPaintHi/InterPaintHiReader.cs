using System;
using System.IO;

namespace FileFormat.InterPaintHi;

/// <summary>Reads Commodore 64 InterPaint Hires files from bytes, streams, or file paths.</summary>
public static class InterPaintHiReader {

  public static InterPaintHiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("InterPaint Hires file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterPaintHiFile FromStream(Stream stream) {
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

  public static InterPaintHiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < InterPaintHiFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid InterPaint Hires file (expected {InterPaintHiFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != InterPaintHiFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid InterPaint Hires file size (expected {InterPaintHiFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += InterPaintHiFile.LoadAddressSize;

    var bitmapData = new byte[InterPaintHiFile.BitmapDataSize];
    data.AsSpan(offset, InterPaintHiFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += InterPaintHiFile.BitmapDataSize;

    var screenRam = new byte[InterPaintHiFile.ScreenRamSize];
    data.AsSpan(offset, InterPaintHiFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
    };
  }
}
