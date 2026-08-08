using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Dwg;
using FileFormat.Png;

namespace FileFormat.Dwg.Tests;

[TestFixture]
public sealed class DwgTests {

  private static byte[] _Png(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11);

    var image = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
    return PngWriter.ToBytes(PngFile.FromRawImage(image));
  }

  /// <summary>
  /// A drawing that is nothing but its header and its thumbnail block, laid out as the format does.
  /// </summary>
  private static byte[] _Drawing(byte[] thumbnail, int type = DwgFile.TypePng, bool withTitle = true, string version = "AC1027") {
    const int seeker = 0x80;
    const int titleLength = 80;

    var entries = withTitle ? 2 : 1;
    var descriptors = DwgFile.ImageSentinel.Length + 4 + 1 + entries * DwgFile.ImageDescriptorSize;
    var titleAt = seeker + descriptors;
    var pictureAt = titleAt + (withTitle ? titleLength : 0);

    var bytes = new List<byte>();
    bytes.AddRange(Encoding.ASCII.GetBytes(version));
    while (bytes.Count < DwgFile.ImageSeekerOffset)
      bytes.Add(0);

    bytes.AddRange(BitConverter.GetBytes(seeker));
    while (bytes.Count < seeker)
      bytes.Add(0);

    bytes.AddRange(DwgFile.ImageSentinel.ToArray());

    // The stated length runs from just past itself to the closing sentinel.
    var blockLength = 1 + entries * DwgFile.ImageDescriptorSize + (withTitle ? titleLength : 0) + thumbnail.Length;
    bytes.AddRange(BitConverter.GetBytes(blockLength));
    bytes.Add((byte)entries);

    if (withTitle) {
      bytes.Add((byte)DwgFile.TypeHeaderData);
      bytes.AddRange(BitConverter.GetBytes(titleAt));
      bytes.AddRange(BitConverter.GetBytes(titleLength));
    }

    bytes.Add((byte)type);
    bytes.AddRange(BitConverter.GetBytes(pictureAt));
    bytes.AddRange(BitConverter.GetBytes(thumbnail.Length));

    if (withTitle)
      bytes.AddRange(new byte[titleLength]);

    bytes.AddRange(thumbnail);

    foreach (var b in DwgFile.ImageSentinel)
      bytes.Add((byte)~b);

    return bytes.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => DwgReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_NoVersionString_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => DwgReader.FromBytes(new byte[256]));

  [Test]
  [Category("Unit")]
  public void FromBytes_VersionThatIsNotAcAndFourDigits_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => DwgReader.FromBytes(_Drawing(_Png(4, 4), version: "ACx027")));

  [Test]
  [Category("Unit")]
  public void FromBytes_NoSentinelWhereTheSeekerPoints_ThrowsInvalidDataException() {
    var data = _Drawing(_Png(4, 4));
    data[0x80] ^= 0xFF;

    Assert.Throws<InvalidDataException>(() => DwgReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_BlockThatDoesNotCloseWithItsSentinel_ThrowsInvalidDataException() {
    var data = _Drawing(_Png(4, 4));
    data[^1] ^= 0xFF;

    Assert.Throws<InvalidDataException>(() => DwgReader.FromBytes(data), "the complement is what says the whole block was read");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ABlockHoldingNothingButItsTitle_ThrowsInvalidDataException() {
    var data = _Drawing([], DwgFile.TypeHeaderData, withTitle: false);

    Assert.Throws<InvalidDataException>(() => DwgReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheThumbnailAtTheOffsetAndLengthStated() {
    var file = DwgReader.FromBytes(_Drawing(_Png(23, 11)));
    var image = DwgFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Version, Is.EqualTo("AC1027"));
      Assert.That(file.ThumbnailType, Is.EqualTo(DwgFile.TypePng));
      Assert.That((image.Width, image.Height), Is.EqualTo((23, 11)));
    });
  }

  /// <summary>
  /// The title entry comes first in the block and is not a picture; a reader that took the first
  /// descriptor it saw would try to decode eighty bytes of nothing.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_StepsPastTheTitleToReachThePicture() {
    var withTitle = DwgFile.ToRawImage(DwgReader.FromBytes(_Drawing(_Png(7, 5))));
    var without = DwgFile.ToRawImage(DwgReader.FromBytes(_Drawing(_Png(7, 5), withTitle: false)));

    Assert.That((withTitle.Width, withTitle.Height), Is.EqualTo((without.Width, without.Height)));
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignature_TakesAcAndFourDigitsAndNothingElse() {
    Assert.Multiple(() => {
      Assert.That(_Matches("AC1027"u8.ToArray()), Is.True);
      Assert.That(_Matches("AC1015"u8.ToArray()), Is.True);
      Assert.That(_Matches("ACADXX"u8.ToArray()), Is.False);
      Assert.That(_Matches("ZZ1027"u8.ToArray()), Is.False);
      Assert.That(_Matches("AC10"u8.ToArray()), Is.Null);
    });
  }

  private static bool? _Matches(byte[] header) => _Signature<DwgFile>(header);

  /// <summary>Asks a format its own opinion of a header, which only a type parameter can.</summary>
  private static bool? _Signature<T>(byte[] header) where T : IImageFormatMetadata<T> => T.MatchesSignature(header);
}
