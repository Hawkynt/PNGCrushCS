using System;
using System.IO;

namespace FileFormat.Neochrome;

/// <summary>Reads NEOchrome files from bytes, streams, or file paths.</summary>
public static class NeochromeReader {

  private const int _ExpectedFileSize = NeochromeHeader.StructSize + 32000;

  public static NeochromeFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NEOchrome file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static NeochromeFile FromStream(Stream stream) {
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

  public static NeochromeFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < NeochromeHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid NEOchrome file.");

    if (data.Length < _ExpectedFileSize)
      throw new InvalidDataException("Data too small for the expected 32128-byte NEOchrome file.");

    var span = data.AsSpan();
    var header = NeochromeHeader.ReadFrom(span);
    var palette = header.GetPalette();

    var pixelData = new byte[32000];
    data.AsSpan(NeochromeHeader.StructSize, 32000).CopyTo(pixelData.AsSpan(0));

    return new NeochromeFile {
      Width = 320,
      Height = 200,
      Flag = header.Flag,
      Palette = palette,
      AnimSpeed = header.AnimSpeed,
      AnimDirection = header.AnimDirection,
      AnimSteps = header.AnimSteps,
      AnimXOffset = header.AnimXOffset,
      AnimYOffset = header.AnimYOffset,
      AnimWidth = header.AnimWidth,
      AnimHeight = header.AnimHeight,
      PixelData = pixelData
    };
  }
}
