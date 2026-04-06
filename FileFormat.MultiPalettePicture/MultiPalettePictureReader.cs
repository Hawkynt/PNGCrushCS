using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.MultiPalettePicture;

/// <summary>Reads Atari ST Multi Palette Picture (MPP) files from bytes, streams, or file paths.</summary>
public static class MultiPalettePictureReader {

  public static MultiPalettePictureFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("MPP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MultiPalettePictureFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static MultiPalettePictureFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static MultiPalettePictureFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < MultiPalettePictureFile.ExpectedFileSize)
      throw new InvalidDataException($"Data too small for a valid MPP file: expected {MultiPalettePictureFile.ExpectedFileSize} bytes, got {data.Length}.");

    var span = data.AsSpan();
    var pixelData = new byte[MultiPalettePictureFile.BytesPerScanline * MultiPalettePictureFile.ImageHeight];
    var palettes = new short[MultiPalettePictureFile.ImageHeight][];

    for (var y = 0; y < MultiPalettePictureFile.ImageHeight; ++y) {
      var recordOffset = y * MultiPalettePictureFile.RecordSize;

      // Copy 160 bytes of pixel data
      data.AsSpan(recordOffset, MultiPalettePictureFile.BytesPerScanline)
        .CopyTo(pixelData.AsSpan(y * MultiPalettePictureFile.BytesPerScanline));

      // Read 16-word palette after pixel data
      var palette = new short[16];
      var paletteOffset = recordOffset + MultiPalettePictureFile.BytesPerScanline;
      for (var i = 0; i < 16; ++i)
        palette[i] = BinaryPrimitives.ReadInt16BigEndian(span[(paletteOffset + i * 2)..]);

      palettes[y] = palette;
    }

    return new MultiPalettePictureFile {
      PixelData = pixelData,
      Palettes = palettes
    };
  }
}
