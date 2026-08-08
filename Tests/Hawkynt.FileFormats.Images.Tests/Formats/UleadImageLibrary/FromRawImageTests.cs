using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.UleadImageLibrary.Tests;

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

  private static UleadImageLibraryFile _RoundTrip(RawImage image)
    => UleadImageLibraryReader.FromBytes(UleadImageLibraryWriter.ToBytes(UleadImageLibraryFile.FromRawImage(image)));

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ramp_ComesBackAtItsSizeAndVeryNearlyItsColours() {
    var source = _Ramp(37, 11);
    var decoded = UleadImageLibraryFile.ToRawImage(_RoundTrip(source));
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
    var wide = UleadImageLibraryFile.ToRawImage(_RoundTrip(_Ramp(200, 3)));
    var tall = UleadImageLibraryFile.ToRawImage(_RoundTrip(_Ramp(3, 200)));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = new byte[37 * 11] };

    Assert.That(UleadImageLibraryFile.ToRawImage(_RoundTrip(grey)).Width, Is.EqualTo(37));
  }

  /// <summary>
  /// There is no directory in one of these. The count stands at 0x100, the first record begins at
  /// <c>0x210 + 4n</c>, and the record's own lengths give where the next one starts — so the writer
  /// has to compute the same chain the reader walks rather than state a table nothing reads.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_TheCountAndTheFirstRecordStandWhereTheyAreLookedFor() {
    var bytes = UleadImageLibraryWriter.ToBytes(UleadImageLibraryFile.FromRawImage(_Ramp(37, 11)));
    var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x100));
    var first = 0x210 + 4 * count;

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).SequenceEqual(UleadImageLibraryFile.Magic), Is.True);
      Assert.That(count, Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(first)), Is.EqualTo(20), "every record states type twenty");
    });
  }
}
