using System;
using System.IO;

namespace FileFormat.PhotoPaint;

/// <summary>Assembles Corel Photo-Paint CPT file bytes from a PhotoPaintFile model.</summary>
public static class PhotoPaintWriter {

  public static byte[] ToBytes(PhotoPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var expectedPixelBytes = file.Width * file.Height * 3;
    var result = new byte[PhotoPaintFile.HeaderSize + expectedPixelBytes];

    // Magic "CPT\0"
    result[0] = PhotoPaintFile.Magic[0];
    result[1] = PhotoPaintFile.Magic[1];
    result[2] = PhotoPaintFile.Magic[2];
    result[3] = PhotoPaintFile.Magic[3];

    // Version (uint16 LE)
    result[4] = (byte)(PhotoPaintFile.Version & 0xFF);
    result[5] = (byte)((PhotoPaintFile.Version >> 8) & 0xFF);

    // Reserved (uint16 LE = 0)
    result[6] = 0;
    result[7] = 0;

    // Width (uint32 LE)
    result[8] = (byte)(file.Width & 0xFF);
    result[9] = (byte)((file.Width >> 8) & 0xFF);
    result[10] = (byte)((file.Width >> 16) & 0xFF);
    result[11] = (byte)((file.Width >> 24) & 0xFF);

    // Height (uint32 LE)
    result[12] = (byte)(file.Height & 0xFF);
    result[13] = (byte)((file.Height >> 8) & 0xFF);
    result[14] = (byte)((file.Height >> 16) & 0xFF);
    result[15] = (byte)((file.Height >> 24) & 0xFF);

    // Bit depth (uint16 LE)
    result[16] = (byte)(PhotoPaintFile.BitDepth & 0xFF);
    result[17] = (byte)((PhotoPaintFile.BitDepth >> 8) & 0xFF);

    // Compression (uint16 LE = 0, none)
    result[18] = 0;
    result[19] = 0;

    // Reserved (uint32 LE = 0)
    result[20] = 0;
    result[21] = 0;
    result[22] = 0;
    result[23] = 0;

    // Pixel data
    var copyLen = Math.Min(expectedPixelBytes, file.PixelData.Length);
    file.PixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(PhotoPaintFile.HeaderSize));

    return result;
  }
}
