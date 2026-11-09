#if NET10_0_OR_GREATER
using System.Runtime.Versioning;
#endif

namespace CABber;

/// <summary>Default <see cref="ICabinetExtractorFactory"/> implementation, backed by <see cref="CabinetExtractor"/>.</summary>
#if NET10_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public sealed class CabinetExtractorFactory : ICabinetExtractorFactory
{
    public ICabinetExtractor Open(string cabinetPath) => new CabinetExtractor(cabinetPath);
}
