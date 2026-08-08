using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Gif;
using FileFormat.Xar;

namespace FileFormat.Xar.Tests;

[TestFixture]
public sealed class XarTests {

  /// <summary>A picture to stand in for the preview, in a format the reader has to recognise.</summary>
  private static byte[] _Gif(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      pixels[i * 3] = (byte)(i * 7);

    var image = new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
    return GifWriter.ToBytes(GifFile.FromRawImage(image));
  }

  /// <summary>The file header record the format says has to come first.</summary>
  private static byte[] _FileHeaderBody() {
    var body = new List<byte>();
    body.AddRange("CXN"u8.ToArray());
    body.AddRange(BitConverter.GetBytes(0));
    body.AddRange(BitConverter.GetBytes(0));
    body.AddRange(BitConverter.GetBytes(0));
    body.AddRange(Encoding.ASCII.GetBytes("Xara X\0"));
    body.AddRange(Encoding.ASCII.GetBytes("1.0\0"));
    body.AddRange(Encoding.ASCII.GetBytes("build\0"));
    return body.ToArray();
  }

  private static byte[] _Drawing(params (uint Tag, byte[] Body)[] records) {
    var bytes = new List<byte>();
    bytes.AddRange(XarFile.Magic.ToArray());

    foreach (var (tag, body) in records) {
      bytes.AddRange(BitConverter.GetBytes(tag));
      bytes.AddRange(BitConverter.GetBytes((uint)body.Length));
      bytes.AddRange(body);
    }

    return bytes.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => XarReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => XarReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_FirstRecordIsNotTheFileHeader_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => XarReader.FromBytes(_Drawing((XarFile.TagPreviewGif, _Gif(4, 4)))));

  [Test]
  [Category("Unit")]
  public void FromBytes_ARecordLongerThanTheFile_ThrowsInvalidDataException() {
    var data = _Drawing(((uint)XarFile.TagFileHeader, _FileHeaderBody()));
    // Overstate the header record's length by a mile.
    BitConverter.GetBytes(1u << 20).CopyTo(data, XarFile.Magic.Length + 4);

    Assert.Throws<InvalidDataException>(() => XarReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NoPreviewRecord_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => XarReader.FromBytes(_Drawing(((uint)XarFile.TagFileHeader, _FileHeaderBody()))));

  [Test]
  [Category("Unit")]
  public void FromBytes_APreviewBehindTheCompressedRecords_IsRefused() {
    var data = _Drawing(
      ((uint)XarFile.TagFileHeader, _FileHeaderBody()),
      ((uint)XarFile.TagStartCompression, []),
      ((uint)XarFile.TagPreviewGif, _Gif(4, 4)));

    Assert.Throws<InvalidDataException>(() => XarReader.FromBytes(data), "past that point the lengths are not lengths");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsThePreviewAtTheSizeItStates() {
    var data = _Drawing(
      ((uint)XarFile.TagFileHeader, _FileHeaderBody()),
      ((uint)XarFile.TagPreviewGif, _Gif(13, 7)));

    var file = XarReader.FromBytes(data);
    var image = XarFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.PreviewTag, Is.EqualTo(XarFile.TagPreviewGif));
      Assert.That(file.Producer, Is.EqualTo("Xara X"));
      Assert.That((image.Width, image.Height), Is.EqualTo((13, 7)));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_SkipsPastRecordsItDoesNotCareAbout() {
    var data = _Drawing(
      ((uint)XarFile.TagFileHeader, _FileHeaderBody()),
      (40u, new byte[37]),
      (1u, []),
      ((uint)XarFile.TagPreviewGif, _Gif(9, 5)));

    var image = XarFile.ToRawImage(XarReader.FromBytes(data));

    Assert.That((image.Width, image.Height), Is.EqualTo((9, 5)), "the walk stepped over three records by their stated lengths");
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignature_AcceptsTheMagicAndRefusesAnythingElse() {
    Assert.Multiple(() => {
      Assert.That(_Matches(XarFile.Magic.ToArray()), Is.True);
      Assert.That(_Matches("XARB\xa3\xa3\r\n"u8.ToArray()), Is.False);
      Assert.That(_Matches(new byte[4]), Is.Null, "too short to have an opinion");
    });
  }

  private static bool? _Matches(byte[] header) => _Signature<XarFile>(header);

  /// <summary>Asks a format its own opinion of a header, which only a type parameter can.</summary>
  private static bool? _Signature<T>(byte[] header) where T : IImageFormatMetadata<T> => T.MatchesSignature(header);
}
