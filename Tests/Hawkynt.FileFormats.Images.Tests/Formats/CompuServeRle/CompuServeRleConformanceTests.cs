using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.CompuServeRle.Tests;

[TestFixture]
public sealed class CompuServeRleConformanceTests {

  private const byte _Escape = 0x1B;

  [Test]
  [Category("Unit")]
  public void Reader_MediumVector_DecodesAlternatingRunsAcrossScanlines() {
    // 127 black, 3 white, then black to the end of the 128x96 screen.
    var data = new List<byte> { _Escape, (byte)'G', (byte)'M', 0x7E, 0x20, 0x41, 0x23 };
    _AppendSameBackgroundRun(data, CompuServeRleFile.MediumWidth * CompuServeRleFile.MediumHeight - 130);
    data.AddRange([_Escape, (byte)'G', (byte)'N']);

    var file = CompuServeRleReader.FromBytes([.. data]);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(128));
      Assert.That(file.Height, Is.EqualTo(96));
      Assert.That(file.RasterData[15], Is.EqualTo(0x01), "pixel 127 is the final pixel of row zero");
      Assert.That(file.RasterData[16], Is.EqualTo(0xC0), "pixels 128 and 129 continue at the next scanline");
      Assert.That(file.RasterData[17..], Is.All.EqualTo(0));
    });
  }

  [TestCase('M', CompuServeRleFile.MediumWidth, CompuServeRleFile.MediumHeight)]
  [TestCase('H', CompuServeRleFile.HighWidth, CompuServeRleFile.HighHeight)]
  [Category("Unit")]
  public void Reader_ModeHeader_SelectsFixedGeometry(char mode, int width, int height) {
    var data = _AllBlackStream((byte)mode, width * height);
    var file = CompuServeRleReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.RasterData, Is.All.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_ControlCharacters_DoNotConsumePixelsOrToggleRunColour() {
    var data = _AllBlackStream((byte)'M', CompuServeRleFile.MediumWidth * CompuServeRleFile.MediumHeight).ToList();
    data.InsertRange(3, [0x0D, 0x0A, 0x07, 0x09]);
    data.InsertRange(data.Count - 3, [0x0D, 0x0A]);

    var file = CompuServeRleReader.FromBytes([.. data]);
    Assert.That(file.RasterData, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Reader_ZeroLengthRun_TogglesToForegroundWithoutConsumingPixels() {
    var total = CompuServeRleFile.MediumWidth * CompuServeRleFile.MediumHeight;
    var data = new List<byte> { _Escape, (byte)'G', (byte)'M', 0x20, 0x21 };
    _AppendSameBackgroundRun(data, total - 1);
    data.AddRange([_Escape, (byte)'G', (byte)'N']);

    var file = CompuServeRleReader.FromBytes([.. data]);
    Assert.Multiple(() => {
      Assert.That(file.RasterData[0], Is.EqualTo(0x80));
      Assert.That(file.RasterData[1..], Is.All.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_DelRunByte_DecodesLegacy95PixelCount() {
    var total = CompuServeRleFile.MediumWidth * CompuServeRleFile.MediumHeight;
    var data = new List<byte> { _Escape, (byte)'G', (byte)'M', 0x7F, 0x20 };
    _AppendSameBackgroundRun(data, total - 95);
    data.AddRange([_Escape, (byte)'G', (byte)'N']);

    var file = CompuServeRleReader.FromBytes([.. data]);
    Assert.That(file.RasterData, Is.All.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Writer_AllBlackMedium_UsesPrintable94PixelChunksAndZeroForegroundRuns() {
    var file = new CompuServeRleFile {
      Width = CompuServeRleFile.MediumWidth,
      Height = CompuServeRleFile.MediumHeight,
      RasterData = new byte[CompuServeRleFile.MediumWidth / 8 * CompuServeRleFile.MediumHeight],
    };

    var expected = _AllBlackStream((byte)'M', CompuServeRleFile.MediumWidth * CompuServeRleFile.MediumHeight);
    Assert.That(CompuServeRleWriter.ToBytes(file), Is.EqualTo(expected));
  }

  [TestCase(CompuServeRleFile.MediumWidth, CompuServeRleFile.MediumHeight)]
  [TestCase(CompuServeRleFile.HighWidth, CompuServeRleFile.HighHeight)]
  [Category("Unit")]
  public void WriterReader_RoundTripsBothStandardModes(int width, int height) {
    var raster = new byte[width / 8 * height];
    for (var i = 0; i < raster.Length; ++i)
      raster[i] = (byte)(i * 73 + 19);

    var source = new CompuServeRleFile { Width = width, Height = height, RasterData = raster };
    var decoded = CompuServeRleReader.FromBytes(CompuServeRleWriter.ToBytes(source));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.RasterData, Is.EqualTo(raster));
    });
  }

  [Test]
  [Category("Unit")]
  public void RawImageConversion_UsesForegroundWhiteAndRequiresNativeGeometry() {
    var pixels = new byte[CompuServeRleFile.MediumWidth * CompuServeRleFile.MediumHeight * 3];
    pixels[0] = pixels[1] = pixels[2] = 255;
    var raw = new RawImage {
      Width = CompuServeRleFile.MediumWidth,
      Height = CompuServeRleFile.MediumHeight,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };

    var file = CompuServeRleFile.FromRawImage(raw);
    var decoded = CompuServeRleFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.RasterData[0], Is.EqualTo(0x80));
      Assert.That(decoded.Palette, Is.EqualTo(new byte[] { 0, 0, 0, 255, 255, 255 }));
      Assert.That(decoded.PixelData[0], Is.EqualTo(1));
      Assert.That(decoded.PixelData[1], Is.EqualTo(0));
      Assert.Throws<ArgumentOutOfRangeException>(() => CompuServeRleFile.FromRawImage(new RawImage {
        Width = 64,
        Height = 64,
        Format = PixelFormat.Rgb24,
        PixelData = new byte[64 * 64 * 3],
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Registry_DetectsCompuServeMagicDespiteSharedRleExtension() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.DetectFromBytes([_Escape, (byte)'G', (byte)'H']), Is.EqualTo(ImageFormat.CompuServeRle));
      Assert.That(FormatRegistry.DetectFromBytes([_Escape, (byte)'G', (byte)'M']), Is.EqualTo(ImageFormat.CompuServeRle));
      Assert.That(FormatRegistry.DetectCandidatesFromExtension(".rle"), Does.Contain(ImageFormat.CompuServeRle));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_InvalidHeader_IsRejected() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => CompuServeRleReader.FromBytes([0x1B, 0x47, 0x58, 0, 0, 0]));
      Assert.Throws<InvalidDataException>(() => CompuServeRleReader.FromBytes([0x47, 0x48, 0x20, 0, 0, 0]));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_UnderfilledScreen_IsRejected() {
    Assert.Throws<InvalidDataException>(() => CompuServeRleReader.FromBytes([
      _Escape, (byte)'G', (byte)'M', 0x21, 0x21, _Escape, (byte)'G', (byte)'N',
    ]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_RunPastScreen_IsRejected() {
    var data = _AllBlackStream((byte)'M', CompuServeRleFile.MediumWidth * CompuServeRleFile.MediumHeight).ToList();
    data.Insert(data.Count - 3, 0x21);
    Assert.Throws<InvalidDataException>(() => CompuServeRleReader.FromBytes([.. data]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_MissingOrMalformedTerminator_IsRejected() {
    var valid = _AllBlackStream((byte)'M', CompuServeRleFile.MediumWidth * CompuServeRleFile.MediumHeight);
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => CompuServeRleReader.FromBytes(valid[..^3]));
      Assert.Throws<InvalidDataException>(() => CompuServeRleReader.FromBytes([.. valid[..^1], (byte)'X']));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_NonSevenBitRunAndPrintableTrailingPayload_AreRejected() {
    var valid = _AllBlackStream((byte)'M', CompuServeRleFile.MediumWidth * CompuServeRleFile.MediumHeight);
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => CompuServeRleReader.FromBytes([
        _Escape, (byte)'G', (byte)'M', 0x80, _Escape, (byte)'G', (byte)'N',
      ]));
      Assert.Throws<InvalidDataException>(() => CompuServeRleReader.FromBytes([.. valid, (byte)'X']));
      Assert.DoesNotThrow(() => CompuServeRleReader.FromBytes([.. valid, 0x0D, 0x0A]));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_RejectsInvalidGeometryAndRasterLength() {
    Assert.Multiple(() => {
      Assert.Throws<ArgumentOutOfRangeException>(() => CompuServeRleWriter.ToBytes(new() {
        Width = 64,
        Height = 64,
        RasterData = new byte[512],
      }));
      Assert.Throws<ArgumentException>(() => CompuServeRleWriter.ToBytes(new() {
        Width = CompuServeRleFile.MediumWidth,
        Height = CompuServeRleFile.MediumHeight,
        RasterData = new byte[1],
      }));
    });
  }

  private static byte[] _AllBlackStream(byte mode, int pixelCount) {
    var result = new List<byte> { _Escape, (byte)'G', mode };
    _AppendSameBackgroundRun(result, pixelCount);
    result.AddRange([_Escape, (byte)'G', (byte)'N']);
    return [.. result];
  }

  private static void _AppendSameBackgroundRun(List<byte> output, int count) {
    while (count > 94) {
      output.Add(0x7E);
      output.Add(0x20);
      count -= 94;
    }

    output.Add((byte)(0x20 + count));
    output.Add(0x20);
  }
}
