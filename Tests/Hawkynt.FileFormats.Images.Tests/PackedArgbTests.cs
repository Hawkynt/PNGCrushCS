using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>The one shape a picture has to take to reach a screen.</summary>
[TestFixture]
public sealed class PackedArgbTests {

  /// <summary>
  /// Each pixel becomes one integer, alpha in the top byte and blue in the bottom.
  /// </summary>
  /// <remarks>
  /// Getting this backwards is the classic way a picture arrives on screen with red and blue
  /// swapped, and it is invisible in any test that only checks sizes.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void EachChannel_LandsInItsOwnByte() {
    var source = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [
        0x10, 0x20, 0x30, 0x40,   // B=0x10, G=0x20, R=0x30, A=0x40
        0xFF, 0x00, 0x00, 0xFF,   // pure blue, opaque
      ],
    };

    var packed = source.ToPackedArgb();

    Assert.Multiple(() => {
      Assert.That(packed, Has.Length.EqualTo(2));
      Assert.That(packed[0], Is.EqualTo(unchecked((int)0x40302010)));
      Assert.That(packed[1], Is.EqualTo(unchecked((int)0xFF0000FF)));
    });
  }

  /// <summary>A picture in another layout has to be converted, not reinterpreted.</summary>
  [Test]
  [Category("Unit")]
  public void Rgb24Source_ComesBackOpaqueAndInOrder() {
    var source = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC],
    };

    var packed = source.ToPackedArgb();

    Assert.That(packed[0], Is.EqualTo(unchecked((int)0xFFAABBCC)));
  }
}
