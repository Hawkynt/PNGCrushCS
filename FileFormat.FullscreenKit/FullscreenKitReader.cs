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

    if (data.Length < FullscreenKitHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Fullscreen Kit file.");

    var (width, height) = FullscreenKitFile.DetectDimensions(data.Length);
    var pixelDataSize = data.Length - FullscreenKitHeader.StructSize;

    var header = FullscreenKitHeader.ReadFrom(data);
    var palette = header.GetPaletteArray();

    var pixelData = new byte[pixelDataSize];
    data.Slice(FullscreenKitHeader.StructSize, pixelDataSize).CopyTo(pixelData);

    return new FullscreenKitFile {
      Width = width,
      Height = height,
      Palette = palette,
      PixelData = pixelData,
    };
    }

  public static FullscreenKitFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < FullscreenKitHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid Fullscreen Kit file.");

    var (width, height) = FullscreenKitFile.DetectDimensions(data.Length);
    var pixelDataSize = data.Length - FullscreenKitHeader.StructSize;

    var header = FullscreenKitHeader.ReadFrom(data.AsSpan());
    var palette = header.GetPaletteArray();

    var pixelData = new byte[pixelDataSize];
    data.AsSpan(FullscreenKitHeader.StructSize, pixelDataSize).CopyTo(pixelData);

    return new FullscreenKitFile {
      Width = width,
      Height = height,
      Palette = palette,
      PixelData = pixelData,
    };
  }
}
