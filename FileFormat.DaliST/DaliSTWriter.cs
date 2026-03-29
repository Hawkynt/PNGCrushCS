using System;
using System.Buffers.Binary;

namespace FileFormat.DaliST;

/// <summary>Assembles Atari ST Dali (SD0/SD1/SD2) image bytes from a DaliSTFile.</summary>
public static class DaliSTWriter {

  public static byte[] ToBytes(DaliSTFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[DaliSTFile.ExpectedFileSize];
    var span = result.AsSpan();

    for (var i = 0; i < 16; ++i)
      BinaryPrimitives.WriteInt16BigEndian(span[(i * 2)..], i < file.Palette.Length ? file.Palette[i] : (short)0);

    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, DaliSTFile.PlanarDataSize)).CopyTo(result.AsSpan(DaliSTFile.PaletteSize));

    return result;
  }
}
