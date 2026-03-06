using System;
using System.Drawing;
using System.IO;
using Hawkynt.GifFileFormat;

namespace Optimizer.Gif;

internal static class GifAssembler {
  private const byte GCT_PRESENT = 0x80;
  private const byte EXTENSION_INTRODUCER = 0x21;
  private const byte APPLICATION_EXTENSION = 0xFF;
  private const byte GRAPHIC_CONTROL_EXTENSION = 0xF9;
  private const byte BLOCK_TERMINATOR = 0x00;
  private const byte LCT_PRESENT = 0x80;
  private const byte USE_TRANSPARENCY = 0x01;
  private const byte NO_TRANSPARENCY = 0x00;
  private const byte IMAGE_SEPARATOR = 0x2C;
  private const byte FILE_TERMINATOR = 0x3B;

  public static byte[] Assemble(AssembledGif gif) {
    using var ms = new MemoryStream();
    using var writer = new BinaryWriter(ms);

    _WriteHeader(writer);
    _WriteLogicalScreenDescriptor(writer, gif.LogicalScreenSize, gif.BackgroundColorIndex, gif.GlobalColorTable);

    if (gif.GlobalColorTable != null)
      _WriteColorTable(writer, gif.GlobalColorTable);

    if (gif.LoopCount.IsSet)
      _WriteApplicationExtension(writer, gif.LoopCount.Value);

    foreach (var frame in gif.Frames) {
      _WriteGraphicsControlExtension(writer, frame.Delay, frame.DisposalMethod, frame.TransparentColorIndex);
      _WriteImageDescriptor(writer, frame.Size, frame.Position, frame.LocalColorTable);

      if (frame.LocalColorTable != null)
        _WriteColorTable(writer, frame.LocalColorTable);

      _WriteImageData(writer, frame.CompressedData, frame.BitsPerPixel);
    }

    _WriteTrailer(writer);
    writer.Flush();
    return ms.ToArray();
  }

  private static void _WriteHeader(BinaryWriter writer) => writer.Write("GIF89a"u8);

  private static void _WriteLogicalScreenDescriptor(BinaryWriter writer, Dimensions size, byte bgIndex, Color[]? gct) {
    writer.Write(size.Width);
    writer.Write(size.Height);

    byte packed = 0x70; // color resolution = 8 bits (7 << 4)
    if (gct is { Length: > 0 }) {
      packed |= GCT_PRESENT;
      packed |= _GetColorTableSizeBits(gct.Length);
    }

    writer.Write(packed);
    writer.Write(bgIndex);
    writer.Write((byte)0); // pixel aspect ratio
  }

  private static void _WriteColorTable(BinaryWriter writer, Color[] colorTable) {
    var tableSize = 1 << (_GetColorTableSizeBits(colorTable.Length) + 1);
    for (var i = 0; i < tableSize; ++i)
      if (i < colorTable.Length) {
        writer.Write(colorTable[i].R);
        writer.Write(colorTable[i].G);
        writer.Write(colorTable[i].B);
      } else {
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
      }
  }

  private static void _WriteApplicationExtension(BinaryWriter writer, ushort loopCount) {
    writer.Write(EXTENSION_INTRODUCER);
    writer.Write(APPLICATION_EXTENSION);
    writer.Write((byte)0x0B);
    writer.Write("NETSCAPE"u8);
    writer.Write("2.0"u8);
    writer.Write((byte)0x03);
    writer.Write((byte)0x01);
    writer.Write(loopCount);
    writer.Write(BLOCK_TERMINATOR);
  }

  private static void _WriteGraphicsControlExtension(BinaryWriter writer, TimeSpan delay,
    FrameDisposalMethod disposal, byte? transparentIndex) {
    writer.Write(EXTENSION_INTRODUCER);
    writer.Write(GRAPHIC_CONTROL_EXTENSION);
    writer.Write((byte)0x04);
    var packed = (byte)(((byte)disposal << 2) | (transparentIndex.HasValue ? USE_TRANSPARENCY : NO_TRANSPARENCY));
    writer.Write(packed);
    writer.Write((ushort)(delay.TotalMilliseconds / 10));
    writer.Write(transparentIndex ?? 0);
    writer.Write(BLOCK_TERMINATOR);
  }

  private static void _WriteImageDescriptor(BinaryWriter writer, Dimensions size, Offset position, Color[]? lct) {
    writer.Write(IMAGE_SEPARATOR);
    writer.Write(position.X);
    writer.Write(position.Y);
    writer.Write(size.Width);
    writer.Write(size.Height);

    if (lct is { Length: > 0 }) {
      var packed = (byte)(LCT_PRESENT | _GetColorTableSizeBits(lct.Length));
      writer.Write(packed);
    } else {
      writer.Write((byte)0);
    }
  }

  private static void _WriteImageData(BinaryWriter writer, byte[] compressedLzwData, byte bitsPerPixel) {
    writer.Write((byte)(bitsPerPixel == 1 ? 2 : bitsPerPixel));

    // Write sub-blocks
    var offset = 0;
    while (offset < compressedLzwData.Length) {
      var blockSize = Math.Min(255, compressedLzwData.Length - offset);
      writer.Write((byte)blockSize);
      writer.Write(compressedLzwData, offset, blockSize);
      offset += blockSize;
    }

    writer.Write(BLOCK_TERMINATOR);
  }

  private static void _WriteTrailer(BinaryWriter writer) => writer.Write(FILE_TERMINATOR);

  private static byte _GetColorTableSizeBits(int entryCount) {
    return entryCount switch {
      <= 2 => 0,
      <= 4 => 1,
      <= 8 => 2,
      <= 16 => 3,
      <= 32 => 4,
      <= 64 => 5,
      <= 128 => 6,
      _ => 7
    };
  }
}