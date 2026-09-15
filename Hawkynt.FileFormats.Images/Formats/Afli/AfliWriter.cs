using System;
using System.Buffers.Binary;

namespace FileFormat.Afli;

/// <summary>Assembles AFLI (Advanced FLI) file bytes from an <see cref="AfliFile"/>.</summary>
/// <remarks>
/// The file is <see cref="AfliFile.FileSize"/> bytes and not the 16194 the picture occupies. AFLI
/// carries no magic number, so a decoder has only the length to go on and every other decoder of it
/// insists on the length real files have; a file that stopped after the bitmap round-tripped through
/// this package and was refused everywhere else.
/// </remarks>
public static class AfliWriter {

  public static byte[] ToBytes(AfliFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);
    ArgumentNullException.ThrowIfNull(file.Screens);

    var result = new byte[AfliFile.FileSize];
    BinaryPrimitives.WriteUInt16LittleEndian(result, file.LoadAddress);

    file.Screens
      .AsSpan(0, Math.Min(file.Screens.Length, AfliFile.ScreenCount * AfliFile.ScreenStride))
      .CopyTo(result.AsSpan(AfliFile.ScreensOffset));
    file.BitmapData
      .AsSpan(0, Math.Min(file.BitmapData.Length, AfliFile.BitmapDataSize))
      .CopyTo(result.AsSpan(AfliFile.BitmapOffset));

    return result;
  }
}
