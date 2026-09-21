using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer.Tests;

/// <summary>Deterministic pictures and containers the viewer tests are pointed at.</summary>
internal static class ViewerTestPictures {

  /// <summary>A picture of one flat colour, at whatever size is asked for.</summary>
  internal static RawImage Flat(int width, int height, byte blue, byte green, byte red, byte alpha = 255) {
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < pixels.Length; i += 4) {
      pixels[i] = blue;
      pixels[i + 1] = green;
      pixels[i + 2] = red;
      pixels[i + 3] = alpha;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  /// <summary>Writes a flat picture as a PNG and returns the file.</summary>
  internal static FileInfo WritePng(DirectoryInfo folder, string name, int width, int height, byte blue, byte green, byte red) {
    folder.Create();
    var file = new FileInfo(Path.Combine(folder.FullName, name));
    Assert.That(
      FormatRegistry.Write(Flat(width, height, blue, green, red), ImageFormat.Png, file),
      Is.True,
      $"the PNG writer refused the {width}x{height} fixture");
    return file;
  }

  /// <summary>Encodes a flat picture as a standalone PNG file's bytes.</summary>
  internal static byte[] PngBytes(int width, int height, byte blue, byte green, byte red) {
    var entry = FormatRegistry.GetEntry(ImageFormat.Png);
    Assert.That(entry?.ConvertFromRawImage, Is.Not.Null, "the registry has no PNG encoder");
    return entry!.ConvertFromRawImage!(Flat(width, height, blue, green, red));
  }

  /// <summary>
  /// Assembles an icon file byte by byte out of PNG-payload entries.
  /// </summary>
  /// <remarks>
  /// Built here rather than taken from a fixture so the test states the whole container: a six-byte
  /// ICONDIR of type 1, one sixteen-byte ICONDIRENTRY per picture with its side lengths, payload
  /// length and offset, then the payloads. That is the only way a test of "this file holds two
  /// pictures, and they are these two" is testing the reader rather than agreeing with whatever
  /// wrote the fixture.
  /// </remarks>
  internal static FileInfo WriteIcon(DirectoryInfo folder, string name, IReadOnlyList<byte[]> pngPayloads, IReadOnlyList<(int Width, int Height)> sizes) {
    folder.Create();
    const int directoryHeader = 6;
    const int directoryEntry = 16;

    var total = directoryHeader + directoryEntry * pngPayloads.Count;
    foreach (var payload in pngPayloads)
      total += payload.Length;

    var bytes = new byte[total];
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0), 0);                      // reserved
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 1);                      // 1 = icon
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)pngPayloads.Count);

    var offset = directoryHeader + directoryEntry * pngPayloads.Count;
    for (var i = 0; i < pngPayloads.Count; ++i) {
      var at = directoryHeader + directoryEntry * i;
      var (width, height) = sizes[i];

      // 256 is written as 0: the field is one byte and the format says zero means the maximum.
      bytes[at] = (byte)(width == 256 ? 0 : width);
      bytes[at + 1] = (byte)(height == 256 ? 0 : height);
      bytes[at + 2] = 0;                                                               // palette size, 0 = not indexed
      bytes[at + 3] = 0;                                                               // reserved
      BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 4), 1);               // colour planes
      BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at + 6), 32);              // bits per pixel
      BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 8), pngPayloads[i].Length);
      BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at + 12), offset);

      pngPayloads[i].CopyTo(bytes.AsSpan(offset));
      offset += pngPayloads[i].Length;
    }

    var file = new FileInfo(Path.Combine(folder.FullName, name));
    File.WriteAllBytes(file.FullName, bytes);
    file.Refresh();
    return file;
  }
}

/// <summary>A folder under the temporary directory that takes itself away again.</summary>
internal sealed class ScratchFolder : IDisposable {

  internal ScratchFolder(string label) {
    this.Directory = new(Path.Combine(Path.GetTempPath(), $"crush-viewer-tests-{label}-{Guid.NewGuid():N}"));
    this.Directory.Create();
  }

  internal DirectoryInfo Directory { get; }

  internal DirectoryInfo Sub(string name) {
    var child = new DirectoryInfo(Path.Combine(this.Directory.FullName, name));
    child.Create();
    return child;
  }

  public void Dispose() {
    try {
      this.Directory.Delete(true);
    } catch (IOException) {
      // A leftover temporary folder is not worth reddening a test run over.
    } catch (UnauthorizedAccessException) {
      // Same: something else still has a handle on a file we wrote.
    }
  }
}
