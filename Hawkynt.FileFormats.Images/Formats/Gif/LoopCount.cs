namespace FileFormat.Gif;

/// <summary>The NETSCAPE2.0 application-extension loop count.</summary>
/// <remarks>
/// GIF doesn't natively encode "loop forever" vs "play once" the same way — the absence of the
/// NETSCAPE2.0 extension means "play once"; a count of 0 means "loop forever"; any other count is the
/// literal number of additional times to play after the first.
/// </remarks>
public readonly record struct LoopCount(ushort Count, bool IsPresent) {

  /// <summary>The NETSCAPE2.0 extension is absent — the animation plays exactly once.</summary>
  public static LoopCount PlayOnce => new(0, false);

  /// <summary>Alias for <see cref="PlayOnce"/> matching the external API.</summary>
  public static LoopCount NotSet => PlayOnce;

  /// <summary>The NETSCAPE2.0 extension is present with count = 0 — the animation loops indefinitely.</summary>
  public static LoopCount LoopForever => new(0, true);

  /// <summary>The NETSCAPE2.0 extension is present with the given count — the animation plays
  /// <c>count + 1</c> total times (count = 1 → plays twice).</summary>
  public static LoopCount LoopTimes(ushort count) => new(count, true);

  /// <summary>True when the loop is explicitly infinite (NETSCAPE2.0 present with count = 0).</summary>
  public bool IsInfinite => this.IsPresent && this.Count == 0;

  /// <summary>Alias for <see cref="IsPresent"/> matching the external <c>Hawkynt.GifFileFormat.LoopCount</c>'s API.</summary>
  public bool IsSet => this.IsPresent;

  /// <summary>Alias for <see cref="Count"/> matching the external <c>Hawkynt.GifFileFormat.LoopCount</c>'s API.</summary>
  public ushort Value => this.Count;

  /// <summary>True when the NETSCAPE2.0 extension is absent.</summary>
  public bool IsNotSet => !this.IsPresent;

  /// <summary>Alias for <see cref="LoopForever"/> matching the external API.</summary>
  public static LoopCount Infinite => LoopForever;

  /// <summary>NETSCAPE2.0 with count 1 — one repeat after the first play. Named as the external API
  /// names it.</summary>
  public static LoopCount Once => LoopTimes(1);

  /// <summary>NETSCAPE2.0 with count 2 — two repeats after the first play.</summary>
  public static LoopCount Twice => LoopTimes(2);

  /// <summary>A bare count is a present NETSCAPE2.0 extension carrying it.</summary>
  public static implicit operator LoopCount(ushort count) => LoopTimes(count);

  /// <summary>A null count is an absent NETSCAPE2.0 extension; anything else is a present one.</summary>
  public static implicit operator LoopCount(ushort? count) => count is { } c ? LoopTimes(c) : PlayOnce;
}
