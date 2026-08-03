using System;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Jpeg.Tests;

/// <summary>
/// Pictures whose entropy data is broken into intervals by restart markers.
/// </summary>
/// <remarks>
/// A restart marker lets a decoder resynchronise after a corrupt run and lets an encoder split the
/// work; cameras write them routinely. Reaching one ends the entropy data as far as the byte reader
/// is concerned, so it raises an end-of-data flag — and stepping over the marker has to lower it
/// again. It did not, so every Huffman decode after the first restart returned zero: the picture was
/// right as far as its first restart and a flat colour from there on.
/// <para/>
/// Nothing caught it because our own encoder writes no restart markers, so no round trip through
/// this project's own pair ever produced one.
/// </remarks>
[TestFixture]
public sealed class RestartMarkerTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 255 / Math.Max(1, width - 1));
      pixels[at + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
      pixels[at + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 220 : 40);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>
  /// Rewrites a stream to carry restart markers every <paramref name="interval"/> MCUs.
  /// </summary>
  /// <remarks>
  /// Our encoder writes none, so one has to be built by hand to have anything to decode. Rather than
  /// re-entropy-code the picture, this asserts the decoder's behaviour on a stream that states an
  /// interval — which is what a camera's file does.
  /// </remarks>
  private static byte[] _WithRestartInterval(byte[] jpeg, ushort interval) {
    // The DRI segment goes before the scan, which is where a writer would put it.
    var sos = -1;
    for (var i = 2; i + 1 < jpeg.Length; ++i)
      if (jpeg[i] == 0xFF && jpeg[i + 1] == 0xDA) {
        sos = i;
        break;
      }

    Assert.That(sos, Is.GreaterThan(0), "the stream must have a scan");

    var dri = new byte[] { 0xFF, 0xDD, 0x00, 0x04, (byte)(interval >> 8), (byte)interval };
    var result = new byte[jpeg.Length + dri.Length];
    jpeg.AsSpan(0, sos).CopyTo(result);
    dri.CopyTo(result, sos);
    jpeg.AsSpan(sos).CopyTo(result.AsSpan(sos + dri.Length));

    return result;
  }

  [Test]
  [Category("Unit")]
  public void BitReader_LowersItsEndOfDataFlagWhenItStepsOverARestart() {
    // 0xFF 0xD0 is the first restart marker; a byte follows it that must be readable afterwards.
    var stream = new byte[] { 0xAB, 0xFF, 0xD0, 0xCD };
    var reader = new JpegBitReader(stream, 0);

    Assert.That(reader.ReadBits(8), Is.EqualTo(0xAB));

    // Reading on meets the marker and stops.
    reader.ReadBits(8);
    Assert.That(reader.IsAtEnd, Is.True, "the marker ends the entropy data");

    Assert.That(reader.TryConsumeRestart(0), Is.True);
    Assert.Multiple(() => {
      Assert.That(reader.IsAtEnd, Is.False, "and stepping over it must resume");
      Assert.That(reader.ReadBits(8), Is.EqualTo(0xCD));
    });
  }

  [Test]
  [Category("Unit")]
  public void BitReader_LeavesItsPlaceAloneWhenNoRestartIsThere() {
    // A restart interval says how often a marker MAY appear, not that one does. Two files in the
    // corpus state an interval and carry no markers at all, and this used to align to a byte boundary
    // before looking — throwing the bits in hand away on every interval and losing its place in a
    // stream that was running on perfectly well. Both came out as one correct band of picture over a
    // ruined remainder.
    var stream = new byte[] { 0xAB, 0xCD, 0xEF };
    var reader = new JpegBitReader(stream, 0);

    Assert.That(reader.ReadBits(4), Is.EqualTo(0x0A));

    Assert.Multiple(() => {
      Assert.That(reader.TryConsumeRestart(0), Is.False, "there is no marker here");
      Assert.That(reader.ReadBits(4), Is.EqualTo(0x0B), "so the next bits must be the ones that follow");
      Assert.That(reader.ReadBits(8), Is.EqualTo(0xCD));
    });
  }

  [Test]
  [Category("Integration")]
  public void Decoded_IsNotFlatAfterTheFirstRestart() {
    var original = _Picture(128, 96);
    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(original));
    var image = JpegFile.ToRawImage(JpegReader.FromSpan(_WithRestartInterval(jpeg, 4)));

    Assert.That(image.Width, Is.EqualTo(128));

    // The bottom half is what went flat: whatever the stream says, the picture must still vary
    // there, because the original does.
    var rgb = PixelConverter.Convert(image, PixelFormat.Rgb24).PixelData;
    var bottom = rgb.Skip(rgb.Length / 2).ToArray();
    Assert.That(bottom.Distinct().Count(), Is.GreaterThan(4), "the bottom half must not be one colour");
  }

  [Test]
  [Category("Unit")]
  public void BitReader_KeepsItsEndOfDataFlagAtARealMarker() {
    // A start-of-image is not a restart, so the flag must stay raised and the reader must not step
    // over it — otherwise a truncated stream would be read on into whatever followed it.
    var stream = new byte[] { 0xAB, 0xFF, 0xD9 };
    var reader = new JpegBitReader(stream, 0);

    reader.ReadBits(8);
    reader.ReadBits(8);

    Assert.Multiple(() => {
      Assert.That(reader.IsAtEnd, Is.True);
      Assert.That(reader.TryConsumeRestart(0), Is.False);
      Assert.That(reader.IsAtEnd, Is.True, "and it stays ended");
    });
  }
}
