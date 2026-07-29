using System;
using System.Buffers.Binary;

namespace FileFormat.Imagic;

/// <summary>Assembles Imagic file bytes from an <see cref="ImagicFile"/>.</summary>
public static class ImagicWriter {

  public static byte[] ToBytes(ImagicFile file) {
    var (data, escape) = ImagicCompressor.Compress(file.ScreenData ?? new byte[ImagicCompressor.ScreenSize]);

    var result = new byte[ImagicFile.DataOffset + data.Length];
    ImagicFile.Signature.CopyTo(result);
    result[ImagicFile.ModeOffset] = (byte)file.Resolution;

    var palette = file.Palette ?? [];
    for (var i = 0; i < ImagicFile.PaletteCount; ++i)
      BinaryPrimitives.WriteInt16BigEndian(
        result.AsSpan(ImagicFile.PaletteOffset + i * 2, 2), i < palette.Length ? palette[i] : (short)0);

    var reserved = file.Reserved ?? [];
    reserved.AsSpan(0, Math.Min(reserved.Length, ImagicFile.ReservedSize)).CopyTo(result.AsSpan(ImagicFile.ReservedOffset));

    ImagicFile.Stamp.CopyTo(result.AsSpan(ImagicFile.StampOffset));
    result[ImagicFile.EscapeOffset] = escape;
    data.CopyTo(result, ImagicFile.DataOffset);

    return result;
  }
}
