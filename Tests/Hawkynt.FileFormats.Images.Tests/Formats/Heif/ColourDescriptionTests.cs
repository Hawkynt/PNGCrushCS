using System;
using System.IO;
using System.Linq;
using FileFormat.Codecs.H265;
using FileFormat.Core;
using FileFormat.Heif;
using Hawkynt.FileFormats.Images.Tests;
using NUnit.Framework;

namespace FileFormat.Heif.Tests;

/// <summary>
/// What a HEIF says its samples mean, and what the conversion out of Y/Cb/Cr does about it.
/// </summary>
/// <remarks>
/// The reader used to convert every item as studio-swing BT.601 no matter what the file said. That
/// is the right reading for a stream describing itself no further, and the wrong one for almost
/// every HEIC in existence: libheif and x265 write full range unless told otherwise, and reading
/// full-range samples as studio swing compresses the contrast of the whole picture by the ratio
/// 219/255. It measured as two and a half to three and a half per cent mean error against libheif's
/// own decode of the same file, on every quality, depth and matrix — a decode that worked, only
/// washed out.
/// <para/>
/// Two places state the answer and they can disagree. The item's colour information property is one;
/// the sequence parameter set's video usability information is the other. The container wins, per
/// ISO/IEC 23000-22 clause 7.3.6.4, and the bitstream is the fallback for an item that carries no
/// such property — which is what ImageMagick's HEIC writer produces.
/// </remarks>
[TestFixture]
public sealed class ColourDescriptionTests {

  private const int _SIZE = 4;

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Heif", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <summary>Converts a flat 8-bit picture, so no chroma interpolation can enter the answer.</summary>
  private static (byte R, byte G, byte B) _Convert(int luma, int cb, int cr, RawImageColorInfo? colour) {
    var picture = new H265Picture(_SIZE, _SIZE, 2);
    Array.Fill(picture.Luma, (ushort)luma);
    Array.Fill(picture.Cb, (ushort)cb);
    Array.Fill(picture.Cr, (ushort)cr);

    var rgb = H265ColorConversion.ToRgb24(picture, 0, 0, _SIZE, _SIZE, 8, 8, colour);
    return (rgb[0], rgb[1], rgb[2]);
  }

  private static RawImageColorInfo _Colour(RawColorRange range, RawMatrixCoefficients matrix)
    => new() { Range = range, Matrix = matrix };

  /// <summary>
  /// A stream that says nothing still reads as studio swing, which is what it always did.
  /// </summary>
  /// <remarks>
  /// The anchors are the whole point of the range: 16 is black and 235 is white, so a picture that
  /// only ever reaches those two must come back as 0 and 255 rather than as a dark grey and a light
  /// one.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void UndescribedStream_StaysStudioSwingBt601() {
    Assert.Multiple(() => {
      Assert.That(_Convert(16, 128, 128, null), Is.EqualTo(((byte)0, (byte)0, (byte)0)));
      Assert.That(_Convert(235, 128, 128, null), Is.EqualTo(((byte)255, (byte)255, (byte)255)));
      Assert.That(_Convert(126, 128, 128, null), Is.EqualTo(((byte)128, (byte)128, (byte)128)));
    });
  }

  /// <summary>Full range says the samples already fill the byte, so nothing is stretched.</summary>
  [Test]
  [Category("Unit")]
  public void FullRange_LeavesTheSamplesWhereTheyAre() {
    var full = _Colour(RawColorRange.Full, RawMatrixCoefficients.Bt601);

    Assert.Multiple(() => {
      Assert.That(_Convert(0, 128, 128, full), Is.EqualTo(((byte)0, (byte)0, (byte)0)));
      Assert.That(_Convert(255, 128, 128, full), Is.EqualTo(((byte)255, (byte)255, (byte)255)));
      // The measurable difference: studio swing calls this black and full range calls it 16.
      Assert.That(_Convert(16, 128, 128, full), Is.EqualTo(((byte)16, (byte)16, (byte)16)));
      Assert.That(_Convert(126, 128, 128, full), Is.EqualTo(((byte)126, (byte)126, (byte)126)));
    });
  }

  /// <summary>
  /// The matrix decides how far a chrominance difference moves each colour, and BT.709's weights are
  /// not BT.601's.
  /// </summary>
  /// <remarks>
  /// Expected values are ITU-R BT.601 and BT.709 evaluated in double precision on the same samples,
  /// to within the level the eight-bit output quantises to.
  /// </remarks>
  [TestCase(RawMatrixCoefficients.Bt601, 179, 113, 72)]
  [TestCase(RawMatrixCoefficients.Bt709, 185, 117, 69)]
  [TestCase(RawMatrixCoefficients.Bt2020NonConstantLuminance, 182, 113, 68)]
  [Category("Unit")]
  public void StatedMatrix_WeighsTheChrominanceDifferencesItsOwnWay(
    RawMatrixCoefficients matrix, int red, int green, int blue) {
    var (r, g, b) = _Convert(126, 100, 160, _Colour(RawColorRange.Limited, matrix));

    Assert.Multiple(() => {
      Assert.That((int)r, Is.EqualTo(red).Within(1));
      Assert.That((int)g, Is.EqualTo(green).Within(1));
      Assert.That((int)b, Is.EqualTo(blue).Within(1));
    });
  }

  /// <summary>
  /// The identity matrix is not a matrix: the planes are green, blue and red already.
  /// </summary>
  /// <remarks>
  /// This is what <c>heif-enc -L</c> writes, because lossless coding cannot afford the rounding a
  /// colour transform costs. Running those planes through a BT.601 inverse turns a green picture
  /// into a grey-brown one.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void IdentityMatrix_HandsBackThePlanesAsGreenBlueRed() {
    var identity = _Colour(RawColorRange.Full, RawMatrixCoefficients.Identity);
    Assert.That(_Convert(200, 40, 90, identity), Is.EqualTo(((byte)90, (byte)200, (byte)40)));
  }

  /// <summary>The video usability information really is read, rather than stepped over.</summary>
  /// <remarks>
  /// The fixture is ImageMagick's own writing, and ffmpeg reports the same four values for it:
  /// full range, SMPTE 170M matrix, BT.709 primaries, sRGB transfer.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  public void SequenceParameterSet_KeepsTheColourSignalType() {
    var sps = _SequenceParameterSetOf(_Fixture("main10.265"));

    Assert.That(sps.ColorInfo, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(sps.ColorInfo!.Range, Is.EqualTo(RawColorRange.Full));
      Assert.That(sps.ColorInfo.Matrix, Is.EqualTo(RawMatrixCoefficients.Bt601));
      Assert.That(sps.ColorInfo.Primaries, Is.EqualTo(RawColorPrimaries.Bt709));
      Assert.That(sps.ColorInfo.Transfer, Is.EqualTo(RawTransferCharacteristic.Srgb));
    });
  }

  /// <summary>
  /// A container property outranks the bitstream, and its absence leaves the bitstream in charge.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void ContainerColourProperty_OutranksTheSequenceParameterSet() {
    var bytes = _Fixture("main10.heic");
    var file = HeifReader.FromBytes(bytes);
    var sample = file.Images[0].RawImageData;
    var configuration = _HevcConfigurationOf(bytes);

    var fromBitstream = HeifHevcDecoder.Decode(sample, configuration).PixelData;
    var fromProperty = HeifHevcDecoder.Decode(
      sample, configuration, _Colour(RawColorRange.Limited, RawMatrixCoefficients.Bt601)).PixelData;

    Assert.Multiple(() => {
      // The fixture carries no colour property, so the reader's own answer is the bitstream's.
      Assert.That(fromBitstream, Is.EqualTo(file.PixelData));
      // And a property that contradicts the bitstream is what the picture is read by.
      Assert.That(fromProperty, Is.Not.EqualTo(fromBitstream));
    });
  }

  /// <summary>
  /// A file a reference encoder wrote decodes to the colours that encoder's own decoder produces.
  /// </summary>
  /// <remarks>
  /// This is the measurement the defect was recorded as. ImageMagick writes HEIC through libheif and
  /// reads it back through libheif, so its decode is the reference.
  /// <para/>
  /// The source is a gradient rather than a fractal on purpose. libheif upsamples the half-size
  /// chroma planes by replicating each sample where this converter interpolates them onto the siting
  /// H.265 states, and on high-frequency colour that difference alone is worth about two and a half
  /// levels — which would swamp the thing being measured. A gradient has almost no chroma detail for
  /// the two upsamplers to disagree about, so what is left is the conversion itself.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  public void LibheifWrittenFile_DecodesToLibheifsOwnColours() {
    var directory = Directory.CreateTempSubdirectory("heif-colour");
    try {
      var source = Path.Combine(directory.FullName, "source.png");
      var heic = Path.Combine(directory.FullName, "picture.heic");
      var reference = Path.Combine(directory.FullName, "reference.ppm");

      _RunOrIgnore("magick",
        $"-size 128x96 -define gradient:angle=45 gradient:red-cyan -colorspace sRGB \"{source}\"");
      _RunOrIgnore("magick", $"\"{source}\" \"{heic}\"");
      if (!File.Exists(heic) || !_IsIsoBmff(File.ReadAllBytes(heic)))
        Assert.Ignore("ImageMagick has no HEIF encoder here.");

      _RunOrIgnore("magick", $"\"{heic}\" -depth 8 -colorspace sRGB \"{reference}\"");
      if (!File.Exists(reference))
        Assert.Ignore("ImageMagick has no HEIF decoder here.");

      var (width, height, expected) = _ReadPpm(reference);
      var actual = HeifFile.ToRawImage(HeifReader.FromBytes(File.ReadAllBytes(heic)));

      Assert.Multiple(() => {
        Assert.That(actual.Width, Is.EqualTo(width));
        Assert.That(actual.Height, Is.EqualTo(height));
        Assert.That(actual.PixelData.Length, Is.EqualTo(expected.Length));
      });

      var total = 0L;
      for (var i = 0; i < expected.Length; ++i)
        total += Math.Abs(actual.PixelData[i] - expected[i]);

      var mean = total / (double)expected.Length;
      Assert.That(mean, Is.LessThan(1.5),
        $"mean difference from libheif's own decode was {mean:F3} levels; reading a full-range file "
        + "as studio swing costs about seven");
    } finally {
      directory.Delete(true);
    }
  }

  /// <summary>
  /// A greyscale source comes back from libheif as a monochrome item, and it decodes.
  /// </summary>
  /// <remarks>
  /// Monochrome used to be refused along with 4:2:2 and 4:4:4, which put an all-black picture and
  /// every greyscale one outside what this reader would open — libheif picks
  /// <c>chroma_format_idc</c> of zero for both. It is not a subsampling: the sequence codes no
  /// chrominance, so there is nothing to read, nothing to reconstruct, nothing to deblock and
  /// nothing to interpolate back up, and the luminance is the whole picture.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  public void GreyscaleSource_DecodesAsAMonochromeItem() {
    var directory = Directory.CreateTempSubdirectory("heif-mono");
    try {
      var source = Path.Combine(directory.FullName, "source.png");
      var heic = Path.Combine(directory.FullName, "picture.heic");
      var reference = Path.Combine(directory.FullName, "reference.ppm");

      _RunOrIgnore("magick", $"-size 128x96 gradient:black-white -colorspace gray \"{source}\"");
      _RunOrIgnore("heif-enc", $"-q 90 -o \"{heic}\" \"{source}\"");
      if (!File.Exists(heic) || !_IsIsoBmff(File.ReadAllBytes(heic)))
        Assert.Ignore("heif-enc would not write a monochrome picture here.");

      _RunOrIgnore("magick", $"\"{heic}\" -depth 8 -colorspace sRGB \"{reference}\"");
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

      // No chrominance means no upsampler to disagree about, so the only slack is the rounding of
      // one multiplication.
      Assert.That(worst, Is.LessThanOrEqualTo(1),
        $"the worst sample differed from libheif's decode by {worst} levels");
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
    Assert.That(_Token(stream), Is.EqualTo("P6"), "ImageMagick was asked for a binary PPM");

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
        while ((c = stream.ReadByte()) >= 0 && c != '\n') { }
        continue;
      }

      if (c is ' ' or '\t' or '\r' or '\n') {
        if (token.Length > 0)
          break;
        continue;
      }

      token.Append((char)c);
    }

    return token.ToString();
  }

  /// <summary>The sequence parameter set of an Annex B elementary stream.</summary>
  private static H265SequenceParameterSet _SequenceParameterSetOf(byte[] annexB) {
    foreach (var nal in H265NalReader.SplitAnnexB(annexB))
      if (nal.Type == H265NalUnitType.SequenceParameterSet)
        return H265SequenceParameterSet.Parse(nal.Payload);

    throw new InvalidDataException("the fixture carries no sequence parameter set");
  }

  /// <summary>The hvcC property of a HEIF's item property container.</summary>
  private static byte[] _HevcConfigurationOf(byte[] bytes) {
    var meta = IsoBmffBox.ReadBoxes(bytes, 0, bytes.Length).First(box => box.Type == IsoBmffBox.Meta);

    // meta is a FullBox: its four bytes of version and flags come before the child boxes.
    var iprp = IsoBmffBox.ReadBoxes(meta.Data, 4, meta.Data.Length - 4)
      .First(box => box.Type == IsoBmffBox.Iprp);
    var ipco = IsoBmffBox.ReadBoxes(iprp.Data, 0, iprp.Data.Length)
      .First(box => box.Type == IsoBmffBox.Ipco);
    return IsoBmffBox.ReadBoxes(ipco.Data, 0, ipco.Data.Length)
      .First(box => box.Type == IsoBmffBox.HvcC).Data;
  }
}
