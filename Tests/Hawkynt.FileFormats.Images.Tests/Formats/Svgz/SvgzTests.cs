using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileFormat.Core;
using FileFormat.Svg;
using FileFormat.Svgz;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Svgz.Tests;

/// <summary>
/// The gzipped SVG, which is the same drawing under a wrapper and nothing else.
/// </summary>
/// <remarks>
/// The whole of the format is the compression: a <c>.svgz</c> is a <c>.svg</c> that has been
/// gzipped, so every question about geometry, paint or size is the SVG reader's and the only ones
/// left here are whether the wrapper comes off, whether a file that is not one is refused, and
/// whether the picture that comes out is the one the uncompressed drawing gives.
/// </remarks>
[TestFixture]
public sealed class SvgzTests {

  private const string _Drawing =
    "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"61\" height=\"37\">" +
    "<rect x=\"0\" y=\"0\" width=\"30\" height=\"20\" fill=\"#0000ff\"/></svg>";

  private static byte[] _Gzip(string text) {
    using var memory = new MemoryStream();
    using (var gzip = new GZipStream(memory, CompressionLevel.SmallestSize, leaveOpen: true))
      gzip.Write(Encoding.UTF8.GetBytes(text));

    return memory.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_NullData_Throws()
    => Assert.Throws<ArgumentNullException>(() => SvgzReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_NotGzipped_Throws()
    => Assert.Throws<InvalidDataException>(() => SvgzReader.FromBytes(Encoding.UTF8.GetBytes(_Drawing)));

  /// <summary>Gzip alone does not make a drawing; what is inside still has to be one.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_GzippedSomethingElse_Throws()
    => Assert.Throws<InvalidDataException>(() => SvgzReader.FromBytes(_Gzip("<html><body/></html>")));

  [Test]
  [Category("Unit")]
  public void FromBytes_TruncatedGzip_Throws() {
    var whole = _Gzip(_Drawing);

    Assert.Throws<InvalidDataException>(() => SvgzReader.FromBytes(whole[..(whole.Length / 2)]));
  }

  /// <summary>The wrapper is the only difference, so the picture has to be the same one.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_MatchesThePlainDrawingPixelForPixel() {
    var plain = SvgFile.ToRawImage(SvgReader.FromBytes(Encoding.UTF8.GetBytes(_Drawing)));
    var zipped = SvgzFile.ToRawImage(SvgzReader.FromBytes(_Gzip(_Drawing)));

    Assert.Multiple(() => {
      Assert.That((zipped.Width, zipped.Height), Is.EqualTo((plain.Width, plain.Height)));
      Assert.That(zipped.Format, Is.EqualTo(plain.Format));
      Assert.That(zipped.PixelData, Is.EqualTo(plain.PixelData));
    });
  }

  /// <summary>
  /// A picture goes out as an embedded raster, so it must come back as the pixels it went in as.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void RoundTrip_KeepsEveryPixel() {
    var source = new RawImage {
      Width = 61,
      Height = 37,
      Format = PixelFormat.Rgba32,
      PixelData = new byte[61 * 37 * 4],
    };
    for (var y = 0; y < 37; ++y)
    for (var x = 0; x < 61; ++x) {
      var at = (y * 61 + x) * 4;
      source.PixelData[at] = (byte)(x * 4);
      source.PixelData[at + 1] = (byte)(y * 6);
      source.PixelData[at + 2] = (byte)(x + y);
      source.PixelData[at + 3] = 255;
    }

    var bytes = SvgzWriter.ToBytes(SvgzFile.FromRawImage(source));
    var read = SvgzFile.ToRawImage(SvgzReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0x1F), "gzip");
      Assert.That(bytes[1], Is.EqualTo(0x8B), "gzip");
      Assert.That((read.Width, read.Height), Is.EqualTo((61, 37)));
      Assert.That(read.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Registry_KnowsTheExtension()
    => Assert.That(FormatRegistry.DetectFromExtension(".svgz"), Is.EqualTo(ImageFormat.Svgz));

  /// <summary>
  /// Gzip's own two bytes say nothing about what is inside, so the header has to be opened far
  /// enough to see the drawing — otherwise every gzipped file on the disk would answer to this.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Registry_DetectsItFromTheBytes() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromBytes(_Gzip(_Drawing)), Is.EqualTo(ImageFormat.Svgz));
      Assert.That(FormatRegistry.DetectFromBytes(_Gzip("<html><body/></html>")), Is.Not.EqualTo(ImageFormat.Svgz));
    });
  }
}
