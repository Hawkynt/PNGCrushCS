using System;
using System.Buffers.Binary;

namespace FileFormat.Afli;

/// <summary>Assembles AFLI (Advanced FLI) file bytes from an <see cref="AfliFile"/>.</summary>
public static class AfliWriter {

  public static byte[] ToBytes(AfliFile file) {
    ArgumentNullException.ThrowIfNull(file.BitmapData);
    ArgumentNullException.ThrowIfNull(file.Screens);

    var result = new byte[AfliFile.MinimumFileSize];
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
