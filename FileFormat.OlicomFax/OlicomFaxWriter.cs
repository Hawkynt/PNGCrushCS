using System;

namespace FileFormat.OlicomFax;

/// <summary>Assembles OlicomFax OFX file bytes from an OlicomFaxFile.</summary>
public static class OlicomFaxWriter {

  public static byte[] ToBytes(OlicomFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[OlicomFaxFile.HeaderSize + pixelDataSize];

    result[0] = OlicomFaxFile.Magic[0];
    result[1] = OlicomFaxFile.Magic[1];
    result[2] = OlicomFaxFile.Magic[2];
    result[3] = OlicomFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Flags);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(OlicomFaxFile.HeaderSize));

    return result;
  }
}
