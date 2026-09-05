using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.H265;
using FileFormat.Heif;
using NUnit.Framework;

namespace FileFormat.Heif.Tests;

/// <summary>
/// HEIF items coded deeper than eight bits, checked sample for sample against ffmpeg's decode.
/// </summary>
/// <remarks>
/// Ten bits is not an exotic case: it is what libheif and x265 produce unless told otherwise, and
/// ImageMagick's HEIC writer goes to twelve. A reader that stops at eight therefore refuses most of
/// the files in the wild, which is what this one used to do.
/// <para/>
/// The fixtures come in pairs. The <c>.265</c> is the item's coded picture as an Annex B elementary
/// stream, and the <c>.yuv</c> beside it is ffmpeg's decode of that same stream, in the sequence's
/// own depth, little endian. Comparing there rather than in RGB is deliberate: the reconstruction is
/// exactly defined by the standard and two conforming decoders agree on every sample, whereas what
/// to do with those samples afterwards — which matrix, which range, how to bring the half-size
/// chroma planes back up — is a display convention that two programs may spell differently. An RGB
/// comparison would put a tolerance on a step that has none.
/// </remarks>
[TestFixture]
public sealed class DeepColorTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Heif", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  [TestCase("main10", 10)]
  [TestCase("main12", 12)]
  [Category("Conformance")]
  public void DeepIntraPicture_ReconstructsEverySampleAsFfmpegDoes(string fixture, int expectedDepth) {
    var (planes, sps) = _Decode(_Fixture(fixture + ".265"));
    var reference = _Fixture(fixture + ".yuv");

    Assert.Multiple(() => {
      Assert.That(sps.BitDepthLuma, Is.EqualTo(expectedDepth));
      Assert.That(sps.BitDepthChroma, Is.EqualTo(expectedDepth));
      Assert.That(planes.Length, Is.EqualTo(reference.Length),
        "the cropped picture must be the size ffmpeg decoded");
    });

    var differing = 0;
    var worst = 0;
    var at = -1;
    for (var i = 0; i + 1 < reference.Length; i += 2) {
      var ours = planes[i] | (planes[i + 1] << 8);
      var theirs = reference[i] | (reference[i + 1] << 8);
      if (ours == theirs)
        continue;

      ++differing;
      if (Math.Abs(ours - theirs) <= worst)
        continue;
      worst = Math.Abs(ours - theirs);
      at = i >> 1;
    }

    Assert.That(differing, Is.Zero,
      $"{differing} of {reference.Length / 2} samples differ; the worst is sample {at}, "
      + "and a conforming decoder has no tolerance here");
  }

  [TestCase("main10.heic")]
  [TestCase("main12.heic")]
  [Category("Conformance")]
  public void DeepHeifFile_ReadsThroughTheOrdinarySingleImageContract(string fixture) {
    var file = HeifReader.FromBytes(_Fixture(fixture));
    var image = HeifFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(64));
      Assert.That(image.Height, Is.EqualTo(64));
      Assert.That(image.PixelData.Length, Is.EqualTo(64 * 64 * 3));
    });

    // The reader used to hand back a buffer sized for the picture with almost nothing in it when the
    // decode failed. A picture of plasma has no such run, so this says the samples are really there.
    var longestRun = 0;
    var run = 0;
    foreach (var value in image.PixelData) {
      run = value == 0 ? run + 1 : 0;
      if (run > longestRun)
        longestRun = run;
    }

    Assert.That(longestRun, Is.LessThan(64 * 3), "the decode must be a picture, not a padded buffer");
  }

  /// <summary>
  /// Decodes one Annex B intra picture and returns its cropped planes in the sequence's own depth,
  /// little endian — the layout ffmpeg's <c>rawvideo</c> writes.
  /// </summary>
  private static (byte[] Planes, H265SequenceParameterSet Sps) _Decode(byte[] annexB) {
    var sequenceSets = new Dictionary<int, H265SequenceParameterSet>();
    var pictureSets = new Dictionary<int, H265PictureParameterSet>();
    H265FrameDecoder? frame = null;
    H265SequenceParameterSet? sps = null;

    foreach (var nal in H265NalReader.SplitAnnexB(annexB)) {
      switch (nal.Type) {
        case H265NalUnitType.SequenceParameterSet: {
          var parsed = H265SequenceParameterSet.Parse(nal.Payload);
          sequenceSets[parsed.Id] = parsed;
          continue;
        }
        case H265NalUnitType.PictureParameterSet: {
          var parsed = H265PictureParameterSet.Parse(nal.Payload);
          pictureSets[parsed.Id] = parsed;
          continue;
        }
      }

      if (!nal.IsSlice)
        continue;

      var header = H265SliceHeader.Parse(nal, sequenceSets, pictureSets);
      if (header.FirstSliceSegmentInPicture) {
        Assert.That(frame, Is.Null, "the fixture is one coded picture");
        frame = new(header.Sps, header.Pps);
        sps = header.Sps;
      }

      frame!.DecodeSliceSegment(header, [[], []]);
    }

    Assert.That(frame, Is.Not.Null);
    frame!.RefuseIfIncomplete();
    H265Deblocking.Filter(frame);
    H265SampleAdaptiveOffset.Filter(frame);

    var picture = frame.Picture;
    var width = sps!.DisplayWidth;
    var height = sps.DisplayHeight;
    var chromaWidth = (width + 1) >> 1;
    var chromaHeight = (height + 1) >> 1;

    using var planes = new MemoryStream();
    _WritePlane(planes, picture.Luma, picture.Width, sps.CropOffsetX, sps.CropOffsetY, width, height);
    _WritePlane(planes, picture.Cb, picture.ChromaWidth, sps.CropOffsetX >> 1, sps.CropOffsetY >> 1,
      chromaWidth, chromaHeight);
    _WritePlane(planes, picture.Cr, picture.ChromaWidth, sps.CropOffsetX >> 1, sps.CropOffsetY >> 1,
      chromaWidth, chromaHeight);
    return (planes.ToArray(), sps);
  }

  private static void _WritePlane(
    Stream target, ushort[] plane, int stride, int left, int top, int width, int height) {
    var row = new byte[width * 2];
    for (var y = 0; y < height; ++y) {
      var at = (top + y) * stride + left;
      for (var x = 0; x < width; ++x) {
        var sample = plane[at + x];
        row[x * 2] = (byte)sample;
        row[x * 2 + 1] = (byte)(sample >> 8);
      }
      target.Write(row);
    }
  }
}
