using System;
using System.IO;

namespace FileFormat.RunPaint;

/// <summary>Reads Run Paint (.rpm) files from bytes, streams, or file paths.</summary>
public static class RunPaintReader {

  public static RunPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Run Paint file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static RunPaintFile FromStream(Stream stream) {
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

  public static RunPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < RunPaintFile.LoadAddressSize + 1)
      throw new InvalidDataException($"Data too small for a valid Run Paint file (got {data.Length} bytes).");

    var loadAddress = (ushort)(data[0] | (data[1] << 8));

    var compressed = new byte[data.Length - RunPaintFile.LoadAddressSize];
    data.AsSpan(RunPaintFile.LoadAddressSize, compressed.Length).CopyTo(compressed.AsSpan(0));

    var decompressed = RunPaintFile.RleDecode(compressed);
    if (decompressed.Length < RunPaintFile.UncompressedPayloadSize)
      throw new InvalidDataException($"Decompressed data too small (expected at least {RunPaintFile.UncompressedPayloadSize} bytes, got {decompressed.Length}).");

    var offset = 0;

    var bitmapData = new byte[RunPaintFile.BitmapDataSize];
    decompressed.AsSpan(offset, RunPaintFile.BitmapDataSize).CopyTo(bitmapData.AsSpan(0));
    offset += RunPaintFile.BitmapDataSize;

    var screenRam = new byte[RunPaintFile.ScreenRamSize];
    decompressed.AsSpan(offset, RunPaintFile.ScreenRamSize).CopyTo(screenRam.AsSpan(0));
    offset += RunPaintFile.ScreenRamSize;

    var colorRam = new byte[RunPaintFile.ColorRamSize];
    decompressed.AsSpan(offset, RunPaintFile.ColorRamSize).CopyTo(colorRam.AsSpan(0));
    offset += RunPaintFile.ColorRamSize;

    var backgroundColor = decompressed[offset];

    return new() {
      LoadAddress = loadAddress,
      BitmapData = bitmapData,
      ScreenRam = screenRam,
      ColorRam = colorRam,
      BackgroundColor = backgroundColor,
    };
  }
}
