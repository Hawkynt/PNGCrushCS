using System;
using System.IO;

namespace FileFormat.Clp;

/// <summary>Assembles CLP file bytes from pixel data.</summary>
public static class ClpWriter {

  private const ushort _CF_DIB = 8;
  private const int _BITMAPINFOHEADER_SIZE = 40;

  public static byte[] ToBytes(ClpFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Width, file.Height, file.BitsPerPixel, file.Palette);
  }

  internal static byte[] _Assemble(byte[] pixelData, int width, int height, int bitsPerPixel, byte[]? palette) {
    using var ms = new MemoryStream();

    var paletteSize = palette?.Length ?? 0;
    var dibSize = _BITMAPINFOHEADER_SIZE + paletteSize + pixelData.Length;

    // Format entry: FormatId(2) + DataLength(4) + DataOffset(4) + name + null
    var formatName = "DIB"u8;
    var formatEntrySize = 2 + 4 + 4 + formatName.Length + 1; // +1 for null terminator
    var dataOffset = (uint)(ClpHeader.StructSize + formatEntrySize);

    // Write CLP header
    var header = new ClpHeader(
      FileId: ClpHeader.FileIdValue,
      FormatCount: 1
    );
    var headerBuf = new byte[ClpHeader.StructSize];
    header.WriteTo(headerBuf);
    ms.Write(headerBuf, 0, headerBuf.Length);

    // Write format directory entry
    // FormatId (ushort LE)
    ms.WriteByte((byte)(_CF_DIB & 0xFF));
    ms.WriteByte((byte)(_CF_DIB >> 8));

    // DataLength (uint32 LE)
    _WriteUInt32LE(ms, (uint)dibSize);

    // DataOffset (uint32 LE)
    _WriteUInt32LE(ms, dataOffset);

    // Name (null-terminated)
    ms.Write(formatName);
    ms.WriteByte(0); // null terminator

    // Write BITMAPINFOHEADER
    var bih = new byte[_BITMAPINFOHEADER_SIZE];
    BitConverter.TryWriteBytes(bih.AsSpan(0), _BITMAPINFOHEADER_SIZE); // biSize
    BitConverter.TryWriteBytes(bih.AsSpan(4), width);                   // biWidth
    BitConverter.TryWriteBytes(bih.AsSpan(8), height);                  // biHeight (positive = bottom-up)
    BitConverter.TryWriteBytes(bih.AsSpan(12), (short)1);               // biPlanes
    BitConverter.TryWriteBytes(bih.AsSpan(14), (short)bitsPerPixel);    // biBitCount
    // biCompression = 0 (BI_RGB) at offset 16, already zero
    var bytesPerRow = ((width * bitsPerPixel + 31) / 32) * 4;
    BitConverter.TryWriteBytes(bih.AsSpan(20), bytesPerRow * height);   // biSizeImage
    // biClrUsed at offset 32
    if (palette != null)
      BitConverter.TryWriteBytes(bih.AsSpan(32), palette.Length / 4);

    ms.Write(bih, 0, bih.Length);

    // Write palette
    if (palette is { Length: > 0 })
      ms.Write(palette, 0, palette.Length);

    // Write pixel data
    ms.Write(pixelData, 0, pixelData.Length);

    return ms.ToArray();
  }

  private static void _WriteUInt32LE(MemoryStream ms, uint value) {
    ms.WriteByte((byte)(value & 0xFF));
    ms.WriteByte((byte)((value >> 8) & 0xFF));
    ms.WriteByte((byte)((value >> 16) & 0xFF));
    ms.WriteByte((byte)((value >> 24) & 0xFF));
  }
}
