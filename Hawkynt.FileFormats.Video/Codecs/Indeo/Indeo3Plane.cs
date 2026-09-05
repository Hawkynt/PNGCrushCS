using System;

namespace FileFormat.Codecs.Indeo;

/// <summary>
/// One plane of an Indeo 3 picture, held twice over.
/// </summary>
/// <remarks>
/// <b>Two buffers and not one.</b> A frame states which of the two it is written into, and cells that
/// are motion-compensated read the other one; a stream alternates between them, but not necessarily on
/// every frame, so neither buffer is "the previous frame" in general. Keeping both is the whole of the
/// codec's inter-frame model.
/// <para/>
/// <b>A line above the picture.</b> Intra cells predict from the line above the one they start on, and
/// the cells on the top row have none — so a row of the neutral sample 0x40 sits in front of the
/// picture and every offset is measured past it. It is written once, when the buffers are made, and
/// read for as long as they last.
/// <para/>
/// The pitch is padded to a multiple of sixteen because the coding modes write four, eight and — while
/// averaging — sixteen bytes at a time from a block's left edge, and a block on the right edge of a
/// narrow picture would otherwise write past the row.
/// </remarks>
internal sealed class Indeo3Plane {

  /// <summary>The sample an intra cell on the top row of the picture predicts from.</summary>
  private const byte _NEUTRAL = 0x40;

  private readonly byte[][] _buffers = new byte[2][];

  internal Indeo3Plane(int width, int height) {
    this.Width = width;
    this.Height = height;
    this.Pitch = Align(width, 16);

    // One line more than the picture: the prediction line in front of it.
    var size = this.Pitch * (height + 1);
    for (var i = 0; i < 2; ++i) {
      var buffer = this._buffers[i] = new byte[size];
      buffer.AsSpan(0, this.Pitch).Fill(_NEUTRAL);
    }
  }

  /// <summary>The plane's width in samples, which for chrominance is padded to a multiple of four.</summary>
  internal int Width { get; }

  /// <summary>The plane's height in samples.</summary>
  internal int Height { get; }

  /// <summary>The distance between two rows in a buffer.</summary>
  internal int Pitch { get; }

  /// <summary>The offset of the picture's first row inside a buffer, past the prediction line.</summary>
  internal int Origin => this.Pitch;

  /// <summary>One of the two buffers this plane is written into and predicted from.</summary>
  internal byte[] Buffer(int which) => this._buffers[which];

  /// <summary>
  /// The plane as eight-bit samples for display: seven bits as coded, doubled to fill the byte.
  /// </summary>
  /// <remarks>
  /// Indeo 3 codes seven bits a sample and the eighth is never anything: every write masks it away, so
  /// a plane holds 0 to 127 and the picture it stands for is that doubled. Trimming happens here too —
  /// the buffers are padded out to whole blocks in both directions and the picture is whatever the
  /// frame said it was.
  /// </remarks>
  internal byte[] ToSamples(int which, int width, int height) {
    var buffer = this._buffers[which];
    var columns = Math.Min(width, this.Width);
    var rows = Math.Min(height, this.Height);
    var samples = new byte[width * height];

    for (var y = 0; y < rows; ++y) {
      var source = this.Origin + y * this.Pitch;
      var target = y * width;
      for (var x = 0; x < columns; ++x)
        samples[target + x] = (byte)((buffer[source + x] & 0x7F) << 1);
    }

    return samples;
  }

  /// <summary>Rounds up to the next multiple of a power of two.</summary>
  internal static int Align(int value, int to) => (value + to - 1) & ~(to - 1);
}
