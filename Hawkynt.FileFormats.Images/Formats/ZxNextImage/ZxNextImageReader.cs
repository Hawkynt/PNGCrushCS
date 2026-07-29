using System;
using System.IO;

namespace FileFormat.ZxNextImage;

/// <summary>Reads ZX Spectrum Next (.nxi) images from bytes, streams, or file paths.</summary>
public static class ZxNextImageReader {

  public static ZxNextImageFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("NXI file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxNextImageFile FromStream(Stream stream) {
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

  public static ZxNextImageFile FromSpan(ReadOnlySpan<byte> data) {
    // No signature; the fixed size is what identifies the format.
    if (data.Length != ZxNextImageFile.FileSize)
      throw new InvalidDataException($"An NXI file is exactly {ZxNextImageFile.FileSize} bytes, got {data.Length}.");

    var palette = new byte[ZxNextImageFile.PaletteDataSize];
    data[..ZxNextImageFile.PaletteDataSize].CopyTo(palette);

    var pixels = new byte[ZxNextImageFile.PixelDataSize];
    data.Slice(ZxNextImageFile.PixelDataOffset, ZxNextImageFile.PixelDataSize).CopyTo(pixels);

    return new() { PaletteData = palette, PixelData = pixels };
  }

  public static ZxNextImageFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
