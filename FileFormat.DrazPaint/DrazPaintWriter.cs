using System;

namespace FileFormat.DrazPaint;

/// <summary>Assembles DrazPaint (.drz) file bytes from a DrazPaintFile.</summary>
public static class DrazPaintWriter {

  public static byte[] ToBytes(DrazPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var payload = new byte[DrazPaintFile.UncompressedPayloadSize];
    var offset = 0;

    file.BitmapData.AsSpan(0, DrazPaintFile.BitmapDataSize).CopyTo(payload.AsSpan(offset));
    offset += DrazPaintFile.BitmapDataSize;

    file.ScreenRam.AsSpan(0, DrazPaintFile.ScreenRamSize).CopyTo(payload.AsSpan(offset));
    offset += DrazPaintFile.ScreenRamSize;

    file.ColorRam.AsSpan(0, DrazPaintFile.ColorRamSize).CopyTo(payload.AsSpan(offset));
    offset += DrazPaintFile.ColorRamSize;

    payload[offset] = file.BackgroundColor;

    var compressed = DrazPaintFile.RleEncode(payload);

    var result = new byte[DrazPaintFile.LoadAddressSize + compressed.Length];
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);
    compressed.AsSpan(0, compressed.Length).CopyTo(result.AsSpan(DrazPaintFile.LoadAddressSize));

    return result;
  }
}
