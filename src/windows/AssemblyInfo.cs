using System.Runtime.CompilerServices;

// Test the internal protocol boundary directly without exposing it as a UI API.
[assembly: InternalsVisibleTo("FrontendRegression")]
