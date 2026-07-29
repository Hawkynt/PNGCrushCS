using System;
using System.Buffers.Binary;

namespace FileFormat.AutodeskCel;

/// <summary>Assembles Autodesk Animator CEL/PIC file bytes from an <see cref="AutodeskCelFile"/>.</summary>
public static class AutodeskCelWriter {

  public static byte[] ToBytes(AutodeskCelFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var bpp = file.BitsPerPixel == 0 ? 8 : file.BitsPerPixel;
    return Assemble(file.PixelData ?? Array.Empty<byte>(), file.Width, file.Height, file.XOffset, file.YOffset, bpp, file.Compression, file.Palette ?? Array.Empty<byte>());
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height, int xOffset, int yOffset, int bitsPerPixel, byte compression, byte[] palette) {
    var fileSize = AutodeskCelFile.HeaderSize + pixelData.Length + AutodeskCelFile.PaletteSize;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    // Header: 16 bytes
    BinaryPrimitives.WriteUInt16LittleEndian(span, AutodeskCelFile.Magic);
    new AutodeskCelHeader((ushort)width, (ushort)height, (ushort)xOffset, (ushort)yOffset, (ushort)bitsPerPixel).WriteTo(span);
    span[12] = compression;
    // Bytes 13-15: padding (already zeroed)

    // Pixel data
    pixelData.AsSpan(0, pixelData.Length).CopyTo(result.AsSpan(AutodeskCelFile.HeaderSize));

    // Palette: 768 bytes, 6-bit VGA values (divide by 4)
    var paletteOffset = AutodeskCelFile.HeaderSize + pixelData.Length;
    for (var i = 0; i < AutodeskCelFile.PaletteSize; ++i)
      result[paletteOffset + i] = (byte)(Math.Min(palette.Length > i ? palette[i] : 0, 252) / 4);

    return result;
  }
}
