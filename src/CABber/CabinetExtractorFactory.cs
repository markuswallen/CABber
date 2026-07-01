namespace CABber;

/// <summary>Default <see cref="ICabinetExtractorFactory"/> implementation, backed by <see cref="CabinetExtractor"/>.</summary>
public sealed class CabinetExtractorFactory : ICabinetExtractorFactory
{
    public ICabinetExtractor Open(string cabinetPath) => new CabinetExtractor(cabinetPath);
}
