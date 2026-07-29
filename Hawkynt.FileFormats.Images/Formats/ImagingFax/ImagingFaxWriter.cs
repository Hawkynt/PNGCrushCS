using System;

namespace FileFormat.ImagingFax;

/// <summary>Assembles ImagingFax G3N file bytes from an ImagingFaxFile.</summary>
public static class ImagingFaxWriter {

  public static byte[] ToBytes(ImagingFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[ImagingFaxFile.HeaderSize + pixelDataSize];

    result[0] = ImagingFaxFile.Magic[0];
    result[1] = ImagingFaxFile.Magic[1];
    result[2] = ImagingFaxFile.Magic[2];
    result[3] = ImagingFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), file.Encoding);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Flags);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(ImagingFaxFile.HeaderSize));

    return result;
  }
}
