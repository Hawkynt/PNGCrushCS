using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.NeoBookCartoon;
using FileFormat.Png;
using Hawkynt.FileFormats.Images;

namespace FileFormat.NeoBookCartoon.Tests;

/// <summary>
/// The fixtures are the two letters, the offset word, and a PNG this library writes standing where
/// the word says. Those are the three things XnView's own reader was shown to require.
/// </summary>
[TestFixture]
public sealed class NeoBookCartoonTests {

  private static byte[] _Png(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 11 % 251);

    return PngWriter.ToBytes(PngFile.FromRawImage(new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    }));
  }

  private static RawImage _Image(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17 % 251);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

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
  public void Writer_UsesTheRealFileOffsetAndLeavesUnknownGapZero() {
    var bytes = FormatIO.Encode<NeoBookCartoonFile>(_Image(3, 2));

    Assert.Multiple(() => {
      Assert.That(bytes[..12], Is.EqualTo(new byte[] {
        (byte)'S', (byte)'N', 0x0C, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
      }));
      Assert.That(bytes.AsSpan(12, 8).ToArray(), Is.EqualTo(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A }));
    });
  }

  [Test]
  [Category("Integration")]
  public void RawImage_WriteReadRoundTrip_IsPixelExact() {
    var source = _Image(7, 5);

    var bytes = FormatIO.Encode<NeoBookCartoonFile>(source);
    var restored = NeoBookCartoonFile.ToRawImage(NeoBookCartoonReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(source.Width));
      Assert.That(restored.Height, Is.EqualTo(source.Height));
      Assert.That(restored.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(restored.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_PreservesCallerSelectedVerifiedOffset() {
    var png = _Png(2, 2);

    var bytes = NeoBookCartoonWriter.ToBytes(new() { PictureOffset = 300, Picture = png });
    var restored = NeoBookCartoonReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(restored.PictureOffset, Is.EqualTo(300));
      Assert.That(restored.Picture, Is.EqualTo(png));
      Assert.That(bytes.Skip(6).Take(294), Is.All.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_RejectsPictureOffsetBeforeHeader()
    => Assert.Throws<ArgumentOutOfRangeException>(() => NeoBookCartoonWriter.ToBytes(new() { PictureOffset = 5, Picture = _Png(1, 1) }));

  [Test]
  [Category("Unit")]
  public void Writer_RejectsTrailingBytesAfterPng() {
    var png = _Png(1, 1);
    Assert.Throws<ArgumentException>(() => NeoBookCartoonWriter.ToBytes(new() { PictureOffset = 12, Picture = [.. png, 0x42] }));
  }

  [Test]
  [Category("Integration")]
  public void Registry_ExposesNeoBookAsWritable() {
    var entry = FormatRegistry.AllFormats.Single(e => e.Name.Contains("NeoBook", StringComparison.OrdinalIgnoreCase));

    Assert.Multiple(() => {
      Assert.That(entry.PrimaryExtension, Is.EqualTo(".car"));
      Assert.That(entry.SupportsRead, Is.True);
      Assert.That(entry.SupportsWrite, Is.True);
    });
  }
}
