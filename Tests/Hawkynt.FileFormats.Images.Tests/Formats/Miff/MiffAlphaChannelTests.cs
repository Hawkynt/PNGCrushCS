using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Miff;
using Hawkynt.FileFormats.Images.Tests;

namespace FileFormat.Miff.Tests;

/// <summary>
/// How many samples a pixel has is taken from <c>alpha-trait</c> and the colourspace, not from
/// <c>type</c>.
/// </summary>
/// <remarks>
/// This is the reading half of the writer fault fixed alongside it. ImageMagick's own files with an
/// alpha channel carry no <c>type</c> line at all — only <c>alpha-trait=Blend</c> and the older
/// <c>matte=True</c> — so a reader that counts channels from <c>type</c> falls back to its default
/// of three and reads a four-sample file three at a time. Every fourth sample becomes the next
/// pixel's red and the picture shears.
/// <para/>
/// Measured against ImageMagick's own reading of a 61x37 sample, 2257 pixels: truecolour with alpha
/// differed in 748 of them, greyscale with alpha in 1192. Both are 0 once the channel count comes
/// from the fields ImageMagick itself takes it from.
/// <para/>
/// The rule is ImageMagick's own packet size, in its order: one sample, three if the class is
/// DirectClass, one again if the colourspace is grey, plus one for alpha and plus one for CMYK.
/// Confirmed against the <c>number-channels</c> its writer states — 1 grey, 2 grey with alpha, 3
/// truecolour, 4 truecolour with alpha, 5 CMYK with alpha.
/// </remarks>
[TestFixture]
public sealed class MiffAlphaChannelTests {

  /// <summary>A header shaped the way ImageMagick writes one, which states no <c>type</c>.</summary>
  private static byte[] _BuildMiff(int width, int height, int depth, string colorspace, string extraFields, byte[] samples) {
    var header = Encoding.ASCII.GetBytes(
      "id=ImageMagick version=1.0\n"
      + $"class=DirectClass colors=0 {extraFields}\n"
      + $"columns={width} rows={height} depth={depth}\n"
      + $"colorspace={colorspace}\n"
      + "\f\n:\x1a");

    var data = new byte[header.Length + samples.Length];
    header.CopyTo(data, 0);
    samples.CopyTo(data, header.Length);
    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AlphaTraitWithoutType_ReadsFourSamplesAPixel() {
    byte[] samples = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80];
    var result = MiffReader.FromBytes(_BuildMiff(2, 1, 8, "sRGB", "alpha-trait=Blend", samples));

    Assert.Multiple(() => {
      Assert.That(result.PixelData, Is.EqualTo(samples), "the second pixel starts at the fifth sample");
      Assert.That(MiffFile.ToRawImage(result).Format, Is.EqualTo(PixelFormat.Rgba32));
    });
  }

  /// <summary>The older field says it on its own, and a reader predating alpha-trait looks there.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_MatteWithoutType_ReadsFourSamplesAPixel() {
    byte[] samples = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80];
    var result = MiffReader.FromBytes(_BuildMiff(2, 1, 8, "sRGB", "matte=True", samples));

    Assert.That(result.PixelData, Is.EqualTo(samples));
  }

  /// <summary>A picture without the field has three, which is what it always had.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_AlphaTraitUndefined_ReadsThreeSamplesAPixel() {
    byte[] samples = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60];
    var result = MiffReader.FromBytes(_BuildMiff(2, 1, 8, "sRGB", "alpha-trait=Undefined", samples));

    Assert.Multiple(() => {
      Assert.That(result.PixelData, Is.EqualTo(samples));
      Assert.That(MiffFile.ToRawImage(result).Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  /// <summary>
  /// Grey with alpha is two samples a pixel, and the format for it already exists.
  /// </summary>
  /// <remarks>
  /// Reported earlier as needing "a grey+alpha pixel path that does not exist yet". It does exist:
  /// <see cref="PixelFormat.GrayAlpha16"/> has been there for PNG's fourth colour type, and
  /// <see cref="PixelConverter"/> converts it both ways. Nothing new was needed.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void FromBytes_GreyWithAlpha_ReadsTwoSamplesAPixel() {
    byte[] samples = [0x10, 0x20, 0x30, 0x40];
    var result = MiffReader.FromBytes(_BuildMiff(2, 1, 8, "Gray", "alpha-trait=Blend", samples));

    Assert.Multiple(() => {
      Assert.That(result.PixelData, Is.EqualTo(samples));
      Assert.That(MiffFile.ToRawImage(result).Format, Is.EqualTo(PixelFormat.GrayAlpha16));
    });
  }

  /// <summary>Grey without alpha stays one sample a pixel.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_GreyWithoutAlpha_ReadsOneSampleAPixel() {
    byte[] samples = [0x10, 0x20];
    var result = MiffReader.FromBytes(_BuildMiff(2, 1, 8, "Gray", "alpha-trait=Undefined", samples));

    Assert.Multiple(() => {
      Assert.That(result.PixelData, Is.EqualTo(samples));
      Assert.That(MiffFile.ToRawImage(result).Format, Is.EqualTo(PixelFormat.Gray8));
    });
  }

  /// <summary>Run-length packets are as wide as the pixel, so they inherit the same fault.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_RunLengthWithAlphaAndNoType_ReadsFourSamplePackets() {
    var header = Encoding.ASCII.GetBytes(
      "id=ImageMagick version=1.0\n"
      + "class=DirectClass colors=0 alpha-trait=Blend\n"
      + "columns=3 rows=1 depth=8\n"
      + "colorspace=sRGB\ncompression=RLE  quality=0\n"
      + "\f\n:\x1a");

    // One pixel stated twice, then a different one: four samples and a count byte each.
    byte[] packets = [0x10, 0x20, 0x30, 0x40, 0x01, 0x50, 0x60, 0x70, 0x80, 0x00];
    var data = new byte[header.Length + packets.Length];
    header.CopyTo(data, 0);
    packets.CopyTo(data, header.Length);

    var result = MiffReader.FromBytes(data);

    Assert.That(result.PixelData, Is.EqualTo(new byte[] {
      0x10, 0x20, 0x30, 0x40,
      0x10, 0x20, 0x30, 0x40,
      0x50, 0x60, 0x70, 0x80,
    }));
  }

  /// <summary>Asks ImageMagick to write the file, then to read the very same file back.</summary>
  private static void _AgreesWithImageMagick(string createArguments, string name) {
    var directory = Directory.CreateTempSubdirectory("miffalpharead");
    try {
      var path = Path.Combine(directory.FullName, $"{name}.miff");
      var reference = Path.Combine(directory.FullName, $"{name}.rgba");

      using (var write = ExternalTool.StartOrIgnore("magick", $"{createArguments} \"{path}\"")) {
        var complaint = write.StandardError.ReadToEnd().Trim();
        write.WaitForExit();
        if (write.ExitCode != 0)
          Assert.Fail($"ImageMagick would not write the sample: {complaint}");
      }

      using (var read = ExternalTool.StartOrIgnore("magick", $"\"{path}\" -depth 8 RGBA:\"{reference}\"")) {
        var complaint = read.StandardError.ReadToEnd().Trim();
        read.WaitForExit();
        if (read.ExitCode != 0)
          Assert.Fail($"ImageMagick would not read its own sample: {complaint}");
      }

      var ours = MiffFile.ToRawImage(MiffReader.FromFile(new(path)));
      if (ours.Format != PixelFormat.Rgba32)
        ours = PixelConverter.Convert(ours, PixelFormat.Rgba32);

      Assert.That(ours.PixelData, Is.EqualTo(File.ReadAllBytes(reference)));
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  /// <summary>The file from the report: 748 of 2257 pixels differed before this.</summary>
  [Test]
  [Category("Conformance")]
  public void ImageMagicksOwnAlphaFile_ReadsTheSameAsImageMagickReadsIt()
    => _AgreesWithImageMagick("-size 61x37 gradient:blue-yellow -alpha set", "alpha");

  /// <summary>An alpha channel that varies, so a sheared read cannot look right by accident.</summary>
  [Test]
  [Category("Conformance")]
  public void ImageMagicksOwnVaryingAlphaFile_ReadsTheSameAsImageMagickReadsIt()
    => _AgreesWithImageMagick(
      "-size 61x37 gradient:blue-yellow ( -size 61x37 gradient:black-white ) -alpha off -compose CopyOpacity -composite",
      "varying");

  /// <summary>1192 of 2257 pixels differed before this.</summary>
  [Test]
  [Category("Conformance")]
  public void ImageMagicksOwnGreyAlphaFile_ReadsTheSameAsImageMagickReadsIt()
    => _AgreesWithImageMagick("-size 61x37 gradient:blue-yellow -colorspace Gray -alpha set", "greyalpha");

  [Test]
  [Category("Conformance")]
  public void ImageMagicksOwnRunLengthAlphaFile_ReadsTheSameAsImageMagickReadsIt()
    => _AgreesWithImageMagick("-size 61x37 gradient:blue-yellow -alpha set -compress RLE", "rlealpha");

  /// <summary>The files that already agreed still do, at every depth and both compressions.</summary>
  /// <remarks>
  /// The palette case is stated at eight bits because that is the one that agreed. A PseudoClass
  /// file at the default depth of sixteen has sixteen-bit indices and a sixteen-bit colormap, and
  /// neither is narrowed on the way out — it differs from ImageMagick's reading in 822 of the 2257
  /// pixels, measured on this branch's parent and unchanged by anything here. That is a fault of the
  /// palette path rather than of the channel count, and it is left where it was found.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  [TestCase("-size 61x37 gradient:blue-yellow", "truecolour")]
  [TestCase("-size 61x37 gradient:blue-yellow -depth 8", "truecolour8")]
  [TestCase("-size 61x37 gradient:blue-yellow -colorspace Gray", "grey")]
  [TestCase("-size 61x37 gradient:blue-yellow -compress RLE", "rle")]
  [TestCase("-size 61x37 gradient:blue-yellow -colors 16 -depth 8", "palette")]
  public void ImageMagicksOtherFiles_StillReadTheSame(string createArguments, string name)
    => _AgreesWithImageMagick(createArguments, name);

  /// <summary>
  /// A palette file with an alpha channel is refused by name rather than drawn wrongly.
  /// </summary>
  /// <remarks>
  /// ImageMagick writes one for <c>-colors 16 -alpha set</c>: <c>class=PseudoClass</c> with
  /// <c>alpha-trait=Blend</c>, which is an index and an alpha sample per pixel, and its
  /// <c>number-channels</c> says 5. Reading the channel count correctly is what makes this case
  /// visible at all; the palette path behind it hands out <see cref="PixelFormat.Indexed8"/>, which
  /// has nowhere to put the alpha sample and would take every second byte for an index. Refusing is
  /// the honest answer until the palette path carries alpha.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void ToRawImage_PaletteWithAlpha_IsRefusedRatherThanDrawn() {
    var header = Encoding.ASCII.GetBytes(
      "id=ImageMagick version=1.0\n"
      + "class=PseudoClass colors=2 alpha-trait=Blend\n"
      + "columns=2 rows=1 depth=8\n"
      + "colorspace=sRGB\n"
      + "\f\n:\x1a");

    byte[] payload = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x80, 0x01, 0x40];
    var data = new byte[header.Length + payload.Length];
    header.CopyTo(data, 0);
    payload.CopyTo(data, header.Length);

    var file = MiffReader.FromBytes(data);
    Assert.That(() => MiffFile.ToRawImage(file), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("alpha"));
  }
}
