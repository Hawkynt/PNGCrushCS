using System;
using System.IO;

namespace FileFormat.FullscreenKit;

/// <summary>Reads Fullscreen Construction Kit (.kid) files from bytes, streams, or file paths.</summary>
public static class FullscreenKitReader {

  public static FullscreenKitFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fullscreen Kit file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FullscreenKitFile FromStream(Stream stream) {
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

  public static FullscreenKitFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != FullscreenKitFile.FileSize
        || data[0] != FullscreenKitFile.Signature[0] || data[1] != FullscreenKitFile.Signature[1])
      throw new InvalidDataException(
        $"A Fullscreen Kit picture is {FullscreenKitFile.FileSize} bytes beginning \"{FullscreenKitFile.Signature}\", got {data.Length}.");

    var width = FullscreenKitFile.PixelWidth;
    var height = FullscreenKitFile.PixelHeight;

    var palette = new short[FullscreenKitFile.ColorCount];
    for (var i = 0; i < palette.Length; ++i)
      palette[i] = (short)((data[FullscreenKitFile.PaletteOffset + i * 2] << 8) | data[FullscreenKitFile.PaletteOffset + i * 2 + 1]);

    var pixelData = data[FullscreenKitFile.BitmapOffset..].ToArray();

    return new FullscreenKitFile {
      Width = width,
      Height = height,
      Palette = palette,
      PixelData = pixelData,
    };
    }

  /// <summary>Reads a picture from a byte array.</summary>
  /// <remarks>
  /// A call into the one that takes a span rather than a second copy of it. It was a copy, and the
  /// two drifted the way copies do.
  /// </remarks>
  public static FullscreenKitFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
