using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.AtariGfb;

/// <summary>Assembles a DeskPic picture: the header, the bitplanes, then the palette.</summary>
public static class AtariGfbWriter {

  public static byte[] ToBytes(AtariGfbFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var bitplanes = file.Bitplanes;
    var bitmapLength = AtariGfbFile.Stride(file.Width, bitplanes) * file.Height;
    var result = new byte[AtariGfbFile.HeaderSize + bitmapLength + AtariGfbFile.PaletteSize];

    Encoding.ASCII.GetBytes(AtariGfbFile.Signature).CopyTo(result.AsSpan(0));
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), 1 << bitplanes);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(8), file.Width);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(12), file.Height);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(16), bitmapLength);

    AtariStGraphics
      .PackBitplanes(file.PixelData ?? [], AtariGfbFile.Stride(file.Width, bitplanes), bitplanes, file.Width, file.Height)
      .CopyTo(result.AsSpan(AtariGfbFile.HeaderSize));

    // The palette always has 256 entries, whatever the depth; the ones past the depth stay black.
    AtariStGraphics.WriteVdiPalette(
      file.Palette ?? [], 1 << bitplanes, bitplanes,
      result.AsSpan(AtariGfbFile.HeaderSize + bitmapLength, AtariGfbFile.PaletteSize));

    return result;
  }

  public static void ToFile(AtariGfbFile file, FileInfo target) {
    ArgumentNullException.ThrowIfNull(target);
    File.WriteAllBytes(target.FullName, ToBytes(file));
  }
}
