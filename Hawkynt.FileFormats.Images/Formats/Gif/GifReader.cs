using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Gif;

/// <summary>Reads a GIF byte stream into a <see cref="GifFile"/>. Tolerant of trailing garbage and
/// truncated blocks — partial frames are kept where possible.</summary>
public static class GifReader {

  public static GifFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    using var fs = file.OpenRead();
    return FromStream(fs);
  }

  public static GifFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static GifFile FromSpan(ReadOnlySpan<byte> data) {
    using var ms = new MemoryStream(data.ToArray(), writable: false);
    return FromStream(ms);
  }

  public static GifFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);

    var version = _ReadSignatureAndVersion(stream);
    var lsd = _ReadLogicalScreenDescriptor(stream);

    byte[]? globalColorTable = null;
    var globalSorted = lsd.GlobalColorTableSorted;
    if (lsd.HasGlobalColorTable)
      globalColorTable = _ReadColorTable(stream, lsd.GlobalColorTableEntryCount);

    var frames = new List<Frame>();
    var globalComments = new List<GifCommentExtension>();
    var globalAppExtensions = new List<GifApplicationExtension>();
    var globalPlainText = new List<GifPlainTextExtension>();
    var loopCount = LoopCount.PlayOnce;

    // Per-frame pending GCE state.
    var pendingDelay = TimeSpan.Zero;
    var pendingDisposal = FrameDisposalMethod.Unspecified;
    var pendingUserInput = false;
    byte? pendingTransparent = null;

    while (true) {
      var introducer = stream.ReadByte();
      if (introducer < 0) break; // truncated — stop gracefully
      switch (introducer) {
        case 0x3B: // Trailer
          return new GifFile {
            Version = version,
            LogicalScreenDescriptor = lsd,
            GlobalColorTable = globalColorTable,
            LoopCount = loopCount,
            Frames = frames,
            Comments = globalComments,
            ApplicationExtensions = globalAppExtensions,
            PlainTextExtensions = globalPlainText,
          };

        case 0x2C: { // Image Descriptor → frame
          var frame = _ReadFrame(stream, pendingDelay, pendingDisposal, pendingUserInput, pendingTransparent);
          frames.Add(frame);
          pendingDelay = TimeSpan.Zero;
          pendingDisposal = FrameDisposalMethod.Unspecified;
          pendingUserInput = false;
          pendingTransparent = null;
          break;
        }

        case 0x21: { // Extension Introducer
          var label = stream.ReadByte();
          if (label < 0) goto Done;
          switch (label) {
            case 0xF9: { // Graphic Control Extension
              var blockSize = stream.ReadByte();
              if (blockSize != 4) { _SkipSubBlocks(stream); break; }
              Span<byte> gce = stackalloc byte[4];
              _ReadExactly(stream, gce);
              _ReadSubBlockTerminator(stream);
              var packed = gce[0];
              pendingDisposal = (FrameDisposalMethod)((packed >> 2) & 0x07);
              pendingUserInput = (packed & 0x02) != 0;
              var hasTransparent = (packed & 0x01) != 0;
              var delayCs = BinaryPrimitives.ReadUInt16LittleEndian(gce.Slice(1, 2));
              pendingDelay = TimeSpan.FromMilliseconds(delayCs * 10);
              pendingTransparent = hasTransparent ? gce[3] : null;
              break;
            }
            case 0xFE: { // Comment Extension
              var data = _ReadSubBlocks(stream);
              globalComments.Add(new GifCommentExtension(data));
              break;
            }
            case 0xFF: { // Application Extension
              var appBlockSize = stream.ReadByte();
              if (appBlockSize != 11) { _SkipSubBlocks(stream); break; }
              Span<byte> idAuth = stackalloc byte[11];
              _ReadExactly(stream, idAuth);
              var identifier = Encoding.ASCII.GetString(idAuth.Slice(0, 8));
              var auth = idAuth.Slice(8, 3).ToArray();
              var payload = _ReadSubBlocks(stream);
              var ext = new GifApplicationExtension(identifier, auth, payload);
              if (ext.IsNetscapeLoop && ext.NetscapeLoopCount is { } count)
                loopCount = count == 0 ? LoopCount.LoopForever : LoopCount.LoopTimes(count);
              globalAppExtensions.Add(ext);
              break;
            }
            case 0x01: { // Plain Text Extension
              var ptBlockSize = stream.ReadByte();
              if (ptBlockSize != 12) { _SkipSubBlocks(stream); break; }
              Span<byte> pt = stackalloc byte[12];
              _ReadExactly(stream, pt);
              var text = _ReadSubBlocks(stream);
              globalPlainText.Add(new GifPlainTextExtension(
                GridLeft: BinaryPrimitives.ReadUInt16LittleEndian(pt.Slice(0, 2)),
                GridTop: BinaryPrimitives.ReadUInt16LittleEndian(pt.Slice(2, 2)),
                GridWidth: BinaryPrimitives.ReadUInt16LittleEndian(pt.Slice(4, 2)),
                GridHeight: BinaryPrimitives.ReadUInt16LittleEndian(pt.Slice(6, 2)),
                CellWidth: pt[8],
                CellHeight: pt[9],
                ForegroundColorIndex: pt[10],
                BackgroundColorIndex: pt[11],
                Text: text));
              break;
            }
            default:
              _SkipSubBlocks(stream);
              break;
          }
          break;
        }

        default:
          // Garbage byte in the stream — skip and keep going (tolerant mode).
          break;
      }
    }

  Done:
    return new GifFile {
      Version = version,
      LogicalScreenDescriptor = lsd,
      GlobalColorTable = globalColorTable,
      LoopCount = loopCount,
      Frames = frames,
      Comments = globalComments,
      ApplicationExtensions = globalAppExtensions,
      PlainTextExtensions = globalPlainText,
    };
  }

  // ---- low-level helpers ----

  private static GifVersion _ReadSignatureAndVersion(Stream stream) {
    Span<byte> sig = stackalloc byte[6];
    _ReadExactly(stream, sig);
    if (sig[0] != 'G' || sig[1] != 'I' || sig[2] != 'F')
      throw new InvalidDataException("Not a GIF file (missing 'GIF' signature).");
    var versionStr = Encoding.ASCII.GetString(sig.Slice(3, 3));
    return versionStr switch {
      "87a" => GifVersion.Gif87a,
      "89a" => GifVersion.Gif89a,
      _ => GifVersion.Gif89a, // tolerate weird "9Xa" variants by defaulting to 89a
    };
  }

  private static GifLogicalScreenDescriptor _ReadLogicalScreenDescriptor(Stream stream) {
    Span<byte> lsd = stackalloc byte[7];
    _ReadExactly(stream, lsd);
    var packed = lsd[4];
    return new GifLogicalScreenDescriptor(
      Width: BinaryPrimitives.ReadUInt16LittleEndian(lsd.Slice(0, 2)),
      Height: BinaryPrimitives.ReadUInt16LittleEndian(lsd.Slice(2, 2)),
      HasGlobalColorTable: (packed & 0x80) != 0,
      ColorResolution: (byte)(((packed >> 4) & 0x07) + 1),
      GlobalColorTableSorted: (packed & 0x08) != 0,
      GlobalColorTableSize: (byte)(packed & 0x07),
      BackgroundColorIndex: lsd[5],
      PixelAspectRatio: lsd[6]);
  }

  private static byte[] _ReadColorTable(Stream stream, int entryCount) {
    var buf = new byte[entryCount * 3];
    _ReadExactly(stream, buf);
    return buf;
  }

  private static Frame _ReadFrame(Stream stream, TimeSpan delay, FrameDisposalMethod disposal, bool userInput, byte? transparent) {
    Span<byte> id = stackalloc byte[9];
    _ReadExactly(stream, id);
    var left = BinaryPrimitives.ReadUInt16LittleEndian(id.Slice(0, 2));
    var top = BinaryPrimitives.ReadUInt16LittleEndian(id.Slice(2, 2));
    var width = BinaryPrimitives.ReadUInt16LittleEndian(id.Slice(4, 2));
    var height = BinaryPrimitives.ReadUInt16LittleEndian(id.Slice(6, 2));
    var packed = id[8];
    var hasLct = (packed & 0x80) != 0;
    var interlaced = (packed & 0x40) != 0;
    var lctSorted = (packed & 0x20) != 0;
    var lctSizeExp = packed & 0x07;
    var lctEntries = hasLct ? 1 << (lctSizeExp + 1) : 0;
    byte[]? lct = hasLct ? _ReadColorTable(stream, lctEntries) : null;

    var pixelCount = width * height;
    byte[] decoded;
    try { decoded = GifLzwCodec.Decode(stream, pixelCount); }
    catch (EndOfStreamException) { decoded = []; }       // truncated LZW — partial frame is OK
    catch (InvalidDataException) { decoded = []; }
    if (decoded.Length > pixelCount) Array.Resize(ref decoded, pixelCount);
    else if (decoded.Length < pixelCount) {
      // Truncated frame — pad with the background index so downstream renderers still get a full grid.
      var pad = new byte[pixelCount];
      decoded.AsSpan().CopyTo(pad);
      decoded = pad;
    }

    if (interlaced) decoded = _DeinterlaceTopDown(decoded, width, height);

    return new Frame {
      Left = left, Top = top, Width = width, Height = height,
      LocalColorTable = lct,
      LocalColorTableSorted = lctSorted,
      IsInterlaced = interlaced,
      PixelData = decoded,
      Delay = delay,
      DisposalMethod = disposal,
      UserInputFlag = userInput,
      TransparentColorIndex = transparent,
    };
  }

  private static byte[] _DeinterlaceTopDown(byte[] interlaced, int width, int height) {
    // GIF 4-pass interlace: rows 0,8,16,...; 4,12,...; 2,6,10,...; 1,3,5,...
    var output = new byte[interlaced.Length];
    var src = 0;
    int[] starts = { 0, 4, 2, 1 };
    int[] strides = { 8, 8, 4, 2 };
    for (var pass = 0; pass < 4; ++pass) {
      for (var y = starts[pass]; y < height; y += strides[pass]) {
        Buffer.BlockCopy(interlaced, src, output, y * width, width);
        src += width;
      }
    }
    return output;
  }

  private static byte[] _ReadSubBlocks(Stream stream) {
    using var collector = new MemoryStream();
    while (true) {
      var size = stream.ReadByte();
      if (size <= 0) return collector.ToArray();
      var buf = new byte[size];
      _ReadExactly(stream, buf);
      collector.Write(buf, 0, size);
    }
  }

  private static void _SkipSubBlocks(Stream stream) {
    while (true) {
      var size = stream.ReadByte();
      if (size <= 0) return;
      for (var i = 0; i < size; ++i)
        if (stream.ReadByte() < 0) return;
    }
  }

  private static void _ReadSubBlockTerminator(Stream stream) {
    var b = stream.ReadByte();
    if (b > 0) {
      // Non-terminator — read the spurious data and stop at terminator.
      var buf = new byte[b];
      _ReadExactly(stream, buf);
      _SkipSubBlocks(stream);
    }
  }

  private static void _ReadExactly(Stream s, Span<byte> dst) {
    var total = 0;
    while (total < dst.Length) {
      var n = s.Read(dst.Slice(total));
      if (n == 0) throw new EndOfStreamException("Unexpected EOF reading GIF data.");
      total += n;
    }
  }
}
