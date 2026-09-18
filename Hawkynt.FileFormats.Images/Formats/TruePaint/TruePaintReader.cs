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

  public static TruePaintFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static TruePaintFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != TruePaintFile.ExpectedFileSize)
      throw new InvalidDataException($"Invalid True Paint file size (expected {TruePaintFile.ExpectedFileSize} bytes, got {data.Length}).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var bitmapData1 = new byte[TruePaintFile.BitmapDataSize];
    data.Slice(TruePaintFile.BitmapData1Offset, TruePaintFile.BitmapDataSize).CopyTo(bitmapData1);

    var screenRam1 = new byte[TruePaintFile.ScreenRamSize];
    data.Slice(TruePaintFile.ScreenRam1Offset, TruePaintFile.ScreenRamSize).CopyTo(screenRam1);

    var bitmapData2 = new byte[TruePaintFile.BitmapDataSize];
    data.Slice(TruePaintFile.BitmapData2Offset, TruePaintFile.BitmapDataSize).CopyTo(bitmapData2);

    var screenRam2 = new byte[TruePaintFile.ScreenRamSize];
    data.Slice(TruePaintFile.ScreenRam2Offset, TruePaintFile.ScreenRamSize).CopyTo(screenRam2);

    var colorRam = new byte[TruePaintFile.ColorRamSize];
    data.Slice(TruePaintFile.ColorRamOffset, TruePaintFile.ColorRamSize).CopyTo(colorRam);

    return new() {
      LoadAddress = loadAddress,
      BitmapData1 = bitmapData1,
      ScreenRam1 = screenRam1,
      BitmapData2 = bitmapData2,
      ScreenRam2 = screenRam2,
      ColorRam = colorRam,
      BackgroundColor = data[TruePaintFile.BackgroundOffset],
      BorderColor = data[TruePaintFile.BorderOffset],
    };
  }

  public static TruePaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
