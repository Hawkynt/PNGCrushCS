using System;
using System.IO;
using FileFormat.Heif;
using Hawkynt.FileFormats.Images.Tests;
using NUnit.Framework;

namespace FileFormat.Heif.Tests;

/// <summary>
/// The three chroma formats a HEVC image item can carry, decoded against the encoder's own decoder.
/// </summary>
/// <remarks>
/// 4:2:2 and 4:4:4 used to be refused by name, which put every file <c>heif-enc -p chroma=422</c> or
/// <c>-p chroma=444</c> writes outside what this reader would open — and <c>heif-enc -L</c> as well,
/// because lossless coding cannot afford a colour transform and libheif therefore writes it as 4:4:4
/// with the identity matrix.
/// <para/>
/// They are three decoding processes rather than three plane sizes. 4:2:2 cuts each chrominance
/// transform block into two stacked squares, each with a coded-block flag, a residual and — for an
/// intra unit — a prediction of its own that reads the reconstructed square above it, and it bends
/// every intra direction through Table 8-4 because a half-width grid points an angle somewhere else.
/// 4:4:4 gives chrominance the luminance block sizes, a prediction mode per prediction block rather
/// than per coding unit, the mode-dependent coefficient scan at 8x8, and the reference smoothing
/// that is otherwise luminance's alone. Both take the quantiser bound of clause 8.6.1 in place of
/// Table 8-10, and each has its own deblocking and sample-adaptive-offset grid.
/// <para/>
/// <b>What the oracle can and cannot say here.</b> The samples themselves are checked elsewhere, on
/// the coded planes, against ffmpeg and libde265 — comparing decoded RGB compares two chroma
/// upsamplers and two colour matrices as much as two decoders. What these tests add is the end of
/// the path: a file libheif wrote, opened through the container, comes back the size libheif says
/// and the colours libheif gives it. 4:4:4 is where that comparison is exact, because neither side
/// has anything to upsample; at 4:2:2 libheif replicates the half-width chrominance samples where
/// this reader interpolates them onto the siting H.265 states, so the two disagree on colour detail
/// by a bounded amount that is not a fault, and a source with little colour detail is what separates
/// the two effects.
/// </remarks>
[TestFixture]
public sealed class SubsampledChromaTests {

  /// <summary>
  /// A 4:4:4 item comes back sample for sample as libheif decodes it.
  /// </summary>
  /// <remarks>
  /// The strongest statement any of these comparisons can make, and only 4:4:4 allows it: with a
  /// chrominance sample under every luminance sample there is no upsampler on either side, so the
  /// only slack left between the two decoders is the rounding of one matrix multiplication.
  /// </remarks>
  [TestCase(422, 3)]
  [TestCase(444, 1)]
  [Category("Conformance")]
  public void ASubsampledItem_DecodesToLibheifsOwnColours(int chroma, int allowed) {
    var directory = Directory.CreateTempSubdirectory($"heif-chroma-{chroma}");
    try {
      var source = Path.Combine(directory.FullName, "source.png");
      var heic = Path.Combine(directory.FullName, "picture.heic");
      var reference = Path.Combine(directory.FullName, "reference.ppm");

      // A gradient rather than a fractal: the two upsamplers disagree only where the colour has
      // detail, and this test is not about them.
      _RunOrIgnore("magick",
        $"-size 128x96 -define gradient:angle=45 gradient:red-cyan -colorspace sRGB \"{source}\"");
      _RunOrIgnore("heif-enc", $"--hevc -p chroma={chroma} -q 90 -o \"{heic}\" \"{source}\"");
      if (!File.Exists(heic) || !_IsIsoBmff(File.ReadAllBytes(heic)))
        Assert.Ignore($"heif-enc would not write a {chroma} picture here.");

      // ImageMagick decodes HEIC through libheif, so this is the encoder's own decoder answering.
      _RunOrIgnore("magick", $"\"{heic}\" -depth 8 \"{reference}\"");
      if (!File.Exists(reference))
        Assert.Ignore("ImageMagick has no HEIF decoder here.");

      var (width, height, expected) = _ReadPpm(reference);
      var actual = HeifFile.ToRawImage(HeifReader.FromBytes(File.ReadAllBytes(heic)));

      Assert.Multiple(() => {
        Assert.That(actual.Width, Is.EqualTo(width));
        Assert.That(actual.Height, Is.EqualTo(height));
        Assert.That(actual.PixelData.Length, Is.EqualTo(expected.Length));
      });

      var worst = 0;
      for (var i = 0; i < expected.Length; ++i)
        worst = Math.Max(worst, Math.Abs(actual.PixelData[i] - expected[i]));

      Assert.That(worst, Is.LessThanOrEqualTo(allowed),
        $"the worst sample differed from libheif's own decode by {worst} levels");
    } finally {
      directory.Delete(true);
    }
  }

  /// <summary>
  /// A twelve-bit 4:2:2 or 4:4:4 item decodes too, rather than only the ten bits libheif defaults to.
  /// </summary>
  /// <remarks>
  /// The depth and the chroma format are independent, and both change the decoding process: the
  /// quantiser range, the deblocking thresholds and the sample-adaptive offsets all scale with the
  /// depth, and the chroma format decides what is scaled. Twelve bits is what libheif writes from a
  /// sixteen-bit source when asked, and it is the deepest anything to hand will write — x265 builds
  /// eight, ten and twelve bits and nothing beyond.
  /// </remarks>
  [TestCase(422)]
  [TestCase(444)]
  [Category("Conformance")]
  public void ATwelveBitSubsampledItem_Decodes(int chroma) {
    var directory = Directory.CreateTempSubdirectory($"heif-chroma-{chroma}-12");
    try {
      var source = Path.Combine(directory.FullName, "source.png");
      var heic = Path.Combine(directory.FullName, "picture.heic");
      var reference = Path.Combine(directory.FullName, "reference.ppm");

      _RunOrIgnore("magick",
        $"-size 128x96 -define gradient:angle=45 gradient:red-cyan -colorspace sRGB -depth 16 \"{source}\"");
      _RunOrIgnore("heif-enc", $"--hevc -p chroma={chroma} -b 12 -q 90 -o \"{heic}\" \"{source}\"");
      if (!File.Exists(heic) || !_IsIsoBmff(File.ReadAllBytes(heic)))
        Assert.Ignore($"heif-enc would not write a twelve-bit {chroma} picture here.");

      // ImageMagick decodes HEIC through libheif, so this is the encoder's own decoder answering.
      _RunOrIgnore("magick", $"\"{heic}\" -depth 8 \"{reference}\"");
      if (!File.Exists(reference))
        Assert.Ignore("ImageMagick has no HEIF decoder here.");

      var (width, height, expected) = _ReadPpm(reference);
      var actual = HeifFile.ToRawImage(HeifReader.FromBytes(File.ReadAllBytes(heic)));

      Assert.Multiple(() => {
        Assert.That(actual.Width, Is.EqualTo(width));
        Assert.That(actual.Height, Is.EqualTo(height));
      });

      var worst = 0;
      for (var i = 0; i < expected.Length; ++i)
        worst = Math.Max(worst, Math.Abs(actual.PixelData[i] - expected[i]));

      Assert.That(worst, Is.LessThanOrEqualTo(chroma == 444 ? 1 : 3),
        $"the worst sample differed from libheif's own decode by {worst} levels");
    } finally {
      directory.Delete(true);
    }
  }

  /// <summary>
  /// What <c>heif-enc -L</c> writes decodes exactly, and it is a 4:4:4 item whatever was asked for.
  /// </summary>
  /// <remarks>
  /// Lossless is the case where being one level out is a failure rather than a rounding, so it is
  /// worth its own claim. libheif writes it as 4:4:4 with <c>matrix_coefficients</c> of zero — the
  /// planes are green, blue and red already — and codes every coding unit with
  /// <c>cu_transquant_bypass_flag</c> set, which takes the transform, the quantiser and both in-loop
  /// filters out of the path. Nothing about that is 4:2:0, so the whole of it used to be refused.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  public void ALosslessItem_DecodesExactly() {
    var directory = Directory.CreateTempSubdirectory("heif-lossless");
    try {
      var source = Path.Combine(directory.FullName, "source.png");
      var heic = Path.Combine(directory.FullName, "picture.heic");
      var reference = Path.Combine(directory.FullName, "reference.ppm");

      _RunOrIgnore("magick", $"-size 96x64 plasma:fractal -colorspace sRGB \"{source}\"");
      _RunOrIgnore("heif-enc", $"--hevc -L -o \"{heic}\" \"{source}\"");
      if (!File.Exists(heic) || !_IsIsoBmff(File.ReadAllBytes(heic)))
        Assert.Ignore("heif-enc would not write a lossless picture here.");

      // ImageMagick decodes HEIC through libheif, so this is the encoder's own decoder answering.
      _RunOrIgnore("magick", $"\"{heic}\" -depth 8 \"{reference}\"");
      if (!File.Exists(reference))
        Assert.Ignore("ImageMagick has no HEIF decoder here.");

      var (width, height, expected) = _ReadPpm(reference);
      var actual = HeifFile.ToRawImage(HeifReader.FromBytes(File.ReadAllBytes(heic)));

      Assert.Multiple(() => {
        Assert.That(actual.Width, Is.EqualTo(width));
        Assert.That(actual.Height, Is.EqualTo(height));
      });

      var worst = 0;
      for (var i = 0; i < expected.Length; ++i)
        worst = Math.Max(worst, Math.Abs(actual.PixelData[i] - expected[i]));

      Assert.That(worst, Is.LessThanOrEqualTo(1),
        $"the worst sample of a lossless picture differed from libheif's decode by {worst} levels");
    } finally {
      directory.Delete(true);
    }
  }

  private static void _RunOrIgnore(string tool, string arguments) {
    using var process = ExternalTool.StartOrIgnore(tool, arguments);
    process.StandardError.ReadToEnd();
    process.StandardOutput.ReadToEnd();
    process.WaitForExit();
  }

  private static bool _IsIsoBmff(byte[] bytes)
    => bytes.Length >= 12 && bytes[4] == (byte)'f' && bytes[5] == (byte)'t'
       && bytes[6] == (byte)'y' && bytes[7] == (byte)'p';

  private static (int Width, int Height, byte[] Pixels) _ReadPpm(string path) {
    using var stream = File.OpenRead(path);
    Assert.That(_Token(stream), Is.EqualTo("P6"), "the reference must be a binary PPM");

    var width = int.Parse(_Token(stream));
    var height = int.Parse(_Token(stream));
    Assert.That(_Token(stream), Is.EqualTo("255"), "the reference must be eight bits a component");

    var pixels = new byte[width * height * 3];
    stream.ReadExactly(pixels);
    return (width, height, pixels);
  }

  /// <summary>One whitespace-delimited PPM header field, comments skipped.</summary>
  private static string _Token(Stream stream) {
    var token = new System.Text.StringBuilder();
    int c;

    while ((c = stream.ReadByte()) >= 0) {
      if (c == '#') {
        while ((c = stream.ReadByte()) >= 0 && c != '\n') {
        }

        continue;
      }

      if (char.IsWhiteSpace((char)c)) {
        if (token.Length > 0)
          break;

        continue;
      }

      token.Append((char)c);
    }

    return token.ToString();
  }
}
