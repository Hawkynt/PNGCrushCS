using System;
using System.IO;

namespace FileFormat.Bsave;

/// <summary>Assembles BSAVE file bytes from screen memory data.</summary>
public static class BsaveWriter {

  public static byte[] ToBytes(BsaveFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return _Assemble(file.PixelData, file.Mode);
  }

  internal static byte[] _Assemble(byte[] pixelData, BsaveMode mode) {
    var (segment, offset) = _GetSegmentOffset(mode);
    var length = (ushort)pixelData.Length;

    var header = new BsaveHeader(
      Magic: BsaveHeader.MagicValue,
      Segment: segment,
      Offset: offset,
      Length: length
    );

    var result = new byte[BsaveHeader.StructSize + pixelData.Length];
    header.WriteTo(result.AsSpan());
    pixelData.AsSpan(0, pixelData.Length).CopyTo(result.AsSpan(BsaveHeader.StructSize));

    return result;
  }

  private static (ushort Segment, ushort Offset) _GetSegmentOffset(BsaveMode mode) => mode switch {
    BsaveMode.Cga320x200x4 => (0xB800, 0x0000),
    BsaveMode.Cga640x200x2 => (0xB800, 0x0000),
    BsaveMode.Ega640x350x16 => (0xA000, 0x0000),
    BsaveMode.Vga320x200x256 => (0xA000, 0x0000),
    _ => (0xB800, 0x0000)
  };
}
