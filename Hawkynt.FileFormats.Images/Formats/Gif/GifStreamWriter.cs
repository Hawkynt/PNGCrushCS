using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Gif;

/// <summary>Writes a GIF incrementally: header first, then frames one at a time straight to the
/// output stream, then the trailer.</summary>
/// <remarks>
/// <para><see cref="GifWriter.ToBytes"/> needs the whole <see cref="GifFile"/> — every frame's pixel
/// data — resident before it can emit a byte. For an animation that is generated rather than loaded
/// that is the wrong shape: a 1920x1080 high-colour GIF built as 256 palette layers is half a
/// gigabyte of indexed pixels, all of it live at once, plus the output buffer beside it. This class
/// holds one frame.</para>
/// <para>The cost of streaming is that the version cannot be inferred. <see cref="GifWriter"/> scans
/// the frames and downgrades to GIF87a when nothing needs GIF89a; here the frames are not available
/// yet, so the version is whatever the caller states and defaults to
/// <see cref="GifVersion.Gif89a"/>.</para>
/// <para>Colour tables are padded out to the power-of-two entry count the descriptor declares, so a
/// caller may hand over a table of any length and still get a readable file.</para>
/// </remarks>
public sealed class GifStreamWriter : IDisposable {

  private readonly Stream _output;
  private readonly GifWriteOptions _options;
  private bool _completed;

  /// <summary>Starts a GIF on <paramref name="output"/>, emitting the signature, the Logical Screen
  /// Descriptor, the global colour table and the NETSCAPE2.0 loop extension immediately.</summary>
  /// <param name="output">Destination stream. Not disposed by this class.</param>
  /// <param name="logicalScreenDescriptor">Canvas description. Its
  /// <see cref="GifLogicalScreenDescriptor.GlobalColorTableSize"/> is widened if
  /// <paramref name="globalColorTable"/> needs more entries than it declares.</param>
  /// <param name="globalColorTable">Packed RGB triplets, or <c>null</c> for none. Padded to the next
  /// power-of-two entry count.</param>
  /// <param name="loopCount">Animation loop count; the NETSCAPE2.0 extension is emitted only when
  /// <see cref="LoopCount.IsPresent"/>.</param>
  /// <param name="version">Signature to write. Defaults to GIF89a.</param>
  /// <param name="options">Encoder options; defaults to <see cref="GifWriteOptions.Default"/>.</param>
  public GifStreamWriter(
    Stream output,
    GifLogicalScreenDescriptor logicalScreenDescriptor,
    byte[]? globalColorTable,
    LoopCount loopCount,
    GifVersion version = GifVersion.Gif89a,
    GifWriteOptions? options = null) {
    ArgumentNullException.ThrowIfNull(output);
    this._output = output;
    this._options = options ?? GifWriteOptions.Default;

    var lsd = logicalScreenDescriptor;
    var hasGct = globalColorTable is { Length: > 0 };
    byte gctSizeExp = 0;
    if (hasGct)
      gctSizeExp = Math.Max(lsd.GlobalColorTableSize, GifColorTable.SizeExponent(globalColorTable!.Length / 3));

    lsd = lsd with { HasGlobalColorTable = hasGct, GlobalColorTableSize = gctSizeExp };

    output.Write(Encoding.ASCII.GetBytes(version == GifVersion.Gif87a ? "GIF87a" : "GIF89a"));
    GifWriter.WriteLogicalScreenDescriptor(output, lsd);
    if (hasGct)
      GifColorTable.Write(output, globalColorTable!, gctSizeExp);

    if (loopCount.IsPresent)
      GifWriter.WriteNetscapeLoop(output, loopCount.Count);
  }

  /// <summary>Appends one frame — its Graphic Control Extension (when it needs one), image
  /// descriptor, optional local colour table and LZW data.</summary>
  public void WriteFrame(Frame frame) {
    ArgumentNullException.ThrowIfNull(frame);
    this._ThrowIfCompleted();
    GifWriter.WriteFrame(this._output, frame, this._options);
  }

  /// <summary>Appends a Comment Extension.</summary>
  public void WriteComment(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    this._ThrowIfCompleted();
    GifWriter.WriteCommentExtension(this._output, data);
  }

  /// <summary>Appends an Application Extension. A NETSCAPE2.0 loop is normally supplied through the
  /// constructor's <c>loopCount</c> instead; passing one here writes a second copy.</summary>
  public void WriteApplicationExtension(GifApplicationExtension extension) {
    ArgumentNullException.ThrowIfNull(extension);
    this._ThrowIfCompleted();
    GifWriter.WriteApplicationExtension(this._output, extension);
  }

  /// <summary>Appends a Plain Text Extension.</summary>
  public void WritePlainText(GifPlainTextExtension extension) {
    ArgumentNullException.ThrowIfNull(extension);
    this._ThrowIfCompleted();
    GifWriter.WritePlainTextExtension(this._output, extension);
  }

  /// <summary>Writes the trailer byte. Idempotent — a second call does nothing, so
  /// <see cref="Dispose"/> after an explicit <c>Complete</c> is safe.</summary>
  public void Complete() {
    if (this._completed)
      return;
    this._completed = true;
    this._output.WriteByte(0x3B);
  }

  /// <summary>Completes the file if it has not been completed yet. The output stream is the caller's
  /// to dispose.</summary>
  public void Dispose() => this.Complete();

  private void _ThrowIfCompleted() {
    if (this._completed)
      throw new InvalidOperationException("The GIF has already been completed; nothing can be appended after the trailer.");
  }
}
