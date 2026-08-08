using System;
using System.Linq;
using FileFormat.Core;
using FileFormat.Png;
using Hawkynt.FileFormats.Images;

namespace FileFormat.Png.Tests;

/// <summary>Exercises <c>PngMetadataCodec</c> indirectly through the public <see cref="PngFile"/> /
/// <see cref="RawImage"/> surface — <c>ToRawImage</c>/<c>FromRawImage</c> are where it's wired in.</summary>
[TestFixture]
public sealed class PngMetadataTests {

  private static RawImage _TinyImage(ImageMetadata? metadata) => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Rgb24,
    PixelData = new byte[2 * 2 * 3],
    Metadata = metadata,
  };

  [Test]
  public void FromRawImage_ThenToRawImage_RoundTripsExif() {
    var exif = new ExifData {
      LittleEndian = true,
      Ifd0 = new ExifIfd { Entries = [new ExifTagEntry(ExifData.TagMake, ExifTagType.Ascii, 5, "ACME\0"u8.ToArray())] },
    };
    var raw = _TinyImage(new ImageMetadata { Exif = exif });

    var png = PngFile.FromRawImage(raw);
    var bytes = PngWriter.ToBytes(png);
    var reparsed = PngReader.FromBytes(bytes);
    var roundTripped = PngFile.ToRawImage(reparsed);

    Assert.That(roundTripped.Metadata, Is.Not.Null);
    Assert.That(roundTripped.Metadata!.Exif, Is.Not.Null);
    var make = roundTripped.Metadata.Exif!.Ifd0.Find(ExifData.TagMake);
    Assert.That(ExifData.DecodeAscii(make!), Is.EqualTo("ACME"));
  }

  [Test]
  public void FromRawImage_WritesExifAsLowercaseAncillaryChunk() {
    var exif = new ExifData { LittleEndian = true, Ifd0 = new ExifIfd() };
    var raw = _TinyImage(new ImageMetadata { Exif = exif });
    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(raw));

    var chunkNames = FormatRegistry.EnumerateChunks(bytes).Select(c => c.Name).ToList();
    Assert.That(chunkNames, Contains.Item("eXIf"));
  }

  [Test]
  public void FromRawImage_ThenToRawImage_RoundTripsCommentText() {
    var raw = _TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Comment", "hello world")],
    });

    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(raw));
    var roundTripped = PngFile.ToRawImage(PngReader.FromBytes(bytes));

    var entry = roundTripped.Metadata!.TextEntries.Single();
    Assert.That(entry.Keyword, Is.EqualTo("Comment"));
    Assert.That(entry.Text, Is.EqualTo("hello world"));
  }

  [Test]
  public void FromRawImage_CompressedTextRoundTripsAsZtxt() {
    var raw = _TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Description", "a long enough comment to be worth compressing", PreferCompression: true)],
    });

    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(raw));
    Assert.That(FormatRegistry.EnumerateChunks(bytes).Select(c => c.Name), Contains.Item("zTXt"));

    var roundTripped = PngFile.ToRawImage(PngReader.FromBytes(bytes));
    Assert.That(roundTripped.Metadata!.TextEntries.Single().Text, Is.EqualTo("a long enough comment to be worth compressing"));
  }

  [Test]
  public void FromRawImage_NonLatin1TextUsesItxt() {
    var raw = _TinyImage(new ImageMetadata {
      TextEntries = [new TextMetadataEntry("Comment", "café 日本語")], // non-Latin1 codepoints force iTXt
    });

    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(raw));
    Assert.That(FormatRegistry.EnumerateChunks(bytes).Select(c => c.Name), Contains.Item("iTXt"));

    var roundTripped = PngFile.ToRawImage(PngReader.FromBytes(bytes));
    Assert.That(roundTripped.Metadata!.TextEntries.Single().Text, Is.EqualTo("café 日本語"));
  }

  [Test]
  public void FromRawImage_ThenToRawImage_RoundTripsIccProfile() {
    var profile = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    var raw = _TinyImage(new ImageMetadata { IccProfile = profile, IccProfileName = "sRGB IEC61966" });

    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(raw));
    var roundTripped = PngFile.ToRawImage(PngReader.FromBytes(bytes));

    Assert.That(roundTripped.Metadata!.IccProfile, Is.EqualTo(profile));
    Assert.That(roundTripped.Metadata.IccProfileName, Is.EqualTo("sRGB IEC61966"));
  }

  [Test]
  public void FromRawImage_ThenToRawImage_RoundTripsDpi() {
    var raw = _TinyImage(new ImageMetadata { DpiX = 300, DpiY = 300 });
    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(raw));
    var roundTripped = PngFile.ToRawImage(PngReader.FromBytes(bytes));

    Assert.That(roundTripped.Metadata!.DpiX, Is.EqualTo(300).Within(0.1));
    Assert.That(roundTripped.Metadata.DpiY, Is.EqualTo(300).Within(0.1));
  }

  [Test]
  public void FromRawImage_ThenToRawImage_RoundTripsXmp() {
    var xmp = System.Text.Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x='adobe:ns:meta/'/>");
    var raw = _TinyImage(new ImageMetadata { XmpPacket = xmp });

    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(raw));
    var chunkNames = FormatRegistry.EnumerateChunks(bytes).Select(c => c.Name).ToList();
    Assert.That(chunkNames, Contains.Item("iTXt"));

    var roundTripped = PngFile.ToRawImage(PngReader.FromBytes(bytes));
    Assert.That(roundTripped.Metadata!.XmpPacket, Is.EqualTo(xmp));
    // The XMP iTXt is recognized by keyword, so it must not also show up as a generic text entry.
    Assert.That(roundTripped.Metadata.TextEntries, Is.Empty);
  }

  [Test]
  public void ToRawImage_TruncatedItxtChunk_DoesNotThrow() {
    // An iTXt body cut off right after the keyword's NUL (missing both the compression-flag and
    // compression-method bytes the format requires) must be skipped, not crash the whole decode.
    var truncated = "Comment\0"u8.ToArray(); // keyword + NUL, nothing else
    var bytes = _BuildPngWithTrailingChunk("iTXt", truncated);

    RawImage? roundTripped = null;
    Assert.DoesNotThrow(() => roundTripped = PngFile.ToRawImage(PngReader.FromBytes(bytes)));
    Assert.That(roundTripped!.Metadata is null || roundTripped.Metadata.TextEntries.Count == 0, Is.True);
  }

  private static byte[] _BuildPngWithTrailingChunk(string chunkType, byte[] data) {
    var png = PngFile.FromRawImage(_TinyImage(null)) with { ChunksAfterIdat = [new PngChunk(chunkType, data)] };
    return PngWriter.ToBytes(png);
  }

  [Test]
  public void ToRawImage_NoMetadataChunks_MetadataIsNull() {
    var bytes = PngWriter.ToBytes(PngFile.FromRawImage(_TinyImage(null)));
    var roundTripped = PngFile.ToRawImage(PngReader.FromBytes(bytes));
    Assert.That(roundTripped.Metadata, Is.Null);
  }

  [Test]
  public void ToRawImage_PhysWithUnknownUnit_DoesNotFabricateDpi() {
    // A pHYs chunk with unit=0 declares only an aspect ratio, not an absolute density — we must not
    // report a DPI value we don't actually have.
    var pngWithAspectOnlyPhys = _BuildPngWithAspectOnlyPhys();
    var roundTripped = PngFile.ToRawImage(PngReader.FromBytes(pngWithAspectOnlyPhys));
    Assert.That(roundTripped.Metadata is null || roundTripped.Metadata.DpiX is null, Is.True);
  }

  private static byte[] _BuildPngWithAspectOnlyPhys() {
    // Build directly via PngWriter.Assemble so we control the raw pHYs bytes precisely.
    var physUnitZero = new byte[9];
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(physUnitZero.AsSpan(0, 4), 4);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(physUnitZero.AsSpan(4, 4), 3);
    physUnitZero[8] = 0; // unit = unknown/aspect-only

    var raw = _TinyImage(null);
    var png = PngFile.FromRawImage(raw);
    return PngWriterAssembleHelper.WithChunkBeforePlte(png, "pHYs", physUnitZero);
  }
}

/// <summary>Small helper so the aspect-only-pHYs test can inject a raw chunk without needing a fully
/// separate hand-rolled PNG builder.</summary>
internal static class PngWriterAssembleHelper {
  public static byte[] WithChunkBeforePlte(PngFile file, string chunkType, byte[] data) {
    var extra = new PngChunk(chunkType, data);
    var chunks = (file.ChunksBeforePlte ?? Array.Empty<PngChunk>()).Append(extra).ToList();
    var modified = file with { ChunksBeforePlte = chunks };
    return PngWriter.ToBytes(modified);
  }
}
