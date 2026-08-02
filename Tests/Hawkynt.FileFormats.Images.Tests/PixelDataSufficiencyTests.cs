using System;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// The check that a decoded picture carries enough samples to fill the size it claims.
/// </summary>
/// <remarks>
/// Decoders that give up part way were returning pictures whose dimensions and contents disagreed,
/// and counting as a success: one JPEG XL file came back stating 1024 by 1024 with sixty-seven bytes
/// behind it, and a BPG and a PICT came back stating their full size with nothing behind them at
/// all. Anything reading such a picture by its stated size runs off the end of the buffer or draws
/// whatever follows, so the registry now turns these into the refusal they always were.
/// <para/>
/// The bound is the loosest one there is — the samples packed end to end — because some formats pad
/// each row to a whole byte and some do not, and a stricter bound would call a four-bit PNG of an
/// odd width short.
/// </remarks>
[TestFixture]
public sealed class PixelDataSufficiencyTests {

  private static RawImage _Image(int width, int height, PixelFormat format, int bytes) => new() {
    Width = width,
    Height = height,
    Format = format,
    PixelData = new byte[bytes],
  };

  [Test]
  [Category("Unit")]
  public void APictureWithEveryPixelIsAccepted()
    => Assert.That(_Image(16, 16, PixelFormat.Rgb24, 16 * 16 * 3).HasEnoughPixelData, Is.True);

  [Test]
  [Category("Unit")]
  public void APictureWithNothingBehindItIsNot()
    => Assert.That(_Image(494, 371, PixelFormat.Rgb24, 0).HasEnoughPixelData, Is.False);

  [Test]
  [Category("Unit")]
  public void APictureWithAHandfulOfBytesBehindItIsNot()
    => Assert.That(_Image(1024, 1024, PixelFormat.Rgb24, 67).HasEnoughPixelData, Is.False);

  [Test]
  [Category("Unit")]
  public void APictureOneRowShortIsNot()
    => Assert.That(_Image(256, 365, PixelFormat.Rgb24, 29261).HasEnoughPixelData, Is.False);

  [Test]
  [Category("Unit")]
  public void RowsPackedWithoutPaddingAreNotCalledShort() {
    // Five pixels of four bits is two and a half bytes; three such rows pack into eight, not nine.
    var image = _Image(5, 3, PixelFormat.Indexed4, 8);

    Assert.That(image.HasEnoughPixelData, Is.True, "a tightly packed picture is not a short one");
  }

  [Test]
  [Category("Unit")]
  public void ASizeOfNothingIsNotAPicture() {
    Assert.Multiple(() => {
      Assert.That(_Image(0, 10, PixelFormat.Rgb24, 0).HasEnoughPixelData, Is.False);
      Assert.That(_Image(10, 0, PixelFormat.Rgb24, 0).HasEnoughPixelData, Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void MoreThanEnoughIsStillEnough()
    => Assert.That(_Image(8, 8, PixelFormat.Gray8, 8 * 8 + 100).HasEnoughPixelData, Is.True);
}
