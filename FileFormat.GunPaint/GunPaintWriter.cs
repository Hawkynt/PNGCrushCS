using System;

namespace FileFormat.GunPaint;

/// <summary>Assembles C64 GunPaint FLI file bytes from a GunPaintFile.</summary>
public static class GunPaintWriter {

  public static byte[] ToBytes(GunPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[GunPaintFile.ExpectedFileSize];

    // Load address (2 bytes, little-endian)
    result[0] = (byte)(file.LoadAddress & 0xFF);
    result[1] = (byte)(file.LoadAddress >> 8);

    // Raw data payload
    var copyLength = Math.Min(file.RawData.Length, GunPaintFile.RawDataSize);
    file.RawData.AsSpan(0, copyLength).CopyTo(result.AsSpan(GunPaintFile.LoadAddressSize));

    return result;
  }
}
