using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FileFormat.Pdf;

/// <summary>Writes a minimal valid PDF file containing embedded raster images.</summary>
public static class PdfWriter {

  // PDF header + binary comment (4 bytes > 0x80 indicate binary content to transport layers)
  private static readonly byte[] _Header = [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', (byte)'1', (byte)'.', (byte)'4', (byte)'\n', (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n'];

  public static byte[] ToBytes(PdfFile file) {
    ArgumentNullException.ThrowIfNull(file);

    if (file.Images.Count == 0)
      return _WriteEmptyPdf();

    return file.Images.Count == 1
      ? _WriteSingleImagePdf(file.Images[0])
      : _WriteMultiImagePdf(file.Images);
  }

  private static byte[] _WriteEmptyPdf() {
    using var ms = new MemoryStream();
    var offsets = new List<long>();

    ms.Write(_Header, 0, _Header.Length);

    // Object 1: Catalog
    offsets.Add(ms.Position);
    _WriteAscii(ms, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

    // Object 2: Pages (empty)
    offsets.Add(ms.Position);
    _WriteAscii(ms, "2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

    // xref
    var xrefOffset = ms.Position;
    _WriteAscii(ms, "xref\n");
    _WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "0 {0}\n", offsets.Count + 1));
    _WriteAscii(ms, "0000000000 65535 f \n");
    foreach (var off in offsets)
      _WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "{0:D10} 00000 n \n", off));

    _WriteAscii(ms, "trailer\n");
    _WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "<< /Size {0} /Root 1 0 R >>\n", offsets.Count + 1));
    _WriteAscii(ms, "startxref\n");
    _WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "{0}\n", xrefOffset));
    _WriteAscii(ms, "%%EOF\n");

    return ms.ToArray();
  }

  private static byte[] _WriteSingleImagePdf(PdfImage image) {
    using var ms = new MemoryStream();
    var offsets = new List<long>();

    ms.Write(_Header, 0, _Header.Length);

    var compressedPixels = _Compress(image.PixelData);
    var csName = _ColorSpaceName(image.ColorSpace);

    // Object 1: Catalog
    offsets.Add(ms.Position);
    _WriteAscii(ms, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

    // Object 2: Pages
    offsets.Add(ms.Position);
    _WriteAscii(ms, "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

    // Object 3: Page
    offsets.Add(ms.Position);
    _WriteAscii(ms, string.Format(
      CultureInfo.InvariantCulture,
      "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0} {1}] /Contents 5 0 R /Resources << /XObject << /Im1 4 0 R >> >> >>\nendobj\n",
      image.Width, image.Height));

    // Object 4: Image XObject
    offsets.Add(ms.Position);
    _WriteAscii(ms, string.Format(
      CultureInfo.InvariantCulture,
      "4 0 obj\n<< /Type /XObject /Subtype /Image /Width {0} /Height {1} /ColorSpace {2} /BitsPerComponent {3} /Filter /FlateDecode /Length {4} >>\nstream\n",
      image.Width, image.Height, csName, image.BitsPerComponent, compressedPixels.Length));
    ms.Write(compressedPixels, 0, compressedPixels.Length);
    _WriteAscii(ms, "\nendstream\nendobj\n");

    // Object 5: Content stream
    var contentStr = string.Format(
      CultureInfo.InvariantCulture,
      "q {0} 0 0 {1} 0 0 cm /Im1 Do Q",
      image.Width, image.Height);
    var contentBytes = Encoding.ASCII.GetBytes(contentStr);
    var compressedContent = _Compress(contentBytes);

    offsets.Add(ms.Position);
    _WriteAscii(ms, string.Format(
      CultureInfo.InvariantCulture,
      "5 0 obj\n<< /Length {0} /Filter /FlateDecode >>\nstream\n",
      compressedContent.Length));
    ms.Write(compressedContent, 0, compressedContent.Length);
    _WriteAscii(ms, "\nendstream\nendobj\n");

    _WriteXrefAndTrailer(ms, offsets);
    return ms.ToArray();
  }

  private static byte[] _WriteMultiImagePdf(IReadOnlyList<PdfImage> images) {
    using var ms = new MemoryStream();
    var offsets = new List<long>();

    ms.Write(_Header, 0, _Header.Length);

    // Object numbering:
    // 1 = Catalog, 2 = Pages
    // For each image i (0-based): page = 3 + i*3, image xobj = 4 + i*3, content = 5 + i*3
    var imageCount = images.Count;

    // Object 1: Catalog
    offsets.Add(ms.Position);
    _WriteAscii(ms, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

    // Build Kids array string
    var kidsBuilder = new StringBuilder();
    kidsBuilder.Append('[');
    for (var i = 0; i < imageCount; ++i) {
      if (i > 0)
        kidsBuilder.Append(' ');

      var pageObjNum = 3 + i * 3;
      kidsBuilder.Append(pageObjNum.ToString(CultureInfo.InvariantCulture));
      kidsBuilder.Append(" 0 R");
    }
    kidsBuilder.Append(']');

    // Object 2: Pages
    offsets.Add(ms.Position);
    _WriteAscii(ms, string.Format(
      CultureInfo.InvariantCulture,
      "2 0 obj\n<< /Type /Pages /Kids {0} /Count {1} >>\nendobj\n",
      kidsBuilder, imageCount));

    var nextObj = 3;

    // Write each image's triple (page, image xobj, content stream)
    for (var i = 0; i < imageCount; ++i) {
      var image = images[i];
      var pageObj = 3 + i * 3;
      var imgObj = 4 + i * 3;
      var contentObj = 5 + i * 3;

      var compressedPixels = _Compress(image.PixelData);
      var csName = _ColorSpaceName(image.ColorSpace);

      // Page object
      offsets.Add(ms.Position);
      _WriteAscii(ms, string.Format(
        CultureInfo.InvariantCulture,
        "{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1} {2}] /Contents {3} 0 R /Resources << /XObject << /Im1 {4} 0 R >> >> >>\nendobj\n",
        pageObj, image.Width, image.Height, contentObj, imgObj));

      // Image XObject
      offsets.Add(ms.Position);
      _WriteAscii(ms, string.Format(
        CultureInfo.InvariantCulture,
        "{0} 0 obj\n<< /Type /XObject /Subtype /Image /Width {1} /Height {2} /ColorSpace {3} /BitsPerComponent {4} /Filter /FlateDecode /Length {5} >>\nstream\n",
        imgObj, image.Width, image.Height, csName, image.BitsPerComponent, compressedPixels.Length));
      ms.Write(compressedPixels, 0, compressedPixels.Length);
      _WriteAscii(ms, "\nendstream\nendobj\n");

      // Content stream
      var contentStr = string.Format(
        CultureInfo.InvariantCulture,
        "q {0} 0 0 {1} 0 0 cm /Im1 Do Q",
        image.Width, image.Height);
      var contentBytes = Encoding.ASCII.GetBytes(contentStr);
      var compressedContent = _Compress(contentBytes);

      offsets.Add(ms.Position);
      _WriteAscii(ms, string.Format(
        CultureInfo.InvariantCulture,
        "{0} 0 obj\n<< /Length {1} /Filter /FlateDecode >>\nstream\n",
        contentObj, compressedContent.Length));
      ms.Write(compressedContent, 0, compressedContent.Length);
      _WriteAscii(ms, "\nendstream\nendobj\n");

      nextObj = contentObj + 1;
    }

    _WriteXrefAndTrailer(ms, offsets);
    return ms.ToArray();
  }

  private static void _WriteXrefAndTrailer(MemoryStream ms, List<long> offsets) {
    var xrefOffset = ms.Position;
    _WriteAscii(ms, "xref\n");
    _WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "0 {0}\n", offsets.Count + 1));
    _WriteAscii(ms, "0000000000 65535 f \n");
    foreach (var off in offsets)
      _WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "{0:D10} 00000 n \n", off));

    _WriteAscii(ms, "trailer\n");
    _WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "<< /Size {0} /Root 1 0 R >>\n", offsets.Count + 1));
    _WriteAscii(ms, "startxref\n");
    _WriteAscii(ms, string.Format(CultureInfo.InvariantCulture, "{0}\n", xrefOffset));
    _WriteAscii(ms, "%%EOF\n");
  }

  private static byte[] _Compress(byte[] data) {
    using var output = new MemoryStream();

    // Write 2-byte zlib header (CM=8, CINFO=7, no dict, FCHECK)
    output.WriteByte(0x78);
    output.WriteByte(0x9C);

    using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
      deflate.Write(data, 0, data.Length);

    // Compute and write Adler-32 checksum
    var adler = _Adler32(data);
    output.WriteByte((byte)(adler >> 24));
    output.WriteByte((byte)(adler >> 16));
    output.WriteByte((byte)(adler >> 8));
    output.WriteByte((byte)adler);

    return output.ToArray();
  }

  private static uint _Adler32(byte[] data) {
    uint a = 1, b = 0;
    foreach (var d in data) {
      a = (a + d) % 65521;
      b = (b + a) % 65521;
    }

    return (b << 16) | a;
  }

  private static string _ColorSpaceName(PdfColorSpace cs) => cs switch {
    PdfColorSpace.DeviceGray => "/DeviceGray",
    PdfColorSpace.DeviceCMYK => "/DeviceCMYK",
    _ => "/DeviceRGB",
  };

  private static void _WriteAscii(MemoryStream ms, string text) {
    var bytes = Encoding.ASCII.GetBytes(text);
    ms.Write(bytes, 0, bytes.Length);
  }
}
