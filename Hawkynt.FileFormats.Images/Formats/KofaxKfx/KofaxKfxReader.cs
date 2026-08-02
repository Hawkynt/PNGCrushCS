using System;
using System.IO;

namespace FileFormat.KofaxKfx;

/// <summary>Reads Kofax Group 4 fax image files from bytes, streams, or file paths.</summary>
public static class KofaxKfxReader {

  public static KofaxKfxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("KofaxKfx file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static KofaxKfxFile FromStream(Stream stream) {
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

  public static KofaxKfxFile FromSpan(ReadOnlySpan<byte> data) {

    // The size used to be read from the first six bytes, which are picture, and replaced with a
    // default when that looked wrong. There is no header at all: the file is the bitmap.
    if (data.Length == 0 || data.Length % KofaxKfxFile.BytesPerRow != 0)
      throw new InvalidDataException($"A Kofax KFX is a whole number of {KofaxKfxFile.BytesPerRow}-byte rows; this file is {data.Length} bytes.");

    var width = KofaxKfxFile.RowWidth;
    var height = data.Length / KofaxKfxFile.BytesPerRow;
    var pixelData = data.ToArray();

    return new() {
      Width = width,
      Height = height,
      PixelData = pixelData,
    };
    }

  public static KofaxKfxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
