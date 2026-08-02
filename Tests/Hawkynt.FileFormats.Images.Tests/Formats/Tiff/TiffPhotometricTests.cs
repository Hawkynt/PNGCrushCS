using System;
using System.Buffers.Binary;
using FileFormat.Core;

namespace FileFormat.Tiff.Tests;

/// <summary>
/// Honouring the tag that says which end of the scale is white.
/// </summary>
/// <remarks>
/// The tag was read and then ignored: both photometric kinds fold into one colour mode, and nothing
/// afterwards asked which it had been. A min-is-white picture stores nought for white, the opposite
/// of everything downstream, so every one of them came back as its own negative — and that is how
/// every fax is stored, along with a great many scans.
/// <para/>
/// Checked against ImageMagick on a real Group 3 fax: all its pixels now match, where before not one
/// of them did.
/// </remarks>
[TestFixture]
public sealed class TiffPhotometricTests {

  /// <summary>Builds a little-endian one-strip greyscale TIFF with the given photometric.</summary>
  private static byte[] _Grey(int photometric, byte[] samples, int width, int height) {
    const int entries = 8;
    var ifd = 8;
    var dataAt = ifd + 2 + entries * 12 + 4;
    var file = new byte[dataAt + samples.Length];

    file[0] = (byte)'I'; file[1] = (byte)'I';
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 42);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)ifd);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(ifd), entries);

    var at = ifd + 2;
    void Entry(ushort tag, ushort type, uint count, uint value) {
      BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at), tag);
      BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 2), type);
      BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), count);
      BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 8), value);
      at += 12;
    }

    Entry(256, 3, 1, (uint)width);              // width
    Entry(257, 3, 1, (uint)height);             // height
    Entry(258, 3, 1, 8);                        // bits per sample
    Entry(259, 3, 1, 1);                        // no compression
    Entry(262, 3, 1, (uint)photometric);        // photometric
    Entry(273, 4, 1, (uint)dataAt);             // strip offset
    Entry(277, 3, 1, 1);                        // samples per pixel
    Entry(279, 4, 1, (uint)samples.Length);     // strip byte count

    samples.CopyTo(file, dataAt);
    return file;
  }

  [Test]
  [Category("Unit")]
  public void MinIsBlackKeepsItsSamples() {
    var image = TiffFile.ToRawImage(TiffReader.FromBytes(_Grey(1, [0, 255, 128, 64], 4, 1)));
    var rgb = image.ToRgb24();

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.Zero, "nought is black here");
      Assert.That(rgb[3], Is.EqualTo(255));
    });
  }

  [Test]
  [Category("Unit")]
  public void MinIsWhiteTurnsItsSamplesRoundTheOtherWay() {
    var image = TiffFile.ToRawImage(TiffReader.FromBytes(_Grey(0, [0, 255, 128, 64], 4, 1)));
    var rgb = image.ToRgb24();

    Assert.Multiple(() => {
      Assert.That(rgb[0], Is.EqualTo(255), "nought is white here, which is the whole of the tag");
      Assert.That(rgb[3], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheTwoKindsAreExactNegativesOfEachOther() {
    byte[] samples = [10, 60, 130, 200, 250, 3, 90, 170];
    var black = TiffFile.ToRawImage(TiffReader.FromBytes(_Grey(1, samples, 8, 1))).ToRgb24();
    var white = TiffFile.ToRawImage(TiffReader.FromBytes(_Grey(0, samples, 8, 1))).ToRgb24();

    Assert.Multiple(() => {
      for (var i = 0; i < black.Length; ++i)
        Assert.That(white[i], Is.EqualTo(255 - black[i]), $"sample {i}");
    });
  }
}
