namespace Optimizer.Gif;

/// <summary>What the LZW encoder does once its 4096-entry dictionary is full. Every mode produces a
/// stream any conforming decoder reads back identically, so this is purely a size trade; which one
/// wins depends on the frame, which is why the optimizer tries them and keeps the smallest.</summary>
public enum LzwMode {

  /// <summary>Clear the dictionary and start again the moment it fills. Never catastrophic, and the
  /// only mode that re-learns when a frame changes character part-way through.</summary>
  Standard = 0,

  /// <summary>Keep the full dictionary, but watch how many output bits each input pixel is costing and
  /// clear once that ratio degrades by more than 10%. Most of <see cref="FrozenDictionary"/>'s win
  /// without its worst case.</summary>
  DeferredClear = 1,

  /// <summary>Keep the full dictionary as a static codebook and never clear it again — the GIF spec's
  /// own "deferred clear code". Wins wherever a frame stays statistically uniform, which covers most
  /// cel animation and screen capture, and loses badly wherever it does not.</summary>
  FrozenDictionary = 2,
}
