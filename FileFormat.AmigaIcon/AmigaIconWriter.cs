using System;
using System.Buffers.Binary;

namespace FileFormat.AmigaIcon;

/// <summary>Assembles Amiga Workbench icon (.info) file bytes from an <see cref="AmigaIconFile"/>.</summary>
public static class AmigaIconWriter {

  public static byte[] ToBytes(AmigaIconFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file);
  }

  internal static byte[] Assemble(AmigaIconFile file) {
    var expectedPlanarSize = AmigaIconFile.PlanarDataSize(file.Width, file.Height, file.Depth);
    var fileSize = AmigaIconHeader.StructSize + expectedPlanarSize;
    var result = new byte[fileSize];
    var span = result.AsSpan();

    // Preserve original raw header bytes (unmapped fields like gadget data, tool types, etc.)
    if (file.RawHeader is { Length: >= AmigaIconHeader.StructSize })
      file.RawHeader.AsSpan(0, AmigaIconHeader.StructSize).CopyTo(span);

    // Overwrite the key fields manually (big-endian)
    BinaryPrimitives.WriteUInt16BigEndian(span[0..], AmigaIconHeader.MagicValue);
    BinaryPrimitives.WriteUInt16BigEndian(span[2..], 1);                                         // version
    BinaryPrimitives.WriteUInt16BigEndian(span[4..], 0);                                         // next gadget
    BinaryPrimitives.WriteInt16BigEndian(span[10..], (short)file.Width);
    BinaryPrimitives.WriteInt16BigEndian(span[12..], (short)file.Height);
    BinaryPrimitives.WriteInt16BigEndian(span[14..], (short)file.Depth);
    BinaryPrimitives.WriteUInt16BigEndian(span[16..], 1);                                        // image data pointer (nonzero = present)
    BinaryPrimitives.WriteUInt16BigEndian(span[18..], (ushort)((1 << file.Depth) - 1));          // plane pick
    BinaryPrimitives.WriteUInt16BigEndian(span[20..], 0);                                        // plane on/off
    span[54] = (byte)file.IconType;

    // Write planar image data after the header
    file.PlanarData.AsSpan(0, Math.Min(expectedPlanarSize, file.PlanarData.Length)).CopyTo(span[AmigaIconHeader.StructSize..]);

    return result;
  }
}
