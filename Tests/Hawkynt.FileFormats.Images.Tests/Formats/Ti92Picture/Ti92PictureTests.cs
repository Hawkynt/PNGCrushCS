using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Ti92Picture;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Ti92Picture.Tests;

/// <summary>TI-89 / TI-92 / TI-92 Plus / Voyage 200 picture variables.</summary>
/// <remarks>
/// The layout these tests build is the one measured on the four public samples of
/// <c>telparia.com/fileFormatSamples/image/tiCalc</c> (<c>test.92i</c>, <c>Pic2.89i</c>,
/// <c>FOLDER.89I</c>, <c>pict.9xI</c>): directory entries at absolute offsets, a big-endian length,
/// height and width, the bitmap, the <c>0xDF</c> tag and a checksum of the length and data bytes, which
/// the three files the calculators' link software wrote match exactly. The opt-in test reads the
/// samples themselves.
/// </remarks>
[TestFixture]
public sealed class Ti92PictureTests {

  private static Ti92PictureFile _Picture(string model, params (string Name, int Width, int Height, int Seed)[] pictures)
    => new() {
      Model = model,
      Folder = "main",
      Pictures = pictures.Select(p => {
        var bytes = new byte[(p.Width + 7) / 8 * p.Height];
        new Random(p.Seed).NextBytes(bytes);
        return new Ti92Picture(p.Name, p.Width, p.Height, bytes);
      }).ToList(),
    };

  [TestCase("92", "**TI92**")]
  [TestCase("89", "**TI89**")]
  [TestCase("92P", "**TI92P*")]
  [TestCase("V2", "**TIV2**")]
  [Category("Unit")]
  public void EveryModelRoundTripsUnderItsOwnSignature(string model, string signature) {
    var file = _Picture(model, ("pic1", 82, 54, 1));
    var bytes = Ti92PictureWriter.ToBytes(file);
    Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 8), Is.EqualTo(signature));
    var back = Ti92PictureReader.FromBytes(bytes);
    Assert.Multiple(() => {
      Assert.That(back.Model, Is.EqualTo(model));
      Assert.That(back.Folder, Is.EqualTo("main"));
      Assert.That(back.Pictures, Has.Count.EqualTo(1));
      Assert.That((back.Pictures[0].Name, back.Pictures[0].Width, back.Pictures[0].Height), Is.EqualTo(("pic1", 82, 54)));
      Assert.That(back.Pictures[0].PixelData, Is.EqualTo(file.Pictures[0].PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFileAccountsToTheByteAsTheLinkSoftwaresDo() {
    var bytes = Ti92PictureWriter.ToBytes(_Picture("89", ("folder", 16, 16, 2)));
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x3C)), Is.EqualTo(0x52), "the one variable follows the directory");
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x4C)), Is.EqualTo((uint)bytes.Length), "the stated file size");
      Assert.That(bytes[0x50..0x52], Is.EqualTo(new byte[] { 0xA5, 0x5A }));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0x56)), Is.EqualTo(37), "height, width, 32 bytes and the tag");
      Assert.That(bytes[^3], Is.EqualTo(Ti92PictureFile.PictureTag));
      var sum = bytes.AsSpan(0x56, 2 + 37).ToArray().Sum(static b => b) & 0xFFFF;
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(bytes.Length - 2)), Is.EqualTo(sum), "the checksum");
    });
  }

  [Test]
  [Category("Unit")]
  public void SeveralPicturesAreAllRead() {
    var file = _Picture("92", ("abydos1", 127, 63, 3), ("abydos2", 127, 63, 4));
    var back = Ti92PictureReader.FromBytes(Ti92PictureWriter.ToBytes(file));
    Assert.That(Ti92PictureFile.ImageCount(back), Is.EqualTo(2));
    Assert.That(Ti92PictureFile.ToRawImage(back, 1).PixelData, Is.EqualTo(file.Pictures[1].PixelData));
  }

  /// <summary>As the one TI-92 sample has it: no checksum after the first picture, and a file size it does not have.</summary>
  [Test]
  [Category("Unit")]
  public void OffsetsAreFollowedAndTheChecksumAndSizeAreNotHeldAgainstAFile() {
    var file = _Picture("92", ("abydos1", 127, 63, 5), ("abydos2", 127, 63, 6));
    var bytes = Ti92PictureWriter.ToBytes(file).ToList();
    var first = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.ToArray().AsSpan(0x3C));
    var second = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.ToArray().AsSpan(0x4C));
    bytes.RemoveRange(second - 2, 2); // drop the first picture's checksum
    var data = bytes.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x4C), (uint)(second - 2));
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x5C), 1103);
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(data.Length - 2), 0x1234);
    var back = Ti92PictureReader.FromBytes(data);
    Assert.That(back.Pictures.Select(p => p.PixelData), Is.EqualTo(file.Pictures.Select(p => p.PixelData)));
    Assert.That(first, Is.EqualTo(0x62));
  }

  [Test]
  [Category("Unit")]
  public void AVariableOfAnotherKindIsPassedOver() {
    var data = Ti92PictureWriter.ToBytes(_Picture("92P", ("a", 8, 8, 7), ("b", 16, 4, 8)));
    data[0x3C + 12] = 0x1F; // the first entry is now a folder
    var back = Ti92PictureReader.FromBytes(data);
    Assert.That(back.Pictures.Select(p => p.Name), Is.EqualTo(new[] { "b" }));
  }

  [Test]
  [Category("Unit")]
  public void AFileWithNoPictureIsRefused() {
    var data = Ti92PictureWriter.ToBytes(_Picture("92P", ("a", 8, 8, 9)));
    data[0x3C + 12] = 0x1F;
    Assert.Throws<InvalidDataException>(() => Ti92PictureReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void APictureWhoseSizeItsLengthDoesNotHoldIsRefused() {
    var data = Ti92PictureWriter.ToBytes(_Picture("89", ("a", 16, 16, 10)));
    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0x58), 17);
    Assert.Throws<InvalidDataException>(() => Ti92PictureReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void TheOtherTiContainersAreNotClaimed() {
    var header = "**TI83**"u8.ToArray().Concat(new byte[64]).ToArray();
    Assert.That(Ti92PictureReader.ModelOf(header), Is.Null);
    Assert.Throws<InvalidDataException>(() => Ti92PictureReader.FromBytes(header));
  }

  [Test]
  [Category("Unit")]
  public void LitPixelsAreBlackAndAPictureFromRawImageFitsTheScreen() {
    var image = new RawImage { Width = 300, Height = 200, Format = PixelFormat.Gray8, PixelData = new byte[300 * 200] };
    var file = Ti92PictureFile.FromRawImage(image, ".89i");
    Assert.That((file.Model, file.Pictures[0].Width, file.Pictures[0].Height), Is.EqualTo(("89", 160, 100)));
    Assert.That(file.Pictures[0].PixelData.All(static b => b == 0xFF), Is.True, "a black picture is all lit");
    var raw = Ti92PictureFile.ToRawImage(file);
    Assert.That(raw.Palette![3..6], Is.EqualTo(new byte[] { 0, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryFindsItBySignatureUnderEveryExtension() {
    var bytes = Ti92PictureWriter.ToBytes(_Picture("92P", ("pict", 82, 54, 11)));
    Assert.That(FormatRegistry.DetectFromBytes(bytes), Is.EqualTo(ImageFormat.Ti92Picture));
  }

  /// <summary>Opt-in: point <c>TI92_SAMPLES</c> at the four files of telparia's <c>tiCalc</c> sample folder.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs the tiCalc samples; point TI92_SAMPLES at them.")]
  public void ThePublicSamplesReadAsTheyWereMeasured() {
    var directory = Environment.GetEnvironmentVariable("TI92_SAMPLES");
    if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
      Assert.Ignore("TI92_SAMPLES does not name a directory.");
    (string File, string Model, (string, int, int)[] Pictures)[] expected = [
      ("test.92i", "92", [("abydos1", 127, 63), ("abydos2", 127, 63)]),
      ("Pic2.89i", "92P", [("pic2", 159, 77)]),
      ("FOLDER.89I", "92P", [("folder", 16, 16)]),
      ("pict.9xI", "92P", [("pict", 82, 54)]),
    ];
    foreach (var (name, model, pictures) in expected) {
      var file = Ti92PictureReader.FromFile(new(Path.Combine(directory, name)));
      Assert.That(file.Model, Is.EqualTo(model), name);
      Assert.That(file.Pictures.Select(p => (p.Name, p.Width, p.Height)), Is.EqualTo(pictures), name);
    }
  }
}
