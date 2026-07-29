using System;

namespace FileFormat.Deluxe;

/// <summary>Assembles Deluxe Paint ST file bytes from an in-memory representation.</summary>
public static class DeluxeWriter {

  public static byte[] ToBytes(DeluxeFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DeluxeFile.FileSize];
    var span = result.AsSpan();

    var header = new DeluxeHeader((short)file.Resolution, file.Palette);
    header.WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(32000, file.PixelData.Length)).CopyTo(result.AsSpan(DeluxeHeader.StructSize));

    return result;
  }
}
