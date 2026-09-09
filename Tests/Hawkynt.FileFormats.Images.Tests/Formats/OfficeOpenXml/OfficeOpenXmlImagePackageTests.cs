using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Excel;
using FileFormat.PowerPoint;
using FileFormat.Word;

namespace FileFormat.OfficeOpenXml.Tests;

[TestFixture]
public sealed class OfficeOpenXmlImagePackageTests {

  private static readonly object[] _WordVariants = [
    new object[] { ".docx", OfficeOpenXmlImagePackage.WordDocumentContentType },
    new object[] { ".docm", OfficeOpenXmlImagePackage.WordMacroDocumentContentType },
    new object[] { ".dotx", OfficeOpenXmlImagePackage.WordTemplateContentType },
    new object[] { ".dotm", OfficeOpenXmlImagePackage.WordMacroTemplateContentType },
  ];

  private static readonly object[] _ExcelVariants = [
    new object[] { ".xlsx", OfficeOpenXmlImagePackage.ExcelWorkbookContentType },
    new object[] { ".xlsm", OfficeOpenXmlImagePackage.ExcelMacroWorkbookContentType },
    new object[] { ".xltx", OfficeOpenXmlImagePackage.ExcelTemplateContentType },
    new object[] { ".xltm", OfficeOpenXmlImagePackage.ExcelMacroTemplateContentType },
  ];

  private static readonly object[] _PowerPointVariants = [
    new object[] { ".pptx", OfficeOpenXmlImagePackage.PowerPointPresentationContentType },
    new object[] { ".ppsx", OfficeOpenXmlImagePackage.PowerPointSlideShowContentType },
    new object[] { ".potx", OfficeOpenXmlImagePackage.PowerPointTemplateContentType },
    new object[] { ".pptm", OfficeOpenXmlImagePackage.PowerPointMacroPresentationContentType },
    new object[] { ".ppsm", OfficeOpenXmlImagePackage.PowerPointMacroSlideShowContentType },
    new object[] { ".potm", OfficeOpenXmlImagePackage.PowerPointMacroTemplateContentType },
  ];

  private static RawImage _Picture(int width = 19, int height = 11) {
    var pixels = new byte[checked(width * height * 3)];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 29 + y * 17);
      pixels[at + 1] = (byte)(x * 7 + y * 41);
      pixels[at + 2] = (byte)(x * 31 + y * 13);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [TestCaseSource(nameof(_WordVariants))]
  [Category("Integration")]
  public void WordWriter_CreatesNativePackageAndRoundTrips(string extension, string contentType) {
    var source = _Picture();
    var file = WordFile.FromRawImage(source, extension);
    var bytes = WordWriter.ToBytes(file);

    _AssertPackage(bytes, "/word/document.xml", contentType, [
      "word/document.xml",
      "word/_rels/document.xml.rels",
      "word/media/image1.png",
    ]);

    var actual = WordFile.ToRawImage(WordReader.FromSpan(bytes));
    _AssertSameImage(source, actual);
  }

  [TestCaseSource(nameof(_ExcelVariants))]
  [Category("Integration")]
  public void ExcelWriter_CreatesNativePackageAndRoundTrips(string extension, string contentType) {
    var source = _Picture();
    var file = ExcelFile.FromRawImage(source, extension);
    var bytes = ExcelWriter.ToBytes(file);

    _AssertPackage(bytes, "/xl/workbook.xml", contentType, [
      "xl/workbook.xml",
      "xl/worksheets/sheet1.xml",
      "xl/drawings/drawing1.xml",
      "xl/drawings/_rels/drawing1.xml.rels",
      "xl/media/image1.png",
    ]);

    var actual = ExcelFile.ToRawImage(ExcelReader.FromSpan(bytes));
    _AssertSameImage(source, actual);
  }

  [TestCaseSource(nameof(_PowerPointVariants))]
  [Category("Integration")]
  public void PowerPointWriter_CreatesCompleteMinimumPresentationAndRoundTrips(string extension, string contentType) {
    var source = _Picture();
    var file = PowerPointFile.FromRawImage(source, extension);
    var bytes = _Write(file);

    _AssertPackage(bytes, "/ppt/presentation.xml", contentType, [
      "ppt/presentation.xml",
      "ppt/presProps.xml",
      "ppt/slides/slide1.xml",
      "ppt/slideLayouts/slideLayout1.xml",
      "ppt/slideMasters/slideMaster1.xml",
      "ppt/theme/theme1.xml",
      "ppt/media/image1.png",
    ]);

    var actual = PowerPointFile.ToRawImage(_ReadPowerPoint(bytes));
    _AssertSameImage(source, actual);
  }

  [TestCase(".ppt")]
  [TestCase(".pps")]
  [TestCase(".pot")]
  [Category("Integration")]
  public void PowerPointLegacyExtensions_StayCompoundBinary(string extension) {
    var source = _Picture(7, 5);
    var bytes = _Write(PowerPointFile.FromRawImage(source, extension));

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, PowerPointFile.Signature.Length).SequenceEqual(PowerPointFile.Signature), Is.True);
      Assert.That(bytes.AsSpan(0, 4).SequenceEqual("PK\x03\x04"u8), Is.False);
    });

    _AssertSameImage(source, PowerPointFile.ToRawImage(PowerPointReader.FromBytes(bytes)));
  }

  [Test]
  [Category("Unit")]
  public void Writers_RejectUnsupportedExtensions() {
    var source = _Picture(2, 2);

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => WordFile.FromRawImage(source, ".doc"));
      Assert.Throws<ArgumentException>(() => ExcelFile.FromRawImage(source, ".xls"));
      Assert.Throws<ArgumentException>(() => PowerPointFile.FromRawImage(source, ".odp"));
    });
  }

  [Test]
  [Category("Unit")]
  public void Readers_RejectWrongOfficeFamilyEvenThoughAllAreZipPackages() {
    var source = _Picture(3, 2);
    var word = WordWriter.ToBytes(WordFile.FromRawImage(source));
    var excel = ExcelWriter.ToBytes(ExcelFile.FromRawImage(source));

    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => ExcelReader.FromSpan(word));
      Assert.Throws<InvalidDataException>(() => WordReader.FromSpan(excel));
      Assert.Throws<InvalidDataException>(() => _ReadPowerPoint(word));
    });
  }

  private static byte[] _Write(PowerPointFile file) => _WriteViaContract(file);

  private static PowerPointFile _ReadPowerPoint(ReadOnlySpan<byte> bytes) => _ReadViaContract<PowerPointFile>(bytes);

  private static byte[] _WriteViaContract<T>(T file)
    where T : IImageFormatWriter<T>
    => T.ToBytes(file);

  private static T _ReadViaContract<T>(ReadOnlySpan<byte> bytes)
    where T : IImageFormatReader<T>
    => T.FromSpan(bytes);

  private static void _AssertPackage(
    byte[] bytes,
    string mainPart,
    string expectedContentType,
    IReadOnlyCollection<string> requiredEntries) {

    Assert.That(bytes.AsSpan(0, 4).SequenceEqual("PK\x03\x04"u8), Is.True, "OPC/ZIP signature");
    using var memory = new MemoryStream(bytes, false);
    using var archive = new ZipArchive(memory, ZipArchiveMode.Read, false, Encoding.UTF8);

    foreach (var path in requiredEntries)
      Assert.That(archive.GetEntry(path), Is.Not.Null, path);

    var types = archive.GetEntry("[Content_Types].xml");
    Assert.That(types, Is.Not.Null);
    using var reader = new StreamReader(types!.Open(), Encoding.UTF8);
    var content = reader.ReadToEnd();
    Assert.Multiple(() => {
      Assert.That(content, Does.Contain($"PartName=\"{mainPart}\""));
      Assert.That(content, Does.Contain($"ContentType=\"{expectedContentType}\""));
      Assert.That(content, Does.Contain("ContentType=\"image/png\""));
    });

    var media = requiredEntries.First(path => path.EndsWith("/media/image1.png", StringComparison.Ordinal));
    using var image = archive.GetEntry(media)!.Open();
    Span<byte> signature = stackalloc byte[8];
    image.ReadExactly(signature);
    ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    Assert.That(signature.SequenceEqual(pngSignature), Is.True, "embedded PNG signature");
  }

  private static void _AssertSameImage(RawImage expected, RawImage actual) {
    Assert.Multiple(() => {
      Assert.That((actual.Width, actual.Height), Is.EqualTo((expected.Width, expected.Height)));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }
}
