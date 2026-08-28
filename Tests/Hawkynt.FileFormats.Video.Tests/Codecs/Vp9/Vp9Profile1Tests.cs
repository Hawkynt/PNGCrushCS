using System;
using FileFormat.Core;

namespace FileFormat.Codecs.Vp9.Tests;

/// <summary>
/// Profile-1 reconstruction checked against frames produced by libvpx-vp9 and independently decoded
/// by ffmpeg. The source planes use simple deterministic ramps and were encoded losslessly, so the
/// expected answer is the source samples themselves rather than another implementation's rounding.
/// </summary>
[TestFixture]
public sealed class Vp9Profile1Tests {

  private const string _YUV422 =
    "okmDQggADgAOwAcEg4MAAAAEAABylgT///+YBsgMad//+ml/+bX//w39/d6/+bX/03hs3frq/////v+urJVVhv///fyP//xaP///gY9cbkx//++w////+HCOvuU4V///51L/5V////Tv+9rvWTt0HsLt+rD+w6L6Q8AA";

  private const string _YUV440 =
    "okmDQgQADgAOwAcEg4MAAAAEAABo24X///yodKe1Uv//ppf/m1//8N/f3ev/m1/9N4bN366v////8QbtqW6IX///38j//8Wj///zQLwzaL//4tH////wtEz54JDP///Opf/Kv//+ZD45LrLPh7fgAf+xF/Ox//4v9JaAAA==";

  private const string _YUV444 =
    "okmDQgAADgAOwAcEg4MAAAAEAABo2WP///+VDpT2ql//9NL/82v//hv7+71/82v/pvDZu/XV/////iDdtS3RC///+/kf//i0f//+aBeGbRf//Fo///+Bj1xuTH//74F/////C0TPngkM///86l/8q///5kPjkuss+Ht+AB/7EX87H//i/0lr////+nf97XesnboPYXb9WH9h0X0h7///3IoE9d+pG5f//ywWP9AA";

  private const string _GBR444 =
    "okmDQuAAcAB2ADgkHBgAAAAgAABpHSP///+hZZsxTH8uf/yV//6ptsybn/+Svcx1uxv///8KGhSX//6S/U//2GVl96n///4fx/SY//mR/////X69dghn/3K/+lv/+ul+nr/9Lf/6pNCTH+sAAA==";

  private static readonly MediaStreamInfo _Stream = new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    CodecId = "V_VP9",
  };

  [TestCase(_YUV422, PixelFormat.Yuv422P8, 2, 1)]
  [TestCase(_YUV440, PixelFormat.Yuv440P8, 1, 2)]
  [TestCase(_YUV444, PixelFormat.Yuv444P8, 1, 1)]
  [Category("Unit")]
  public void LosslessProfile1Yuv_ReconstructsEveryNativePlane(
    string encoded, PixelFormat format, int chromaDivisorX, int chromaDivisorY) {
    var image = _Decode(encoded);
    var expected = _ExpectedYuv(chromaDivisorX, chromaDivisorY);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(8));
      Assert.That(image.Height, Is.EqualTo(8));
      Assert.That(image.Format, Is.EqualTo(format));
      Assert.That(image.PixelData, Is.EqualTo(expected));
      Assert.That(image.GetPlaneDimensions(1), Is.EqualTo((8 / chromaDivisorX, 8 / chromaDivisorY)));
    });
  }

  [Test]
  [Category("Unit")]
  public void LosslessProfile1Srgb_ReconstructsPlanarGbrAsRgbWithoutAColourMatrix() {
    var image = _Decode(_GBR444);
    var expected = new byte[8 * 8 * 3];

    for (var y = 0; y < 8; ++y)
    for (var x = 0; x < 8; ++x) {
      var at = (y * 8 + x) * 3;
      expected[at] = (byte)(200 - x * 5 - y * 4);       // R plane in the encoded GBR picture
      expected[at + 1] = (byte)(20 + x * 9 + y * 3);   // G
      expected[at + 2] = (byte)(100 + x * 2 + y * 7);  // B
    }

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData, Is.EqualTo(expected));
      Assert.That(image.ColorInfo!.Range, Is.EqualTo(RawColorRange.Full));
      Assert.That(image.ColorInfo.Transfer, Is.EqualTo(RawTransferCharacteristic.Srgb));
      Assert.That(image.ColorInfo.Matrix, Is.EqualTo(RawMatrixCoefficients.Identity));
    });
  }

  private static RawImage _Decode(string base64) {
    var decoder = Vp9VideoDecoder.Create(_Stream);
    var data = Convert.FromBase64String(base64);

    Assert.That(decoder.TryDecode(new(0, data), out var image), Is.True);
    Assert.That(decoder.Flush(), Is.Empty);
    return image;
  }

  private static byte[] _ExpectedYuv(int chromaDivisorX, int chromaDivisorY) {
    const int width = 8;
    const int height = 8;
    var chromaWidth = width / chromaDivisorX;
    var chromaHeight = height / chromaDivisorY;
    var yLength = width * height;
    var chromaLength = chromaWidth * chromaHeight;
    var result = new byte[yLength + 2 * chromaLength];

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x)
      result[y * width + x] = (byte)(16 + (x * 7 + y * 11) % 200);

    for (var y = 0; y < chromaHeight; ++y)
    for (var x = 0; x < chromaWidth; ++x) {
      result[yLength + y * chromaWidth + x] = (byte)((40 + x * 13 + y * 17) & 255);
      result[yLength + chromaLength + y * chromaWidth + x] = (byte)((170 + x * 5 + y * 19) & 255);
    }

    return result;
  }
}
