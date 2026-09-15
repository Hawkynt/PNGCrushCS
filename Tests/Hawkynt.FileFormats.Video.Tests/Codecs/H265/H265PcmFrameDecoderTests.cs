using System.Collections.Generic;

namespace FileFormat.Codecs.H265.Tests;

[TestFixture]
public sealed class H265PcmFrameDecoderTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 64;

  [Test]
  [Category("RoundTrip")]
  [Category("HappyPath")]
  public void GeneralFrameDecoderLeavesAndRestartsCabacAroundFourPcmCodingUnits() {
    var y = new byte[_WIDTH * _HEIGHT];
    var cb = new byte[_WIDTH * _HEIGHT / 4];
    var cr = new byte[_WIDTH * _HEIGHT / 4];
    for (var i = 0; i < y.Length; ++i)
      y[i] = (byte)((i * 37 + (i >> 5) * 11) & 0xFF);
    for (var i = 0; i < cb.Length; ++i) {
      cb[i] = (byte)(64 + (i * 29 & 127));
      cr[i] = (byte)(64 + (i * 43 & 127));
    }

    var level = H265PcmStillCodec._SmallestLevelFor(_WIDTH, _HEIGHT);
    var sps = H265SequenceParameterSet.Parse(
      H265PcmStillCodec._BuildSps(_WIDTH, _HEIGHT, _WIDTH, _HEIGHT, level));
    var pps = H265PictureParameterSet.Parse(H265PcmStillCodec._BuildPps());
    var rbsp = H265PcmStillCodec._BuildSlice(y, cb, cr, _WIDTH, _HEIGHT);
    var nal = new H265NalUnit(H265NalUnitType.IdrWithNoLeadingPictures, 0, 0, rbsp, []);
    var header = H265SliceHeader.Parse(
      nal,
      new Dictionary<int, H265SequenceParameterSet> { [0] = sps },
      new Dictionary<int, H265PictureParameterSet> { [0] = pps });

    var decoder = new H265FrameDecoder(sps, pps);
    decoder.DecodeSliceSegment(header, [[], []]);
    decoder.RefuseIfIncomplete();

    Assert.Multiple(() => {
      Assert.That(decoder.Picture.Luma, Is.EqualTo(_Wide(y)));
      Assert.That(decoder.Picture.Cb, Is.EqualTo(_Wide(cb)));
      Assert.That(decoder.Picture.Cr, Is.EqualTo(_Wide(cr)));
    });
  }

  private static ushort[] _Wide(byte[] source) {
    var result = new ushort[source.Length];
    for (var i = 0; i < source.Length; ++i)
      result[i] = source[i];
    return result;
  }
}
