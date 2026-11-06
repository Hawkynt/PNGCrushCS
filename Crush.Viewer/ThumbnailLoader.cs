using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer;

/// <summary>Decodes a folder's pictures down to icon size without holding up the window.</summary>
/// <remarks>
/// <para>
/// Decoding is what a thumbnail costs — around twenty milliseconds for a picture of ordinary size,
/// against one for reading its bytes — so a folder of two hundred is four seconds of a single core
/// doing arithmetic while the rest idle. The work is handed to as many decoders as there are
/// processors.
/// </para>
/// <para>
/// The queue is served newest-request-first. A folder is queued in listing order, and
/// <see cref="Prioritise"/> puts a range back on top of it, so the pictures somebody is looking at
/// are decoded ahead of the ones at the far end of a long folder. Results therefore arrive out of
/// order, which the caller has to be able to place: the shell adds each decoded thumbnail to the
/// image list and points the item it belongs to at whichever slot it landed in, so arrival order
/// carries no meaning.
/// </para>
/// </remarks>
internal sealed class ThumbnailLoader : IDisposable {

  private readonly int _edge;
  private readonly Action<int, int[]> _onDecoded;
  private readonly Action<Action> _toUiThread;

  /// <summary>The folder being decoded, or <c>null</c> when none is.</summary>
  /// <remarks>
  /// One object per folder, rather than fields reassigned in place, because a decoder abandoned
  /// half-way through the previous folder is still running: it must find the queue it was started
  /// on, not the one that replaced it. Sharing the queue let an abandoned decoder claim an entry of
  /// the new folder and then throw the result away as cancelled, which is a thumbnail that never
  /// arrives and nothing left in the queue to produce it again.
  /// </remarks>
  private Batch? _batch;

  /// <param name="edge">Side length in pixels of the square each thumbnail is fitted into.</param>
  /// <param name="onDecoded">Called on the UI thread with the file's position and its pixels.</param>
  /// <param name="toUiThread">Marshals a callback onto the thread that owns the window.</param>
  internal ThumbnailLoader(int edge, Action<int, int[]> onDecoded, Action<Action> toUiThread) {
    this._edge = edge;
    this._onDecoded = onDecoded;
    this._toUiThread = toUiThread;
  }

  /// <summary>Abandons the folder being decoded and starts on a new one.</summary>
  internal void Start(IReadOnlyList<FileInfo> files) {
    this.Cancel();
    if (files.Count == 0)
      return;

    var batch = new Batch(files);
    this._batch = batch;

    var decoders = Math.Min(Environment.ProcessorCount, files.Count);
    for (var i = 0; i < decoders; ++i)
      _ = Task.Run(() => this._Decode(batch), batch.Token);
  }

  /// <summary>Asks for the <paramref name="count"/> pictures from <paramref name="first"/> next.</summary>
  /// <remarks>
  /// Putting them on top of the queue rather than replacing it keeps the rest of the folder queued:
  /// walking a long folder asks for one neighbourhood after another, and each has to be served first
  /// without discarding what the previous one did not reach.
  /// </remarks>
  internal void Prioritise(int first, int count) => this._batch?.Prioritise(first, count);

  /// <summary>Abandons whatever folder is being decoded.</summary>
  internal void Cancel() => Interlocked.Exchange(ref this._batch, null)?.Abandon();

  private void _Decode(Batch batch) {
    while (!batch.Token.IsCancellationRequested) {
      var index = batch.Take(out var file);
      if (index < 0 || file == null)
        return;

      int[] pixels;
      try {
        var raw = FormatRegistry.Read(file);
        pixels = raw == null ? new int[this._edge * this._edge] : ImageBridge.ToThumbnailArgb(raw, this._edge);
      } catch {
        pixels = new int[this._edge * this._edge];
      }

      if (batch.Token.IsCancellationRequested)
        return;

      var slot = index;
      var decoded = pixels;
      try {
        this._toUiThread(() => {
          if (!batch.Token.IsCancellationRequested)
            this._onDecoded(slot, decoded);
        });
      } catch (Exception) {
        // The window went away between the decode and the hand-off. Nothing is left to show it to.
        return;
      }
    }
  }

  public void Dispose() => this.Cancel();

  /// <summary>One folder's work: the files, what is left of them, and the switch that drops it all.</summary>
  private sealed class Batch {

    private readonly FileInfo[] _files;
    private readonly bool[] _taken;
    private readonly List<int> _pending;
    private readonly CancellationTokenSource _cancellation = new();

    internal Batch(IReadOnlyList<FileInfo> files) {
      this._files = new FileInfo[files.Count];
      for (var i = 0; i < files.Count; ++i)
        this._files[i] = files[i];

      this._taken = new bool[files.Count];

      // Queued in reverse, so taking from the end hands the folder out in listing order until
      // somebody asks for a neighbourhood in particular.
      this._pending = new(files.Count);
      for (var i = files.Count - 1; i >= 0; --i)
        this._pending.Add(i);

      this.Token = this._cancellation.Token;
    }

    internal CancellationToken Token { get; }

    internal void Prioritise(int first, int count) {
      if (count < 1)
        return;

      lock (this._pending) {
        var last = Math.Min(this._files.Length - 1, first + count - 1);
        for (var i = last; i >= Math.Max(0, first); --i)
          if (!this._taken[i])
            this._pending.Add(i);
      }
    }

    /// <summary>Takes the next index to decode, or -1 when nothing is left.</summary>
    /// <remarks>
    /// An index already handed out is skipped rather than decoded twice:
    /// <see cref="Prioritise"/> queues a range that may overlap entries still waiting their turn in
    /// listing order.
    /// </remarks>
    internal int Take(out FileInfo? file) {
      lock (this._pending) {
        while (this._pending.Count > 0) {
          var last = this._pending.Count - 1;
          var index = this._pending[last];
          this._pending.RemoveAt(last);
          if (this._taken[index])
            continue;

          this._taken[index] = true;
          file = this._files[index];
          return index;
        }
      }

      file = null;
      return -1;
    }

    internal void Abandon() {
      lock (this._pending)
        this._pending.Clear();

      try {
        this._cancellation.Cancel();
      } catch (ObjectDisposedException) {
        return;
      }

      // Not disposed here: the decoders still running read Token until they notice, and the shell's
      // own hand-off closures read it after that. Letting the source outlive them costs one finalizable
      // object per folder and avoids handing a disposed token to a thread that is mid-decode.
    }
  }
}
