using System.Runtime.CompilerServices;

// Lets Tests/Editor exercise the internal parity-critical classes (hashing, signing, session
// rules) directly, without weakening their `internal` visibility for game code -- those types
// are an implementation detail of Roas's public facade, not part of the supported API surface.
[assembly: InternalsVisibleTo("RoasSensor.Tests.Editor")]
