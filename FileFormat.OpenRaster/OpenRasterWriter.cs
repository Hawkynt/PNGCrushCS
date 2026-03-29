using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FileFormat.Png;

namespace FileFormat.OpenRaster;

/// <summary>Assembles OpenRaster (.ora) file bytes from an OpenRaster data model.</summary>
public static class OpenRasterWriter {

  private const string MIMETYPE_VALUE = "image/openraster";

  public static byte[] ToBytes(OpenRasterFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
      // mimetype MUST be first entry, stored uncompressed
      _WriteEntry(archive, "mimetype", Encoding.ASCII.GetBytes(MIMETYPE_VALUE), CompressionLevel.NoCompression);

      // Write layer PNGs
      var layerEntries = new List<(string src, OpenRasterLayer layer)>();
      for (var i = 0; i < file.Layers.Count; ++i) {
        var layer = file.Layers[i];
        var src = $"data/layer{i}.png";
        var pngBytes = _RgbaToPng(layer.PixelData, layer.Width, layer.Height);
        _WriteEntry(archive, src, pngBytes, CompressionLevel.SmallestSize);
        layerEntries.Add((src, layer));
      }

      // Write mergedimage.png
      var mergedPngBytes = _RgbaToPng(file.PixelData, file.Width, file.Height);
      _WriteEntry(archive, "mergedimage.png", mergedPngBytes, CompressionLevel.SmallestSize);

      // Build and write stack.xml
      var stackXml = _BuildStackXml(file.Width, file.Height, layerEntries);
      _WriteEntry(archive, "stack.xml", Encoding.UTF8.GetBytes(stackXml), CompressionLevel.SmallestSize);
    }

    return ms.ToArray();
  }

  private static void _WriteEntry(ZipArchive archive, string entryName, byte[] data, CompressionLevel level) {
    var entry = archive.CreateEntry(entryName, level);
    using var stream = entry.Open();
    stream.Write(data, 0, data.Length);
  }

  private static string _BuildStackXml(int width, int height, List<(string src, OpenRasterLayer layer)> layers) {
    var imageElement = new XElement("image",
      new XAttribute("version", "0.0.3"),
      new XAttribute("w", width.ToString(CultureInfo.InvariantCulture)),
      new XAttribute("h", height.ToString(CultureInfo.InvariantCulture))
    );

    var stackElement = new XElement("stack");

    foreach (var (src, layer) in layers) {
      var layerElement = new XElement("layer",
        new XAttribute("name", layer.Name),
        new XAttribute("src", src),
        new XAttribute("x", layer.X.ToString(CultureInfo.InvariantCulture)),
        new XAttribute("y", layer.Y.ToString(CultureInfo.InvariantCulture)),
        new XAttribute("opacity", layer.Opacity.ToString("F1", CultureInfo.InvariantCulture)),
        new XAttribute("visibility", layer.Visibility ? "visible" : "hidden")
      );
      stackElement.Add(layerElement);
    }

    imageElement.Add(stackElement);

    var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), imageElement);
    using var sw = new StringWriter();
    doc.Save(sw);
    return sw.ToString();
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
