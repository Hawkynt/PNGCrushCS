using System;
using FileFormat.Core;

namespace FileFormat.Codecs.Vp9.Tests;

/// <summary>Profile-2/3 streams encoded losslessly by libvpx-vp9 and independently decoded by ffmpeg.</summary>
/// <remarks>
/// The bitstreams are external oracles rather than products of the decoder's own test writer. Their
/// source planes are deterministic formulas below; before embedding, every IVF was decoded by ffmpeg
/// to its original raw input byte-for-byte. Tests therefore catch decoder and test-writer mistakes
/// that could otherwise agree with one another.
/// </remarks>
[TestFixture]
public sealed class Vp9HighBitDepthTests {

  private const int Width = 16;
  private const int Height = 16;

  [TestCase(10, PixelFormat.Yuv420P10, Profile2_420_10)]
  [TestCase(12, PixelFormat.Yuv420P12, Profile2_420_12)]
  [Category("Unit")]
  public void Profile2PreservesNative420Samples(int bitDepth, PixelFormat format, string payload) {
    var picture = _Decode(payload);
    Assert.That(picture.Format, Is.EqualTo(format));
    _AssertYuv(picture, bitDepth, 1, 1);
  }

  [TestCase(10, 1, 0, PixelFormat.Yuv422P10, Profile3_422_10)]
  [TestCase(10, 0, 1, PixelFormat.Yuv440P10, Profile3_440_10)]
  [TestCase(10, 0, 0, PixelFormat.Yuv444P10, Profile3_444_10)]
  [TestCase(12, 0, 0, PixelFormat.Yuv444P12, Profile3_444_12)]
  [Category("Unit")]
  public void Profile3PreservesNativeYuvSamples(
    int bitDepth, int subX, int subY, PixelFormat format, string payload) {
    var picture = _Decode(payload);
    Assert.That(picture.Format, Is.EqualTo(format));
    _AssertYuv(picture, bitDepth, subX, subY);
  }

  [TestCase(10, Profile3_Gbr_10)]
  [TestCase(12, Profile3_Gbr_12)]
  [Category("Unit")]
  public void Profile3SrgbPreservesEveryHighBitDepthCodeValue(int bitDepth, string payload) {
    var picture = _Decode(payload);
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb48));
    Assert.That(picture.ColorInfo!.Range, Is.EqualTo(RawColorRange.Full));
    Assert.That(picture.ColorInfo.Matrix, Is.EqualTo(RawMatrixCoefficients.Identity));

    var max = (1 << bitDepth) - 1;
    for (var y = 0; y < Height; ++y)
    for (var x = 0; x < Width; ++x) {
      var at = (y * Width + x) * 6;
      var r = (picture.PixelData[at] << 8) | picture.PixelData[at + 1];
      var g = (picture.PixelData[at + 2] << 8) | picture.PixelData[at + 3];
      var b = (picture.PixelData[at + 4] << 8) | picture.PixelData[at + 5];

      Assert.That(r, Is.EqualTo(_Expand16(_R(x, y, max), max)), $"R at {x},{y}");
      Assert.That(g, Is.EqualTo(_Expand16(_Y(x, y, max), max)), $"G at {x},{y}");
      Assert.That(b, Is.EqualTo(_Expand16(_U(x, y, max), max)), $"B at {x},{y}");
    }
  }

  private static RawImage _Decode(string base64) {
    var decoder = Vp9VideoDecoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      CodecId = "V_VP9",
    });

    Assert.That(decoder.TryDecode(new(0, Convert.FromBase64String(base64)), out var picture), Is.True);
    return picture;
  }

  private static void _AssertYuv(RawImage picture, int bitDepth, int subX, int subY) {
    Assert.That(picture.Width, Is.EqualTo(Width));
    Assert.That(picture.Height, Is.EqualTo(Height));
    Assert.That(RawImage.YuvBitDepth(picture.Format), Is.EqualTo(bitDepth));

    var max = (1 << bitDepth) - 1;
    _AssertPlane(picture.GetPlaneData(0), Width, Height, bitDepth, (x, y) => _Y(x, y, max), "Y");

    var cw = (Width + (1 << subX) - 1) >> subX;
    var ch = (Height + (1 << subY) - 1) >> subY;
    _AssertPlane(picture.GetPlaneData(1), cw, ch, bitDepth, (x, y) => _U(x, y, max), "U");
    _AssertPlane(picture.GetPlaneData(2), cw, ch, bitDepth, (x, y) => _R(x, y, max), "V");
  }

  private static void _AssertPlane(
    ReadOnlySpan<byte> data, int width, int height, int bitDepth,
    Func<int, int, int> expected, string plane) {
    var bytes = bitDepth == 8 ? 1 : 2;
    Assert.That(data.Length, Is.EqualTo(width * height * bytes));

    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var sample = y * width + x;
      var actual = bytes == 1
        ? data[sample]
        : data[sample * 2] | (data[sample * 2 + 1] << 8);
      Assert.That(actual, Is.EqualTo(expected(x, y)), $"{plane} at {x},{y}");
    }
  }

  private static int _Y(int x, int y, int max) => (17 * x + 29 * y + 123) % (max + 1);
  private static int _U(int x, int y, int max) => (53 * x + 7 * y + 271) % (max + 1);
  private static int _R(int x, int y, int max) => (11 * x + 61 * y + 511) % (max + 1);
  private static int _Expand16(int sample, int max) => (int)((sample * 65535L + max / 2) / max);

  // Raw VP9 frame payloads extracted from one-frame IVF files encoded by libvpx-vp9 -lossless 1.
  private const string Profile2_420_10 = "kkmDQgAAeAB7ABwSDgwAAAAQAAB55fH///9pJr7CDud01///37P//vif//31v+TBEv//fE///jhwrlR//7Ln/////3WfZ/SYEN//9Jf///+8m/////qQX9BkiNxH////8iQP//rfwP//vrf8mCJf/++J///fW/5MES//97Ff///69Gm/IHjn////vJv///37qricv//10W6v//xw4Vyo//9mV///HDhXKj//2ZX///w+tLkx//PX///+8XfjgVKH////IYfQAA==";
  private const string Profile2_420_12 = "kkmDQoAAeAB7ABwSDgwAAAAQAAB55fH///+P/+Sa+un7nXNf//9+z//74n//95b/kv47//3xP//4xGR5Tf/+y5//////H/vZ9n9ELwz//0l////7vd/////j70s/rXMc+I/////j0B//9b+B//95b/kv47//3xP//7y3/Jfx3//vYr////To034w8c////93u///++qNuxY//+ui3V//+MRkeU3//syv//4xGR5Tf/+zK///+HupcmP/56////2i78UCpQ////47lFAA";
  private const string Profile3_422_10 = "sSTBoQIAB4AHsAHBIODAAAABAAAAfHgvv///9pSD7OPvcs1///37P//vif//31v+TBEv//fE///jhwrlR//7Ln/////3MW0fIHEO//9Jf///+8m////D60uTH/89f////qXX9i5jnxH////8iQP//rzP///3i78cCpQ////5Cniv//763/JgiX//vif//31v+TBEv//exX///+vRpvyB45////7xyP///fuquJy///W94Hjf//xw4Vyo//9mV///HDhXKj//2ZX///w+tLkx//PX///w+tLkx//PX////j01H9ubk7SmvNzn9K85+bvf860P30G/bdn9Ig/yQR/Fpv7v+f5It/3f8/yQR////eLvxwKlD////kLRuAA=";
  private const string Profile3_440_10 = "sSTBoQEAB4AHsAHBIODAAAABAAAAeeaZ////tJNfYQdzumv//+/Z//98T//++t/yYIl//74n//8cOFcqP//Zc/////+78+n6uLhn//pL////3k3////Xo035A8c////95N/////URX5Y07vCv////yJA//+vM///791VxOX//638D//763/JgiX//vif//31v+TBEv//exX///+vRpvyB45////7yb////y/GD+68g/enqP4aVH/w0t0/qaz/ZYNH/DSYv//+/dVcTl//+vM///791VxOX//66LXP//8cOFcqP//Zlf//xw4Vyo//9mV///8PrS5Mf/zI////7xd+OBUof///8hRxS///w+tLkx//PX//w+tLkx//PX///+nL/6Snsz+X7CV/p1af///+Xcv/vZuT11/n7bkH9t5If9YPf5KfmfyUzwA";
  private const string Profile3_444_10 = "sSTBoQAAB4AHsAHBIODAAAABAAAAeeYF////7STX2EHc7pr///v2f//fE///vrf8mCJf/++J///HDhXKj//2XP/////u/Pp+ri4Z//6S////95N////16NN+QPHP////eTf///h9aXJj/+ZH////+oivyxp3eFf////kSB//9eZ///37qricv//15n///7xd+OBUof///8hR0v//++t/yYIl//74n//99b/kwRL//3sV////r0ab8geOf///+8m////8vxg/uvIP3p6j+GlR/8NLdP6ms/2WDR/w0jA///791VxOX//68z///v3VXE5f//rzP///5fht/p+X3x2/T9LX/9P0baf9WcMuOVeX//9dI58MtF77pn9tlJ+yi1F++7J/tsn4/+/D4J7L2iv//+OHCuVH//syv//44cK5Uf/+zK///+H1pcmP/5t3///D60uTH/827////HpqP7c3J2lNebnP6V5z83e/51ofvoN+27P6RB/kgj+LTf3f8/yRb/u/5/kfb////eLvxwKlD////kKZlX////rpl/hmw/fjmfvxOQ/hmL6/78Vd++98SP4Zv8lw////Gqj5Y07vCv////yJA//+tuAA";
  private const string Profile3_444_12 = "sSTBoUAAB4AHsAHBIODAAAABAAAAeeYF////8f/8k19dP3Oua///79n//3xP//7y3/Jfx3//vif//xiMjym//9lz/////+P/f8+n6eLhn//pL////3e7////To034w8c////93u///+HupcmP/5kf////+PvV/+0kzgYd////+PQH//15n///fVG3Ysf//Xmf///tF34oFSh////x2LS///7y3/Jfx3//vif//3lv+S/jv//exX///+nRpvxh45////7vd////p0ab8YeOf///+7ur///31Rt2LH//15n///fVG3Ysf//W+DL///4xGR5Tf/+zK///jEZHlN//7Mr///4e6lyY//m3f//8PdS5Mf/zbv///2i78UCpQ////47l////tF34oFSh////x2qaAA";
  private const string Profile3_Gbr_10 = "sSTBoTgAPAA9gA4JBwYAAAAIAAB55gX////tJNfYQdzumv//+/Z//98T//++t/yYIl//74n//8cOFcqP//Zc/////+78+n6uLhn//pL////3k3////Xo035A8c////95N///+H1pcmP/5kf////6iK/LGnd4V////+RIH//15n///fuquJy///Xmf///vF344FSh////yFHS///763/JgiX//vif//31v+TBEv//exX///+vRpvyB45////7yb////y/GD+68g/enqP4aVH/w0t0/qaz/ZYNH/DSMD///v3VXE5f//rzP//+/dVcTl//+vM////l+G3+n5ffHb9P0tf/0/Rtp/1Zwy45V5f//10jnwy0Xvumf22Un7KLUX77sn+2yfj/78PgnsvaK///44cK5Uf/+zK///jhwrlR//7Mr///4fWlyY//m3f//8PrS5Mf/zbv///8emo/tzcnaU15uc/pXnPzd7/nWh++g37bs/pEH+SCP4tN/d/z/JFv+7/n+R9v///94u/HAqUP///+QpmVf///+umX+GbD9+OZ+/E5D+GYvr/vxV3773xI/hm/yXD///8aqPljTu8K/////IkD//624AA=";
  private const string Profile3_Gbr_12 = "sSTBoXgAPAA9gA4JBwYAAAAIAAB55gX////x//yTX10/c65r///v2f//fE///vLf8l/Hf/++J///GIyPKb//2XP/////4/9/z6fp4uGf/+kv////d7v///9OjTfjDxz////3e7///4e6lyY//mR/////4+9X/7STOBh3////49Af//Xmf//99Ubdix//9eZ///+0XfigVKH////HYtL///vLf8l/Hf/++J///eW/5L+O//97Ff///6dGm/GHjn////u93///+nRpvxh45////7u6v///fVG3Ysf//Xmf//99Ubdix//9b4Mv///jEZHlN//7Mr//+MRkeU3//syv///h7qXJj/+bd///w91Lkx//Nu////aLvxQKlD////juX///+0XfigVKH////HapoAA=";
}
