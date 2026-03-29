using System;
using System.Buffers.Binary;

namespace FileFormat.MegaPaint;

/// <summary>Assembles Atari ST MegaPaint monochrome image bytes from a MegaPaintFile.</summary>
public static class MegaPaintWriter {

  public static byte[] ToBytes(MegaPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bytesPerRow = (file.Width + 7) / 8;
    var pixelDataSize = bytesPerRow * file.Height;
    var result = new byte[MegaPaintFile.HeaderSize + pixelDataSize];
    var span = result.AsSpan();

    BinaryPrimitives.WriteUInt16BigEndian(span, (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)file.Height);
    // bytes 4-7 are reserved (zero)

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixelDataSize)).CopyTo(result.AsSpan(MegaPaintFile.HeaderSize));

    return result;
  }
}
