using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.DaliCompressed.Tests;

[TestFixture]
public sealed class DaliCompressedConformanceTests {

  private static byte[] _Screen(byte value = 0) {
    var screen = new byte[DaliCompressor.ScreenSize];
    Array.Fill(screen, value);
    return screen;
  }

  private static byte[] _File(byte[] counts, byte[] values, byte[]? palette = null) {
    using var ms = new MemoryStream();
    ms.Write(palette ?? new byte[DaliCompressedFile.PaletteSize]);
    ms.Write(Encoding.ASCII.GetBytes(counts.Length + "\r\n"));
    ms.Write(Encoding.ASCII.GetBytes(values.Length + "\r\n"));
    ms.Write(counts);
    ms.Write(values);
    return ms.ToArray();
  }

  private static int _ScreenOffsetForStoredGroup(int storedGroup) {
    var column = storedGroup / 200;
    var row = storedGroup % 200;
    return column * DaliCompressor.GroupSize + row * DaliCompressor.BytesPerRow;
  }

  [Test]
  [Category("Unit")]
  public void Decode_ZeroCountMeans256Groups() {
    // 31 x 256 + 64 = exactly 8,000 groups. The original uint8 decoder loads zero and then
    // decrements it after the first copy, which wraps to 255 remaining copies.
    var counts = new byte[32];
    counts[31] = 64;
    var values = new byte[counts.Length * DaliCompressor.GroupSize];
    for (var i = 0; i < 31; ++i)
      values[i * DaliCompressor.GroupSize] = 0xA5;
    values[31 * DaliCompressor.GroupSize] = 0x5A;

    var screen = DaliCompressor.Decompress(counts, values);

    Assert.Multiple(() => {
      Assert.That(screen[_ScreenOffsetForStoredGroup(0)], Is.EqualTo(0xA5));
      Assert.That(screen[_ScreenOffsetForStoredGroup(7935)], Is.EqualTo(0xA5));
      Assert.That(screen[_ScreenOffsetForStoredGroup(7936)], Is.EqualTo(0x5A));
      Assert.That(screen[_ScreenOffsetForStoredGroup(7999)], Is.EqualTo(0x5A));
    });
  }

  [Test]
  [Category("Unit")]
  public void Compress_UsesZeroForA256GroupRun() {
    var screen = _Screen();
    var (counts, values) = DaliCompressor.Compress(screen);

    Assert.Multiple(() => {
      Assert.That(counts[0], Is.Zero);
      Assert.That(counts, Has.Length.EqualTo(32));
      Assert.That(counts[^1], Is.EqualTo(64));
      Assert.That(values, Has.Length.EqualTo(counts.Length * DaliCompressor.GroupSize));
      Assert.That(DaliCompressor.Decompress(counts, values), Is.EqualTo(screen));
    });
  }

  [Test]
  [Category("Unit")]
  public void Decode_RejectsUnderExpansion()
    => Assert.Throws<InvalidDataException>(() => DaliCompressor.Decompress([1], new byte[4]));

  [Test]
  [Category("Unit")]
  public void Decode_RejectsExpansionBeyondTheScreen() {
    var counts = new byte[32];
    counts[^1] = 65;
    Assert.Throws<InvalidDataException>(() => DaliCompressor.Decompress(counts, new byte[counts.Length * 4]));
  }

  [Test]
  [Category("Unit")]
  public void Decode_RejectsMissingOrTrailingValues() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => DaliCompressor.Decompress([1], new byte[3]));
      Assert.Throws<InvalidDataException>(() => DaliCompressor.Decompress([1], new byte[8]));
    });
  }

  [Test]
  [Category("Unit")]
  public void Reader_RequiresOneFourByteValuePerCount()
    => Assert.Throws<InvalidDataException>(() => DaliCompressedReader.FromBytes(_File([1], new byte[8])));

  [Test]
  [Category("Unit")]
  public void Reader_RejectsTrailingPhysicalBytes() {
    var (counts, values) = DaliCompressor.Compress(_Screen());
    var valid = _File(counts, values);
    Assert.Throws<InvalidDataException>(() => DaliCompressedReader.FromBytes([.. valid, 0xEE]));
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsUnknownExtensionInsteadOfAssumingLow() {
    var (counts, values) = DaliCompressor.Compress(_Screen());
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dat");
    try {
      File.WriteAllBytes(path, _File(counts, values));
      Assert.Throws<ArgumentException>(() => DaliCompressedReader.FromFile(new FileInfo(path)));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Unit")]
  public void Reader_RejectsInvalidResolutionEnum() {
    var (counts, values) = DaliCompressor.Compress(_Screen());
    Assert.Throws<ArgumentOutOfRangeException>(() => DaliCompressedReader.FromBytes(_File(counts, values), (DaliResolution)99));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RejectsPaletteAndScreenPaddingOrClipping() {
    var valid = new DaliCompressedFile {
      Resolution = DaliResolution.Low,
      Palette = new byte[DaliCompressedFile.PaletteSize],
      ScreenData = _Screen(),
    };

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => DaliCompressedWriter.ToBytes(valid with { Palette = new byte[31] }));
      Assert.Throws<ArgumentException>(() => DaliCompressedWriter.ToBytes(valid with { ScreenData = new byte[DaliCompressor.ScreenSize - 1] }));
    });
  }

  [TestCase(".lpk", 320, 200, DaliResolution.Low)]
  [TestCase(".mpk", 640, 200, DaliResolution.Medium)]
  [TestCase(".hpk", 640, 400, DaliResolution.High)]
  [Category("Unit")]
  public void ExtensionAwareAuthoringUsesTheNamedMode(string extension, int width, int height, DaliResolution resolution) {
    var image = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[width * height * 3],
    };

    var file = DaliCompressedFile.FromRawImage(image, extension);
    Assert.Multiple(() => {
      Assert.That(file.Resolution, Is.EqualTo(resolution));
      Assert.That(file.Palette, Has.Length.EqualTo(DaliCompressedFile.PaletteSize));
      Assert.That(file.ScreenData, Has.Length.EqualTo(DaliCompressor.ScreenSize));
    });
  }

  [Test]
  [Category("Unit")]
  public void ExtensionAwareAuthoringRejectsMismatchedGeometry() {
    var image = new RawImage {
      Width = 320,
      Height = 200,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[320 * 200 * 3],
    };

    Assert.Throws<ArgumentException>(() => DaliCompressedFile.FromRawImage(image, ".hpk"));
  }

  [Test]
  [Category("Unit")]
  public void HighResolutionIgnoresStoredPaletteAndUsesMachineMonochrome() {
    var palette = new byte[DaliCompressedFile.PaletteSize];
    palette[0] = 0x07;
    palette[1] = 0x00;
    palette[2] = 0x00;
    palette[3] = 0x07;

    var image = DaliCompressedFile.ToRawImage(new DaliCompressedFile {
      Resolution = DaliResolution.High,
      Palette = palette,
      ScreenData = _Screen(),
    });

    Assert.That(image.Palette, Is.EqualTo(AtariStGraphics.MonochromePalette()));
  }
}
