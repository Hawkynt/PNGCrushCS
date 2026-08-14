using System;
using System.IO;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.Miff;
using Hawkynt.FileFormats.Images.Tests;

namespace FileFormat.Miff.Tests;

/// <summary>
/// A run-length packet is the pixel followed by a plain count byte, and the byte is always there.
/// </summary>
/// <remarks>
/// Taken from a 61x37 picture written by <c>magick src.png -compress RLE rle.miff</c>. Its samples
/// open with <c>00 00 00 00 ff ff 3c</c>: a sixteen-bit blue pixel and then 0x3C, which is the first
/// row's sixty-one pixels stated as sixty. The count is <c>byte + 1</c> and it follows every packet,
/// so a reader that only takes a count when bit seven is set reads 0x3C as the next pixel's red
/// channel and every pixel after the first is wrong — measured as 828 of 2257 pixels differing from
/// what ImageMagick makes of its own file.
/// </remarks>
[TestFixture]
public sealed class MiffRunLengthPacketTests {

  private static byte[] _BuildRleMiff(int width, int height, int depth, byte[] packets) {
    var header = Encoding.ASCII.GetBytes(
      "id=ImageMagick version=1.0\n"
      + "class=DirectClass colors=0 alpha-trait=Undefined\n"
      + $"columns={width} rows={height} depth={depth}\n"
      + "type=TrueColor\ncolorspace=sRGB\ncompression=RLE  quality=0\n"
      + "\f\n:\x1a");

    var data = new byte[header.Length + packets.Length];
    header.CopyTo(data, 0);
    packets.CopyTo(data, header.Length);
    return data;
  }

  /// <summary>The count byte is read even when its top bit is clear.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_CountByteBelow128_IsACount() {
    // Two rows of sixty-one, exactly as the reference file states them.
    byte[] packets = [0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x3C, 0x07, 0x1C, 0x07, 0x1C, 0xF8, 0xE3, 0x3C];
    var result = MiffReader.FromBytes(_BuildRleMiff(61, 2, 16, packets));

    Assert.Multiple(() => {
      Assert.That(result.PixelData, Has.Length.EqualTo(61 * 2 * 6));
      Assert.That(result.PixelData[..6], Is.EqualTo(new byte[] { 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF }));
      Assert.That(result.PixelData[360..366], Is.EqualTo(new byte[] { 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF }), "the sixty-first pixel is still blue");
      Assert.That(result.PixelData[366..372], Is.EqualTo(new byte[] { 0x07, 0x1C, 0x07, 0x1C, 0xF8, 0xE3 }), "the second row starts after sixty-one blue pixels");
    });
  }

  /// <summary>A count byte of zero means one pixel, not none.</summary>
  [Test]
  [Category("Unit")]
  public void Decompress_ZeroCount_YieldsOnePixel() {
    byte[] packets = [0x10, 0x20, 0x30, 0x00, 0x40, 0x50, 0x60, 0x00];
    var pixels = MiffRleCompressor.Decompress(packets, 3, 2);

    Assert.That(pixels, Is.EqualTo(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60 }));
  }

  /// <summary>The longest run a single packet states is 256 pixels.</summary>
  [Test]
  [Category("Unit")]
  public void Decompress_MaximumCount_Yields256Pixels() {
    byte[] packets = [0x01, 0x02, 0x03, 0xFF];
    var pixels = MiffRleCompressor.Decompress(packets, 3, 256);

    Assert.Multiple(() => {
      Assert.That(pixels, Has.Length.EqualTo(256 * 3));
      Assert.That(pixels[765..], Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));
    });
  }

  /// <summary>What we write is read back the same way ImageMagick's own packets are.</summary>
  [Test]
  [Category("Unit")]
  public void Compress_EmitsACountByteAfterEveryPacket() {
    byte[] pixels = [0x11, 0x22, 0x33, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66];
    var packed = MiffRleCompressor.Compress(pixels, 3);

    Assert.That(packed, Is.EqualTo(new byte[] { 0x11, 0x22, 0x33, 0x01, 0x44, 0x55, 0x66, 0x00 }));
  }

  /// <summary>ImageMagick reads the packets we write, which is the only proof they are its packets.</summary>
  [Test]
  [Category("Conformance")]
  public void SomethingElseReadsWhatWeWrite() {
    const int WIDTH = 37;
    const int HEIGHT = 11;

    // Long runs, so most packets carry a count, and a stretch of singles so the zero counts appear.
    var pixels = new byte[WIDTH * HEIGHT * 3];
    for (var y = 0; y < HEIGHT; ++y)
    for (var x = 0; x < WIDTH; ++x) {
      var at = (y * WIDTH + x) * 3;
      pixels[at] = (byte)(x < WIDTH / 2 ? y * 20 : x * 7);
      pixels[at + 1] = (byte)(x < WIDTH / 2 ? 0x80 : x * 3 + y);
      pixels[at + 2] = (byte)(x < WIDTH / 2 ? 0xFF : 255 - x * 5);
    }

    var bytes = MiffWriter.ToBytes(new MiffFile {
      Width = WIDTH, Height = HEIGHT, Depth = 8,
      ColorClass = MiffColorClass.DirectClass, Compression = MiffCompression.Rle,
      Colorspace = "sRGB", Type = "TrueColor", PixelData = pixels,
    });

    var directory = Directory.CreateTempSubdirectory("miffrle");
    try {
      var path = Path.Combine(directory.FullName, "sample.miff");
      var readBack = Path.Combine(directory.FullName, "sample.ppm");
      File.WriteAllBytes(path, bytes);

      using var magick = ExternalTool.StartOrIgnore("magick", $"\"{path}\" -depth 8 \"{readBack}\"");
      var complaint = magick.StandardError.ReadToEnd().Trim();
      magick.WaitForExit();

      if (magick.ExitCode != 0)
        Assert.Fail($"ImageMagick refused the run-length MIFF we wrote: {complaint}");

      var written = File.ReadAllBytes(readBack);
      var header = Encoding.ASCII.GetBytes($"P6\n{WIDTH} {HEIGHT}\n255\n");
      Assert.That(written.Skip(header.Length), Is.EqualTo(pixels), "ImageMagick read our packets as different pixels");
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  /// <summary>A run longer than 256 pixels is stated as more than one packet.</summary>
  [Test]
  [Category("Unit")]
  public void Compress_RunLongerThan256_SplitsIntoPackets() {
    var pixels = new byte[300 * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = 0xAB;
      pixels[i + 1] = 0xCD;
      pixels[i + 2] = 0xEF;
    }

    var packed = MiffRleCompressor.Compress(pixels, 3);
    var unpacked = MiffRleCompressor.Decompress(packed, 3, 300);

    Assert.Multiple(() => {
      Assert.That(packed, Is.EqualTo(new byte[] { 0xAB, 0xCD, 0xEF, 0xFF, 0xAB, 0xCD, 0xEF, 0x2B }));
      Assert.That(unpacked, Is.EqualTo(pixels));
    });
  }
}
