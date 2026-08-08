using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.SecondNatureSlideShow.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>A gentle ramp, since what comes back has been through a lossy coder.</summary>
  private static RawImage _Ramp(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        data[at] = (byte)(100 + x % 128);
        data[at + 1] = (byte)(110 + y % 128);
        data[at + 2] = 128;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static SecondNatureSlideShowFile _RoundTrip(RawImage image)
    => SecondNatureSlideShowReader.FromBytes(SecondNatureSlideShowWriter.ToBytes(SecondNatureSlideShowFile.FromRawImage(image)));

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ramp_ComesBackAtItsSizeAndVeryNearlyItsColours() {
    var source = _Ramp(37, 11);
    var decoded = SecondNatureSlideShowFile.ToRawImage(_RoundTrip(source));
    var rgb = PixelConverter.Convert(decoded, PixelFormat.Rgb24);

    long error = 0;
    for (var i = 0; i < source.PixelData.Length; ++i)
      error += Math.Abs(rgb.PixelData[i] - source.PixelData[i]);

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That((double)error / source.PixelData.Length, Is.LessThan(4.0));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = SecondNatureSlideShowFile.ToRawImage(_RoundTrip(_Ramp(200, 3)));
    var tall = SecondNatureSlideShowFile.ToRawImage(_RoundTrip(_Ramp(3, 200)));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = new byte[37 * 11] };

    Assert.That(SecondNatureSlideShowFile.ToRawImage(_RoundTrip(grey)).Width, Is.EqualTo(37));
  }

  /// <summary>
  /// The directory has no count of its own: how many slides there are is the space between it and
  /// the first of them, eight bytes to a slide, and every entry has to be the one before it plus that
  /// one's length with the last ending on the last byte of the file.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_TheDirectoryAccountsForTheFile() {
    var bytes = SecondNatureSlideShowWriter.ToBytes(SecondNatureSlideShowFile.FromRawImage(_Ramp(37, 11)));

    var first = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(SecondNatureSlideShowFile.DirectoryOffset));
    var span = first - SecondNatureSlideShowFile.DirectoryOffset;
    var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(SecondNatureSlideShowFile.DirectoryOffset + 4));

    Assert.Multiple(() => {
      Assert.That(span % SecondNatureSlideShowFile.DirectoryEntrySize, Is.Zero);
      Assert.That(span / SecondNatureSlideShowFile.DirectoryEntrySize, Is.EqualTo(1));
      Assert.That(first + length, Is.EqualTo(bytes.Length));
    });
  }

  /// <summary>
  /// The slide's record states its size at two places, and the reader refuses a slide where the two
  /// disagree or where either differs from the JPEG's own.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_StatesTheSizeTwiceAndAgrees() {
    var bytes = SecondNatureSlideShowWriter.ToBytes(SecondNatureSlideShowFile.FromRawImage(_Ramp(37, 11)));
    var first = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(SecondNatureSlideShowFile.DirectoryOffset));

    var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(first + SecondNatureSlideShowFile.SlideSizeOffset));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(first + SecondNatureSlideShowFile.SlideSizeOffset + 2));
    var againWidth = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(first + SecondNatureSlideShowFile.SlideSizeRepeatOffset));
    var againHeight = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(first + SecondNatureSlideShowFile.SlideSizeRepeatOffset + 2));

    Assert.Multiple(() => {
      Assert.That((width, height), Is.EqualTo(((ushort)37, (ushort)11)));
      Assert.That((againWidth, againHeight), Is.EqualTo((width, height)));
    });
  }
}
