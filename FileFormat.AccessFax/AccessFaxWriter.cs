using System;

namespace FileFormat.AccessFax;

/// <summary>Assembles AccessFax G4 file bytes from an AccessFaxFile.</summary>
public static class AccessFaxWriter {

  public static byte[] ToBytes(AccessFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[AccessFaxFile.HeaderSize + pixelDataSize];

    result[0] = AccessFaxFile.Magic[0];
    result[1] = AccessFaxFile.Magic[1];
    BitConverter.TryWriteBytes(new Span<byte>(result, 2, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), file.Flags);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(AccessFaxFile.HeaderSize));

    return result;
  }
}
