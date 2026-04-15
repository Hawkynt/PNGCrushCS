using System;
using System.IO;

namespace FileFormat.EzArt;

/// <summary>Reads EZ-Art Professional (.eza) files from bytes, streams, or file paths.</summary>
public static class EzArtReader {

  private const int _PALETTE_SIZE = 32;
  private const int _PIXEL_DATA_SIZE = 32000;
  private const int _PALETTE_ENTRIES = 16;

  public static EzArtFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("EZ-Art file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static EzArtFile FromStream(Stream stream) {
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

  public static EzArtFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < EzArtFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid EZ-Art file (expected {EzArtFile.FileSize} bytes, got {data.Length}).");


    var header = EzArtHeader.ReadFrom(data);
    var palette = header.Palette;

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.Slice(_PALETTE_SIZE, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new EzArtFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData,
    };
    }

  public static EzArtFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < EzArtFile.FileSize)
      throw new InvalidDataException($"Data too small for a valid EZ-Art file (expected {EzArtFile.FileSize} bytes, got {data.Length}).");


    var header = EzArtHeader.ReadFrom(data);
    var palette = header.Palette;

    var pixelData = new byte[_PIXEL_DATA_SIZE];
    data.AsSpan(_PALETTE_SIZE, _PIXEL_DATA_SIZE).CopyTo(pixelData.AsSpan(0));

    return new EzArtFile {
      Width = 320,
      Height = 200,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
