using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.AutoFx;
using FileFormat.Core;

namespace FileFormat.AutoFx.Tests;

[TestFixture]
public sealed class AutoFxTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i)
      pixels[i * 3] = pixels[i * 3 + 1] = pixels[i * 3 + 2] = (byte)(i * 5);

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AutoFxReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AutoFxReader.FromBytes(new byte[512]));

  /// <summary>A PNG opens with the same eight bytes but for the name, and is not one of these.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_APngIsNotOneOfThese() {
    var data = new byte[512];
    ReadOnlySpan<byte> png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    png.CopyTo(data);

    Assert.Throws<InvalidDataException>(() => AutoFxReader.FromBytes(data));
  }

  /// <summary>The offset and the length together are the length of the file, in every sample there is.
  /// A file failing that is not one of these however it opens.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_TheOffsetAndLengthMustAccountForTheFile() {
    var data = AutoFxWriter.ToBytes(AutoFxFile.FromRawImage(_Picture(16, 8)));
    Array.Resize(ref data, data.Length + 1);

    Assert.Throws<InvalidDataException>(() => AutoFxReader.FromBytes(data));
  }

  /// <summary>Where the header points, a JPEG must begin — otherwise the file is refused rather than
  /// searched for something that looks like one.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_NoJpegWhereTheHeaderPoints_ThrowsInvalidDataException() {
    var data = AutoFxWriter.ToBytes(AutoFxFile.FromRawImage(_Picture(16, 8)));
    data[AutoFxFile.DefaultPictureOffset] = 0x00;

    Assert.Throws<InvalidDataException>(() => AutoFxReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_TheHeaderAccountsForTheWholeFile() {
    var bytes = AutoFxWriter.ToBytes(AutoFxFile.FromRawImage(_Picture(16, 8)));
    var offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(AutoFxFile.PictureOffsetAt));
    var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(AutoFxFile.PictureLengthAt));

    Assert.That(offset + length, Is.EqualTo((uint)bytes.Length));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ThePictureComesBackAtItsSize() {
    var decoded = AutoFxFile.ToRawImage(
      AutoFxReader.FromBytes(AutoFxWriter.ToBytes(AutoFxFile.FromRawImage(_Picture(32, 16)))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(32));
      Assert.That(decoded.Height, Is.EqualTo(16));
    });
  }
}
