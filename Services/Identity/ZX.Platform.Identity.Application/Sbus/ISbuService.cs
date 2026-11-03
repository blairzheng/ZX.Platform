using ZX.Platform.Identity.Application.Sbus.Dtos;

namespace ZX.Platform.Identity.Application.Sbus
{
    public interface ISbuService
    {
        Task<Guid> CreateAsync(CreateSbuDto createSbu, CancellationToken cancellationToken);

        Task<SbuDetailDto> GetByIdAsync(Guid guid, CancellationToken cancellationToken);

        Task<List<SbuDetailDto>> GetAllAsync(CancellationToken cancellationToken);

        Task UpdateNameAsync(UpdateSbuNameDto updateSbuName, CancellationToken cancellationToken);

        Task SetSbuEnableAsync(Guid id, CancellationToken cancellationToken);

        Task SetSbuDisableAsync(Guid id, CancellationToken cancellationToken);

        Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken);

        Task RestoreAsync(Guid id, CancellationToken cancellationToken);

        Task<List<SbuDetailDto>> GetAllEnabledSbusAsync(CancellationToken cancellationToken);
    }
}
