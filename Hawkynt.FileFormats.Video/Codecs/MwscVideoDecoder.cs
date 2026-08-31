using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>Decodes MatchWare Screen Capture Codec (<c>MWSC</c>) video.</summary>
/// <remarks>
/// Adapted from FFmpeg's <c>libavcodec/mwsc.c</c>, copyright (c) 2018 Paul B Mahol, distributed
/// there under LGPL-2.1-or-later. This adaptation is distributed with PNGCrushCS under
/// LGPL-3.0-or-later.
/// <para/>
/// A packet is one zlib stream containing a bottom-up RLE walk. Every command begins with a 24-bit
/// little-endian value and one opcode byte. Opcodes 1..254 repeat the 24-bit BGR value that many
/// pixels; opcode zero extends that count to a following 32-bit value; opcode 255 instead interprets
/// the 24-bit value as a number of pixels copied unchanged from the previous frame at the same
/// position.
/// </remarks>
public sealed class MwscVideoDecoder : IVideoCodecDecoder<MwscVideoDecoder> {

  private static readonly CodecTag _Tag = CodecTag.FromCharacters("MWSC");

  private readonly int _width;
  private readonly int _height;
  private readonly int _streamIndex;
  private byte[]? _previous;

  private MwscVideoDecoder(int width, int height, int streamIndex) {
    this._width = width;
    this._height = height;
    this._streamIndex = streamIndex;
  }

  /// <summary>Gets the codec name.</summary>
  public static string CodecName => "MatchWare Screen Capture Codec";

  /// <summary>Determines whether the specified media stream is supported.</summary>
  public static bool Accepts(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    return stream.Kind == MediaStreamKind.Video && stream.Codec.EqualsIgnoringCase(_Tag);
  }

  /// <summary>Creates a decoder for the specified media stream.</summary>
  public static MwscVideoDecoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.Width <= 0 || stream.Height <= 0)
      throw new InvalidDataException(
        $"MWSC stream {stream.Index} states a picture of {stream.Width}x{stream.Height}, which has no pixels.");
    if ((long)stream.Width * stream.Height * 3 > int.MaxValue)
      throw new InvalidDataException($"MWSC stream {stream.Index}'s frame is too large to hold in memory.");
    return new(stream.Width, stream.Height, stream.Index);
  }

  /// <summary>Attempts to decode the specified coded packet into a raw image frame.</summary>
  public bool TryDecode(CodedPacket packet, out RawImage frame) {
    var commands = _Inflate(packet.Data.Span, checked(this._width * this._height * 32), this._streamIndex);
    var current = new byte[checked(this._width * this._height * 3)];
    var at = 0;
    var pixel = 0;
    var totalPixels = checked(this._width * this._height);

    while (at < commands.Length) {
      if (commands.Length - at < 4)
        throw new InvalidDataException($"MWSC stream {this._streamIndex} ends in the middle of an RLE command.");

      var value = commands[at] | commands[at + 1] << 8 | commands[at + 2] << 16;
      var opcode = commands[at + 3];
      at += 4;

      long count = opcode;
      if (opcode == 0) {
        if (commands.Length - at < 4)
          throw new InvalidDataException($"MWSC stream {this._streamIndex} omits an extended run length.");
        count = BinaryPrimitives.ReadUInt32LittleEndian(commands.AsSpan(at));
        at += 4;
      } else if (opcode == 255) {
        count = value;
      }

      if (count < 0 || count > totalPixels - pixel)
        throw new InvalidDataException(
          $"MWSC stream {this._streamIndex} writes {count} pixel(s) at {pixel}, past its {totalPixels}-pixel frame.");

      if (opcode == 255) {
        if (this._previous is null)
          throw new InvalidDataException($"MWSC stream {this._streamIndex} references a previous frame before one exists.");
        for (var i = 0L; i < count; ++i, ++pixel)
          this._CopyPreviousPixel(current, pixel);
      } else {
        for (var i = 0L; i < count; ++i, ++pixel)
          this._WritePixel(current, pixel, value);
      }
    }

    if (pixel != totalPixels)
      throw new InvalidDataException(
        $"MWSC stream {this._streamIndex} reconstructs {pixel} pixel(s), not its complete {totalPixels}-pixel picture.");

    this._previous = current;
    frame = new() {
      Width = this._width,
      Height = this._height,
      Format = PixelFormat.Bgr24,
      PixelData = (byte[])current.Clone(),
    };
    return true;
  }

  private void _WritePixel(byte[] frame, int codedPixel, int value) {
    var outputPixel = this._BottomUpToTopDown(codedPixel);
    var at = outputPixel * 3;
    frame[at] = (byte)value;
    frame[at + 1] = (byte)(value >> 8);
    frame[at + 2] = (byte)(value >> 16);
  }

  private void _CopyPreviousPixel(byte[] frame, int codedPixel) {
    var outputPixel = this._BottomUpToTopDown(codedPixel);
    var at = outputPixel * 3;
    frame[at] = this._previous![at];
    frame[at + 1] = this._previous[at + 1];
    frame[at + 2] = this._previous[at + 2];
  }

  private int _BottomUpToTopDown(int codedPixel) {
    var codedRow = codedPixel / this._width;
    var column = codedPixel % this._width;
    return (this._height - 1 - codedRow) * this._width + column;
  }

  private static byte[] _Inflate(ReadOnlySpan<byte> compressed, int maximum, int streamIndex) {
    try {
      using var input = new MemoryStream(compressed.ToArray(), writable: false);
      using var zlib = new ZLibStream(input, CompressionMode.Decompress);
      using var output = new MemoryStream();
      var buffer = new byte[8192];
      while (true) {
        var read = zlib.Read(buffer, 0, buffer.Length);
        if (read == 0)
          break;
        if (output.Length + read > maximum)
          throw new InvalidDataException(
            $"MWSC stream {streamIndex} inflates beyond its {maximum}-byte safety bound.");
        output.Write(buffer, 0, read);
      }
      return output.ToArray();
    } catch (InvalidDataException) {
      throw;
    } catch (Exception ex) when (ex is IOException or NotSupportedException) {
      throw new InvalidDataException($"MWSC stream {streamIndex} carries invalid zlib data.", ex);
    }
  }
}
