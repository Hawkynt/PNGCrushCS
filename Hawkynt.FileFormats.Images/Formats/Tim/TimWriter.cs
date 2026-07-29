using System;
using System.IO;

namespace FileFormat.Tim;

/// <summary>Assembles PlayStation 1 TIM texture file bytes from a <see cref="TimFile"/>.</summary>
public static class TimWriter {

  public static byte[] ToBytes(TimFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    var flags = (uint)file.Bpp;
    if (file.HasClut)
      flags |= 0x08;

    var header = new TimHeader(TimHeader.ExpectedMagic, flags);
    Span<byte> headerBuf = stackalloc byte[TimHeader.StructSize];
    header.WriteTo(headerBuf);
    ms.Write(headerBuf);

    if (file.HasClut && file.ClutData != null) {
      var clutDataSize = file.ClutData.Length;
      var clutBlockSize = (uint)(TimBlockHeader.StructSize + clutDataSize);
      var clutBlock = new TimBlockHeader(clutBlockSize, (ushort)file.ClutX, (ushort)file.ClutY, (ushort)file.ClutWidth, (ushort)file.ClutHeight);
      Span<byte> clutBlockBuf = stackalloc byte[TimBlockHeader.StructSize];
      clutBlock.WriteTo(clutBlockBuf);
      ms.Write(clutBlockBuf);
      ms.Write(file.ClutData);
    }

    var vramWidth = _PixelsToVramWidth(file.Width, file.Bpp);
    var imageDataSize = file.PixelData.Length;
    var imageBlockSize = (uint)(TimBlockHeader.StructSize + imageDataSize);
    var imageBlock = new TimBlockHeader(imageBlockSize, (ushort)file.ImageX, (ushort)file.ImageY, (ushort)vramWidth, (ushort)file.Height);
    Span<byte> imageBlockBuf = stackalloc byte[TimBlockHeader.StructSize];
    imageBlock.WriteTo(imageBlockBuf);
    ms.Write(imageBlockBuf);
    ms.Write(file.PixelData);

    return ms.ToArray();
  }

  private static int _PixelsToVramWidth(int pixelWidth, TimBpp bpp) => bpp switch {
    TimBpp.Bpp4 => pixelWidth / 4,
    TimBpp.Bpp8 => pixelWidth / 2,
    TimBpp.Bpp16 => pixelWidth,
    TimBpp.Bpp24 => pixelWidth * 3 / 2,
    _ => pixelWidth
  };
}
