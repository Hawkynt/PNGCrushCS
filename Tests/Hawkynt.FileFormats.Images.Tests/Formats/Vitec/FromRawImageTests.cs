using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Vitec.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 13);
      data[i * 3 + 2] = (byte)(i * 29);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Colour_ReproducesEveryPixel() {
    var source = _Gradient(37, 11);
    var decoded = VitecFile.ToRawImage(VitecReader.FromBytes(VitecWriter.ToBytes(VitecFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = VitecFile.FromRawImage(_Gradient(200, 3));
    var tall = VitecFile.FromRawImage(_Gradient(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grey_StaysOneSampleAPixel() {
    var pixels = new byte[37 * 11];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 5);

    var source = new RawImage { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = pixels };
    var file = VitecFile.FromRawImage(source);
    var decoded = VitecFile.ToRawImage(VitecReader.FromBytes(VitecWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(file.Samples, Is.EqualTo(1));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>
  /// The second header states the size of the data as itself plus the samples, and the two headers
  /// and the samples have to be the whole file. Both are what the reader accounts for the file by,
  /// so a writer stating either of them from anything but the picture's own numbers writes a file
  /// nothing accepts.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_StatesTheDataSizeTwiceAndAgrees() {
    var bytes = VitecWriter.ToBytes(VitecFile.FromRawImage(_Gradient(37, 11)));

    var firstHeader = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(VitecFile.FirstHeaderLengthOffset));
    var secondAt = 4 + firstHeader;
    var secondHeader = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(secondAt));
    var statedData = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(secondAt + 4));

    Assert.Multiple(() => {
      Assert.That(bytes.Length, Is.EqualTo(secondAt + secondHeader + 37 * 11 * 3));
      Assert.That(statedData, Is.EqualTo(secondHeader + 37 * 11 * 3));
      Assert.That(bytes.AsSpan(VitecFile.NameOffset, 5).SequenceEqual(VitecFile.Name), Is.True, "the name sits inside the first header");
    });
  }
}
