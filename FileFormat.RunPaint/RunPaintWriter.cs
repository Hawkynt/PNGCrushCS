using System;

namespace FileFormat.RunPaint;

/// <summary>Assembles Run Paint (.rpm) file bytes from a RunPaintFile.</summary>
public static class RunPaintWriter {

  public static byte[] ToBytes(RunPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var payload = new byte[RunPaintFile.UncompressedPayloadSize];
    var offset = 0;

    file.BitmapData.AsSpan(0, RunPaintFile.BitmapDataSize).CopyTo(payload.AsSpan(offset));
    offset += RunPaintFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, RunPaintFile.ScreenRamSize).CopyTo(payload.AsSpan(offset));
    offset += RunPaintFile.ScreenRamSize;

    file.ColorRam.AsSpan(0, RunPaintFile.ColorRamSize).CopyTo(payload.AsSpan(offset));
    offset += RunPaintFile.ColorRamSize;

    payload[offset] = file.BackgroundColor;

    var compressed = RunPaintFile.RleEncode(payload);

    var result = new byte[RunPaintFile.LoadAddressSize + compressed.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    compressed.AsSpan(0, compressed.Length).CopyTo(result.AsSpan(RunPaintFile.LoadAddressSize));

    return result;
  }
}
