using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileFormat.Apx;
using FileFormat.Core;
using FileFormat.Crd;
using FileFormat.Fff;
using FileFormat.Hta;
using FileFormat.Jpeg;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests.GapClosures;

/// <summary>
/// Four names out of XnView's coverage gap, all recovered by reading the readers its own format
/// table points at and then asking its converter, one byte at a time, whether the layout that came
/// out was the layout it wanted.
/// <para/>
/// Three of them turned out to be carriers: Hemera Thumbs (<c>.hta</c>) holds whole PNG files behind
/// a directory, PowerCard maker (<c>.crd</c>) holds a JPEG behind a length-prefixed name, and MAGGI
/// Hairstyles &amp; Cosmetics (<c>.fff</c>) holds a JPEG at a fixed place inside a client record. The
/// fourth, Ability Photopaint (<c>.apx</c>), is a picture in its own right: a layered document with
/// an uncompressed 32-bit raster behind a header whose length the header itself describes.
/// <para/>
/// Every fixture here is built in code, byte by byte, out of the same layout the readers parse, so
/// what these tests pin is the layout and not a sample file. The sizes and offsets in them are the
/// ones the converter accepted and, where they are boundaries, the ones one byte to either side of
/// which it refused.
/// </summary>
[TestFixture]
public sealed class GapB0CarrierTests {

  private const int _WIDTH = 7;
  private const int _HEIGHT = 5;

  private static RawImage _Picture(int width = _WIDTH, int height = _HEIGHT) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)(x * 30 % 256);
        pixels[at + 1] = (byte)(y * 50 % 256);
        pixels[at + 2] = (byte)((x * 17 + y * 23) % 256);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte[] _Png(RawImage image) => FormatRegistry.Write(image, ImageFormat.Png)!;

  private static byte[] _Jpeg(RawImage image) => FormatRegistry.Write(image, ImageFormat.Jpeg)!;

  private static void _Literal(List<byte> into, ReadOnlySpan<byte> bytes) {
    foreach (var one in bytes)
      into.Add(one);
  }

  private static void _Word(List<byte> into, uint value) {
    into.Add((byte)value);
    into.Add((byte)(value >> 8));
    into.Add((byte)(value >> 16));
    into.Add((byte)(value >> 24));
  }

  // ============================================================
  // Hemera Thumbs (.hta)
  // ============================================================

  /// <summary>
  /// Builds the file both descriptions of this name agree on: deark's archive header and directory,
  /// with the first member standing at or after byte 64 so that XnView's scan, which starts there,
  /// finds it.
  /// </summary>
  private static byte[] _Hta(IReadOnlyList<byte[]> members, int firstMemberOffset = HtaFile.FirstMemberOffset, int versionOverride = HtaFile.SupportedVersion, int? statedLength = null) {
    var header = new List<byte>();
    _Literal(header, HtaFile.Magic);
    _Word(header, (uint)versionOverride);
    _Word(header, (uint)members.Count);

    var at = firstMemberOffset;
    foreach (var member in members) {
      _Word(header, (uint)at);
      _Word(header, (uint)(statedLength ?? member.Length));
      at += member.Length;
    }

    var file = new List<byte>(header);
    while (file.Count < firstMemberOffset)
      file.Add(0);

    foreach (var member in members)
      file.AddRange(member);

    return file.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void Hta_ReadsTheMemberTheDirectoryPointsAt() {
    var source = _Picture();
    var png = _Png(source);
    var file = HtaReader.FromBytes(_Hta([png]));

    Assert.That(HtaFile.ImageCount(file), Is.EqualTo(1));

    var picture = HtaFile.ToRawImage(file);
    Assert.That((picture.Width, picture.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
    Assert.That(PixelConverter.Convert(picture, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Hta_ReadsEveryMemberTheDirectoryLists() {
    var first = _Picture();
    var second = _Picture(4, 3);
    var file = HtaReader.FromBytes(_Hta([_Png(first), _Png(second)]));

    Assert.That(HtaFile.ImageCount(file), Is.EqualTo(2));
    Assert.That((HtaFile.ToRawImage(file, 0).Width, HtaFile.ToRawImage(file, 0).Height), Is.EqualTo((_WIDTH, _HEIGHT)));
    Assert.That((HtaFile.ToRawImage(file, 1).Width, HtaFile.ToRawImage(file, 1).Height), Is.EqualTo((4, 3)));
  }

  [Test]
  [Category("Unit")]
  public void Hta_RefusesAMemberAheadOfWhereTheFormatLooks() {
    // XnView steps sixty bytes past the four it compares before it starts scanning, so a member at
    // 63 is one it never sees. Its converter reads 64 and refuses 63; so does this.
    var png = _Png(_Picture());
    Assert.That(() => HtaReader.FromBytes(_Hta([png], firstMemberOffset: HtaFile.FirstMemberOffset - 1)), Throws.InstanceOf<InvalidDataException>());
    Assert.That(() => HtaReader.FromBytes(_Hta([png])), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void Hta_RefusesALengthThePngItselfContradicts() {
    // The directory entry and the member have to agree about how long the member is; that agreement
    // is what identifies the file rather than the four bytes at the front.
    var png = _Png(_Picture());
    Assert.That(() => HtaReader.FromBytes(_Hta([png], statedLength: png.Length - 1)), Throws.InstanceOf<InvalidDataException>());
    Assert.That(() => HtaReader.FromBytes(_Hta([png], statedLength: png.Length)), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void Hta_RefusesAVersionItDoesNotKnow() {
    var png = _Png(_Picture());
    Assert.That(() => HtaReader.FromBytes(_Hta([png], versionOverride: 1)), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void Hta_RefusesAFileOfAnotherFormat()
    => Assert.That(() => HtaReader.FromBytes(_Png(_Picture())), Throws.InstanceOf<InvalidDataException>());

  // ============================================================
  // PowerCard maker (.crd)
  // ============================================================

  private static byte[] _Crd(byte[] jpeg, int padding = 0) {
    var file = new List<byte>();
    _Literal(file, CrdFile.Magic);
    file.Add(0);
    for (var i = 0; i < padding; ++i)
      file.Add(0);
    file.AddRange(jpeg);
    return file.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void Crd_ReadsTheJpegItsOwnApp0Announces() {
    var jpeg = _Jpeg(_Picture());
    var file = CrdReader.FromBytes(_Crd(jpeg));

    Assert.That(file.PictureOffset, Is.EqualTo(CrdFile.HeaderSize));
    Assert.That(file.PictureData, Is.EqualTo(jpeg));

    var picture = CrdFile.ToRawImage(file);
    var direct = JpegFile.ToRawImage(JpegReader.FromBytes(jpeg));
    Assert.That((picture.Width, picture.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
    Assert.That(picture.PixelData, Is.EqualTo(direct.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Crd_FindsTheJpegWhereverTheDocumentPutIt() {
    // XnView slides a window through the whole document rather than seeking to a fixed byte, and
    // five hundred bytes of padding ahead of the picture changed nothing when its converter was
    // asked. Neither does it here.
    var jpeg = _Jpeg(_Picture());
    var file = CrdReader.FromBytes(_Crd(jpeg, padding: 500));

    Assert.That(file.PictureOffset, Is.EqualTo(CrdFile.HeaderSize + 500));
    Assert.That(file.PictureData, Is.EqualTo(jpeg));
  }

  [Test]
  [Category("Unit")]
  public void Crd_RefusesADocumentWithNoPictureInIt() {
    var file = new List<byte>();
    _Literal(file, CrdFile.Magic);
    file.AddRange(Encoding.ASCII.GetBytes(new string('.', 400)));
    Assert.That(() => CrdReader.FromBytes(file.ToArray()), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void Crd_RefusesAMisspeltName() {
    var jpeg = _Jpeg(_Picture());
    var broken = _Crd(jpeg);
    broken[0] = 8;
    Assert.That(() => CrdReader.FromBytes(broken), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void Crd_RefusesAFileOfAnotherFormat()
    => Assert.That(() => CrdReader.FromBytes(_Png(_Picture())), Throws.InstanceOf<InvalidDataException>());

  // ============================================================
  // MAGGI Hairstyles & Cosmetics (.fff)
  // ============================================================

  private static byte[] _Fff(byte[] jpeg, int signatureOffset = FffFile.SignatureOffset, int pictureOffset = FffFile.PictureOffset) {
    var file = new byte[pictureOffset + jpeg.Length];
    FffFile.Magic.CopyTo(file.AsSpan(signatureOffset));
    jpeg.CopyTo(file.AsSpan(pictureOffset));
    return file;
  }

  [Test]
  [Category("Unit")]
  public void Fff_ReadsThePortraitAtTheByteTheFormatKeepsItAt() {
    var jpeg = _Jpeg(_Picture());
    var file = FffReader.FromBytes(_Fff(jpeg));

    Assert.That(file.PictureData, Is.EqualTo(jpeg));

    var picture = FffFile.ToRawImage(file);
    var direct = JpegFile.ToRawImage(JpegReader.FromBytes(jpeg));
    Assert.That((picture.Width, picture.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
    Assert.That(picture.PixelData, Is.EqualTo(direct.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void Fff_RefusesASignatureOneByteEitherSideOfWhereItBelongs() {
    // Asked of XnView's converter one byte at a time: 451 and 453 are both refused, 452 is read.
    var jpeg = _Jpeg(_Picture());
    Assert.That(() => FffReader.FromBytes(_Fff(jpeg, signatureOffset: FffFile.SignatureOffset - 1)), Throws.InstanceOf<InvalidDataException>());
    Assert.That(() => FffReader.FromBytes(_Fff(jpeg, signatureOffset: FffFile.SignatureOffset + 1)), Throws.InstanceOf<InvalidDataException>());
    Assert.That(() => FffReader.FromBytes(_Fff(jpeg)), Throws.Nothing);
  }

  [Test]
  [Category("Unit")]
  public void Fff_RefusesAPortraitOneByteEitherSideOfWhereItBelongs() {
    // Likewise: a picture at 3271 or at 3273 is refused and one at 3272 is read.
    var jpeg = _Jpeg(_Picture());
    Assert.That(() => FffReader.FromBytes(_Fff(jpeg, pictureOffset: FffFile.PictureOffset - 1)), Throws.InstanceOf<InvalidDataException>());
    Assert.That(() => FffReader.FromBytes(_Fff(jpeg, pictureOffset: FffFile.PictureOffset + 1)), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void Fff_RefusesAFileOfAnotherFormat() {
    // The same extension is claimed by Imacon and Hasselblad's raw format, which is a TIFF at heart
    // and carries none of this record's structure.
    Assert.That(() => FffReader.FromBytes(_Jpeg(_Picture())), Throws.InstanceOf<InvalidDataException>());
    Assert.That(() => FffReader.FromBytes(_Png(_Picture())), Throws.InstanceOf<InvalidDataException>());
  }

  // ============================================================
  // Ability Photopaint (.apx)
  // ============================================================

  /// <summary>
  /// Builds the header the format's own reader walks: the signature, three words it steps past, the
  /// two whose product decides the next step, the fields that matter, and one record per layer.
  /// </summary>
  private static byte[] _Apx(RawImage source, ReadOnlySpan<byte> signature, uint a = 0, uint b = 0, uint resolution = 72, int layers = 1, int layerNameLength = 0) {
    var file = new List<byte>();
    _Literal(file, signature);
    _Word(file, 1);
    _Word(file, 2);
    _Word(file, 3);
    _Word(file, a);
    _Word(file, b);
    for (var i = 0; i < a * b * 4 + 0x28; ++i)
      file.Add(0);

    _Word(file, resolution);
    _Word(file, (uint)source.Width);
    _Word(file, (uint)source.Height);
    _Word(file, (uint)layers);
    _Word(file, 0);
    _Word(file, 0);
    for (var i = 0; i < 0x10; ++i)
      file.Add(0);

    for (var layer = 0; layer < layers; ++layer) {
      for (var i = 0; i < 4; ++i)
        _Word(file, 0);
      _Word(file, (uint)layerNameLength);
      for (var i = 0; i < layerNameLength; ++i)
        file.Add((byte)'.');
      for (var i = 0; i < 3; ++i)
        _Word(file, 0);
    }

    // Rows run bottom to top and the bytes inside a pixel run alpha, blue, green, red.
    for (var y = source.Height - 1; y >= 0; --y)
      for (var x = 0; x < source.Width; ++x) {
        var at = (y * source.Width + x) * 3;
        file.Add(255);
        file.Add(source.PixelData[at + 2]);
        file.Add(source.PixelData[at + 1]);
        file.Add(source.PixelData[at]);
      }

    return file.ToArray();
  }

  private static byte[] _Rgba(RawImage source) {
    var pixels = new byte[source.Width * source.Height * 4];
    for (var i = 0; i < source.Width * source.Height; ++i) {
      pixels[i * 4] = source.PixelData[i * 3];
      pixels[i * 4 + 1] = source.PixelData[i * 3 + 1];
      pixels[i * 4 + 2] = source.PixelData[i * 3 + 2];
      pixels[i * 4 + 3] = 255;
    }

    return pixels;
  }

  [Test]
  [Category("Unit")]
  public void Apx_ReadsTheRasterBehindTheEarlierSignature() {
    var source = _Picture();
    var file = ApxReader.FromBytes(_Apx(source, ApxFile.MagicPaint));

    Assert.That((file.Width, file.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
    Assert.That(file.Resolution, Is.EqualTo(72));
    Assert.That(file.LayerCount, Is.EqualTo(1));
    Assert.That(file.IsPro, Is.False);

    var picture = ApxFile.ToRawImage(file);
    Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(picture.PixelData, Is.EqualTo(_Rgba(source)));
  }

  [Test]
  [Category("Unit")]
  public void Apx_ReadsTheRasterBehindTheProSignature() {
    var source = _Picture();
    var file = ApxReader.FromBytes(_Apx(source, ApxFile.MagicPaintPro));

    Assert.That(file.IsPro, Is.True);
    Assert.That(ApxFile.ToRawImage(file).PixelData, Is.EqualTo(_Rgba(source)));
  }

  [Test]
  [Category("Unit")]
  public void Apx_HeaderLengthFollowsTheFieldsInsideIt() {
    // The two words whose product sizes the first step, the layer count and the length of each
    // layer's name all move the raster, and the converter followed every one of them.
    var source = _Picture();
    var file = ApxReader.FromBytes(_Apx(source, ApxFile.MagicPaint, a: 3, b: 5, layers: 2, layerNameLength: 37, resolution: 300));

    Assert.That((file.Width, file.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
    Assert.That(file.Resolution, Is.EqualTo(300));
    Assert.That(file.LayerCount, Is.EqualTo(2));
    Assert.That(ApxFile.ToRawImage(file).PixelData, Is.EqualTo(_Rgba(source)));
  }

  [Test]
  [Category("Unit")]
  public void Apx_RefusesADocumentHoldingNoLayer() {
    // XnView says "APX : No layer !" and reads nothing; a file with no layer holds no raster either.
    var source = _Picture();
    var broken = _Apx(source, ApxFile.MagicPaint, layers: 1);
    var countAt = ApxFile.SignatureSize + 5 * 4 + 0x28 + 3 * 4;
    broken[countAt] = 0;
    Assert.That(() => ApxReader.FromBytes(broken), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void Apx_RefusesATruncatedRaster() {
    var source = _Picture();
    var whole = _Apx(source, ApxFile.MagicPaint);
    Assert.That(() => ApxReader.FromBytes(whole[..^4]), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void Apx_RefusesASignatureChangedInItsLastByte() {
    // Both signatures are 21 bytes and both are compared to their last byte, so the earlier one's
    // terminating zero is as much a part of it as its letters are.
    var source = _Picture();
    var broken = _Apx(source, ApxFile.MagicPaint);
    broken[ApxFile.SignatureSize - 1] = 1;
    Assert.That(() => ApxReader.FromBytes(broken), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void Apx_RefusesAFileOfAnotherFormat() {
    // Two files gathered under this extension in an earlier pass open with "SD3S" and XnView refuses
    // them too; whatever they are, they are not this.
    var strayed = new byte[512];
    "SD3S"u8.CopyTo(strayed);
    Assert.That(() => ApxReader.FromBytes(strayed), Throws.InstanceOf<InvalidDataException>());
    Assert.That(() => ApxReader.FromBytes(_Png(_Picture())), Throws.InstanceOf<InvalidDataException>());
  }

  // ============================================================
  // Registration
  // ============================================================

  [Test]
  [Category("Unit")]
  public void EachNameIsRegisteredUnderTheExtensionItOwns() {
    Assert.That(FormatRegistry.DetectFromExtension(".hta"), Is.EqualTo(ImageFormat.Hta));
    Assert.That(FormatRegistry.DetectFromExtension(".crd"), Is.EqualTo(ImageFormat.Crd));
    Assert.That(FormatRegistry.DetectFromExtension(".fff"), Is.EqualTo(ImageFormat.Fff));
    Assert.That(FormatRegistry.DetectFromExtension(".apx"), Is.EqualTo(ImageFormat.Apx));
  }
}
