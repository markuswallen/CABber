namespace CABber;

/// <summary>Creates <see cref="ICabinetExtractor"/> instances, so extractor creation is mockable too.</summary>
public interface ICabinetExtractorFactory
{
    ICabinetExtractor Open(string cabinetPath);
}
