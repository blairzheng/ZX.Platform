using ZX.Platform.Identity.Application.Factories.Dtos;

namespace ZX.Platform.Identity.Application.Factories
{
    public interface IFactoryService
    {
        Task<Guid> CreateAsync(CreateFactoryDto createFactory, CancellationToken cancellationToken);

        Task<FactoryDetailDto> GetByIdAsync(Guid guid, CancellationToken cancellationToken);

        Task<List<FactoryDetailDto>> GetAllAsync(CancellationToken cancellationToken);

        Task UpdateNameAsync(UpdateFactoryNameDto updateFactoryName, CancellationToken cancellationToken);

        Task SetFactoryEnableAsync(Guid id, CancellationToken cancellationToken);

        Task SetFactoryDisableAsync(Guid id, CancellationToken cancellationToken);

        Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);

        Task RestoreAsync(Guid id, CancellationToken cancellationToken);

        Task<List<FactoryDetailDto>> GetAllEnabledFactoriesAsync(CancellationToken cancellationToken);
    }
}
