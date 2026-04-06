using System;
using System.IO;

namespace FileFormat.SaracenPaint;

/// <summary>Reads Saracen Paint C64 hires files from bytes, streams, or file paths.</summary>
public static class SaracenPaintReader {

  public static SaracenPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Saracen Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SaracenPaintFile FromStream(Stream stream) {
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

  public static SaracenPaintFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static SaracenPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < SaracenPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid Saracen Paint file (expected {SaracenPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    if (data.Length != SaracenPaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid Saracen Paint file size (expected {SaracenPaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    var offset = 0;

    var loadAddress = (ushort)(data[offset] | (data[offset + 1] << 8));
    offset += SaracenPaintFile.LoadAddressSize;

    // Layout: loadAddress(2) + screenRam(1000) + bitmapData(8000) + padding(7)
    var screenRam = new byte[SaracenPaintFile.ScreenRamSize];
    data.AsSpan(offset, SaracenPaintFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += SaracenPaintFile.ScreenRamSize;

    var bitmapData = new byte[SaracenPaintFile.BitmapDataSize];
    data.AsSpan(offset, SaracenPaintFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));

    return new() {
      LoadAddress = loadAddress,
      ScreenRam = screenRam,
      BitmapData = bitmapData,
    };
  }
}
