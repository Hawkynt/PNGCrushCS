// The image registry generator emits this host-level runtime bridge by its unqualified name.
// Optimizer.Image reuses the public descriptor rather than maintaining a parallel capability model.
global using RawImageWriteCapability = Hawkynt.FileFormats.Images.RawImageWriteCapability;
