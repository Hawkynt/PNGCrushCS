using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.QuickTimeRle.Tests;

/// <summary>
/// Writes QuickTime Animation frames and the sample description that says how to read them, so a
/// test can state exactly which opcode it is exercising.
/// </summary>
/// <remarks>
/// Built rather than checked in, for the same reason the MPEG-1 streams are: the paths worth testing
/// are largely the ones ffmpeg's encoder never produces. It writes no mid-line skip, no colour table,
/// and nothing at all below eight bits — it refuses to encode one, two and four bits, and refuses a
/// width that is not a whole number of coded units. Every one of those is a real Animation stream and
/// none of them can be reached by handing ffmpeg a picture.
/// <para/>
/// The arithmetic this exercises was settled against ffmpeg all the same, by writing streams here in
/// the same shape and asking ffmpeg what it read them as.
/// </remarks>
internal sealed class QuickTimeRleTestStream {

  private readonly List<byte> _bytes = [];

  /// <summary>Starts a frame that says which band of lines it touches.</summary>
  internal QuickTimeRleTestStream Frame(int startLine, int lines) {
    this._bytes.Clear();
    this._bytes.AddRange([0, 0, 0, 0]);
    this._Big16(0x0008);
    this._Big16(startLine);
    this._Big16(0);
    this._Big16(lines);
    this._Big16(0);
    return this;
  }

  /// <summary>Starts a frame that touches every line, which is what a header of zero means.</summary>
  internal QuickTimeRleTestStream Frame() {
    this._bytes.Clear();
    this._bytes.AddRange([0, 0, 0, 0]);
    this._Big16(0x0000);
    return this;
  }

  /// <summary>The byte that begins a line: one more than the number of units to step past.</summary>
  internal QuickTimeRleTestStream Skip(int units) {
    this._bytes.Add((byte)(units + 1));
    return this;
  }

  /// <summary>A skip in the middle of a line, which the zero opcode introduces.</summary>
  internal QuickTimeRleTestStream SkipAgain(int units) {
    this._bytes.Add(0);
    this._bytes.Add((byte)(units + 1));
    return this;
  }

  /// <summary>A literal copy of whole units, taken straight from the stream.</summary>
  internal QuickTimeRleTestStream Copy(int units, params byte[] data) {
    this._bytes.Add((byte)units);
    this._bytes.AddRange(data);
    return this;
  }

  /// <summary>One unit written a number of times over.</summary>
  internal QuickTimeRleTestStream Run(int times, params byte[] unit) {
    this._bytes.Add((byte)(256 - times));
    this._bytes.AddRange(unit);
    return this;
  }

  /// <summary>The marker that ends a line.</summary>
  internal QuickTimeRleTestStream EndLine() {
    this._bytes.Add(0xFF);
    return this;
  }

  /// <summary>One opcode of the one-bit shape: a skip and a code together, read as a pair.</summary>
  internal QuickTimeRleTestStream OneBitOpcode(bool startsLine, int units, sbyte code, params byte[] data) {
    this._bytes.Add((byte)((startsLine ? 0x80 : 0x00) | units));
    this._bytes.Add((byte)code);
    this._bytes.AddRange(data);
    return this;
  }

  /// <summary>An arbitrary byte, for the cases that are about a byte and not about an opcode.</summary>
  internal QuickTimeRleTestStream Raw(params byte[] data) {
    this._bytes.AddRange(data);
    return this;
  }

  /// <summary>Finishes the frame, filling in the length the first four bytes carry.</summary>
  internal byte[] End() {
    var frame = this._bytes.ToArray();
    BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), frame.Length);
    return frame;
  }

  private void _Big16(int value) {
    this._bytes.Add((byte)(value >> 8));
    this._bytes.Add((byte)value);
  }

  // ============================================================================================
  // The description a container hands across
  // ============================================================================================

  /// <summary>
  /// A visual sample entry for an Animation stream, with a colour table when one is given.
  /// </summary>
  /// <remarks>
  /// The whole box, header and all, because that is what a sample description is and what every
  /// container carries across as codec private data.
  /// </remarks>
  internal static byte[] SampleEntry(int width, int height, int depth, byte[]? palette = null) {
    var body = new List<byte>();
    body.AddRange(new byte[6]);                 // reserved
    body.AddRange([0, 1]);                      // data reference index
    body.AddRange(new byte[16]);                // version, revision, vendor, two qualities
    body.AddRange([(byte)(width >> 8), (byte)width, (byte)(height >> 8), (byte)height]);
    body.AddRange(new byte[8]);                 // resolutions
    body.AddRange(new byte[4]);                 // data size
    body.AddRange([0, 1]);                      // frame count
    body.AddRange(new byte[32]);                // compressor name
    body.AddRange([(byte)(depth >> 8), (byte)depth]);

    if (palette == null)
      body.AddRange([0xFF, 0xFF]);
    else {
      body.AddRange([0, 0]);
      var entries = palette.Length / 3;
      body.AddRange(new byte[4]);               // seed
      body.AddRange([0, 0]);                    // flags
      body.AddRange([(byte)((entries - 1) >> 8), (byte)(entries - 1)]);
      for (var i = 0; i < entries; ++i) {
        body.AddRange([0, 0]);
        body.AddRange([palette[i * 3], palette[i * 3]]);
        body.AddRange([palette[i * 3 + 1], palette[i * 3 + 1]]);
        body.AddRange([palette[i * 3 + 2], palette[i * 3 + 2]]);
      }
    }

    var box = new byte[8 + body.Count];
    BinaryPrimitives.WriteInt32BigEndian(box.AsSpan(0, 4), box.Length);
    "rle "u8.CopyTo(box.AsSpan(4));
    body.CopyTo(box, 8);
    return box;
  }

  /// <summary>The stream description a decoder is built from.</summary>
  internal static MediaStreamInfo Stream(int width, int height, int depth, byte[]? palette = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("rle "),
    Width = width,
    Height = height,
    BitsPerPixel = depth,
    CodecPrivateData = SampleEntry(width, height, depth, palette),
  };
}
