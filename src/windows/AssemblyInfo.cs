using System.Runtime.CompilerServices;
using System.Reflection;

[assembly: AssemblyTitle("Codex Token Status Bar")]
[assembly: AssemblyProduct("Codex Token Status Bar")]
[assembly: AssemblyVersion("1.9.0.0")]
[assembly: AssemblyFileVersion("1.9.0.0")]

// Test the internal protocol boundary directly without exposing it as a UI API.
[assembly: InternalsVisibleTo("FrontendRegression")]
