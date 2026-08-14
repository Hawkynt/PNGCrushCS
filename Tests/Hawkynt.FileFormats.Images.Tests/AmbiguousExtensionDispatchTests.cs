using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Three names several formats claim, and whether the file arriving under one of them reaches a
/// reader that can take it.
/// </summary>
/// <remarks>
/// Each of these was found by having XnView's converter write a file and trying to read it back.
/// The registry already tries every format an extension names and keeps the first that accepts, so
/// what was left was not the dispatch but the readers behind it — a claimant that answers and gets
/// the picture wrong is no better than no claimant at all.
/// <list type="bullet">
///   <item><c>.iff</c> — six formats claim it and the converter writes an Amiga <c>FORM ILBM</c>
///   under it. The ILBM reader accepted the file and read its 24 planes as an 8-bit palette index,
///   so the picture came back a third of itself with no palette to draw it by.</item>
///   <item><c>.raw</c> — the camera-raw reader was the only claimant and wants a TIFF byte-order
///   mark, where the converter writes a headerless dump. The greyscale dump reader, which is the
///   same reader XnView uses for that row, now holds the name too.</item>
///   <item><c>.flt</c> — one claimant, the Windows bitmap, and it is the right one: the converter
///   reads and writes a DIB under that name. Kept here as a regression, the name having been the
///   third of the three suspected and the only one that was already correct.</item>
/// </list>
/// </remarks>
[TestFixture]
public sealed class AmbiguousExtensionDispatchTests {

  private const int _WIDTH = 320;
  private const int _HEIGHT = 240;

  private static byte[] _Rgb() {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var at = (y * _WIDTH + x) * 3;
      pixels[at] = (byte)(x * 4);
      pixels[at + 1] = (byte)(y * 5);
      pixels[at + 2] = (byte)((x + y) * 3);
    }

    return pixels;
  }

  private static FileInfo _Write(byte[] data, string extension) {
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    File.WriteAllBytes(path, data);

    return new(path);
  }

  /// <summary>The 24-plane ILBM the converter writes, built exactly as it builds it.</summary>
  private static byte[] _DeepIlbm(byte[] pixels) {
    const int PLANES = 24;
    var bytesPerPlaneRow = (_WIDTH + 15) / 16 * 2;
    var body = new byte[bytesPerPlaneRow * PLANES * _HEIGHT];

    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x)
    for (var plane = 0; plane < PLANES; ++plane)
      if ((pixels[(y * _WIDTH + x) * 3 + plane / 8] & (1 << (plane % 8))) != 0)
        body[y * bytesPerPlaneRow * PLANES + plane * bytesPerPlaneRow + x / 8] |= (byte)(1 << (7 - x % 8));

    var bmhd = new byte[20];
    BinaryPrimitives.WriteUInt16BigEndian(bmhd, _WIDTH);
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(2), _HEIGHT);
    bmhd[8] = PLANES;
    bmhd[9] = 2;
    bmhd[14] = 1;
    bmhd[15] = 1;
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(16), _WIDTH);
    BinaryPrimitives.WriteUInt16BigEndian(bmhd.AsSpan(18), _HEIGHT);

    using var chunks = new MemoryStream();
    void Chunk(string id, byte[] payload) {
      chunks.Write(Encoding.ASCII.GetBytes(id));
      var size = new byte[4];
      BinaryPrimitives.WriteInt32BigEndian(size, payload.Length);
      chunks.Write(size);
      chunks.Write(payload);
    }

    Chunk("BMHD", bmhd);
    Chunk("CAMG", [0, 0, 0x10, 0]);
    Chunk("BODY", body);

    using var file = new MemoryStream();
    file.Write(Encoding.ASCII.GetBytes("FORM"));
    var formSize = new byte[4];
    BinaryPrimitives.WriteInt32BigEndian(formSize, 4 + (int)chunks.Length);
    file.Write(formSize);
    file.Write(Encoding.ASCII.GetBytes("ILBM"));
    file.Write(chunks.ToArray());

    return file.ToArray();
  }

  /// <summary>A 24-bit bottom-up Windows bitmap, which is what a <c>.flt</c> turns out to be.</summary>
  private static byte[] _Dib(byte[] pixels) {
    var stride = (_WIDTH * 3 + 3) & ~3;
    var data = new byte[54 + stride * _HEIGHT];
    data[0] = (byte)'B';
    data[1] = (byte)'M';
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(2), data.Length);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(10), 54);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(14), 40);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(18), _WIDTH);
    BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(22), _HEIGHT);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(26), 1);
    BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(28), 24);

    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var from = (y * _WIDTH + x) * 3;
      var to = 54 + (_HEIGHT - 1 - y) * stride + x * 3;
      data[to] = pixels[from + 2];
      data[to + 1] = pixels[from + 1];
      data[to + 2] = pixels[from];
    }

    return data;
  }

  [Test]
  [Category("Integration")]
  public void AnAmigaBitmapNamedIffIsReadRatherThanHandedToTheMayaReader() {
    var pixels = _Rgb();
    var file = _Write(_DeepIlbm(pixels), ".iff");

    try {
      var image = FormatRegistry.Read(file);

      Assert.That(image, Is.Not.Null);
      Assert.Multiple(() => {
        Assert.That((image!.Width, image.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
        Assert.That(image.ToRgb24(), Is.EqualTo(pixels));
      });
    } finally {
      file.Delete();
    }
  }

  /// <summary>Every format claiming the name is asked, not only the one registered first.</summary>
  [Test]
  [Category("Unit")]
  public void TheNameIsClaimedByMoreThanOneFormatAndAllOfThemAreCandidates() {
    var claimants = FormatRegistry.DetectCandidatesFromExtension(".iff");

    Assert.Multiple(() => {
      Assert.That(claimants, Does.Contain(ImageFormat.Ilbm));
      Assert.That(claimants, Does.Contain(ImageFormat.MayaIff));
      Assert.That(claimants.Count, Is.GreaterThan(1));
    });
  }

  /// <summary>The Maya reader keeps its magic check; it simply is not the only one asked.</summary>
  [Test]
  [Category("Unit")]
  public void TheMayaReaderStillRefusesAFileThatIsNotFor4() {
    var entry = FormatRegistry.GetEntry(ImageFormat.MayaIff);
    var file = _Write(_DeepIlbm(_Rgb()), ".iff");

    try {
      Assert.That(entry, Is.Not.Null);
      Assert.Throws<InvalidDataException>(() => entry!.LoadRawImageOrThrow!(file));
    } finally {
      file.Delete();
    }
  }

  [Test]
  [Category("Integration")]
  public void AWindowsBitmapNamedFltIsRead() {
    var pixels = _Rgb();
    var file = _Write(_Dib(pixels), ".flt");

    try {
      var image = FormatRegistry.Read(file);

      Assert.That(image, Is.Not.Null);
      Assert.Multiple(() => {
        Assert.That((image!.Width, image.Height), Is.EqualTo((_WIDTH, _HEIGHT)));
        Assert.That(image.ToRgb24(), Is.EqualTo(pixels));
      });
    } finally {
      file.Delete();
    }
  }

  /// <summary>Nothing here reads an ESRI float grid, and a file of one is refused rather than drawn.</summary>
  /// <remarks>
  /// A <c>.flt</c> band file is a bare stream of little-endian floats whose shape lives in a
  /// <c>.hdr</c> beside it. No reader here parses that companion, and the bitmap reader that holds
  /// the name refuses the file for want of a <c>BM</c> — which is the answer wanted, the alternative
  /// being a picture invented out of a header nobody read.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void AnEsriFloatGridNamedFltIsRefused() {
    var grid = new byte[_WIDTH * _HEIGHT * 4];
    for (var i = 0; i < _WIDTH * _HEIGHT; ++i)
      BinaryPrimitives.WriteSingleLittleEndian(grid.AsSpan(i * 4), i % 1000 / 10f);

    var file = _Write(grid, ".flt");

    try {
      Assert.That(FormatRegistry.Read(file), Is.Null);
    } finally {
      file.Delete();
    }
  }
}
