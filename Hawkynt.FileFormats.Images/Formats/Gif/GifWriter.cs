using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Gif;

/// <summary>Emits a <see cref="GifFile"/> as on-disk bytes. Honors the file's <see cref="GifFile.Version"/>
/// when emitting the magic, but if the file uses any GIF89a-only feature (transparency, animation, comment
/// or application extensions) and is marked as 87a, the writer up-grades the signature to 89a.</summary>
/// <remarks>Use <see cref="GifStreamWriter"/> instead when the frames are produced lazily and holding
/// all of them at once is not affordable.</remarks>
public static class GifWriter {

  /// <summary>Serialises <paramref name="file"/> to a byte array.</summary>
  public static byte[] ToBytes(GifFile file) => ToBytes(file, null);

  /// <summary>Serialises <paramref name="file"/> to a byte array with explicit encoder options.</summary>
  public static byte[] ToBytes(GifFile file, GifWriteOptions? options) {
    ArgumentNullException.ThrowIfNull(file);
    using var ms = new MemoryStream();
    WriteTo(file, ms, options);
    return ms.ToArray();
  }

  /// <summary>Serialises <paramref name="file"/> onto <paramref name="output"/>.</summary>
  public static void WriteTo(GifFile file, Stream output) => WriteTo(file, output, null);

  /// <summary>Serialises <paramref name="file"/> onto <paramref name="output"/> with explicit encoder options.</summary>
  public static void WriteTo(GifFile file, Stream output, GifWriteOptions? options) {
    ArgumentNullException.ThrowIfNull(file);
    ArgumentNullException.ThrowIfNull(output);
    options ??= GifWriteOptions.Default;

    var lsd = file.LogicalScreenDescriptor;
    var gct = file.GlobalColorTable;
    var hasGct = lsd.HasGlobalColorTable && gct is { Length: > 0 };
    byte gctSizeExp = 0;
    if (hasGct) {
      gctSizeExp = Math.Max(lsd.GlobalColorTableSize, GifColorTable.SizeExponent(gct!.Length / 3));
      lsd = lsd with { GlobalColorTableSize = gctSizeExp };
    } else {
      lsd = lsd with { HasGlobalColorTable = false };
    }

    var version = _ResolveVersion(file);
    output.Write(Encoding.ASCII.GetBytes(version == GifVersion.Gif87a ? "GIF87a" : "GIF89a"));
    WriteLogicalScreenDescriptor(output, lsd);
    if (hasGct)
      GifColorTable.Write(output, gct!, gctSizeExp);

    // NETSCAPE2.0 loop extension precedes the first frame when looping is requested.
    if (file.LoopCount.IsPresent)
      WriteNetscapeLoop(output, file.LoopCount.Count);

    // Other global extensions kept in source order.
    foreach (var ext in file.ApplicationExtensions) {
      if (ext.IsNetscapeLoop) continue; // already emitted via LoopCount
      WriteApplicationExtension(output, ext);
    }
    foreach (var c in file.Comments) WriteCommentExtension(output, c.Data);
    foreach (var p in file.PlainTextExtensions) WritePlainTextExtension(output, p);

    foreach (var frame in file.Frames) WriteFrame(output, frame, options);

    output.WriteByte(0x3B); // Trailer
  }

  // ---- low-level emitters, shared with GifStreamWriter ----

  private static GifVersion _ResolveVersion(GifFile file) {
    if (file.Version == GifVersion.Gif89a) return GifVersion.Gif89a;
    // Upgrade silently when GIF89a features are present.
    if (file.LoopCount.IsPresent) return GifVersion.Gif89a;
    if (file.Comments.Count > 0 || file.ApplicationExtensions.Count > 0 || file.PlainTextExtensions.Count > 0)
      return GifVersion.Gif89a;
    foreach (var frame in file.Frames) {
      if (frame.TransparentColorIndex != null) return GifVersion.Gif89a;
      if (frame.DisposalMethod != FrameDisposalMethod.Unspecified) return GifVersion.Gif89a;
      if (frame.Delay > TimeSpan.Zero) return GifVersion.Gif89a;
      if (frame.UserInputFlag) return GifVersion.Gif89a;
    }
    return GifVersion.Gif87a;
  }

  internal static void WriteLogicalScreenDescriptor(Stream s, GifLogicalScreenDescriptor lsd) {
    Span<byte> buf = stackalloc byte[7];
    BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(0, 2), lsd.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(buf.Slice(2, 2), lsd.Height);
    var packed = 0;
    if (lsd.HasGlobalColorTable) packed |= 0x80;
    packed |= ((lsd.ColorResolution - 1) & 0x07) << 4;
    if (lsd.GlobalColorTableSorted) packed |= 0x08;
    packed |= lsd.GlobalColorTableSize & 0x07;
    buf[4] = (byte)packed;
    buf[5] = lsd.BackgroundColorIndex;
    buf[6] = lsd.PixelAspectRatio;
    s.Write(buf);
  }

  internal static void WriteNetscapeLoop(Stream s, ushort loopCount) {
    s.WriteByte(0x21);
    s.WriteByte(0xFF);
    s.WriteByte(11);
    s.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
    s.WriteByte(3); // sub-block size
    s.WriteByte(0x01);
    s.WriteByte((byte)(loopCount & 0xFF));
    s.WriteByte((byte)((loopCount >> 8) & 0xFF));
    s.WriteByte(0); // block terminator
  }

  internal static void WriteApplicationExtension(Stream s, GifApplicationExtension ext) {
    s.WriteByte(0x21);
    s.WriteByte(0xFF);
    s.WriteByte(11);
    var id = Encoding.ASCII.GetBytes(ext.Identifier.PadRight(8).Substring(0, 8));
    s.Write(id);
    var auth = new byte[3];
    Array.Copy(ext.AuthenticationCode, auth, Math.Min(3, ext.AuthenticationCode.Length));
    s.Write(auth);
    _WriteSubBlocks(s, ext.Data);
  }

  internal static void WriteCommentExtension(Stream s, byte[] data) {
    s.WriteByte(0x21);
    s.WriteByte(0xFE);
    _WriteSubBlocks(s, data);
  }

  internal static void WritePlainTextExtension(Stream s, GifPlainTextExtension p) {
    s.WriteByte(0x21);
    s.WriteByte(0x01);
    s.WriteByte(12);
    Span<byte> hdr = stackalloc byte[12];
    BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(0, 2), p.GridLeft);
    BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(2, 2), p.GridTop);
    BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(4, 2), p.GridWidth);
    BinaryPrimitives.WriteUInt16LittleEndian(hdr.Slice(6, 2), p.GridHeight);
    hdr[8] = p.CellWidth;
    hdr[9] = p.CellHeight;
    hdr[10] = p.ForegroundColorIndex;
    hdr[11] = p.BackgroundColorIndex;
    s.Write(hdr);
    _WriteSubBlocks(s, p.Text);
  }

  internal static void WriteFrame(Stream s, Frame frame, GifWriteOptions options) {
    var needsGce =
      frame.TransparentColorIndex != null
      || frame.DisposalMethod != FrameDisposalMethod.Unspecified
      || frame.Delay > TimeSpan.Zero
      || frame.UserInputFlag;
    if (needsGce) _WriteGraphicControlExtension(s, frame);

    var lct = frame.LocalColorTable is { Length: > 0 } ? frame.LocalColorTable : null;
    byte lctSizeExp = 0;
    // No local table: the frame is encoded against the global one, whose size we do not narrow to —
    // a frame may legally index past a short global table, and 8 bits always covers it.
    var paletteEntries = 256;
    if (lct != null) {
      lctSizeExp = GifColorTable.SizeExponent(lct.Length / 3);
      paletteEntries = GifColorTable.EntryCount(lctSizeExp);
    }

    // Image Descriptor
    s.WriteByte(0x2C);
    Span<byte> id = stackalloc byte[9];
    BinaryPrimitives.WriteUInt16LittleEndian(id.Slice(0, 2), frame.Left);
    BinaryPrimitives.WriteUInt16LittleEndian(id.Slice(2, 2), frame.Top);
    BinaryPrimitives.WriteUInt16LittleEndian(id.Slice(4, 2), frame.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(id.Slice(6, 2), frame.Height);
    var packed = 0;
    if (lct != null) {
      packed |= 0x80;
      if (frame.LocalColorTableSorted) packed |= 0x20;
      packed |= lctSizeExp & 0x07;
    }
    if (frame.IsInterlaced) packed |= 0x40;
    id[8] = (byte)packed;
    s.Write(id);

    if (lct != null)
      GifColorTable.Write(s, lct, lctSizeExp);

    // LZW-compressed pixel data. Interlace on emit if the frame is flagged interlaced.
    var pixels = frame.IsInterlaced ? _InterlacePixels(frame.PixelData, frame.Width, frame.Height) : frame.PixelData;
    // The code size has to cover whichever is wider, the palette or the indices actually used: a
    // frame whose pixels reach past its own colour table is malformed but common, and encoding it at
    // the table's width turns those indices into the clear and end-of-information codes.
    var lzwMinCodeSize = Math.Max(2, Math.Max(_BitsNeededForRange(paletteEntries), _BitsNeededForRange(_HighestIndex(pixels) + 1)));
    var encoded = GifLzwCodec.Encode(pixels, lzwMinCodeSize, _ToEncodeOptions(options));
    s.Write(encoded, 0, encoded.Length);
  }

  private static GifLzwCodec.EncodeOptions _ToEncodeOptions(GifWriteOptions options) => new(
    options.Compression switch {
      GifCompressionLevel.None => GifLzwCodec.CompressionLevel.None,
      GifCompressionLevel.Best => GifLzwCodec.CompressionLevel.Best,
      _ => GifLzwCodec.CompressionLevel.Standard,
    },
    options.DeferClear);

  private static void _WriteGraphicControlExtension(Stream s, Frame frame) {
    s.WriteByte(0x21);
    s.WriteByte(0xF9);
    s.WriteByte(4);
    Span<byte> gce = stackalloc byte[4];
    var packed = 0;
    packed |= ((byte)frame.DisposalMethod & 0x07) << 2;
    if (frame.UserInputFlag) packed |= 0x02;
    if (frame.TransparentColorIndex != null) packed |= 0x01;
    gce[0] = (byte)packed;
    BinaryPrimitives.WriteUInt16LittleEndian(gce.Slice(1, 2), (ushort)Math.Round(frame.Delay.TotalMilliseconds / 10));
    gce[3] = frame.TransparentColorIndex ?? 0;
    s.Write(gce);
    s.WriteByte(0); // block terminator
  }

  private static int _HighestIndex(byte[] pixels) {
    var highest = 0;
    foreach (var p in pixels)
      if (p > highest) highest = p;
    return highest;
  }

  private static int _BitsNeededForRange(int count) {
    if (count <= 2) return 1;
    var n = 0;
    var v = count - 1;
    while (v > 0) { ++n; v >>= 1; }
    return n;
  }

  private static byte[] _InterlacePixels(byte[] linear, int width, int height) {
    var output = new byte[linear.Length];
    var dst = 0;
    int[] starts = { 0, 4, 2, 1 };
    int[] strides = { 8, 8, 4, 2 };
    for (var pass = 0; pass < 4; ++pass) {
      for (var y = starts[pass]; y < height; y += strides[pass]) {
        Buffer.BlockCopy(linear, y * width, output, dst, width);
        dst += width;
      }
    }
    return output;
  }

  private static void _WriteSubBlocks(Stream s, byte[] data) {
    var offset = 0;
    while (offset < data.Length) {
      var chunk = Math.Min(255, data.Length - offset);
      s.WriteByte((byte)chunk);
      s.Write(data, offset, chunk);
      offset += chunk;
    }
    s.WriteByte(0); // terminator
  }
}
