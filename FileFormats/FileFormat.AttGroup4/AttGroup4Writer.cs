using System;

namespace FileFormat.AttGroup4;

/// <summary>Assembles AT&amp;T Group 4 fax file bytes from an AttGroup4File.</summary>
public static class AttGroup4Writer {

  public static byte[] ToBytes(AttGroup4File file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[AttGroup4File.HeaderSize + pixelDataSize];

    result[0] = AttGroup4File.Magic[0];
    result[1] = AttGroup4File.Magic[1];
    result[2] = AttGroup4File.Magic[2];
    result[3] = AttGroup4File.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(AttGroup4File.HeaderSize));

    return result;
  }
}
