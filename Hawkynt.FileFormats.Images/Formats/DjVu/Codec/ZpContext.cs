namespace FileFormat.DjVu.Codec;

/// <summary>
/// Adaptive probability state used by the DjVu ZP arithmetic coder.
/// The low bit is the current most-probable symbol; the complete byte indexes the format's state table.
/// </summary>
internal struct ZpContext {

  public byte Value;

  public ZpContext() => Value = 0;

  public ZpContext(byte value) => Value = value;
}
