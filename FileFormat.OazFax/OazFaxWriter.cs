using System;

namespace FileFormat.OazFax;

/// <summary>Assembles OazFax OAZ file bytes from an OazFaxFile.</summary>
public static class OazFaxWriter {

  public static byte[] ToBytes(OazFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[OazFaxFile.HeaderSize + pixelDataSize];

    result[0] = OazFaxFile.Magic[0];
    result[1] = OazFaxFile.Magic[1];
    result[2] = OazFaxFile.Magic[2];
    result[3] = OazFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Encoding);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(OazFaxFile.HeaderSize));

    return result;
  }
}
