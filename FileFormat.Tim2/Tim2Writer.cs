using System;
using System.IO;

namespace FileFormat.Tim2;

/// <summary>Assembles PlayStation 2/PSP TIM2 texture file bytes from a <see cref="Tim2File"/>.</summary>
public static class Tim2Writer {

  public static byte[] ToBytes(Tim2File file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    var header = new Tim2Header(
      (byte)'T', (byte)'I', (byte)'M', (byte)'2',
      file.Version,
      file.Alignment,
      (ushort)file.Pictures.Count
    );

    Span<byte> headerBuf = stackalloc byte[Tim2Header.StructSize];
    header.WriteTo(headerBuf);
    ms.Write(headerBuf);

    Span<byte> picHeaderBuf = stackalloc byte[Tim2PictureHeader.StructSize];

    for (var i = 0; i < file.Pictures.Count; ++i) {
      var pic = file.Pictures[i];

      var imageDataSize = (uint)pic.PixelData.Length;
      var paletteSize = (uint)(pic.PaletteData?.Length ?? 0);
      var totalSize = (uint)Tim2PictureHeader.StructSize + imageDataSize + paletteSize;

      var picHeader = new Tim2PictureHeader(
        totalSize,
        paletteSize,
        imageDataSize,
        Tim2PictureHeader.StructSize,
        (ushort)pic.PaletteColors,
        (byte)pic.Format,
        pic.MipmapCount,
        0, // PaletteType
        0, // ImageType
        (ushort)pic.Width,
        (ushort)pic.Height,
        0, // GsTex0
        0, // GsTex1
        0, // GsFlags
        0  // GsTexClut
      );

      picHeader.WriteTo(picHeaderBuf);
      ms.Write(picHeaderBuf);
      ms.Write(pic.PixelData);

      if (pic.PaletteData != null)
        ms.Write(pic.PaletteData);
    }

    return ms.ToArray();
  }
}
