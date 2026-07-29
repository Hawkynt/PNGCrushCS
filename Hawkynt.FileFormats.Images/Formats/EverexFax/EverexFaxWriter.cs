using System;

namespace FileFormat.EverexFax;

/// <summary>Assembles Everex Fax EFX file bytes from an EverexFaxFile.</summary>
public static class EverexFaxWriter {

  public static byte[] ToBytes(EverexFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[EverexFaxFile.HeaderSize + pixelDataSize];

    result[0] = EverexFaxFile.Magic[0];
    result[1] = EverexFaxFile.Magic[1];
    result[2] = EverexFaxFile.Magic[2];
    result[3] = EverexFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Pages);
    BitConverter.TryWriteBytes(new Span<byte>(result, 12, 2), file.Compression);
    BitConverter.TryWriteBytes(new Span<byte>(result, 14, 2), file.Reserved);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(EverexFaxFile.HeaderSize));

    return result;
  }
}
