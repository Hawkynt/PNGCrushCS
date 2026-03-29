using System;
using System.IO;
using System.IO.Compression;
using FileFormat.Png;

namespace FileFormat.Krita;

/// <summary>Reads Krita (.kra) files from bytes, streams, or file paths.</summary>
public static class KritaReader {

  private const string _MIMETYPE_VALUE = "application/x-krita";

  public static KritaFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Krita file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static KritaFile FromStream(Stream stream) {
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

  public static KritaFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < 4)
      throw new InvalidDataException("Data too small for a valid Krita file.");

    using var zipStream = new MemoryStream(data, writable: false);
    ZipArchive archive;
    try {
      archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
    } catch (InvalidDataException) {
      throw new InvalidDataException("Data is not a valid ZIP archive.");
    }

    using (archive) {
      var mimetypeEntry = archive.GetEntry("mimetype") ?? throw new InvalidDataException("Krita archive missing mimetype entry.");
      var mimetypeText = _ReadEntryText(mimetypeEntry).Trim();
      if (mimetypeText != _MIMETYPE_VALUE)
        throw new InvalidDataException($"Invalid Krita mimetype: expected '{_MIMETYPE_VALUE}', got '{mimetypeText}'.");

      var mergedEntry = archive.GetEntry("mergedimage.png") ?? throw new InvalidDataException("Krita archive missing mergedimage.png entry.");
      var pngBytes = _ReadEntryBytes(mergedEntry);
      var pngFile = PngReader.FromBytes(pngBytes);

      var width = pngFile.Width;
      var height = pngFile.Height;
      var pixelData = _PngFileToRgba(pngFile);

      return new KritaFile {
        Width = width,
        Height = height,
        PixelData = pixelData
      };
    }
  }

  private static string _ReadEntryText(ZipArchiveEntry entry) {
    using var stream = entry.Open();
    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
    return reader.ReadToEnd();
  }

  private static byte[] _ReadEntryBytes(ZipArchiveEntry entry) {
    using var stream = entry.Open();
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return ms.ToArray();
  }

  private static byte[] _PngFileToRgba(PngFile pngFile) {
    if (pngFile.PixelData == null)
      return [];

    var width = pngFile.Width;
    var height = pngFile.Height;
    var result = new byte[width * height * 4];
    var bytesPerPixel = _GetBytesPerPixel(pngFile.ColorType);

    for (var y = 0; y < height; ++y) {
      if (y >= pngFile.PixelData.Length)
        break;

      var scanline = pngFile.PixelData[y];
      for (var x = 0; x < width; ++x) {
        var srcOffset = x * bytesPerPixel;
        var dstOffset = (y * width + x) * 4;

        switch (pngFile.ColorType) {
          case PngColorType.RGBA:
            result[dstOffset] = scanline[srcOffset];
            result[dstOffset + 1] = scanline[srcOffset + 1];
            result[dstOffset + 2] = scanline[srcOffset + 2];
            result[dstOffset + 3] = scanline[srcOffset + 3];
            break;
          case PngColorType.RGB:
            result[dstOffset] = scanline[srcOffset];
            result[dstOffset + 1] = scanline[srcOffset + 1];
            result[dstOffset + 2] = scanline[srcOffset + 2];
            result[dstOffset + 3] = 255;
            break;
          case PngColorType.GrayscaleAlpha:
            result[dstOffset] = scanline[srcOffset];
            result[dstOffset + 1] = scanline[srcOffset];
            result[dstOffset + 2] = scanline[srcOffset];
            result[dstOffset + 3] = scanline[srcOffset + 1];
            break;
          case PngColorType.Grayscale:
            result[dstOffset] = scanline[srcOffset];
            result[dstOffset + 1] = scanline[srcOffset];
            result[dstOffset + 2] = scanline[srcOffset];
            result[dstOffset + 3] = 255;
            break;
          case PngColorType.Palette:
            if (pngFile.Palette != null && srcOffset < scanline.Length) {
              var index = scanline[srcOffset];
              var palOffset = index * 3;
              if (palOffset + 2 < pngFile.Palette.Length) {
                result[dstOffset] = pngFile.Palette[palOffset];
                result[dstOffset + 1] = pngFile.Palette[palOffset + 1];
                result[dstOffset + 2] = pngFile.Palette[palOffset + 2];
              }

              result[dstOffset + 3] = pngFile.Transparency != null && index < pngFile.Transparency.Length
                ? pngFile.Transparency[index]
                : (byte)255;
            }
            break;
        }
      }
    }

    return result;
  }

  private static int _GetBytesPerPixel(PngColorType colorType) => colorType switch {
    PngColorType.Grayscale => 1,
    PngColorType.GrayscaleAlpha => 2,
    PngColorType.RGB => 3,
    PngColorType.RGBA => 4,
    PngColorType.Palette => 1,
    _ => 4
  };
}
