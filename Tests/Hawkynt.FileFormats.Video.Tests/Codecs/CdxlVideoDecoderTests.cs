using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// CDXL's video decode, built here byte by byte: bit planar plane assembly, the RGB and HAM6 pixel
/// paths, and the refusals nothing here was measured against.
/// </summary>
/// <remarks>
/// Four real files — 160x120 six-plane HAM, 128x80 four-plane RGB, 176x128 eight-plane RGB and 176x128
/// eight-plane HAM, seventy-three frames of the first three combined — were decoded here and by ffmpeg
/// and compared sample for sample against ffmpeg's own <c>rgb24</c> output: every one of the seventy-three
/// is identical, maximum delta nought. The fourth file's HAM8 frames are refused rather than shipped —
/// see <see cref="CdxlVideoDecoder"/>'s remarks for the unresolved red channel that stopped them short of
/// the same bar.
/// </remarks>
[TestFixture]
public sealed class CdxlVideoDecoderTests {

  private const int _HEADER_LENGTH = 32;

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheCdxlCodeIsTaken()
    => Assert.That(CdxlVideoDecoder.Accepts(_Stream()), Is.True);

  [Test]
  [Category("Unit")]
  public void AnotherCodecsCodeIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("cvid") };

    Assert.That(CdxlVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var stream = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("CDXL") };

    Assert.That(CdxlVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream();

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Commodore CDXL Video"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<CdxlVideoDecoder>());
  }

  // ============================================================================================
  // RGB
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ARgbPixelLooksUpThePaletteByIndex() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    var palette = new (int R, int G, int B)[] { (0, 0, 0), (15, 0, 0) }; // index 1: full red nibble
    var indices = new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 }; // 1x8: only the first pixel set

    var packet = _Frame(width: 8, height: 1, planes: 1, videoEncoding: 0, palette: palette, indices: indices);
    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);

    Assert.That(frame.PixelData[0], Is.EqualTo(0xFF)); // nibble 0xF widened to 0xFF
    Assert.That(frame.PixelData[3], Is.EqualTo(0)); // second pixel: index 0, black
  }

  [Test]
  [Category("Unit")]
  public void BitplanesAreReadPlaneMajorWithPlaneZeroAsTheLeastSignificantBit() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    // Two planes, one pixel wide, one row: plane 0's single byte then plane 1's single byte.
    // Plane 0 sets bit 7 (the only pixel), plane 1 leaves it clear — combined value 1 (bit 0 = plane 0).
    var palette = new (int R, int G, int B)[] { (0, 0, 0), (15, 15, 15) };
    var packet = _RawFrame(width: 8, height: 1, planes: 2, videoEncoding: 0, palette: palette,
      planeBytes: [[0b1000_0000], [0b0000_0000]]);

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData[0], Is.EqualTo(0xFF)); // palette index 1: white
  }

  // ============================================================================================
  // HAM6
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AHam6ControlZeroLooksUpThePalette() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    var palette = new (int R, int G, int B)[] { (0, 0, 0), (15, 0, 0) };
    // Six planes, control (top two bits) = 00, value (low four) = 1 -> palette index 1.
    var indices = new byte[] { 0b00_0001 };

    var packet = _Frame(width: 1, height: 1, planes: 6, videoEncoding: 1, palette: palette, indices: indices);
    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);

    Assert.That(frame.PixelData[0], Is.EqualTo(0xFF));
    Assert.That(frame.PixelData[1], Is.EqualTo(0));
    Assert.That(frame.PixelData[2], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AHam6ControlOneHoldsAndModifiesBlue() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    var palette = new (int R, int G, int B)[] { (10, 5, 3) }; // background: (170, 85, 51)
    // control = 01 (modify blue), value = 0xA -> widened (0xA << 4 | 0xA) = 0xAA.
    var indices = new byte[] { 0b01_1010 };

    var packet = _Frame(width: 1, height: 1, planes: 6, videoEncoding: 1, palette: palette, indices: indices);
    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);

    Assert.That(frame.PixelData[0], Is.EqualTo(0xAA)); // background red held
    Assert.That(frame.PixelData[1], Is.EqualTo(0x55)); // background green held
    Assert.That(frame.PixelData[2], Is.EqualTo(0xAA)); // blue overwritten, widened nibble
  }

  [Test]
  [Category("Unit")]
  public void EachRowStartsFromThePalettesFirstEntryRatherThanBlack() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    var palette = new (int R, int G, int B)[] { (15, 0, 0) }; // background: pure red
    // control = 11 (modify green) on the very first pixel of the row, before any fresh lookup.
    var indices = new byte[] { 0b11_0101 };

    var packet = _Frame(width: 1, height: 1, planes: 6, videoEncoding: 1, palette: palette, indices: indices);
    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);

    Assert.That(frame.PixelData[0], Is.EqualTo(0xFF)); // red held from the palette's own first entry
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void APacketShorterThanTheHeaderRefuses() {
    var decoder = CdxlVideoDecoder.Create(_Stream());

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[10]), out _));
  }

  [Test]
  [Category("Unit")]
  public void APlaneArrangementOtherThanBitPlanarRefuses() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    var palette = new (int R, int G, int B)[] { (0, 0, 0) };
    var packet = _Frame(width: 8, height: 1, planes: 1, videoEncoding: 0, palette: palette, indices: new byte[8], planeArrangement: 2);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void AYuvOrAvmDctvEncodingRefuses() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    var palette = new (int R, int G, int B)[] { (0, 0, 0) };
    var packet = _Frame(width: 8, height: 1, planes: 1, videoEncoding: 2, palette: palette, indices: new byte[8]);

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void Ham8Refuses() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    var palette = new (int R, int G, int B)[] { (0, 0, 0) };
    var packet = _Frame(width: 1, height: 1, planes: 8, videoEncoding: 1, palette: palette, indices: [0]);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
    Assert.That(failure!.Message, Does.Contain("HAM8").Or.Contain("Hold-And-Modify"));
  }

  [Test]
  [Category("Unit")]
  public void APacketShorterThanItsOwnStatedPaletteAndPixelBytesRefuses() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    var header = new byte[_HEADER_LENGTH];
    header[0] = 1;
    header[1] = 0;
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(14), 8);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(16), 1);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(18), 1);
    BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(20), 4); // states two palette entries, none follow

    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, header), out _));
  }

  [Test]
  [Category("Unit")]
  public void APaletteIndexBeyondTheStatedPaletteRefuses() {
    var decoder = CdxlVideoDecoder.Create(_Stream());
    var palette = new (int R, int G, int B)[] { (0, 0, 0) }; // one entry, index 0 only
    var indices = new byte[] { 1 }; // names index 1

    var packet = _Frame(width: 1, height: 1, planes: 1, videoEncoding: 0, palette: palette, indices: indices);
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Stream() => new() {
    Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("CDXL"),
  };

  /// <summary>Builds a whole chunk from palette-index pixels (RGB) or raw six-bit HAM values, packed
  /// plane by plane exactly as bit planar states them.</summary>
  private static byte[] _Frame(
    int width, int height, int planes, int videoEncoding,
    (int R, int G, int B)[] palette, byte[] indices, int planeArrangement = 0) {
    var bytesPerRow = (width + 7) / 8;
    var planeSize = bytesPerRow * height;
    var planeBytes = new byte[planes][];
    for (var p = 0; p < planes; ++p) {
      var bytes = new byte[planeSize];
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          if ((indices[y * width + x] >> p & 1) != 0)
            bytes[y * bytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
      planeBytes[p] = bytes;
    }

    return _RawFrame(width, height, planes, videoEncoding, palette, planeBytes, planeArrangement);
  }

  private static byte[] _RawFrame(
    int width, int height, int planes, int videoEncoding,
    (int R, int G, int B)[] palette, byte[][] planeBytes, int planeArrangement = 0) {
    var paletteBytes = palette.Length * 2;
    var pixelBytes = planeBytes.Sum(p => p.Length);
    var chunk = new byte[_HEADER_LENGTH + paletteBytes + pixelBytes];

    chunk[0] = 1;
    chunk[1] = (byte)((videoEncoding & 0x07) | ((planeArrangement & 0x07) << 5));
    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(14), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(16), (ushort)height);
    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(18), (ushort)planes);
    BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(20), (ushort)paletteBytes);

    var at = _HEADER_LENGTH;
    foreach (var (r, g, b) in palette) {
      var word = (ushort)((r & 0x0F) << 8 | (g & 0x0F) << 4 | b & 0x0F);
      BinaryPrimitives.WriteUInt16BigEndian(chunk.AsSpan(at), word);
      at += 2;
    }

    foreach (var plane in planeBytes) {
      plane.CopyTo(chunk, at);
      at += plane.Length;
    }

    return chunk;
  }
}
