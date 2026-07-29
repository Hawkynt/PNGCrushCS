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
    var result = new byte[AmigaIconFile.PlanarDataOffset + expectedPlanarSize];
    var span = result.AsSpan();

    // Preserve original DiskObject bytes (tool types, gadget state and so on) when we have them.
    if (file.RawHeader is { Length: >= AmigaIconHeader.StructSize })
      file.RawHeader.AsSpan(0, AmigaIconHeader.StructSize).CopyTo(span);

    BinaryPrimitives.WriteUInt16BigEndian(span, AmigaIconHeader.MagicValue);
    BinaryPrimitives.WriteUInt16BigEndian(span[2..], 1); // version
    span[AmigaIconHeader.IconTypeOffset] = (byte)file.IconType;

    // Palette selector: 0 selects the Workbench 1.x four-colour set, 1 the 2.x eight-colour one.
    // Three bitplanes are only legal with the latter.
    BinaryPrimitives.WriteUInt32BigEndian(
      span[AmigaIconHeader.PaletteSelectorOffset..],
      file.Depth >= AmigaIconFile.Workbench2Depth ? 1u : 0u);

    // Zero here keeps the first Image structure at its standard offset.
    BinaryPrimitives.WriteUInt32BigEndian(span[AmigaIconHeader.ImageOffsetSelectorOffset..], 0u);

    // The Image structure: LeftEdge, TopEdge, Width, Height, Depth, ImageData pointer,
    // PlanePick, PlaneOnOff, NextImage pointer.
    var image = span[AmigaIconFile.ImageStructOffset..];
    BinaryPrimitives.WriteInt16BigEndian(image[4..], (short)file.Width);
    BinaryPrimitives.WriteInt16BigEndian(image[6..], (short)file.Height);
    BinaryPrimitives.WriteInt16BigEndian(image[8..], (short)file.Depth);
    BinaryPrimitives.WriteUInt32BigEndian(image[10..], 1u);                  // non-null ImageData pointer
    image[14] = (byte)((1 << file.Depth) - 1);                               // PlanePick
    image[15] = 0;                                                           // PlaneOnOff

    file.PlanarData.AsSpan(0, Math.Min(expectedPlanarSize, file.PlanarData.Length))
      .CopyTo(span[AmigaIconFile.PlanarDataOffset..]);

    return result;
  }

}
