using System;

namespace FileFormat.AdTechFax;

/// <summary>Assembles AdTech fax file bytes from an AdTechFaxFile.</summary>
public static class AdTechFaxWriter {

  public static byte[] ToBytes(AdTechFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[AdTechFaxFile.HeaderSize + pixelDataSize];

    result[0] = AdTechFaxFile.Magic[0];
    result[1] = AdTechFaxFile.Magic[1];
    result[2] = AdTechFaxFile.Magic[2];
    result[3] = AdTechFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Resolution);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Reserved);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(AdTechFaxFile.HeaderSize));

    return result;
  }
}
