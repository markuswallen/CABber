#if NETSTANDARD2_0
// `init`-only setters (used by records and CabinetBuilderOptions) require this marker type,
// which ships in the runtime for net5.0+ but is absent from the netstandard2.0 reference assemblies.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
