using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Bpg.Codec;
using FileFormat.Codecs.H265;
using FileFormat.Core;

namespace FileFormat.Bpg.Tests;

/// <summary>Real transform-coded HEVC carried in BPG's deliberately stripped form.</summary>
/// <remarks>
/// The fixture is the same x265 stream whose reconstructed component planes
/// <see cref="Heif.Tests.DeepColorTests"/> compares sample-for-sample with ffmpeg. This test removes
/// its VPS/SPS exactly as BPG requires, replaces the SPS with BPG's compact <c>hevc_header()</c>, and
/// then asks the ordinary BPG reader for pixels. The old BPG path cannot pass this test: it searches
/// the picture payload for the SPS the format explicitly forbids carrying.
/// </remarks>
[TestFixture]
public sealed class RealStreamTests {

  [Test]
  [Category("Conformance")]
  public void X265Main10Picture_DecodesAfterBpgStripsItsSequenceParameterSet() {
    var annexB = _Fixture("main10.265");
    var (expected, sequence) = _DecodeAnnexB(annexB);

    Assert.Multiple(() => {
      Assert.That(sequence.BitDepthLuma, Is.EqualTo(10), "the fixture exercises BPG's deep-sample path");
      Assert.That(sequence.DisplayWidth, Is.EqualTo(64));
      Assert.That(sequence.DisplayHeight, Is.EqualTo(64));
      Assert.That(sequence.ColorInfo?.Range, Is.EqualTo(RawColorRange.Full));
      Assert.That(sequence.ColorInfo?.Matrix, Is.EqualTo(RawMatrixCoefficients.Bt601));
    });

    var bpg = new BpgFile {
      Width = sequence.DisplayWidth,
      Height = sequence.DisplayHeight,
      PixelFormat = _BpgPixelFormat(sequence.ChromaFormatIdc),
      BitDepth = sequence.BitDepthLuma,
      ColorSpace = _BpgColorSpace(sequence.ColorInfo?.Matrix),
      LimitedRange = sequence.ColorInfo?.Range != RawColorRange.Full,
      PixelData = _StripForBpg(annexB, sequence),
    };

    // Serialize and parse the container too: the regression belongs to the public image-format path,
    // not merely to the codec helper behind it.
    var actual = BpgFile.ToRawImage(BpgReader.FromBytes(BpgWriter.ToBytes(bpg)));

    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.EqualTo(sequence.DisplayWidth));
      Assert.That(actual.Height, Is.EqualTo(sequence.DisplayHeight));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(actual.PixelData, Is.EqualTo(expected),
        "stripping the SPS into BPG's header must not change one displayed sample");
    });
  }

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Heif", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  private static (byte[] Pixels, H265SequenceParameterSet Sequence) _DecodeAnnexB(byte[] annexB) {
    var sequences = new Dictionary<int, H265SequenceParameterSet>();
    var pictures = new Dictionary<int, H265PictureParameterSet>();
    H265FrameDecoder? frame = null;
    H265SliceHeader? precedingIndependent = null;
    H265SequenceParameterSet? sequence = null;

    foreach (var nal in H265NalReader.SplitAnnexB(annexB)) {
      switch (nal.Type) {
        case H265NalUnitType.SequenceParameterSet: {
          var parsed = H265SequenceParameterSet.Parse(nal.Payload);
          sequences[parsed.Id] = parsed;
          continue;
        }
        case H265NalUnitType.PictureParameterSet: {
          var parsed = H265PictureParameterSet.Parse(nal.Payload);
          pictures[parsed.Id] = parsed;
          continue;
        }
      }

      if (!nal.IsSlice)
        continue;

      var header = H265SliceHeader.Parse(nal, sequences, pictures, precedingIndependent);
      if (!header.DependentSliceSegment)
        precedingIndependent = header;
      if (header.FirstSliceSegmentInPicture) {
        Assert.That(frame, Is.Null, "the fixture must remain a single coded picture");
        frame = new(header.Sps, header.Pps);
        sequence = header.Sps;
      }

      frame!.DecodeSliceSegment(header, [[], []]);
    }

    Assert.That(frame, Is.Not.Null);
    frame!.RefuseIfIncomplete();
    H265Deblocking.Filter(frame);
    H265SampleAdaptiveOffset.Filter(frame);

    var sps = sequence!;
    var pixels = H265ColorConversion.ToRgb24(
      frame.Picture,
      sps.CropOffsetX,
      sps.CropOffsetY,
      sps.DisplayWidth,
      sps.DisplayHeight,
      sps.BitDepthLuma,
      sps.BitDepthChroma,
      sps.ColorInfo);
    return (pixels, sps);
  }

  private static byte[] _StripForBpg(byte[] annexB, H265SequenceParameterSet sps) {
    var picture = new List<byte>();
    var header = _BpgSequenceHeader(sps);
    BpgUe7.Write(picture, header.Length);
    picture.AddRange(header);

    var first = true;
    foreach (var nal in _RawNals(annexB)) {
      var type = (nal.Span[0] >> 1) & 0x3f;
      if (type is (int)H265NalUnitType.VideoParameterSet or (int)H265NalUnitType.SequenceParameterSet)
        continue;
      if (type is not (int)H265NalUnitType.PictureParameterSet and > 31)
        continue; // prefix/suffix SEI is irrelevant to reconstruction and BPG does not need it here

      if (!first)
        picture.AddRange([0, 0, 1]);
      picture.AddRange(nal.ToArray());
      first = false;
    }

    Assert.That(first, Is.False, "the stripped stream must still contain a PPS and coded picture");
    return [.. picture];
  }

  private static byte[] _BpgSequenceHeader(H265SequenceParameterSet sps) {
    var writer = new BitWriter();
    writer.WriteUnsignedExpGolomb(sps.MinCbLog2SizeY - 3);
    writer.WriteUnsignedExpGolomb(sps.CtbLog2SizeY - sps.MinCbLog2SizeY);
    writer.WriteUnsignedExpGolomb(sps.MinTbLog2SizeY - 2);
    writer.WriteUnsignedExpGolomb(sps.MaxTbLog2SizeY - sps.MinTbLog2SizeY);
    writer.WriteUnsignedExpGolomb(sps.MaxTransformHierarchyDepthIntra);
    writer.WriteBit(sps.SampleAdaptiveOffsetEnabled);
    writer.WriteBit(sps.PcmEnabled);
    if (sps.PcmEnabled) {
      writer.WriteBits(sps.PcmBitDepthLuma - 1, 4);
      writer.WriteBits(sps.PcmBitDepthChroma - 1, 4);
      writer.WriteUnsignedExpGolomb(sps.Log2MinPcmCbSizeY - 3);
      writer.WriteUnsignedExpGolomb(sps.Log2MaxPcmCbSizeY - sps.Log2MinPcmCbSizeY);
      writer.WriteBit(sps.PcmLoopFilterDisabled);
    }

    writer.WriteBit(sps.StrongIntraSmoothingEnabled);
    writer.WriteBit(false); // sps_extension_present_flag; this fixture uses no range-extension tool
    writer.PadWithZeroBits();
    return writer.ToArray();
  }

  /// <summary>Returns NAL header+escaped payload while discarding their Annex-B start codes.</summary>
  private static IEnumerable<ReadOnlyMemory<byte>> _RawNals(byte[] annexB) {
    var starts = new List<(int Start, int Payload)>();
    for (var i = 0; i + 3 < annexB.Length;) {
      if (annexB[i] != 0 || annexB[i + 1] != 0) {
        ++i;
        continue;
      }

      var length = annexB[i + 2] == 1 ? 3 : i + 3 < annexB.Length && annexB[i + 2] == 0 && annexB[i + 3] == 1 ? 4 : 0;
      if (length == 0) {
        ++i;
        continue;
      }

      starts.Add((i, i + length));
      i += length;
    }

    for (var i = 0; i < starts.Count; ++i) {
      var end = i + 1 < starts.Count ? starts[i + 1].Start : annexB.Length;
      while (end > starts[i].Payload && annexB[end - 1] == 0)
        --end; // Annex-B trailing_zero_8bits are not part of a NAL unit
      if (end > starts[i].Payload)
        yield return annexB.AsMemory(starts[i].Payload, end - starts[i].Payload);
    }
  }

  private static BpgPixelFormat _BpgPixelFormat(int chromaFormatIdc) => chromaFormatIdc switch {
    0 => BpgPixelFormat.Grayscale,
    // Ordinary HEVC 4:2:0/4:2:2 uses left-sited horizontal chroma, BPG's MPEG-2 variants.
    1 => BpgPixelFormat.YCbCr420Mpeg2,
    2 => BpgPixelFormat.YCbCr422Mpeg2,
    3 => BpgPixelFormat.YCbCr444,
    _ => throw new InvalidDataException($"Unsupported chroma_format_idc {chromaFormatIdc}."),
  };

  private static BpgColorSpace _BpgColorSpace(RawMatrixCoefficients? matrix) => matrix switch {
    RawMatrixCoefficients.Identity => BpgColorSpace.Rgb,
    RawMatrixCoefficients.YCgCo => BpgColorSpace.YCgCo,
    RawMatrixCoefficients.Bt709 => BpgColorSpace.YCbCrBT709,
    RawMatrixCoefficients.Bt2020NonConstantLuminance => BpgColorSpace.YCbCrBT2020Ncl,
    _ => BpgColorSpace.YCbCrBT601,
  };

  private sealed class BitWriter {
    private readonly List<byte> _bytes = [];
    private int _current;
    private int _bits;

    internal void WriteBit(bool value) => this.WriteBits(value ? 1 : 0, 1);

    internal void WriteBits(int value, int count) {
      for (var bit = count - 1; bit >= 0; --bit) {
        this._current = (this._current << 1) | ((value >> bit) & 1);
        if (++this._bits != 8)
          continue;
        this._bytes.Add((byte)this._current);
        this._current = 0;
        this._bits = 0;
      }
    }

    internal void WriteUnsignedExpGolomb(int value) {
      var code = (uint)value + 1;
      var bits = 32 - System.Numerics.BitOperations.LeadingZeroCount(code);
      for (var i = 1; i < bits; ++i)
        this.WriteBit(false);
      this.WriteBits(unchecked((int)code), bits);
    }

    internal void PadWithZeroBits() {
      while (this._bits != 0)
        this.WriteBit(false);
    }

    internal byte[] ToArray() {
      Assert.That(this._bits, Is.Zero);
      return [.. this._bytes];
    }
  }
}
