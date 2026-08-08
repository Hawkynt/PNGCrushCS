using System;
using FileFormat.Core;

namespace FileFormat.CasioQv.Tests;

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

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ramp_ComesBackAtItsSizeAndVeryNearlyItsColours() {
    var source = _Ramp(37, 11);
    var decoded = CasioQvFile.ToRawImage(CasioQvReader.FromBytes(CasioQvWriter.ToBytes(CasioQvFile.FromRawImage(source))));
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
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = CasioQvFile.FromRawImage(_Ramp(200, 3));
    var tall = CasioQvFile.FromRawImage(_Ramp(3, 200));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAGrey() {
    var grey = new RawImage { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = new byte[37 * 11] };
    var decoded = CasioQvFile.ToRawImage(CasioQvReader.FromBytes(CasioQvWriter.ToBytes(CasioQvFile.FromRawImage(grey))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
  }

  /// <summary>
  /// Nothing in the file states where an area begins: the offsets are the running sum of the lengths,
  /// and that sum landing on the end of the file is the whole of the evidence that the table has been
  /// read as it was written. A writer stating a length that is not the area's would produce a file
  /// whose every later offset is wrong.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_TheTableAndItsAreaAccountForTheFile() {
    var file = CasioQvFile.FromRawImage(_Ramp(37, 11));
    var bytes = CasioQvWriter.ToBytes(file);

    var areaCount = (bytes[4] << 8) | bytes[5];
    var descriptor = CasioQvFile.TableOffset;
    var area = (bytes[descriptor] << 8) | bytes[descriptor + 1];
    var length = (bytes[descriptor + 2] << 24) | (bytes[descriptor + 3] << 16)
                 | (bytes[descriptor + 4] << 8) | bytes[descriptor + 5];

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).SequenceEqual(CasioQvFile.Magic), Is.True);
      Assert.That(areaCount, Is.EqualTo(1));
      Assert.That(area, Is.EqualTo(CasioQvFile.AreaWholeJpeg), "the later cameras' whole-stream area");
      Assert.That(CasioQvFile.TableOffset + areaCount * CasioQvFile.DescriptorSize + length, Is.EqualTo(bytes.Length));
      Assert.That(file.WasReassembled, Is.False);
    });
  }
}
