using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.DaliCompressed;

namespace FileFormat.DaliCompressed.Tests;

[TestFixture]
public sealed class DaliCompressedTests {

  private static byte[] _Screen(Func<int, byte> fill) {
    var screen = new byte[32000];
    for (var i = 0; i < screen.Length; ++i)
      screen[i] = fill(i);

    return screen;
  }

  private static DaliCompressedFile _Sample(byte[] screen) => new() {
    Resolution = DaliResolution.Low,
    Palette = new byte[DaliCompressedFile.PaletteSize],
    ScreenData = screen,
  };

  [Test]
  [Category("Unit")]
  public void Compress_RoundTripsAUniformScreen() {
    var screen = _Screen(_ => 0);
    var (counts, values) = DaliCompressor.Compress(screen);

    Assert.That(DaliCompressor.Decompress(counts, values), Is.EqualTo(screen));
  }

  [Test]
  [Category("Unit")]
  public void Compress_RoundTripsAVariedScreen() {
    var screen = _Screen(i => (byte)(i * 31 % 256));
    var (counts, values) = DaliCompressor.Compress(screen);

    Assert.That(DaliCompressor.Decompress(counts, values), Is.EqualTo(screen));
  }

  [Test]
  [Category("Unit")]
  public void Compress_CollapsesAUniformScreenToRuns() {
    // 8000 four-byte groups, all identical, packed 255 at a time.
    var (counts, values) = DaliCompressor.Compress(_Screen(_ => 0));

    Assert.Multiple(() => {
      Assert.That(counts, Has.Length.EqualTo(values.Length / DaliCompressor.GroupSize));
      Assert.That(counts, Has.Length.LessThan(40), "a flat screen should need very few runs");
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesLengthsAsAsciiDecimalWithCrLf() {
    var bytes = DaliCompressedWriter.ToBytes(_Sample(_Screen(_ => 0)));
    var header = Encoding.ASCII.GetString(bytes, DaliCompressedFile.LengthsOffset, 24);

    Assert.That(header, Does.Match(@"^\d+\r\n\d+\r\n"));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesTheScreen() {
    var screen = _Screen(i => (byte)(i * 17 % 256));
    var restored = DaliCompressedReader.FromBytes(DaliCompressedWriter.ToBytes(_Sample(screen)));

    Assert.That(restored.ScreenData, Is.EqualTo(screen));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_RejectsAMissingLengthField()
    => Assert.Throws<InvalidDataException>(() => DaliCompressedReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromRawImage_ProducesAReadableFile() {
    var data = new byte[320 * 200 * 4];
    for (var i = 0; i < data.Length; i += 4) {
      data[i] = (byte)(i % 251);
      data[i + 1] = (byte)(i % 199);
      data[i + 3] = 255;
    }

    var raw = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgba32, PixelData = data };
    var bytes = DaliCompressedWriter.ToBytes(DaliCompressedFile.FromRawImage(raw));

    Assert.That(() => DaliCompressedReader.FromBytes(bytes), Throws.Nothing);
  }
}
