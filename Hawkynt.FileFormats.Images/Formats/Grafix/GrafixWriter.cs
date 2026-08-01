using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Grafix;

/// <summary>Assembles Grafix (.grx) picture bytes.</summary>
/// <remarks>
/// Stored rather than packed. The packed form runs a dictionary coder over each half of the bitmap
/// separately, which buys space on a picture with large flat areas and costs a decoder that has to
/// know the coder; the header says which form a file is in, and stored is a form every reader has.
/// </remarks>
public static class GrafixWriter {

  public static byte[] ToBytes(GrafixFile file) {
    var stride = AtariStGraphics.BytesPerRow(file.Width, file.Planes);
    var bitmapLength = stride * file.Height;
    var result = new byte[GrafixFile.BitmapOffset + bitmapLength];

    Encoding.ASCII.GetBytes(GrafixFile.Signature).CopyTo(result, 0);
    result[4] = 1;
    result[5] = 1;

    // Byte 28 marks the picture as a whole one rather than a piece of a larger sheet, and 29 says
    // the bitmap follows as it lies.
    result[28] = 0;
    result[29] = 0;

    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(30), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(32), (ushort)file.Height);
    BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(34), (ushort)(1 << file.Planes));

    AtariStGraphics.WriteVdiPalette(
      file.Palette ?? [], 1 << file.Planes, file.Planes, result.AsSpan(GrafixFile.PaletteOffset));

    // How long the bitmap is, which a reader checks against the size and the depth before trusting
    // any of the three.
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(1574), bitmapLength);

    var bitmap = file.Bitmap ?? [];
    bitmap.AsSpan(0, Math.Min(bitmap.Length, bitmapLength)).CopyTo(result.AsSpan(GrafixFile.BitmapOffset));

    return result;
  }
}
