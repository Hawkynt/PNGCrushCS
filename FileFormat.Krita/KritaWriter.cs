using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Png;

namespace FileFormat.Krita;

/// <summary>Assembles Krita (.kra) file bytes from a Krita data model.</summary>
public static class KritaWriter {

  private const string _MIMETYPE_VALUE = "application/x-krita";

  public static byte[] ToBytes(KritaFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
      // mimetype MUST be first entry, stored uncompressed
      _WriteEntry(archive, "mimetype", Encoding.ASCII.GetBytes(_MIMETYPE_VALUE), CompressionLevel.NoCompression);

      // Write mergedimage.png
      var mergedPngBytes = _RgbaToPng(file.PixelData, file.Width, file.Height);
      _WriteEntry(archive, "mergedimage.png", mergedPngBytes, CompressionLevel.SmallestSize);

      // Write minimal maindoc.xml
      var maindocXml = _BuildMaindocXml(file.Width, file.Height);
      _WriteEntry(archive, "maindoc.xml", Encoding.UTF8.GetBytes(maindocXml), CompressionLevel.SmallestSize);
    }

    return ms.ToArray();
  }

  private static void _WriteEntry(ZipArchive archive, string entryName, byte[] data, CompressionLevel level) {
    var entry = archive.CreateEntry(entryName, level);
    using var stream = entry.Open();
    stream.Write(data, 0, data.Length);
  }

  private static string _BuildMaindocXml(int width, int height) {
    var w = width.ToString(CultureInfo.InvariantCulture);
    var h = height.ToString(CultureInfo.InvariantCulture);
    return $"""
      <?xml version="1.0" encoding="UTF-8"?>
      <!DOCTYPE DOC PUBLIC '-//KDE//DTD krita 2.0//EN' 'http://www.calligra.org/DTD/krita-2.0.dtd'>
      <DOC xmlns="http://www.calligra.org/DTD/krita" syntaxVersion="2.0" kritaVersion="5.0.0">
        <IMAGE width="{w}" height="{h}" mime="application/x-krita" name="Untitled">
          <layers>
            <layer name="Background" colorspacename="RGBA" />
          </layers>
        </IMAGE>
      </DOC>
      """;
  }

  private static byte[] _RgbaToPng(byte[] rgbaData, int width, int height) {
    if (width <= 0 || height <= 0)
      return _BuildEmptyPng();

    var bytesPerScanline = width * 4;
    var scanlines = new byte[height][];
    for (var y = 0; y < height; ++y) {
      scanlines[y] = new byte[bytesPerScanline];
      var srcOffset = y * bytesPerScanline;
      var available = Math.Min(bytesPerScanline, rgbaData.Length - srcOffset);
      if (available > 0)
        Buffer.BlockCopy(rgbaData, srcOffset, scanlines[y], 0, available);
    }

    var pngFile = new PngFile {
      Width = width,
      Height = height,
      BitDepth = 8,
      ColorType = PngColorType.RGBA,
      PixelData = scanlines
    };

    return PngWriter.ToBytes(pngFile);
  }

  private static byte[] _BuildEmptyPng() {
    var pngFile = new PngFile {
      Width = 1,
      Height = 1,
      BitDepth = 8,
      ColorType = PngColorType.RGBA,
      PixelData = [new byte[4]]
    };

    return PngWriter.ToBytes(pngFile);
  }
}
