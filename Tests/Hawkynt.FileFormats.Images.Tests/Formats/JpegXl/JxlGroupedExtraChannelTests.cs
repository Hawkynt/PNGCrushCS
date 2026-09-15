using FileFormat.JpegXl.Codec;

namespace FileFormat.JpegXl.Tests;

/// <summary>Regression coverage for VarDCT extra channels that span AC groups.</summary>
[TestFixture]
public sealed class JxlGroupedExtraChannelTests {

  private const int _Width = 300;
  private const int _Height = 17;

  /// <summary>
  /// The fixture was encoded from an original generated RGBA PAM with FFmpeg 7.1.5's
  /// libjxl 0.11.1 encoder using <c>-distance 1 -effort 7 -modular 0</c>.
  /// Its colour planes use VarDCT while alpha remains lossless modular data. At 300
  /// pixels wide the alpha plane cannot fit in the default 256-pixel group and is
  /// therefore continued in the per-AC-group modular streams.
  /// </summary>
  [Test]
  public void VarDctAlphaSpanningGroupsIsDecodedFromAcGroupStreams() {
    var bytes = TestHelper.Fixture("ffmpeg_libjxl_grouped_alpha_300x17.jxl");

    Assert.That(JpegXlReader.TryReadSpecImage(bytes, out var metadata, out var raw), Is.True);
    Assert.That(raw, Is.InstanceOf<JxlVarDctImage>());
    var image = (JxlVarDctImage)raw!;

    Assert.Multiple(() => {
      Assert.That(metadata.Width, Is.EqualTo(_Width));
      Assert.That(metadata.Height, Is.EqualTo(_Height));
      Assert.That(image.ExtraChannels, Has.Length.EqualTo(1));
    });

    var alpha = image.ExtraChannels[0];
    Assert.Multiple(() => {
      Assert.That(alpha.Width, Is.EqualTo(_Width));
      Assert.That(alpha.Height, Is.EqualTo(_Height));
    });

    var expected = new int[_Width * _Height];
    for (var y = 0; y < _Height; ++y)
    for (var x = 0; x < _Width; ++x)
      expected[y * _Width + x] = (x * 17 + y * 29 + (x ^ y) * 3) & 255;

    Assert.That(alpha.Pixels, Is.EqualTo(expected));
  }
}
