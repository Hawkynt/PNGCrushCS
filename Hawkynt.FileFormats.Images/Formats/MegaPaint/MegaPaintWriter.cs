using System;

namespace FileFormat.MegaPaint;

/// <summary>Assembles Atari ST MegaPaint monochrome image bytes from a MegaPaintFile.</summary>
public static class MegaPaintWriter {

  public static byte[] ToBytes(MegaPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bytesPerRow = (file.Width + 7) / 8;
    var pixelDataSize = bytesPerRow * file.Height;
    var result = new byte[MegaPaintHeader.StructSize + pixelDataSize];
    var span = result.AsSpan();

    new MegaPaintHeader((ushort)file.Width, (ushort)file.Height, 0).WriteTo(span);

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixelDataSize)).CopyTo(result.AsSpan(MegaPaintHeader.StructSize));

    return result;
  }
}
