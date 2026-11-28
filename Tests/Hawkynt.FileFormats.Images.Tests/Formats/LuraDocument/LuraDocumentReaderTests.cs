using System;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.LuraDocument.Tests;

[TestFixture]
public sealed class LuraDocumentReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => LuraDocumentReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => LuraDocumentReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ldf"));
    Assert.Throws<FileNotFoundException>(() => LuraDocumentReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_MatchesFromBytes() {
    var data = LuraDocumentFixtures.Bytes("luratech-colour-mrc.ldf");
    using var stream = new MemoryStream(data);
    Assert.That(LuraDocumentReader.FromStream(stream).Pages[0].PixelData, Is.EqualTo(LuraDocumentReader.FromBytes(data).Pages[0].PixelData));
  }

  [TestCase("luratech-colour-mrc.ldf", TestName = "Masked page")]
  [TestCase("luratech-grey-photo.ldf", TestName = "Page without a mask")]
  [TestCase("luratech-multipage.ldf", TestName = "Three pages")]
  [Category("Unit")]
  public void IsDetectedByItsDirectories(string name)
    => Assert.That(FormatRegistry.DetectFromBytes(LuraDocumentFixtures.Bytes(name)), Is.EqualTo(ImageFormat.LuraDocument));

  [Test]
  [Category("Unit")]
  public void PlainTiffIsNotTakenForLuraDocument() {
    var tiff = FormatIO.Encode<FileFormat.Tiff.TiffFile>(new RawImage { Width = 4, Height = 3, Format = PixelFormat.Rgb24, PixelData = new byte[36] });
    Assert.Multiple(() => {
      Assert.That(LuraDocumentFile.ReadImageInfo(tiff), Is.Null);
      Assert.That(FormatRegistry.DetectFromBytes(tiff), Is.Not.EqualTo(ImageFormat.LuraDocument));
    });
  }

  [TestCase(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46 }, TestName = "A JPEG")]
  [TestCase(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 }, TestName = "A PNG")]
  [TestCase(new byte[] { 0x42, 0x4D, 0x3A, 0, 0, 0, 0, 0, 0, 0 }, TestName = "A BMP")]
  [Category("Unit")]
  public void OtherFormatsAreRefused(byte[] header) {
    var data = new byte[256];
    header.CopyTo(data, 0);
    Assert.Multiple(() => {
      Assert.That(() => LuraDocumentReader.FromBytes(data), Throws.TypeOf<InvalidDataException>());
      Assert.That(LuraDocumentFile.ReadImageInfo(data), Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void BigEndianTiffIsRefusedByName() {
    var data = new byte[] { 0x4D, 0x4D, 0, 0x2A, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0 };
    Assert.That(() => LuraDocumentReader.FromBytes(data), Throws.TypeOf<InvalidDataException>().With.Message.Contains("Big-endian"));
  }

  [TestCase("luratech-colour-mrc.ldf", 24, "Colour", 1)]
  [TestCase("luratech-grey-mrc.ldf", 8, "Grey", 1)]
  [TestCase("luratech-bilevel.ldf", 1, "Bilevel", 1)]
  [TestCase("crafted-no-foreground.ldf", 1, "Bilevel", 1)]
  [TestCase("luratech-multipage.ldf", 1, "Bilevel", 3)]
  [Category("Unit")]
  public void ImageInfo_MatchesTheDecode(string name, int bits, string mode, int pages) {
    var data = LuraDocumentFixtures.Bytes(name);
    var info = LuraDocumentFile.ReadImageInfo(data);
    var file = LuraDocumentReader.FromBytes(data);
    Assert.That(info, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(info!.Value.Width, Is.EqualTo(file.Width));
      Assert.That(info.Value.Height, Is.EqualTo(file.Height));
      Assert.That(info.Value.BitsPerPixel, Is.EqualTo(bits));
      Assert.That(info.Value.ColorMode, Is.EqualTo(mode));
      Assert.That(file.Pages[0].Kind.ToString(), Is.EqualTo(mode));
      Assert.That(info.Value.FrameCount, Is.EqualTo(pages));
      Assert.That(LuraDocumentFile.ImageCount(file), Is.EqualTo(pages));
    });
  }

  [Test]
  [Category("Unit")]
  public void BilevelPageBecomesABlackAndWhiteIndexedImage() {
    var raw = LuraDocumentFile.ToRawImage(LuraDocumentReader.FromBytes(LuraDocumentFixtures.Bytes("luratech-bilevel.ldf")));
    Assert.Multiple(() => {
      Assert.That(raw.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(raw.Palette, Is.EqualTo(new byte[] { 0, 0, 0, 255, 255, 255 }));
      Assert.That(raw.PaletteCount, Is.EqualTo(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void PagesAreReachableByIndex() {
    var file = LuraDocumentReader.FromBytes(LuraDocumentFixtures.Bytes("luratech-multipage.ldf"));
    Assert.Multiple(() => {
      Assert.That(LuraDocumentFile.ToRawImage(file, 1).Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(LuraDocumentFile.ToRawImage(file, 2).Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(() => LuraDocumentFile.ToRawImage(file, 3), Throws.TypeOf<ArgumentOutOfRangeException>());
    });
  }

  [Test]
  [Category("Unit")]
  public void MaskWithoutResolutionIsRefused() {
    // Renumbering the resolution tags to private ones removes the resolution the mask's scrambling is keyed on.
    var data = LuraDocumentFixtures.Bytes("luratech-bilevel.ldf");
    var count = BitConverter.ToUInt16(data, 8);
    for (var i = 0; i < count; ++i) {
      var entry = 10 + i * 12;
      var tag = BitConverter.ToUInt16(data, entry);
      if (tag is 282 or 283)
        BitConverter.GetBytes((ushort)(tag + 40000)).CopyTo(data, entry);
    }

    Assert.That(() => LuraDocumentReader.FromBytes(data), Throws.TypeOf<InvalidDataException>().With.Message.Contains("resolution"));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedFileIsRefused() {
    var data = LuraDocumentFixtures.Bytes("luratech-colour-mrc.ldf");
    Assert.That(() => LuraDocumentReader.FromBytes(data[..(data.Length / 2)]), Throws.TypeOf<InvalidDataException>());
  }
}
