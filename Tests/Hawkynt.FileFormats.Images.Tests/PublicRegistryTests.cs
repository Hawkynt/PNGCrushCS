using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// Smoke tests for the public registry API. Validate every "shape" of consumer interaction:
/// extension lookup, MIME lookup, byte detection, stream detection (seekable + non-seekable),
/// high-level read/write round-trips, and the basic invariants that make the registry usable.
/// </summary>
[TestFixture]
public sealed class PublicRegistryTests {

  // ============================================================
  // Format detection
  // ============================================================

  [Test]
  public void DetectFromExtension_Png_ReturnsPng() {
    var fmt = FormatRegistry.DetectFromExtension(".png");
    Assert.That(fmt, Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  public void DetectFromExtension_NoLeadingDot_StillWorks() {
    Assert.That(FormatRegistry.DetectFromExtension("png"), Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  public void DetectFromExtension_Unknown_ReturnsUnknown() {
    Assert.That(FormatRegistry.DetectFromExtension(".thisdoesnotexist"), Is.EqualTo(ImageFormat.Unknown));
  }

  [Test]
  public void DetectFromMimeType_ImagePng_ReturnsPng() {
    Assert.That(FormatRegistry.DetectFromMimeType("image/png"), Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  public void DetectFromMimeType_AliasImageXPng_AlsoReturnsPng() {
    Assert.That(FormatRegistry.DetectFromMimeType("image/x-png"), Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  public void DetectFromMimeType_CaseInsensitive() {
    Assert.That(FormatRegistry.DetectFromMimeType("IMAGE/PNG"), Is.EqualTo(ImageFormat.Png));
    Assert.That(FormatRegistry.DetectFromMimeType("Image/Jpeg"), Is.EqualTo(ImageFormat.Jpeg));
  }

  [Test]
  public void DetectFromMimeType_Unknown_ReturnsUnknown() {
    Assert.That(FormatRegistry.DetectFromMimeType("application/x-no-such-thing"), Is.EqualTo(ImageFormat.Unknown));
  }

  [Test]
  public void DetectFromBytes_PngSignature_ReturnsPng() {
    Span<byte> header = stackalloc byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    Assert.That(FormatRegistry.DetectFromBytes(header), Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  public void DetectFromBytes_JpegSignature_ReturnsJpeg() {
    Span<byte> header = stackalloc byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
    Assert.That(FormatRegistry.DetectFromBytes(header), Is.EqualTo(ImageFormat.Jpeg));
  }

  [Test]
  public void DetectFromBytes_Empty_ReturnsUnknown() {
    Span<byte> header = stackalloc byte[] { };
    Assert.That(FormatRegistry.DetectFromBytes(header), Is.EqualTo(ImageFormat.Unknown));
  }

  // ============================================================
  // Stream detection — seekable + non-seekable
  // ============================================================

  [Test]
  public void DetectFromStream_Seekable_RestoresPosition() {
    var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    using var ms = new MemoryStream(pngHeader);
    ms.Position = 0;
    var fmt = FormatRegistry.DetectFromStream(ms);
    Assert.Multiple(() => {
      Assert.That(fmt, Is.EqualTo(ImageFormat.Png));
      Assert.That(ms.Position, Is.EqualTo(0), "Seekable stream position should be restored after detection");
    });
  }

  [Test]
  public void DetectFromStream_NonSeekable_StillDetects() {
    var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    using var inner = new MemoryStream(pngHeader);
    using var nonSeekable = new NonSeekableWrapper(inner);
    var fmt = FormatRegistry.DetectFromStream(nonSeekable);
    Assert.That(fmt, Is.EqualTo(ImageFormat.Png));
  }

  [Test]
  public void DetectFromStreamRewound_NonSeekable_RewoundStreamReplaysPrefix() {
    var data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x49 };
    using var inner = new MemoryStream(data);
    using var nonSeekable = new NonSeekableWrapper(inner);
    var (fmt, rewound) = FormatRegistry.DetectFromStreamRewound(nonSeekable, peekBytes: 8);
    Assert.That(fmt, Is.EqualTo(ImageFormat.Png));
    var roundTripped = new byte[data.Length];
    var totalRead = 0;
    int n;
    while ((n = rewound.Read(roundTripped, totalRead, roundTripped.Length - totalRead)) > 0) totalRead += n;
    Assert.That(totalRead, Is.EqualTo(data.Length));
    Assert.That(roundTripped, Is.EqualTo(data), "Rewound stream must replay the full original byte sequence");
  }

  // ============================================================
  // Extension + MIME exposure invariants
  // ============================================================

  [Test]
  public void Extensions_AreExposed_ForKnownFormats() {
    Assert.That(FormatRegistry.PrimaryExtension(ImageFormat.Png), Is.EqualTo(".png"));
    Assert.That(FormatRegistry.AllExtensions(ImageFormat.Png), Has.Count.GreaterThan(0));
    Assert.That(FormatRegistry.AllExtensions(ImageFormat.Jpeg), Does.Contain(".jpg"));
    Assert.That(FormatRegistry.AllExtensions(ImageFormat.Jpeg), Does.Contain(".jpeg"));
  }

  [Test]
  public void MimeTypes_AreExposed_ForAnnotatedFormats() {
    Assert.Multiple(() => {
      Assert.That(FormatRegistry.PrimaryMimeType(ImageFormat.Png), Is.EqualTo("image/png"));
      Assert.That(FormatRegistry.PrimaryMimeType(ImageFormat.Jpeg), Is.EqualTo("image/jpeg"));
      Assert.That(FormatRegistry.PrimaryMimeType(ImageFormat.WebP), Is.EqualTo("image/webp"));
      Assert.That(FormatRegistry.PrimaryMimeType(ImageFormat.Avif), Is.EqualTo("image/avif"));
      Assert.That(FormatRegistry.AllMimeTypes(ImageFormat.Png), Does.Contain("image/x-png"));
    });
  }

  [Test]
  public void MimeTypes_DefaultToOctetStream_ForUnannotatedFormats() {
    // Pick a long-tail format we know we DIDN'T annotate (e.g. AccessFax).
    var entry = FormatRegistry.GetEntry(ImageFormat.AccessFax);
    Assume.That(entry, Is.Not.Null);
    Assert.That(entry!.PrimaryMimeType, Is.EqualTo("application/octet-stream"));
    Assert.That(entry.MimeTypes, Has.Length.EqualTo(0));
  }

  [Test]
  public void Registry_ContainsManyFormats() {
    var count = FormatRegistry.AllFormats.Count();
    Assert.That(count, Is.GreaterThan(500),
      $"Expected the auto-discovered registry to contain 500+ formats, got {count}");
  }

  [Test]
  public void EveryFormatEntry_HasValidExtensionAndName() {
    foreach (var entry in FormatRegistry.AllFormats) {
      Assert.That(entry.PrimaryExtension, Is.Not.Empty,
        $"Format {entry.Format} has no PrimaryExtension");
      Assert.That(entry.AllExtensions, Has.Length.GreaterThan(0),
        $"Format {entry.Format} has no AllExtensions");
      Assert.That(entry.Name, Is.Not.Empty,
        $"Format {entry.Format} has no Name");
    }
  }

  // ============================================================
  // High-level Read/Write round-trip
  // ============================================================

  [Test]
  public void ReadWrite_Png_RoundTrips() {
    var src = _MakeSolidImage(PixelFormat.Rgba32, 16, 16, r: 255, g: 128, b: 64, a: 255);
    var bytes = FormatRegistry.Write(src, ImageFormat.Png);
    Assert.That(bytes, Is.Not.Null);
    Assert.That(bytes!.Length, Is.GreaterThan(8));
    Assert.That(FormatRegistry.DetectFromBytes(bytes), Is.EqualTo(ImageFormat.Png));
    var roundTripped = FormatRegistry.Read(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped!.Width, Is.EqualTo(16));
    Assert.That(roundTripped.Height, Is.EqualTo(16));
  }

  [Test]
  public void ReadWrite_Qoi_RoundTrips() {
    var src = _MakeSolidImage(PixelFormat.Rgba32, 8, 8, r: 200, g: 100, b: 50, a: 255);
    var bytes = FormatRegistry.Write(src, ImageFormat.Qoi);
    Assert.That(bytes, Is.Not.Null);
    var roundTripped = FormatRegistry.Read(bytes!);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped!.Width, Is.EqualTo(8));
    Assert.That(roundTripped.Height, Is.EqualTo(8));
  }

  // ============================================================
  // Helpers
  // ============================================================

  private static RawImage _MakeSolidImage(PixelFormat pf, int w, int h, byte r, byte g, byte b, byte a) {
    var bytesPerPx = pf switch {
      PixelFormat.Rgba32 => 4,
      PixelFormat.Rgb24 => 3,
      _ => throw new NotSupportedException(pf.ToString()),
    };
    var data = new byte[w * h * bytesPerPx];
    for (var i = 0; i < w * h; ++i) {
      var off = i * bytesPerPx;
      data[off + 0] = r;
      data[off + 1] = g;
      data[off + 2] = b;
      if (bytesPerPx == 4) data[off + 3] = a;
    }
    return new RawImage { Width = w, Height = h, Format = pf, PixelData = data };
  }

  /// <summary>Wraps a stream to make it report CanSeek=false. Used to test the non-seekable code path.</summary>
  private sealed class NonSeekableWrapper : Stream {
    private readonly Stream _inner;
    public NonSeekableWrapper(Stream inner) => this._inner = inner;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => this._inner.Read(buffer, offset, count);
    public override void Flush() => this._inner.Flush();
    public override long Seek(long o, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
