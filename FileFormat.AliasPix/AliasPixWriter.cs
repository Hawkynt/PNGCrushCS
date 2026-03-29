using System;

namespace FileFormat.AliasPix;

/// <summary>Assembles Alias/Wavefront PIX file bytes from pixel data.</summary>
public static class AliasPixWriter {

  public static byte[] ToBytes(AliasPixFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height, file.XOffset, file.YOffset, file.BitsPerPixel);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int xOffset, int yOffset, int bitsPerPixel) {
    var bytesPerPixel = bitsPerPixel / 8;
    var rleData = AliasPixRleCompressor.Compress(pixelData, width, height, bytesPerPixel);

    var result = new byte[AliasPixHeader.StructSize + rleData.Length];
    var span = result.AsSpan();

    var header = new AliasPixHeader((ushort)width, (ushort)height, (ushort)xOffset, (ushort)yOffset, (ushort)bitsPerPixel);
    header.WriteTo(span);

    rleData.AsSpan(0, rleData.Length).CopyTo(result.AsSpan(AliasPixHeader.StructSize));

    return result;
  }
}
