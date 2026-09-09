using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Core;
using FileFormat.Excel;
using FileFormat.Png;
using FileFormat.PowerPoint;
using FileFormat.Word;

namespace FileFormat.OfficeOpenXml.Tests;

[TestFixture]
public sealed class OfficeMultiImageExtractionTests {

  [Test]
  [Category("Integration")]
  public void WordReader_ReturnsMediaBackgroundObjectPreviewThumbnailAndIcon() {
    var expected = _Pictures();
    var package = WordWriter.ToBytes(WordFile.FromRawImage(expected[0]));
    package = _AddPngParts(package, "word/media/", expected[1..]);

    var file = WordReader.FromSpan(package);

    Assert.That(WordFile.ImageCount(file), Is.EqualTo(expected.Length));
    _AssertImages(expected, i => WordFile.ToRawImage(file, i));
  }

  [Test]
  [Category("Integration")]
  public void ExcelReader_ReturnsMediaBackgroundObjectPreviewThumbnailAndIcon() {
    var expected = _Pictures();
    var package = ExcelWriter.ToBytes(ExcelFile.FromRawImage(expected[0]));
    package = _AddPngParts(package, "xl/media/", expected[1..]);

    var file = ExcelReader.FromSpan(package);

    Assert.That(ExcelFile.ImageCount(file), Is.EqualTo(expected.Length));
    _AssertImages(expected, i => ExcelFile.ToRawImage(file, i));
  }

  [Test]
  [Category("Integration")]
  public void PowerPointReader_ReturnsMediaBackgroundObjectPreviewThumbnailAndIcon() {
    var expected = _Pictures();
    var package = _Write(PowerPointFile.FromRawImage(expected[0], ".pptx"));
    package = _AddPngParts(package, "ppt/media/", expected[1..]);

    var file = _ReadPowerPoint(package);

    Assert.That(PowerPointFile.ImageCount(file), Is.EqualTo(expected.Length));
    _AssertImages(expected, i => PowerPointFile.ToRawImage(file, i));
  }

  [Test]
  [Category("Integration")]
  public void LegacyPowerPointReader_ReturnsEveryBlipInPicturesStream() {
    var first = _Picture(7, 5, 17);
    var second = _Picture(11, 3, 93);
    var bytes = PowerPointWriter.ToBytes(PowerPointFile.FromRawImage(first));
    var secondFile = PowerPointWriter.ToBytes(PowerPointFile.FromRawImage(second));

    var firstLength = checked(PowerPointFile.RecordHeaderSize
      + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(PowerPointFile.ScanStart + 4)));
    var secondLength = checked(PowerPointFile.RecordHeaderSize
      + (int)BinaryPrimitives.ReadUInt32LittleEndian(secondFile.AsSpan(PowerPointFile.ScanStart + 4)));
    Assert.That(firstLength + secondLength, Is.LessThan(4096), "test BLIPs must fit in the declared Pictures stream");

    secondFile.AsSpan(PowerPointFile.ScanStart, secondLength)
      .CopyTo(bytes.AsSpan(PowerPointFile.ScanStart + firstLength));

    var file = PowerPointReader.FromBytes(bytes);

    Assert.That(PowerPointFile.ImageCount(file), Is.EqualTo(2));
    _AssertSame(first, PowerPointFile.ToRawImage(file, 0));
    _AssertSame(second, PowerPointFile.ToRawImage(file, 1));
  }

  private static RawImage[] _Pictures() => [
    _Picture(19, 11, 1),
    _Picture(13, 7, 31),
    _Picture(9, 15, 61),
    _Picture(5, 4, 101),
    _Picture(3, 8, 151),
  ];

  private static RawImage _Picture(int width, int height, int seed) {
    var pixels = new byte[checked(width * height * 3)];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(seed + x * 29 + y * 17);
      pixels[at + 1] = (byte)(seed * 3 + x * 7 + y * 41);
      pixels[at + 2] = (byte)(seed * 5 + x * 31 + y * 13);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte[] _AddPngParts(byte[] package, string mediaPrefix, ReadOnlySpan<RawImage> pictures) {
    using var memory = new MemoryStream();
    memory.Write(package);
    memory.Position = 0;
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Update, true, Encoding.UTF8)) {
      _WritePng(archive, mediaPrefix + "background.png", pictures[0]);
      _WritePng(archive, mediaPrefix + "objectPreview.png", pictures[1]);
      _WritePng(archive, "docProps/thumbnail.png", pictures[2]);
      _WritePng(archive, "customUI/icon.png", pictures[3]);
    }
    return memory.ToArray();
  }

  private static void _WritePng(ZipArchive archive, string path, RawImage image) {
    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(image));
    var entry = archive.CreateEntry(path, CompressionLevel.SmallestSize);
    using var stream = entry.Open();
    stream.Write(bytes);
  }

  private static void _AssertImages(IReadOnlyList<RawImage> expected, Func<int, RawImage> actual) {
    for (var i = 0; i < expected.Count; ++i)
      _AssertSame(expected[i], actual(i));
  }

  private static void _AssertSame(RawImage expected, RawImage actual) {
    actual = actual.EnsureFormat(PixelFormat.Rgb24);
    Assert.Multiple(() => {
      Assert.That((actual.Width, actual.Height), Is.EqualTo((expected.Width, expected.Height)));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  private static byte[] _Write(PowerPointFile file) => _WriteViaContract(file);
  private static PowerPointFile _ReadPowerPoint(ReadOnlySpan<byte> bytes) => _ReadViaContract<PowerPointFile>(bytes);

  private static byte[] _WriteViaContract<T>(T file) where T : IImageFormatWriter<T> => T.ToBytes(file);
  private static T _ReadViaContract<T>(ReadOnlySpan<byte> bytes) where T : IImageFormatReader<T> => T.FromSpan(bytes);
}
