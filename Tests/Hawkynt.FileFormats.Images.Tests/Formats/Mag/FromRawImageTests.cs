using System;
using FileFormat.Core;

namespace FileFormat.Mag.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private const int _WIDTH = 24;

  /// <summary>Not a multiple of anything the compression works in, so the last rows are awkward.</summary>
  private const int _HEIGHT = 11;

  /// <summary>
  /// Sixteen colours whose channels are already on the sixteen levels a stored nibble can name, and
  /// laid out so that every copy the coding offers gets used somewhere.
  /// </summary>
  private static RawImage _Bands(int width, int height) {
    var data = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      // Runs of four along a row and repeats down it, so copies from the left and from above both
      // find matches, and a scattered column keeps some units literal.
      var index = y < 2 ? (x * 5 + y * 3) % 16 : (x >> 2) % 16;
      var at = (y * width + x) * 3;
      data[at] = (byte)(index * 17);
      data[at + 1] = (byte)((15 - index) * 17);
      data[at + 2] = (byte)(index % 4 * 85);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SixteenColours_ReproducesExactly() {
    var source = _Bands(_WIDTH, _HEIGHT);
    var file = MagFile.FromRawImage(source);
    var decoded = MagFile.ToRawImage(MagReader.FromBytes(MagWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_SamplesAWidthThatIsNotAWholeNumberOfGroups() {
    // A row is a whole number of four-byte groups, which at four bits a pixel is eight pixels.
    var file = MagFile.FromRawImage(_Bands(37, 11));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(40));
      Assert.That(file.Height, Is.EqualTo(11));
    });
  }

  [Test]
  [Category("Integration")]
  public void ToBytes_CodesARepeatedRowWithoutSpendingAFlagByte() {
    // The row of flags persists, so a row the codes can reach the same way as the one before it
    // costs only its bits — which is what the flag stream being one bit per four bytes buys.
    var flat = new byte[_WIDTH * 32 * 3];
    for (var i = 0; i < flat.Length; i += 3)
      flat[i] = flat[i + 1] = flat[i + 2] = 0x88;

    var file = MagFile.FromRawImage(new() {
      Width = _WIDTH, Height = 32, Format = PixelFormat.Rgb24, PixelData = flat,
    });

    var bytes = MagWriter.ToBytes(file);
    var decoded = MagFile.ToRawImage(MagReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.LessThan(_WIDTH * 32 / 2));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(flat));
    });
  }
}
