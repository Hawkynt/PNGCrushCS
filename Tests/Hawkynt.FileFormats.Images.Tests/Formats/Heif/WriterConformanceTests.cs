using System;
using System.IO;
using FileFormat.Codecs.H265;
using FileFormat.Core;
using FileFormat.Heif;
using Hawkynt.FileFormats.Images.Tests;
using NUnit.Framework;

namespace FileFormat.Heif.Tests;

/// <summary>
/// What the writer puts in the bitstream, judged by the standard and by a decoder that is not ours.
/// </summary>
/// <remarks>
/// Reading our own output back proves the two halves agree with each other and nothing more. The
/// first version of this writer coded 64 by 64 PCM coding units, which our decoder read perfectly
/// and libde265 refused outright: clause 7.4.3.2.1 caps <c>Log2MaxIpcmCbSizeY</c> at
/// <c>Min(CtbLog2SizeY, 5)</c>, so a coding block carrying raw samples is never larger than 32 by
/// 32. Nothing that only asked our own decoder could have found that, which is why the checks below
/// are against the written syntax and against ffmpeg.
/// </remarks>
[TestFixture]
public sealed class WriterConformanceTests {

  private static RawImage _Plasma(int width, int height) {
    var pixels = new byte[width * height * 3];
    var random = new Random(20260905);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)(24 + 200L * x / Math.Max(1, width - 1) ^ random.Next(0, 32));
        pixels[at + 1] = (byte)(32 + 176L * y / Math.Max(1, height - 1) ^ random.Next(0, 32));
        pixels[at + 2] = (byte)(40 + 160L * (x + y) / Math.Max(1, width + height - 2) ^ random.Next(0, 32));
      }
    return new RawImage { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>The sequence parameter set out of the hvcC property the writer emitted.</summary>
  private static H265SequenceParameterSet _WrittenSequence(RawImage source) {
    var heif = HeifWriter.ToBytes(HeifFile.FromRawImage(source));
    var hvcC = _FindHvcC(heif);
    var configuration = H265DecoderConfiguration.TryParse(hvcC);
    Assert.That(configuration, Is.Not.Null, "the hvcC property must be a decoder configuration record");

    foreach (var parameterSet in configuration!.ParameterSets) {
      var nal = H265NalReader.Parse(parameterSet);
      if (nal.Type == H265NalUnitType.SequenceParameterSet)
        return H265SequenceParameterSet.Parse(nal.Payload);
    }

    Assert.Fail("the hvcC property carries no sequence parameter set");
    return null!;
  }

  /// <summary>The payload of the one hvcC box in a file this writer produced.</summary>
  private static byte[] _FindHvcC(byte[] heif) {
    for (var at = 0; at + 8 <= heif.Length; ++at) {
      if (heif[at] != 'h' || heif[at + 1] != 'v' || heif[at + 2] != 'c' || heif[at + 3] != 'C')
        continue;

      var size = (heif[at - 4] << 24) | (heif[at - 3] << 16) | (heif[at - 2] << 8) | heif[at - 1];
      return heif[(at + 4)..(at - 4 + size)];
    }

    Assert.Fail("the written file has no hvcC property");
    return null!;
  }

  /// <summary>
  /// The constraint libde265 enforced and we broke: a PCM coding block is at most 32 by 32.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void WrittenSequence_KeepsPcmCodingBlocksWithinTheSizeTheStandardAllows() {
    var sps = _WrittenSequence(_Plasma(96, 80));

    Assert.Multiple(() => {
      Assert.That(sps.PcmEnabled, Is.True);
      Assert.That(sps.Log2MaxPcmCbSizeY, Is.LessThanOrEqualTo(Math.Min(sps.CtbLog2SizeY, 5)),
        "clause 7.4.3.2.1: Log2MaxIpcmCbSizeY <= Min(CtbLog2SizeY, 5)");
      Assert.That(sps.Log2MinPcmCbSizeY, Is.GreaterThanOrEqualTo(Math.Min(sps.MinCbLog2SizeY, 5)));
      Assert.That(sps.CtbLog2SizeY, Is.InRange(4, 6));
      Assert.That(sps.PcmBitDepthLuma, Is.EqualTo(8));
      Assert.That(sps.PcmBitDepthChroma, Is.EqualTo(8));
    });
  }

  /// <summary>
  /// A level is a promise about how large the picture is, so it cannot be one constant for all of
  /// them: a decoder that sizes its buffers from the level is entitled to refuse a picture that
  /// does not fit.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void WrittenSequence_StatesALevelThePictureFitsInside() {
    // Table A.8: level 4 covers 2228224 luma samples and level 5 covers 8912896, so a 2048 by 2048
    // picture is past level 4 and a 320 by 240 one is far below it.
    var small = _WrittenSequence(_Plasma(320, 240));
    var large = _WrittenSequence(_Plasma(2048, 2048));

    Assert.Multiple(() => {
      Assert.That(large.Width * large.Height, Is.GreaterThan(2228224),
        "the large picture must be past level 4, or the check below proves nothing");
      Assert.That(large.ProfileTierLevel.LevelIdc, Is.GreaterThan(120),
        "a picture past level 4 cannot be announced as level 4");
      Assert.That(small.ProfileTierLevel.LevelIdc, Is.LessThan(large.ProfileTierLevel.LevelIdc),
        "a small picture must not be given the large one's level");
      Assert.That(small.ProfileTierLevel.HighTier, Is.False);
    });
  }

  /// <summary>
  /// The decode nothing of ours took part in. Our writer, ffmpeg's decoder, and the samples the
  /// encoder was handed: PCM keeps every one of them, so anything short of equality is a defect.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void WrittenFile_DecodesInFfmpegToTheSamplesItWasGiven() {
    var source = _Plasma(96, 80);
    var yuv = FastRawImageConverter.Convert(source, PixelFormat.Yuv420P8);

    var directory = Directory.CreateTempSubdirectory("heif-writer-ffmpeg");
    try {
      var heic = Path.Combine(directory.FullName, "written.heic");
      var decoded = Path.Combine(directory.FullName, "decoded.yuv");
      File.WriteAllBytes(heic, HeifWriter.ToBytes(HeifFile.FromRawImage(source)));

      using (var decode = ExternalTool.StartOrIgnore(
               "ffmpeg",
               $"-hide_banner -loglevel error -i \"{heic}\" -pix_fmt yuv420p -f rawvideo \"{decoded}\" -y")) {
        var complaint = decode.StandardError.ReadToEnd();
        decode.WaitForExit();
        if (decode.ExitCode != 0 || !File.Exists(decoded))
          Assert.Ignore($"ffmpeg would not read the file here: {complaint.Trim()}");
      }

      var expected = new byte[yuv.Width * yuv.Height * 3 / 2];
      var at = 0;
      for (var plane = 0; plane < 3; ++plane) {
        var data = yuv.GetPlaneData(plane);
        data.CopyTo(expected.AsSpan(at));
        at += data.Length;
      }

      var actual = File.ReadAllBytes(decoded);
      Assert.That(actual.Length, Is.EqualTo(expected.Length),
        "ffmpeg must decode the picture at the size the writer coded");

      var differing = 0;
      for (var i = 0; i < expected.Length; ++i)
        if (expected[i] != actual[i])
          ++differing;

      Assert.That(differing, Is.Zero,
        $"{differing} of {expected.Length} samples came back changed; PCM coding units are lossless");
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }
}
