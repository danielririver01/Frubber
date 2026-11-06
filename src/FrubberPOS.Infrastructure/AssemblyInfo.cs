using System.Runtime.CompilerServices;

// The failure-injection seams for infrastructure integration tests are not public API.
[assembly: InternalsVisibleTo("FrubberPOS.Tests")]
