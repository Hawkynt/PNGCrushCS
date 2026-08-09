using System.IO;
using FileFormat.Core;

namespace FileFormat.RawGreyscale.Tests;

/// <summary>
/// A raw greyscale dump: one byte a pixel, from the top-left corner, and nothing else in the file.
/// </summary>
/// <remarks>
/// The layout is not taken from a description. A 320 by 240 picture whose grey runs with seven times
/// x plus three times y — asymmetric both ways, so a mirror or a transpose could not pass — was
/// handed to XnView's own converter, and what came back was exactly 76,800 bytes equal to those
/// pixels in that order. The fixture below is that same picture, computed rather than stored.
/// <para/>
/// The size is the part the file cannot state and the converter will not guess: it asks the operator.
/// Here the length has to be exactly one of the sizes the layout is made in, and a length that is
/// none of them is refused.
/// </remarks>
[TestFixture]
public sealed class RawGreyscaleTests {

  private static byte[] _Ramp(int width, int height) {
    var pixels = new byte[width * height];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      pixels[y * width + x] = (byte)(x * 7 + y * 3);

    return pixels;
  }

  [Test]
  [Category("Integration")]
  public void Read_ReturnsThePixelsTheConverterWrote() {
    var pixels = _Ramp(320, 240);
    var image = RawGreyscaleFile.ToRawImage(RawGreyscaleReader.FromBytes(pixels));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(320));
      Assert.That(image.Height, Is.EqualTo(240));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(image.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_StartsAtTheTopLeftCornerAndRunsDown() {
    var pixels = _Ramp(64, 64);
    var image = RawGreyscaleFile.ToRawImage(RawGreyscaleReader.FromBytes(pixels));

    Assert.Multiple(() => {
      Assert.That(image.PixelData[0], Is.EqualTo(0), "the top-left pixel");
      Assert.That(image.PixelData[1], Is.EqualTo(7), "one to its right");
      Assert.That(image.PixelData[64], Is.EqualTo(3), "one below it");
    });
  }

  [TestCase(256, 256)]
  [TestCase(720, 576)]
  [TestCase(64, 64)]
  [Category("Unit")]
  public void Read_PlacesADumpByItsLength(int width, int height) {
    var file = RawGreyscaleReader.FromBytes(new byte[width * height]);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_SettlesTheOneLengthTwoSizesClaimTheWayXnViewOrdersThem() {
    // 720 by 512 and 640 by 576 are both 368,640 bytes.
    var file = RawGreyscaleReader.FromBytes(new byte[720 * 512]);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(720));
      Assert.That(file.Height, Is.EqualTo(512));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesALengthThatIsNoPicture()
    => Assert.Throws<InvalidDataException>(() => RawGreyscaleReader.FromBytes(new byte[1234]));

  [Test]
  [Category("Unit")]
  public void Read_RefusesALengthOneByteOffASizeItKnows()
    => Assert.Throws<InvalidDataException>(() => RawGreyscaleReader.FromBytes(new byte[256 * 256 + 1]));

  private static RawImage _Grey(int width, int height, byte[] pixels)
    => new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };

  /// <summary>
  /// The same picture the converter was given comes back out as the same 76,800 bytes.
  /// </summary>
  /// <remarks>
  /// Compared against the pixels rather than against a stored file, the converter's output for this
  /// picture having been exactly them: there is no header to differ in, so equality of the bytes is
  /// equality of the whole file.
  /// </remarks>
  [Test]
  [Category("Integration")]
  public void Write_ProducesTheSameBytesTheConverterWrote() {
    var pixels = _Ramp(320, 240);

    Assert.That(FormatIO.Encode<RawGreyscaleFile>(_Grey(320, 240, pixels)), Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void Write_RoundTripsAPictureAlreadyAtASizeTheTableHolds() {
    var pixels = _Ramp(64, 64);
    var back = FormatIO.Decode<RawGreyscaleFile>(FormatIO.Encode<RawGreyscaleFile>(_Grey(64, 64, pixels)));

    Assert.Multiple(() => {
      Assert.That((back.Width, back.Height), Is.EqualTo((64, 64)));
      Assert.That(back.PixelData, Is.EqualTo(pixels));
    });
  }

  /// <summary>
  /// A size the table does not hold is moved to the nearest one it does, rather than refused.
  /// </summary>
  /// <remarks>
  /// Writing the pixels at their own size would produce a length nothing can place — not this reader,
  /// which has only the length to go on, and not the converter, which asks the operator. A file that
  /// cannot be opened again is worse than one that has been resampled.
  /// </remarks>
  [TestCase(300, 220, 320, 240)]
  [TestCase(1920, 1080, 1920, 1080)]
  [TestCase(2, 2, 64, 64)]
  [Category("Unit")]
  public void Write_MovesAnUnknownSizeToTheNearestKnownOne(int width, int height, int expectedWidth, int expectedHeight) {
    var bytes = FormatIO.Encode<RawGreyscaleFile>(_Grey(width, height, new byte[width * height]));
    var back = FormatIO.Decode<RawGreyscaleFile>(bytes);

    Assert.Multiple(() => {
      Assert.That((back.Width, back.Height), Is.EqualTo((expectedWidth, expectedHeight)));
      Assert.That(bytes, Has.Length.EqualTo(expectedWidth * expectedHeight));
    });
  }

  /// <summary>Whatever this writes can be read back, there being no size in the file to help.</summary>
  [Test]
  [Category("Unit")]
  public void Write_AlwaysProducesALengthTheReaderPlaces() {
    Assert.Multiple(() => {
      foreach (var (width, height) in new[] { (1, 1), (17, 23), (640, 480), (4000, 3000) })
        Assert.DoesNotThrow(
          () => FormatIO.Decode<RawGreyscaleFile>(
            FormatIO.Encode<RawGreyscaleFile>(_Grey(width, height, new byte[width * height]))),
          $"{width}x{height}");
    });
  }
}
