using System;
using System.IO;
using System.Linq;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The patches layer (ISO/IEC 18181-1 §C.4.5; libjxl
/// <c>lib/jxl/dec_patch_dictionary.cc</c>).
/// </summary>
/// <remarks>
/// Where a picture repeats itself, the encoder codes the repeated thing once
/// into a frame that is never shown and states where to stamp it. What used to
/// be here tested a stub, and the stub read a one-bit flag for whether the
/// frame had patches — the same invention the splines stub carried. There is no
/// such flag: the frame states it in its header flags and the section starts on
/// its entropy histograms.
///
/// <para>The fixture is a grid of one repeated motif encoded by <c>cjxl</c>,
/// which produces a kept-aside 8x10 frame and 41 stamps of a 6x10 rectangle
/// from it.</para>
/// </remarks>
[TestFixture]
internal sealed class JxlPatchesTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>
  /// A patched picture decodes to what libjxl decodes it to.
  /// </summary>
  /// <remarks>
  /// Within one level, because <c>djxl</c> dithers its eight-bit output.
  /// Measured against its float output instead, 3 of 98,304 samples differ and
  /// all three sit on a rounding boundary.
  /// </remarks>
  [Test]
  public void APatchedPictureDecodesToWhatLibjxlDecodesItTo() {
    var file = JpegXlReader.FromBytes(_Fixture("cjxl_patches.jxl"));
    var (width, height, expected) = _ReadPpm(_Fixture("cjxl_patches.ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
    });
    Assert.That(file.PixelData, Has.Length.EqualTo(expected.Length));

    var worst = 0;
    for (var i = 0; i < expected.Length; ++i)
      worst = Math.Max(worst, Math.Abs(file.PixelData[i] - expected[i]));

    Assert.That(worst, Is.LessThanOrEqualTo(1),
      $"a sample is out by {worst} levels, which is more than libjxl's output dither can explain");
  }

  /// <summary>
  /// The dictionary says what it says: one rectangle taken from the kept-aside
  /// frame, and every copy of it added rather than laid over.
  /// </summary>
  [Test]
  public void TheDictionaryStatesOneRectangleAndManyCopiesOfIt() {
    var dictionary = _ReadDictionary();

    Assert.Multiple(() => {
      Assert.That(dictionary.Rectangles, Has.Length.EqualTo(1));
      Assert.That(dictionary.Stamps, Has.Length.EqualTo(41));
      // No extra channels, so each stamp states one blend mode.
      Assert.That(dictionary.BlendingStride, Is.EqualTo(1));
    });

    var rectangle = dictionary.Rectangles[0];
    Assert.Multiple(() => {
      Assert.That(rectangle.Reference, Is.EqualTo(3), "the frame kept aside was saved in slot three");
      Assert.That(rectangle.Width, Is.EqualTo(6));
      Assert.That(rectangle.Height, Is.EqualTo(10));
    });

    Assert.That(dictionary.Stamps.All(s => s.Blending[0].Mode == PatchBlendMode.Add), Is.True);
    // Every copy after the first is stated as a step from the one before, so a
    // wrong step would show as copies marching off the picture rather than as
    // one being slightly out.
    Assert.That(dictionary.Stamps.Select(s => (s.X, s.Y)).Distinct().Count(), Is.EqualTo(41));
  }

  /// <param name="mode">The modes that read an alpha channel, and one that does not.</param>
  [TestCase(PatchBlendMode.BlendAbove, true)]
  [TestCase(PatchBlendMode.BlendBelow, true)]
  [TestCase(PatchBlendMode.AlphaWeightedAddAbove, true)]
  [TestCase(PatchBlendMode.AlphaWeightedAddBelow, true)]
  [TestCase(PatchBlendMode.Replace, false)]
  [TestCase(PatchBlendMode.Add, false)]
  [TestCase(PatchBlendMode.Multiply, false)]
  [TestCase(PatchBlendMode.None, false)]
  public void OnlyTheAlphaModesReadAnAlphaChannel(PatchBlendMode mode, bool expected) {
    Assert.That(JxlPatches.UsesAlpha(mode), Is.EqualTo(expected));
    // Multiply states a clamp without reading an alpha, which is the one place
    // the two questions have different answers.
    Assert.That(JxlPatches.UsesClamp(mode), Is.EqualTo(expected || mode == PatchBlendMode.Multiply));
  }

  /// <summary>
  /// A stamp that claims a rectangle bigger than the frame it comes from is
  /// refused rather than read past the end of it.
  /// </summary>
  [Test]
  public void ARectangleOutsideItsSourceFrameIsRefused() {
    var codestream = _Fixture("cjxl_patches.jxl");
    var reader = _SeekToPatchSection(codestream, out var width, out var height, out var extraChannels);

    // The real frame is 8x10; saying it is 2x2 makes the stated rectangle
    // impossible.
    var sizes = new (int Width, int Height)[4];
    sizes[3] = (2, 2);

    Assert.That(() => JxlPatches.Decode(reader, width, height, extraChannels, sizes),
      Throws.InstanceOf<InvalidDataException>());
  }

  /// <summary>A stamp naming an empty slot is refused.</summary>
  [Test]
  public void AStampFromAnEmptySlotIsRefused() {
    var codestream = _Fixture("cjxl_patches.jxl");
    var reader = _SeekToPatchSection(codestream, out var width, out var height, out var extraChannels);

    Assert.That(() => JxlPatches.Decode(reader, width, height, extraChannels, new (int, int)[4]),
      Throws.InstanceOf<InvalidDataException>());
  }

  private static PatchDictionary _ReadDictionary() {
    var codestream = _Fixture("cjxl_patches.jxl");
    var reader = _SeekToPatchSection(codestream, out var width, out var height, out var extraChannels);
    var sizes = new (int Width, int Height)[4];
    sizes[3] = (8, 10);
    return JxlPatches.Decode(reader, width, height, extraChannels, sizes);
  }

  /// <summary>Walk past the kept-aside frame to the patch section of the one
  /// that uses it.</summary>
  private static JxlBitReader _SeekToPatchSection(
    byte[] codestream, out int width, out int height, out int extraChannels
  ) {
    var reader = new JxlBitReader(codestream, 2);
    (width, height) = JxlSizeHeader.Decode(reader);
    var metadata = JxlImageMetadata.Decode(reader);
    extraChannels = (int)metadata.NumExtraChannels;
    JxlCustomTransformData.Decode(reader, metadata.XybEncoded);
    reader.ZeroPadToByte();

    var first = JxlSpecFrameHeader.Decode(reader, metadata, width, height);
    Assert.That(first.FrameType, Is.EqualTo(JxlFrameType.ReferenceOnly));
    var at = _SkipFrameBody(reader, first, first.FrameWidth, first.FrameHeight);

    var second = new JxlBitReader(codestream, at);
    var frame = JxlSpecFrameHeader.Decode(second, metadata, width, height);
    Assert.That(frame.Flags & 2, Is.EqualTo(2), "the second frame is the one that states patches");
    _SkipToc(second, frame, width, height);
    return second;
  }

  private static int _SkipFrameBody(JxlBitReader reader, JxlSpecFrameHeader frame, int frameWidth, int frameHeight) {
    var toc = _SkipToc(reader, frame, frameWidth, frameHeight);
    var body = (int)(reader.BitsRead / 8);
    var total = toc.SectionSizes.Sum();
    return body + total;
  }

  private static JxlFrameToc _SkipToc(JxlBitReader reader, JxlSpecFrameHeader frame, int frameWidth, int frameHeight) {
    var groupDim = 128 << (int)frame.GroupSizeShift;
    var groupsX = (frameWidth + groupDim - 1) / groupDim;
    var groupsY = (frameHeight + groupDim - 1) / groupDim;
    var lfGroupDim = groupDim * 8;
    var dcGroups = ((frameWidth + lfGroupDim - 1) / lfGroupDim) * ((frameHeight + lfGroupDim - 1) / lfGroupDim);
    return JxlFrameToc.Decode(reader, groupsX * groupsY, (int)frame.NumPasses, dcGroups);
  }

  private static (int Width, int Height, byte[] Pixels) _ReadPpm(byte[] ppm) {
    var at = 0;
    string Token() {
      while (at < ppm.Length && char.IsWhiteSpace((char)ppm[at]))
        ++at;
      var start = at;
      while (at < ppm.Length && !char.IsWhiteSpace((char)ppm[at]))
        ++at;
      return System.Text.Encoding.ASCII.GetString(ppm, start, at - start);
    }

    Assert.That(Token(), Is.EqualTo("P6"));
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    Assert.That(Token(), Is.EqualTo("255"));
    ++at;

    var pixels = new byte[width * height * 3];
    Array.Copy(ppm, at, pixels, 0, pixels.Length);
    return (width, height, pixels);
  }
}
