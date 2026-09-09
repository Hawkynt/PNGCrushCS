using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.NeoBookCartoon;
using FileFormat.Png;

namespace FileFormat.NeoBookCartoon.Tests;

/// <summary>
/// The fixtures are the two letters, the offset word, and a PNG this library writes standing where
/// the word says. Those are the three things XnView's own reader was shown to require.
/// </summary>
[TestFixture]
public sealed class NeoBookCartoonTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11 % 251);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static byte[] _Png(int width, int height)
    => PngWriter.ToBytes(PngFile.FromRawImage(_Picture(width, height)));

  private static byte[] _Build(byte[] payload, int offset = 12, int stated = -1) {
    var at = stated < 0 ? offset : stated;
    var output = new byte[offset + payload.Length];
    output[0] = (byte)'S';
    output[1] = (byte)'N';
    output[2] = (byte)at;
    output[3] = (byte)(at >> 8);
    output[4] = (byte)(at >> 16);
    output[5] = (byte)(at >> 24);
    payload.CopyTo(output, offset);
    return output;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => NeoBookCartoonReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutTheTwoLettersIsRefused() {
    var data = _Build(_Png(4, 4));
    data[0] = (byte)'X';
    Assert.Throws<InvalidDataException>(() => NeoBookCartoonReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsThePictureTheOffsetPointsAt() {
    var file = NeoBookCartoonReader.FromBytes(_Build(_Png(11, 7)));
    var image = NeoBookCartoonFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.PictureOffset, Is.EqualTo(12));
      Assert.That(image.Width, Is.EqualTo(11));
      Assert.That(image.Height, Is.EqualTo(7));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheOffsetIsWhereThePictureIsAndNowhereElse()
    => Assert.Throws<InvalidDataException>(() => NeoBookCartoonReader.FromBytes(_Build(_Png(4, 4), offset: 20, stated: 12)));

  [Test]
  [Category("Unit")]
  public void FromBytes_APayloadThatIsNotAPngIsRefused()
    => Assert.Throws<InvalidDataException>(() => NeoBookCartoonReader.FromBytes(_Build([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0])));

  [Test]
  [Category("Unit")]
  public void FromBytes_APngThatDoesNotReachItsEndIsRefused()
    => Assert.Throws<InvalidDataException>(() => NeoBookCartoonReader.FromBytes(_Build(_Png(4, 4)[..^20])));

  [Test]
  [Category("Unit")]
  public void ToBytes_DefaultModelUsesTheObservedTwelveByteLayout() {
    var picture = _Png(5, 3);

    var data = NeoBookCartoonWriter.ToBytes(new() { Picture = picture });

    Assert.Multiple(() => {
      Assert.That(data.AsSpan(0, 2).ToArray(), Is.EqualTo(NeoBookCartoonFile.Magic.ToArray()));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2, 4)), Is.EqualTo(12u));
      Assert.That(data.AsSpan(6, 6).ToArray(), Is.EqualTo(new byte[6]));
      Assert.That(data.AsSpan(12).ToArray(), Is.EqualTo(picture));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ExplicitValidOffsetIsPreserved() {
    var picture = _Png(4, 4);

    var data = NeoBookCartoonWriter.ToBytes(new() { PictureOffset = 20, Picture = picture });

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2, 4)), Is.EqualTo(20u));
      Assert.That(data.AsSpan(20).ToArray(), Is.EqualTo(picture));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_OffsetInsideTheHeaderIsRefused()
    => Assert.Throws<InvalidDataException>(() => NeoBookCartoonWriter.ToBytes(new() { PictureOffset = 5, Picture = _Png(1, 1) }));

  [Test]
  [Category("Unit")]
  public void ToBytes_MissingPictureIsRefused()
    => Assert.Throws<InvalidDataException>(() => NeoBookCartoonWriter.ToBytes(default));

  [Test]
  [Category("Integration")]
  public void RoundTrip_FromRawImagePreservesEveryPixel() {
    var source = _Picture(13, 9);

    var restored = NeoBookCartoonReader.FromBytes(NeoBookCartoonWriter.ToBytes(NeoBookCartoonFile.FromRawImage(source)));
    var decoded = NeoBookCartoonFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(restored.PictureOffset, Is.EqualTo(NeoBookCartoonFile.DefaultPictureOffset));
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }
}
