using System;
using System.Buffers.Binary;
using FileFormat.Ccitt;

namespace FileFormat.CImage;

/// <summary>Writes CImage/DSI bilevel pages, using the repository's conforming CCITT T.6 encoder.</summary>
public static class CImageWriter {

  public static byte[] ToBytes(CImageFile file) {
    if (file.Width <= 0 || file.Height <= 0)
      throw new ArgumentException("CImage dimensions must be positive.", nameof(file));

    var bytesPerRow = checked((file.Width + 7) / 8);
    var expected = checked(bytesPerRow * file.Height);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"CImage needs {expected} packed 1bpp bytes.", nameof(file));

    byte[] payload;
    if (file.IsGroup4) {
      payload = CcittG4Encoder.Encode(file.PixelData, file.Width, file.Height);
    } else {
      payload = new byte[expected];
      // DSI's uncompressed storage uses the opposite polarity from the internal packed bitmap.
      for (var i = 0; i < expected; ++i)
        payload[i] = (byte)~file.PixelData[i];
    }

    var output = new byte[checked(CImageFile.HeaderSize + payload.Length)];
    output[0] = CImageFile.Magic[0];
    output[1] = CImageFile.Magic[1];
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(CImageFile.HorizontalResolutionOffset, 2), file.HorizontalResolution);
    BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(CImageFile.VerticalResolutionOffset, 2), file.VerticalResolution);
    output[CImageFile.CompressionOffset] = file.IsGroup4 ? (byte)1 : (byte)0;
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(CImageFile.WidthOffset, 4), checked((uint)file.Width));
    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(CImageFile.WidthOffset + 4, 4), checked((uint)file.Height));
    payload.CopyTo(output, CImageFile.HeaderSize);
    return output;
  }
}
