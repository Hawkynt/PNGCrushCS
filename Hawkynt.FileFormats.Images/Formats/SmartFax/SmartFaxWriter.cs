using System;

namespace FileFormat.SmartFax;

/// <summary>Assembles SmartFax SMF file bytes from a SmartFaxFile.</summary>
public static class SmartFaxWriter {

  public static byte[] ToBytes(SmartFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[SmartFaxFile.HeaderSize + pixelDataSize];

    result[0] = SmartFaxFile.Magic[0];
    result[1] = SmartFaxFile.Magic[1];
    result[2] = SmartFaxFile.Magic[2];
    result[3] = SmartFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Flags);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(SmartFaxFile.HeaderSize));

    return result;
  }
}
