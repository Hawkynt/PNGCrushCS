using System;
using FileFormat.Pcd;

namespace FileFormat.Pcd.Tests;

/// <summary>
/// Photo CD round trips at the one resolution it has.
/// </summary>
/// <remarks>
/// These used to write 1x1 and 64x48 images through a writer that invented its own layout, which no
/// other Photo CD reader would have opened. The format holds fixed resolutions only, so anything
/// other than a Base image is refused, and the round trip is checked on greys — chroma is stored at
/// half resolution on both axes, so colour cannot survive it exactly.
/// </remarks>
[TestFixture]
public sealed class RoundTripTests {

  private const int _Width = 768;
  private const int _Height = 512;

  [Test]
  [Category("Integration")]
  public void RoundTrip_BaseImage_KeepsItsDimensions() {
    var original = new PcdFile {
      Width = _Width,
      Height = _Height,
      PixelData = _Grey(96),
    };

    var restored = PcdReader.FromBytes(PcdWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.Width, Is.EqualTo(_Width));
      Assert.That(restored.Height, Is.EqualTo(_Height));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Grey_SurvivesExactly() {
    var original = new PcdFile { Width = _Width, Height = _Height, PixelData = _Grey(96) };

    var restored = PcdReader.FromBytes(PcdWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.PixelData[0], Is.EqualTo(96));
      Assert.That(restored.PixelData[1], Is.EqualTo(96));
      Assert.That(restored.PixelData[2], Is.EqualTo(96));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AnyOtherSize_IsRefused() {
    var odd = new PcdFile { Width = 64, Height = 48, PixelData = new byte[64 * 48 * 3] };

    Assert.Throws<NotSupportedException>(() => PcdWriter.ToBytes(odd));
  }

  private static byte[] _Grey(byte level) {
    var pixels = new byte[_Width * _Height * 3];
    Array.Fill(pixels, level);
    return pixels;
  }
}
