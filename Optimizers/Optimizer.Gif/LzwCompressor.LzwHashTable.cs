using System.Runtime.CompilerServices;

namespace Optimizer.Gif;

internal static partial class LzwCompressor {
  /// <summary>Open-addressing hash table with generation counter for O(1) reset</summary>
  private struct LzwHashTable() {
    private const int SIZE = 8192;
    private const int MASK = SIZE - 1;
    private readonly long[] _keys = new long[SIZE];
    private readonly ushort[] _values = new ushort[SIZE];
    private readonly int[] _generations = new int[SIZE];
    private int _currentGen = 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() {
      ++this._currentGen;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash(long key) {
      return (int)(((ulong)key * 2654435761uL) >> 19) & MASK;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(long key, out ushort value) {
      var slot = Hash(key);
      for (var i = 0; i < SIZE; ++i) {
        var idx = (slot + i) & MASK;
        if (this._generations[idx] != this._currentGen) {
          value = 0;
          return false;
        }

        if (this._keys[idx] == key) {
          value = this._values[idx];
          return true;
        }
      }

      value = 0;
      return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(long key, ushort value) {
      var slot = Hash(key);
      for (var i = 0; i < SIZE; ++i) {
        var idx = (slot + i) & MASK;
        if (this._generations[idx] != this._currentGen) {
          this._keys[idx] = key;
          this._values[idx] = value;
          this._generations[idx] = this._currentGen;
          return;
        }
      }
    }
  }
}
