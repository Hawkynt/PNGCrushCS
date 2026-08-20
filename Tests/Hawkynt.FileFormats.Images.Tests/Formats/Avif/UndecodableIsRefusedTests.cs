using System;
using System.IO;
using FileFormat.Avif;
using FileFormat.Core;

namespace FileFormat.Avif.Tests;

/// <summary>
/// An AVIF this reader cannot decode says so, rather than handing back a rectangle of zeroes.
/// </summary>
/// <remarks>
/// The AV1 path used to end in <c>catch { pixelData = new byte[expectedPixelBytes]; }</c>. A 61 by 37
/// picture written by ImageMagick came out of here as **0 non-zero bytes of 6771** — a black
/// rectangle of exactly the right size, reported as a successful decode. Nothing downstream could
/// tell that apart from a picture that really is black.
/// <para/>
/// That is the same fallback taken out of the HEIF reader, and it is the worst shape a defect takes
/// in this library: a wrong picture nothing announces. Three of them were found in one week — the
/// MIFF writer, <c>.mtv</c> at 34% wrong, and HEIF at 74 non-zero bytes of 6771 — and every one had
/// survived because something returned success. A caller can act on a refusal and cannot act on a
/// plausible black frame.
/// </remarks>
[TestFixture]
public sealed class UndecodableIsRefusedTests {

  /// <summary>An ISO base media container carrying a payload that is not an uncompressed raster.</summary>
  private static byte[] _Container(byte[] payload) {
    static byte[] Box(string type, byte[] body) {
      var box = new byte[8 + body.Length];
      var length = box.Length;
      box[0] = (byte)(length >> 24);
      box[1] = (byte)(length >> 16);
      box[2] = (byte)(length >> 8);
      box[3] = (byte)length;
      for (var i = 0; i < 4; ++i)
        box[4 + i] = (byte)type[i];

      body.CopyTo(box, 8);
      return box;
    }

    var ftyp = Box("ftyp", "avif"u8.ToArray());
    var mdat = Box("mdat", payload);
    var file = new byte[ftyp.Length + mdat.Length];
    ftyp.CopyTo(file, 0);
    mdat.CopyTo(file, ftyp.Length);
    return file;
  }

  /// <summary>A payload that is neither our own raster nor anything recognisable.</summary>
  [Test]
  [Category("Unit")]
  public void APayloadOfNoKnownShapeIsRefused() {
    var noise = new byte[64];
    for (var i = 0; i < noise.Length; ++i)
      noise[i] = (byte)(i * 37 + 11);

    Assert.Throws<NotSupportedException>(() => AvifReader.FromBytes(_Container(noise)));
  }

  /// <summary>
  /// And whatever it refuses, it never answers with an all-zero raster.
  /// </summary>
  /// <remarks>
  /// Written as a property rather than against one file: the defect was not that a particular AVIF
  /// decoded black, it was that failing at all produced black. Anything this reader accepts has to
  /// carry something.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void NothingThisReaderAcceptsComesBackEntirelyBlank([Values(16, 64, 256, 1024)] int length) {
    var payload = new byte[length];
    for (var i = 0; i < payload.Length; ++i)
      payload[i] = (byte)(i * 29 + 7);

    RawImage image;
    try {
      image = AvifFile.ToRawImage(AvifReader.FromBytes(_Container(payload)));
    } catch (Exception failure) when (failure is NotSupportedException or InvalidDataException) {
      Assert.Pass("refused, which is the honest answer");
      return;
    }

    Assert.That(image.PixelData, Is.Not.All.Zero, "an accepted picture that is entirely zero is the defect this guards");
  }
}
