using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FileFormat.Png;

namespace FileFormat.OpenRaster;

/// <summary>Reads OpenRaster (.ora) files from bytes, streams, or file paths.</summary>
public static class OpenRasterReader {

  private const string MIMETYPE_VALUE = "image/openraster";

  public static OpenRasterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("OpenRaster file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static OpenRasterFile FromStream(Stream stream) {
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

  public static OpenRasterFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 4)
      throw new InvalidDataException("Data too small for a valid OpenRaster file.");

    // ZipArchive requires a Stream backed by byte[]
    using var zipStream = new MemoryStream(data.ToArray(), writable: false);
    ZipArchive archive;
    try {
      archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
    } catch (InvalidDataException) {
      throw new InvalidDataException("Data is not a valid ZIP archive.");
    }

    using (archive) {
      // Validate mimetype
      var mimetypeEntry = archive.GetEntry("mimetype") ?? throw new InvalidDataException("OpenRaster archive missing mimetype entry.");
      var mimetypeText = _ReadEntryText(mimetypeEntry).Trim();
      if (mimetypeText != MIMETYPE_VALUE)
        throw new InvalidDataException($"Invalid OpenRaster mimetype: expected '{MIMETYPE_VALUE}', got '{mimetypeText}'.");

      // Parse stack.xml
      var stackEntry = archive.GetEntry("stack.xml") ?? throw new InvalidDataException("OpenRaster archive missing stack.xml entry.");
      var stackXml = _ReadEntryText(stackEntry);
      var doc = XDocument.Parse(stackXml);
      var imageElement = doc.Root ?? throw new InvalidDataException("stack.xml has no root element.");

      var canvasWidth = int.Parse(imageElement.Attribute("w")?.Value ?? "0", CultureInfo.InvariantCulture);
      var canvasHeight = int.Parse(imageElement.Attribute("h")?.Value ?? "0", CultureInfo.InvariantCulture);

      // Read merged image
      byte[] mergedPixelData = [];
      var mergedEntry = archive.GetEntry("mergedimage.png");
      if (mergedEntry != null) {
        var pngBytes = _ReadEntryBytes(mergedEntry);
        mergedPixelData = _PngToRgba(pngBytes, canvasWidth, canvasHeight);
      }

      // Read layers
      var layers = new List<OpenRasterLayer>();
      var stackElement = imageElement.Element("stack");
      if (stackElement != null) {
        foreach (var layerElement in stackElement.Elements("layer")) {
          var name = layerElement.Attribute("name")?.Value ?? "";
          var src = layerElement.Attribute("src")?.Value ?? "";
          var x = int.Parse(layerElement.Attribute("x")?.Value ?? "0", CultureInfo.InvariantCulture);
          var y = int.Parse(layerElement.Attribute("y")?.Value ?? "0", CultureInfo.InvariantCulture);
          var opacityStr = layerElement.Attribute("opacity")?.Value ?? "1.0";
          var opacity = float.Parse(opacityStr, CultureInfo.InvariantCulture);
          var visibilityStr = layerElement.Attribute("visibility")?.Value ?? "visible";
          var visibility = visibilityStr == "visible";

          var layerWidth = 0;
          var layerHeight = 0;
          byte[] layerPixelData = [];

          if (!string.IsNullOrEmpty(src)) {
            var layerEntry = archive.GetEntry(src);
            if (layerEntry != null) {
              var layerPngBytes = _ReadEntryBytes(layerEntry);
              var layerPng = PngReader.FromBytes(layerPngBytes);
              layerWidth = layerPng.Width;
              layerHeight = layerPng.Height;
              layerPixelData = _PngFileToRgba(layerPng);
            }
          }

          layers.Add(new OpenRasterLayer {
            Name = name,
            X = x,
            Y = y,
            Width = layerWidth,
            Height = layerHeight,
            Opacity = opacity,
            Visibility = visibility,
            PixelData = layerPixelData
          });
        }
      }

      return new OpenRasterFile {
        Width = canvasWidth,
        Height = canvasHeight,
        PixelData = mergedPixelData,
        Layers = layers
      };
    }
  
  }

  public static OpenRasterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
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

  private static byte[] _PngToRgba(byte[] pngBytes, int expectedWidth, int expectedHeight) {
    var pngFile = PngReader.FromBytes(pngBytes);
    return _PngFileToRgba(pngFile);
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
