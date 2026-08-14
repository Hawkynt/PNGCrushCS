using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Ilbm.Tests;

/// <summary>
/// An ILBM whose BMHD states 24 or 32 planes, which is a truecolour picture rather than a deep
/// palette.
/// </summary>
/// <remarks>
/// The layout is not taken from a description. XnView's own converter was handed a 320 by 240
/// picture whose red runs with x, green with y and blue with x plus y — asymmetric both ways, so a
/// mirror, a transpose or a channel swap could not pass — and asked for an <c>iff</c>. What came
/// back was a <c>FORM ILBM</c> with <c>nPlanes = 24</c>, no <c>CMAP</c>, and a 230,400-byte
/// uncompressed <c>BODY</c>: exactly three bytes a pixel spread over 24 interleaved bitplane rows.
/// Reading it back through the same converter into a PPM gives those pixels again, which is the
/// fixture below, computed rather than stored.
/// <para/>
/// Plane group decides the component: planes 0 to 7 are the red byte low bit first, 8 to 15 the
/// green, 16 to 23 the blue, and 24 to 31 the alpha where there are that many. That is the only
/// arrangement under which the converter's own PPM comes back equal.
/// <para/>
/// Before this, every plane count landed on the indexed branch, where the chunky conversion keeps
/// one byte a pixel: the 24 planes collapsed onto their lowest eight, so the red channel was handed
/// out as a palette index and the other two were dropped. With no CMAP to go with it the picture
/// then had no palette either, and converting it to RGB threw rather than returning anything — a
/// file the registry reported as decoded and could not draw.
/// </remarks>
[TestFixture]
public sealed class DeepIlbmTests {

  private const int _WIDTH = 320;
  private const int _HEIGHT = 240;

  private static byte[] _Expected(int channels) {
    var pixels = new byte[_WIDTH * _HEIGHT * channels];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var at = (y * _WIDTH + x) * channels;
      pixels[at] = (byte)(x * 4);
      pixels[at + 1] = (byte)(y * 5);
      pixels[at + 2] = (byte)((x + y) * 3);
      if (channels == 4)
        pixels[at + 3] = (byte)(x * 2 + y);
    }

    return pixels;
  }

  /// <summary>Builds the file the converter builds: BMHD, CAMG and an uncompressed planar BODY.</summary>
  private static byte[] _Build(byte[] pixels, int channels) {
    var planes = channels * 8;
    var bytesPerPlaneRow = (_WIDTH + 15) / 16 * 2;
    var body = new byte[bytesPerPlaneRow * planes * _HEIGHT];

    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x)
    for (var plane = 0; plane < planes; ++plane) {
      var component = pixels[(y * _WIDTH + x) * channels + plane / 8];
      if ((component & (1 << (plane % 8))) == 0)
        continue;

      body[y * bytesPerPlaneRow * planes + plane * bytesPerPlaneRow + x / 8] |= (byte)(1 << (7 - x % 8));
    }

    var bmhd = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(bmhd, _WIDTH);
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(2), _HEIGHT);
    bmhd[8] = (byte)planes;
    bmhd[9] = 2; // mskHasTransparentColor, which is what the converter writes and adds no plane
    bmhd[14] = 1; // xAspect
    bmhd[15] = 1; // yAspect
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(16), _WIDTH);
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(18), _HEIGHT);

    using var chunks = new MemoryStream();
    void Chunk(string id, byte[] payload) {
      chunks.Write(System.Text.Encoding.ASCII.GetBytes(id));
      var size = new byte[4];
      BinaryPrimitives.WriteInt32BigEndian(size, payload.Length);
      chunks.Write(size);
      chunks.Write(payload);
      if ((payload.Length & 1) != 0)
        chunks.WriteByte(0);
    }

    Chunk("BMHD", bmhd);
    Chunk("CAMG", [0, 0, 0x10, 0]);
    Chunk("BODY", body);

    using var file = new MemoryStream();
    file.Write(System.Text.Encoding.ASCII.GetBytes("FORM"));
    var formSize = new byte[4];
    BinaryPrimitives.WriteInt32BigEndian(formSize, 4 + (int)chunks.Length);
    file.Write(formSize);
    file.Write(System.Text.Encoding.ASCII.GetBytes("ILBM"));
    file.Write(chunks.ToArray());

    return file.ToArray();
  }

  [Test]
  [Category("Integration")]
  public void TwentyFourPlanesAreTheThreeColourBytesAndNotAPaletteIndex() {
    var expected = _Expected(3);
    var image = IlbmFile.ToRawImage(IlbmReader.FromBytes(_Build(expected, 3)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(_WIDTH));
      Assert.That(image.Height, Is.EqualTo(_HEIGHT));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData, Is.EqualTo(expected));
    });
  }

  [Test]
  [Category("Unit")]
  public void ThirtyTwoPlanesCarryAnAlphaByteAsWell() {
    var expected = _Expected(4);
    var image = IlbmFile.ToRawImage(IlbmReader.FromBytes(_Build(expected, 4)));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgba32));
      Assert.That(image.PixelData, Is.EqualTo(expected));
    });
  }

  /// <summary>A deep picture has no palette, so the conversion the viewer needs must not want one.</summary>
  [Test]
  [Category("Unit")]
  public void ADeepPictureConvertsToRgbWithoutAPalette() {
    var image = IlbmFile.ToRawImage(IlbmReader.FromBytes(_Build(_Expected(3), 3)));

    Assert.That(image.ToRgb24(), Has.Length.EqualTo(_WIDTH * _HEIGHT * 3));
  }
}
