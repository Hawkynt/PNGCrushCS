using System;

namespace FileFormat.VentaFax;

/// <summary>Assembles VentaFax VFX file bytes from a VentaFaxFile.</summary>
public static class VentaFaxWriter {

  public static byte[] ToBytes(VentaFaxFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelDataSize = file.PixelData.Length;
    var result = new byte[VentaFaxFile.HeaderSize + pixelDataSize];

    result[0] = VentaFaxFile.Magic[0];
    result[1] = VentaFaxFile.Magic[1];
    result[2] = VentaFaxFile.Magic[2];
    result[3] = VentaFaxFile.Magic[3];
    BitConverter.TryWriteBytes(new Span<byte>(result, 4, 2), file.Version);
    BitConverter.TryWriteBytes(new Span<byte>(result, 6, 2), (ushort)file.Width);
    BitConverter.TryWriteBytes(new Span<byte>(result, 8, 2), (ushort)file.Height);
    BitConverter.TryWriteBytes(new Span<byte>(result, 10, 2), file.Encoding);

    file.PixelData.AsSpan(0, pixelDataSize).CopyTo(result.AsSpan(VentaFaxFile.HeaderSize));

    return result;
  }
}
