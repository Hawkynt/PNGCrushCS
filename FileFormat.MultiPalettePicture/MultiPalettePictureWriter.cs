using System;
using System.Buffers.Binary;

namespace FileFormat.MultiPalettePicture;

/// <summary>Assembles Atari ST Multi Palette Picture (MPP) file bytes from a MultiPalettePictureFile.</summary>
public static class MultiPalettePictureWriter {

  public static byte[] ToBytes(MultiPalettePictureFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var result = new byte[MultiPalettePictureFile.ExpectedFileSize];
    var span = result.AsSpan();

    for (var y = 0; y < MultiPalettePictureFile.ImageHeight; ++y) {
      var recordOffset = y * MultiPalettePictureFile.RecordSize;

      // Write 160 bytes of pixel data
      var srcOffset = y * MultiPalettePictureFile.BytesPerScanline;
      var srcLength = Math.Min(MultiPalettePictureFile.BytesPerScanline, file.PixelData.Length - srcOffset);
      if (srcLength > 0)
        file.PixelData.AsSpan(srcOffset, srcLength).CopyTo(result.AsSpan(recordOffset));

      // Write 16-word palette
      var palette = file.Palettes[y];
      var paletteOffset = recordOffset + MultiPalettePictureFile.BytesPerScanline;
      for (var i = 0; i < 16; ++i)
        BinaryPrimitives.WriteInt16BigEndian(span[(paletteOffset + i * 2)..], i < palette.Length ? palette[i] : (short)0);
    }

    return result;
  }
}
