using System;
using System.Buffers.Binary;
using System.Linq;
using FileFormat.Core;
using FileFormat.Fpx;
using FileFormat.Word;

namespace FileFormat.Word.Tests;

[TestFixture]
public sealed class WordBinaryFileTests {

  [TestCase(".doc", false)]
  [TestCase(".dot", true)]
  [Category("Integration")]
  public void Writer_CreatesNativeWordBinaryDocumentAndRoundTrips(string extension, bool template) {
    var source = _Picture(19, 11);
    var bytes = WordWriter.ToBytes(WordFile.FromRawImage(source, extension));

    Assert.That(CompoundFile.HasSignature(bytes), Is.True);
    var compound = new CompoundFile(bytes);
    var streams = compound.Streams().Where(pair => pair.Value.Type == CompoundFile.EntryStream).ToArray();
    var word = compound.Read(streams.Single(pair => pair.Key.Equals("/WordDocument", StringComparison.OrdinalIgnoreCase)).Value);
    var data = compound.Read(streams.Single(pair => pair.Key.Equals("/Data", StringComparison.OrdinalIgnoreCase)).Value);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(word), Is.EqualTo(0xA5EC), "FIB magic");
      Assert.That(streams.Any(pair => pair.Key.Equals("/1Table", StringComparison.OrdinalIgnoreCase)), Is.True, "FIB-selected table stream");
      Assert.That((BinaryPrimitives.ReadUInt16LittleEndian(word.AsSpan(10)) & 1) != 0, Is.EqualTo(template), "FIB fDot");
      Assert.That(data.AsSpan().IndexOf([137, 80, 78, 71, 13, 10, 26, 10]), Is.GreaterThanOrEqualTo(0), "OfficeArt PNG");
    });

    _AssertSame(source, WordFile.ToRawImage(WordReader.FromSpan(bytes)));
  }

  [Test]
  [Category("Integration")]
  public void Writer_PreservesLargeSourcePixelsEvenWhenDisplaySizeMustFitPicfTwips() {
    var source = _Picture(2200, 2);
    var bytes = WordWriter.ToBytes(WordFile.FromRawImage(source, ".doc"));
    _AssertSame(source, WordFile.ToRawImage(WordReader.FromSpan(bytes)));
  }

  private static RawImage _Picture(int width, int height) {
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

  private static void _AssertSame(RawImage expected, RawImage actual) {
    Assert.Multiple(() => {
      Assert.That((actual.Width, actual.Height), Is.EqualTo((expected.Width, expected.Height)));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(actual.PixelData, Is.EqualTo(expected.PixelData));
    });
  }
}
