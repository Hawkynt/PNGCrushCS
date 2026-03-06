using System;
using System.IO;

namespace Crush.TestUtilities;

/// <summary>Disposable helper that creates a unique temp file path and deletes it on dispose</summary>
public sealed class TempFileScope : IDisposable {

  /// <summary>Full path to the temporary file</summary>
  public string Path { get; }

  /// <summary>Create a temp file scope with the given extension</summary>
  public TempFileScope(string extension = ".tmp") {
    Path = System.IO.Path.Combine(
      System.IO.Path.GetTempPath(),
      $"crush_test_{Guid.NewGuid():N}{extension}");
  }

  public void Dispose() {
    try {
      if (File.Exists(this.Path))
        File.Delete(this.Path);
    } catch {
      // Ignore cleanup failures in tests
    }
  }
}
