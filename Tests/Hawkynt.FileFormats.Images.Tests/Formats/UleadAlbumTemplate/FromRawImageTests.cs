using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.UleadAlbumTemplate.Tests;

[TestFixture]
public sealed class FromRawImageTests {

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

  private static UleadAlbumTemplateFile _RoundTrip(RawImage image)
    => UleadAlbumTemplateReader.FromBytes(UleadAlbumTemplateWriter.ToBytes(UleadAlbumTemplateFile.FromRawImage(image)));

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ramp_ComesBackAtItsSizeAndVeryNearlyItsColours() {
    var source = _Ramp(37, 11);
    var decoded = UleadAlbumTemplateFile.ToRawImage(_RoundTrip(source));
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
    var wide = UleadAlbumTemplateFile.ToRawImage(_RoundTrip(_Ramp(200, 3)));
    var tall = UleadAlbumTemplateFile.ToRawImage(_RoundTrip(_Ramp(3, 200)));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = new byte[37 * 11] };

    Assert.That(UleadAlbumTemplateFile.ToRawImage(_RoundTrip(grey)).Width, Is.EqualTo(37));
  }

  /// <summary>
  /// The header states where the directory begins and how long it is, and those two have to add up
  /// to the length of the file to the byte — which is the reader's evidence that the header is being
  /// read as the format means it rather than somewhere holding two plausible numbers.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_TheDirectoryRunsToTheEndOfTheFile() {
    var bytes = UleadAlbumTemplateWriter.ToBytes(UleadAlbumTemplateFile.FromRawImage(_Ramp(37, 11)));
    var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(UleadAlbumTemplateFile.DirectoryOffsetAt));
    var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(UleadAlbumTemplateFile.DirectoryLengthAt));
    var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(UleadAlbumTemplateFile.EntryCountAt));

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).SequenceEqual(UleadAlbumTemplateFile.Magic), Is.True);
      Assert.That(offset + length, Is.EqualTo(bytes.Length));
      Assert.That(count, Is.EqualTo(1));
      Assert.That(count * UleadAlbumTemplateFile.DirectoryEntrySize, Is.LessThanOrEqualTo(length));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_KeepsTheNameTheDirectoryHolds()
    => Assert.That(_RoundTrip(_Ramp(37, 11)).Templates[0].Name, Is.EqualTo("P1"));
}
