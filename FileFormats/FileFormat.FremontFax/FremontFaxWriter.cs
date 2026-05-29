using System;

namespace FileFormat.FremontFax;

/// <summary>Assembles Fremont Fax F96 file bytes from a FremontFaxFile.</summary>
public static class FremontFaxWriter {

  public static byte[] ToBytes(FremontFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[FremontFaxFile.HeaderSize + pixelDataSize];

    result[0] = FremontFaxFile.Magic[0];
    result[1] = FremontFaxFile.Magic[1];
    result[2] = FremontFaxFile.Magic[2];
    result[3] = FremontFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(FremontFaxFile.HeaderSize));

    return result;
  }
}
