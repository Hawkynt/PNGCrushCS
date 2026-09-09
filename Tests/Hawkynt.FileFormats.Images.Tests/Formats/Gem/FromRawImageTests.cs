using System;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Gem.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_StandardPaletteColours_ReproducesEveryPixel() {
    var source = _Image(4, 2,
      255, 255, 255,  0, 0, 0,        255, 0, 0,      0, 255, 0,
      0, 0, 255,        0, 255, 255,  255, 255, 0,    255, 0, 255);

    var decoded = GemFile.ToRawImage(GemReader.FromBytes(GemWriter.ToBytes(GemFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((4, 2)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_CoalescesHorizontalRunsIntoSingleBars() {
    var source = _Image(6, 1,
      0, 0, 0,  0, 0, 0,  0, 0, 0,
      255, 255, 255,  255, 255, 255,  255, 0, 0);

    var file = GemFile.FromRawImage(source);
    var bars = file.Records.Where(record => record is { Opcode: GemOpcode.GeneralisedPrimitive, SubOpcode: GemPrimitive.Bar }).ToArray();

    Assert.Multiple(() => {
      Assert.That(bars, Has.Length.EqualTo(3));
      Assert.That(bars[0].Points, Is.EqualTo(new short[] { 0, 0, 3, 1 }));
      Assert.That(bars[1].Points, Is.EqualTo(new short[] { 3, 0, 5, 1 }));
      Assert.That(bars[2].Points, Is.EqualTo(new short[] { 5, 0, 6, 1 }));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ColourOutsideTheWorkstationPalette_UsesItsNearestPen() {
    var source = _Image(1, 1, 250, 8, 6);

    var decoded = GemFile.ToRawImage(GemReader.FromBytes(GemWriter.ToBytes(GemFile.FromRawImage(source))));
    var rgb = PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData;

    Assert.That(rgb, Is.EqualTo(new byte[] { 255, 0, 0 }));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_UsesRasterCoordinatesAndThePicturesOwnExtent() {
    var file = GemFile.FromRawImage(_Image(13, 7, new byte[13 * 7 * 3]));

    Assert.Multiple(() => {
      Assert.That(file.Version, Is.EqualTo(101));
      Assert.That(file.CoordinateFlag, Is.EqualTo(GemFile.RasterCoordinates));
      Assert.That(file.Extent, Is.EqualTo((0, 0, 13, 7)));
      Assert.That(file.PageSize, Is.EqualTo((0, 0)));
      Assert.That(file.Window, Is.EqualTo((0, 7, 13, 0)), "the lower-left precedes the upper-right in an upper-left-origin raster");
      Assert.That(file.HasBitImage, Is.False, "the picture is self-contained VDI geometry, not a sidecar IMG reference");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_AZeroOrUnrepresentableSize_IsRefused() {
    var empty = new RawImage { Width = 0, Height = 1, Format = PixelFormat.Rgb24, PixelData = [] };
    var tooWide = new RawImage { Width = 32768, Height = 1, Format = PixelFormat.Rgb24, PixelData = new byte[32768 * 3] };

    Assert.Multiple(() => {
      Assert.Throws<ArgumentOutOfRangeException>(() => GemFile.FromRawImage(empty));
      var failure = Assert.Throws<ArgumentOutOfRangeException>(() => GemFile.FromRawImage(tooWide));
      Assert.That(failure!.Message, Does.Contain("sixteen-bit"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesTheHeaderFlagRecordsAndTerminator() {
    var source = new GemFile {
      Version = 310,
      CoordinateFlag = GemFile.RasterCoordinates,
      Extent = (-7, -5, 11, 13),
      PageSize = (2100, 2970),
      Window = (-100, 200, 300, -400),
      HasBitImage = true,
      Records = [new(GemOpcode.SetFillColour, 0, [], [3])]
    };

    var bytes = GemWriter.ToBytes(source);
    var decoded = GemReader.FromBytes(bytes);
    var record = decoded.Records.Single();

    Assert.Multiple(() => {
      Assert.That(bytes[0], Is.EqualTo(0xFF));
      Assert.That(bytes[1], Is.EqualTo(0xFF));
      Assert.That(bytes[2], Is.EqualTo(GemFile.StandardHeaderWords));
      Assert.That(bytes[^2], Is.EqualTo(0xFF));
      Assert.That(bytes[^1], Is.EqualTo(0xFF));
      Assert.That(decoded.Version, Is.EqualTo(310));
      Assert.That(decoded.Extent, Is.EqualTo((-7, -5, 11, 13)));
      Assert.That(decoded.PageSize, Is.EqualTo((2100, 2970)));
      Assert.That(decoded.Window, Is.EqualTo((-100, 200, 300, -400)));
      Assert.That(decoded.HasBitImage, Is.True);
      Assert.That((record.Opcode, record.SubOpcode), Is.EqualTo((GemOpcode.SetFillColour, 0)));
      Assert.That(record.Points, Is.Empty);
      Assert.That(record.Integers, Is.EqualTo(new short[] { 3 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_RecordWithHalfAPoint_IsRefused() {
    var file = new GemFile {
      Records = [new(GemOpcode.PolyLine, 0, [1], [])]
    };

    var failure = Assert.Throws<ArgumentException>(() => GemWriter.ToBytes(file));
    Assert.That(failure!.Message, Does.Contain("x/y pairs"));
  }

  private static RawImage _Image(int width, int height, params byte[] rgb)
    => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = rgb };
}
