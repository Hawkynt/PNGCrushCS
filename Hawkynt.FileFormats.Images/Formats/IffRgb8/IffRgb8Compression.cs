namespace FileFormat.IffRgb8;

/// <summary>Compression methods for IFF RGB8 BODY data.</summary>
/// <remarks>
/// The BMHD carries this byte because ILBM's does, but an RGB8 body is always the run scheme built
/// into the pixel units: four bytes carrying a colour and how many pixels take it. ByteRun1 — the
/// ILBM scheme, which packs bytes and knows nothing of pixels — was written and read here instead,
/// so the files agreed with this project's own reader and with nothing else.
/// </remarks>
public enum IffRgb8Compression : byte {

  /// <summary>The run scheme built into the pixel units, which is what RGB8 means.</summary>
  ColorRuns = 4,
}
