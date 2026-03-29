using System;
using System.IO;

namespace FileFormat.TruePaint;

/// <summary>Reads True Paint (.mci) files from bytes, streams, or file paths.</summary>
public static class TruePaintReader {

  public static TruePaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("True Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TruePaintFile FromStream(Stream stream) {
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

  public static TruePaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != TruePaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid True Paint file size (expected {TruePaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var offset = TruePaintFile.LoadAddressSize;

    var bitmapData1 = new byte[TruePaintFile.BitmapDataSize];
    data.AsSpan(offset, TruePaintFile.BitmapDataSize).CopyTo(bitmapData1);
    offset += TruePaintFile.BitmapDataSize;

    var screenRam1 = new byte[TruePaintFile.ScreenRamSize];
    data.AsSpan(offset, TruePaintFile.ScreenRamSize).CopyTo(screenRam1);
    offset += TruePaintFile.ScreenRamSize;

    var bitmapData2 = new byte[TruePaintFile.BitmapDataSize];
    data.AsSpan(offset, TruePaintFile.BitmapDataSize).CopyTo(bitmapData2);
    offset += TruePaintFile.BitmapDataSize;

    var screenRam2 = new byte[TruePaintFile.ScreenRamSize];
    data.AsSpan(offset, TruePaintFile.ScreenRamSize).CopyTo(screenRam2);
    offset += TruePaintFile.ScreenRamSize;

    var colorRam = new byte[TruePaintFile.ColorRamSize];
    data.AsSpan(offset, TruePaintFile.ColorRamSize).CopyTo(colorRam);
    offset += TruePaintFile.ColorRamSize;

    var backgroundColor = data[offset];
    var borderColor = data[offset + 1];

    return new() {
      LoadAddress = loadAddress,
      BitmapData1 = bitmapData1,
      ScreenRam1 = screenRam1,
      BitmapData2 = bitmapData2,
      ScreenRam2 = screenRam2,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
      BorderColor = borderColor,
    };
  }
}
