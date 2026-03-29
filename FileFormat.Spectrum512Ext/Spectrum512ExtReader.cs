using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Spectrum512Ext;

/// <summary>Reads Spectrum 512 Extended (.spx) files from bytes, streams, or file paths.</summary>
public static class Spectrum512ExtReader {

  private const int _PIXEL_DATA_SIZE = 32000;

  public static Spectrum512ExtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Spectrum 512 Extended file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Spectrum512ExtFile FromStream(Stream stream) {
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

  public static Spectrum512ExtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != Spectrum512ExtFile.FileSize)
      throw new InvalidDataException($"SPX file must be exactly {Spectrum512ExtFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(0, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    var palettes = new short[Spectrum512ExtFile.ScanlineCount][];
    var span = data.AsSpan();
    var paletteOffset = _PIXEL_DATA_SIZE;

    for (var line = 0; line < Spectrum512ExtFile.ScanlineCount; ++line) {
      var palette = new short[Spectrum512ExtFile.PaletteEntriesPerLine];
      for (var entry = 0; entry < Spectrum512ExtFile.PaletteEntriesPerLine; ++entry) {
        var offset = paletteOffset + (line * Spectrum512ExtFile.PaletteEntriesPerLine + entry) * 2;
        palette[entry] = BinaryPrimitives.ReadInt16BigEndian(span[offset..]);
      }
      palettes[line] = palette;
    }

    return new Spectrum512ExtFile {
      Width = 320,
      Height = Spectrum512ExtFile.ScanlineCount,
      PixelData = pixelData,
      Palettes = palettes
    };
  }
}
