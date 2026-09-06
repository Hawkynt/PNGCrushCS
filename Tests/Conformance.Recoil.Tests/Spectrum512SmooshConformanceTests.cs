using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Spectrum512Smoosh;
using Hawkynt.FileFormats.Images;

namespace Conformance.Recoil.Tests;

/// <summary>
/// Hands the reference decoder pictures a Spectrum 512 Smooshed file can hold exactly, and requires
/// the picture it gives back to be the one that went in — every pixel of it.
/// </summary>
/// <remarks>
/// The registry-wide fixtures cannot ask this much of it. <c>WriterAcceptanceTests</c> proves RECOIL
/// parses the bytes, which a file with the palette in the wrong place would also pass, and the
/// pairing list drives one probe per format. What is actually at stake here is that the packing is
/// right: the bitmap goes out in byte-wide vertical strips under a run-length coder, and the palette
/// as a bitstream where the map is fourteen bits and each colour nine, so a single bit out of place
/// anywhere shifts everything after it. That shows up as differing pixels and as nothing else.
/// <para/>
/// The probes are chosen for what they put through that coder rather than for looking like pictures:
/// nothing but runs, nothing but literals, the full fourteen-colour budget, and a different palette
/// on each of the 199 scanlines.
/// </remarks>
[TestFixture]
public sealed class Spectrum512SmooshConformanceTests {

  private const int _WIDTH = 320;
  private const int _HEIGHT = 199;

  public readonly record struct Probe(string Name, Func<RawImage> Build) {
    public override string ToString() => this.Name;
  }

  public static readonly Probe[] Probes = [
    new("black", _Black),
    new("flat colour", _Flat),
    new("fourteen colours in bars", _Bars),
    new("fourteen colours and black", _BarsWithBlack),
    new("one-pixel vertical stripes", _Stripes),
    new("a different palette on every scanline", _PerScanlinePalettes),
    new("colour changes on the zone boundaries", _ZoneBoundaries),
    new("random within the budget", _Random),
  ];

  [Test]
  [Category("Conformance")]
  [TestCaseSource(nameof(Probes))]
  public void WhatWeWrite_RecoilDecodesBackExactly(Probe probe) {
    RecoilOracle.RequireAvailable();

    var source = probe.Build();
    var encoded = Spectrum512SmooshWriter.ToBytes(Spectrum512SmooshFile.FromExactRawImage(source));
    var path = Path.Combine(Path.GetTempPath(), $"spsconf_{Guid.NewGuid():N}.sps");

    byte[]? png;
    string output;
    try {
      File.WriteAllBytes(path, encoded);
      (png, output) = RecoilOracle.TryDecodeToPng(path);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }

    Assert.That(png, Is.Not.Null, $"{probe}: RECOIL rejected our {encoded.Length}-byte file — {output}");

    var theirs = PixelConverter.Convert(FormatRegistry.Read(png!)!, PixelFormat.Rgb24);
    Assert.That((theirs.Width, theirs.Height), Is.EqualTo((_WIDTH, _HEIGHT)), $"{probe}: RECOIL read it at the wrong size");

    _AssertSamePixels(probe, "RECOIL", source, theirs);
  }

  [Test]
  [Category("Conformance")]
  [TestCaseSource(nameof(Probes))]
  public void WhatWeWrite_WeDecodeBackExactly(Probe probe) {
    var source = probe.Build();
    var encoded = Spectrum512SmooshWriter.ToBytes(Spectrum512SmooshFile.FromExactRawImage(source));
    var ours = Spectrum512SmooshFile.ToRawImage(Spectrum512SmooshReader.FromBytes(encoded));

    _AssertSamePixels(probe, "our reader", source, ours);
  }

  /// <summary>
  /// The other layout a smooshed file may carry, which nothing in the file distinguishes.
  /// </summary>
  /// <remarks>
  /// A file whose last byte is odd is packed plane by plane instead of in vertical strips, and both
  /// decoders read the parity to tell them apart. Our writer only ever produces the even one, so the
  /// odd path would otherwise be exercised by nothing at all — and it is the path a smooshed file
  /// written by SPSLIDEX takes.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  public void ThePlaneByPlaneLayout_ReadsTheSameAsRecoil() {
    RecoilOracle.RequireAvailable();

    var source = _Bars();
    var repacked = _RepackPlaneByPlane(Spectrum512SmooshWriter.ToBytes(Spectrum512SmooshFile.FromExactRawImage(source)));
    var path = Path.Combine(Path.GetTempPath(), $"spsconf_{Guid.NewGuid():N}.sps");

    byte[]? png;
    string output;
    try {
      File.WriteAllBytes(path, repacked);
      (png, output) = RecoilOracle.TryDecodeToPng(path);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }

    Assert.That(png, Is.Not.Null, $"RECOIL rejected the plane-by-plane file — {output}");

    var theirs = PixelConverter.Convert(FormatRegistry.Read(png!)!, PixelFormat.Rgb24);
    var ours = Spectrum512SmooshFile.ToRawImage(Spectrum512SmooshReader.FromBytes(repacked));

    _AssertSamePixels(new("plane-by-plane layout", _Bars), "RECOIL", ours, theirs);
  }

  /// <summary>A scanline past the fourteen colours the format names is refused rather than reduced.</summary>
  [Test]
  [Category("Conformance")]
  public void APictureTheFormatCannotHold_IsRefusedByName() {
    var source = _BarsOf(Spectrum512SmooshFile.MaxColorsPerScanline + 1);

    var refusal = Assert.Throws<NotSupportedException>(() => Spectrum512SmooshFile.FromExactRawImage(source));
    Assert.That(refusal!.Message, Does.Contain("14"));
  }

  private static void _AssertSamePixels(Probe probe, string who, RawImage expected, RawImage actual) {
    var wanted = PixelConverter.Convert(expected, PixelFormat.Rgb24);
    var got = PixelConverter.Convert(actual, PixelFormat.Rgb24);

    for (var i = 0; i < wanted.PixelData.Length; ++i) {
      if (wanted.PixelData[i] == got.PixelData[i])
        continue;

      var pixel = i / 3;
      Assert.Fail(
        $"{probe}: {who} differs at {pixel % _WIDTH},{pixel / _WIDTH} channel {i % 3} — "
        + $"wanted {wanted.PixelData[i]}, got {got.PixelData[i]}");
    }
  }

  /// <summary>Rewrites the bitmap in the plane-by-plane order, leaving the palette exactly as it was.</summary>
  private static byte[] _RepackPlaneByPlane(byte[] smooshed) {
    const int bitmapOffset = 160;
    const int paletteOffset = 32000;
    const int bytesPerScanline = 160;

    // Unpack what our own writer produced, back into the plain .spu bitmap.
    var plain = new byte[paletteOffset - bitmapOffset];
    var at = 12;
    var pending = new List<byte>();
    while (pending.Count < plain.Length) {
      var command = smooshed[at++];
      if (command < 128) {
        var value = smooshed[at++];
        for (var i = 0; i < command + 3; ++i)
          pending.Add(value);
      } else
        for (var i = 0; i < command - 127; ++i)
          pending.Add(smooshed[at++]);
    }

    for (var plane = 0; plane < 8; plane += 2)
    for (var column = 0; column < 40; ++column)
    for (var line = 0; line < 199; ++line)
      plain[line * bytesPerScanline + ((column & ~1) << 2) + plane + (column & 1)] =
        pending[((plane >> 1) * 40 + column) * 199 + line];

    // And pack it again word by word down each plane, which is what the other layout means.
    var ordered = new List<byte>(plain.Length);
    for (var plane = 0; plane < 8; plane += 2)
    for (var offset = plane; offset < plain.Length; offset += 8) {
      ordered.Add(plain[offset]);
      ordered.Add(plain[offset + 1]);
    }

    var bitmap = new List<byte>();
    for (var i = 0; i < ordered.Count;) {
      var take = Math.Min(128, ordered.Count - i);
      bitmap.Add((byte)(127 + take));
      for (var k = 0; k < take; ++k)
        bitmap.Add(ordered[i + k]);

      i += take;
    }

    var palette = new List<byte>(smooshed[(12 + (int)_BigEndian(smooshed, 4))..]);

    // The layout is stated by the parity of the very last byte and by nothing else.
    if ((palette[^1] & 1) == 0)
      palette.Add(1);

    var result = new byte[12 + bitmap.Count + palette.Count];
    result[0] = (byte)'S';
    result[1] = (byte)'P';
    _WriteBigEndian(result, 4, bitmap.Count);
    _WriteBigEndian(result, 8, palette.Count);
    bitmap.CopyTo(result, 12);
    palette.CopyTo(result, 12 + bitmap.Count);

    return result;
  }

  private static uint _BigEndian(byte[] data, int offset)
    => (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

  private static void _WriteBigEndian(byte[] data, int offset, int value) {
    data[offset] = (byte)(value >> 24);
    data[offset + 1] = (byte)(value >> 16);
    data[offset + 2] = (byte)(value >> 8);
    data[offset + 3] = (byte)value;
  }

  #region probes

  /// <summary>An ST colour, already widened the way both decoders widen it, so nothing is rounded.</summary>
  private static (byte R, byte G, byte B) _St(int red, int green, int blue)
    => (ChannelScaling.Expand3(red & 7), ChannelScaling.Expand3(green & 7), ChannelScaling.Expand3(blue & 7));

  private static RawImage _Paint(Func<int, int, (byte R, byte G, byte B)> pixel) {
    var data = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
    for (var x = 0; x < _WIDTH; ++x) {
      var (r, g, b) = pixel(x, y);
      var at = (y * _WIDTH + x) * 3;
      data[at] = r;
      data[at + 1] = g;
      data[at + 2] = b;
    }

    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = data };
  }

  /// <summary>Fifteen distinct non-black ST colours; the first fourteen are all a scanline can name.</summary>
  private static readonly (int R, int G, int B)[] _Palette = [
    (7, 0, 0), (0, 7, 0), (0, 0, 7), (7, 7, 0), (7, 0, 7),
    (0, 7, 7), (7, 7, 7), (4, 2, 1), (1, 2, 4), (2, 4, 1),
    (5, 5, 2), (3, 0, 6), (6, 3, 0), (1, 1, 1), (2, 2, 5),
  ];

  private static (byte R, byte G, byte B) _Colour(int index) {
    var (r, g, b) = _Palette[index % _Palette.Length];

    return _St(r, g, b);
  }

  private static RawImage _Black() => _Paint(static (_, _) => _St(0, 0, 0));

  private static RawImage _Flat() => _Paint(static (_, _) => _St(7, 3, 1));

  private static RawImage _Bars() => _BarsOf(Spectrum512SmooshFile.MaxColorsPerScanline);

  /// <summary>Vertical bars in <paramref name="colours"/> distinct non-black colours a scanline.</summary>
  private static RawImage _BarsOf(int colours) => _Paint((x, _) => _Colour(x * colours / _WIDTH));

  /// <summary>The same, with black taking one bar — which costs none of the fourteen.</summary>
  private static RawImage _BarsWithBlack()
    => _Paint(static (x, _) => x * 15 / _WIDTH == 0 ? _St(0, 0, 0) : _Colour(x * 15 / _WIDTH - 1));

  /// <summary>Nothing the run-length coder can shorten: every neighbouring byte differs.</summary>
  private static RawImage _Stripes() => _Paint(static (x, y) => _Colour((x + y) % 14));

  /// <summary>Fourteen colours a line, and a different fourteen on each of the 199 lines.</summary>
  private static RawImage _PerScanlinePalettes()
    => _Paint(static (x, y) => _St(x * 14 / _WIDTH + 1, (y * 3) % 8, (y + x * 14 / _WIDTH) % 8));

  /// <summary>
  /// A colour change at each position a palette register reloads at.
  /// </summary>
  /// <remarks>
  /// Which of a scanline's three zones a pixel reads depends on its pen and its x, and the boundaries
  /// are ten pixels apart with a six-pixel nudge for the odd pens. All three zones are written with
  /// the same colours, so crossing one must change nothing — and this is the picture that would show
  /// it if it did.
  /// </remarks>
  private static RawImage _ZoneBoundaries()
    => _Paint(static (x, y) => _Colour(x switch {
      0 => 0,
      < 145 => 1,
      < 161 => 2,
      < 305 => 3,
      _ => 4,
    } + y % 9));

  private static RawImage _Random() {
    var random = new Random(20250906);
    var lines = new (byte R, byte G, byte B)[_HEIGHT][];
    for (var y = 0; y < _HEIGHT; ++y) {
      var palette = new (byte, byte, byte)[14];
      for (var i = 0; i < palette.Length; ++i)
        palette[i] = _St(random.Next(8), random.Next(8), random.Next(8));

      lines[y] = palette;
    }

    var indices = new byte[_WIDTH * _HEIGHT];
    random.NextBytes(indices);

    return _Paint((x, y) => lines[y][indices[y * _WIDTH + x] % 14]);
  }

  #endregion

}
