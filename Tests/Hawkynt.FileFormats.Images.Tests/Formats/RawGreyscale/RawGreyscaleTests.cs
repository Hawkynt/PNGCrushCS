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
}
