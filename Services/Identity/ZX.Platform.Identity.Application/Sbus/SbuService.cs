using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Sbus.Dtos;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Sbus
{
    public class SbuService : ISbuService
    {
        private readonly ISbuRepository _sbuRepository;
        private readonly IUnitOfWork _unitOfWork;

        public SbuService(ISbuRepository sbuRepository, IUnitOfWork unitOfWork)
        {
            _sbuRepository = sbuRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Guid> CreateAsync(CreateSbuDto createSbu, CancellationToken cancellationToken)
        {
            var code = createSbu.Code.Trim().ToUpperInvariant();
            var existingSbu = await _sbuRepository.GetByCodeAsync(code, cancellationToken);
            if (existingSbu != null)
            {
                if (existingSbu.IsDeleted)
                    throw new BusinessException(
                        ErrorCodes.Conflict,
                        "Sbu.CodeDeleted",
                        new
                        {
                            Reason = "SbuCodeDeleted",
                            SbuId = existingSbu.Id
                        });

                if (!existingSbu.IsEnabled)
                    throw new BusinessException(
                        ErrorCodes.Conflict,
                        "Sbu.Disabled",
                        new
                        {
                            Reason = "SbuDisabled",
                            SbuId = existingSbu.Id
                        });

                throw new BusinessException(
                    ErrorCodes.Conflict,
                    "Sbu.CodeExists"
                   );
            }

            var sbu = Sbu.Create(createSbu.Code, createSbu.Name);
            _sbuRepository.Add(sbu);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return sbu.Id;
        }

        public async Task<SbuDetailDto> GetByIdAsync(Guid guid, CancellationToken cancellationToken)
        {
            var data = await _sbuRepository.GetByIdAsync(guid, cancellationToken);
            if (data == null) throw new BusinessException(ErrorCodes.NotFound, "Sbu.NotFound");
            return new SbuDetailDto
            {
                Id = data.Id,
                Code = data.Code,
                Name = data.Name,
                IsEnabled = data.IsEnabled,
                CreatedAtUtc = data.CreatedAtUtc,
                UpdatedAtUtc = data.UpdatedAtUtc
            };
        }

        public async Task<List<SbuDetailDto>> GetAllAsync(CancellationToken cancellationToken)
        {
            var sbus = await _sbuRepository.GetAllAsync(cancellationToken);
            return sbus.Select(f => new SbuDetailDto
            {
                Id = f.Id,
                Code = f.Code,
                Name = f.Name,
                IsEnabled = f.IsEnabled,
                CreatedAtUtc = f.CreatedAtUtc,
                UpdatedAtUtc = f.UpdatedAtUtc
            }).ToList();
        }

        public async Task SetSbuDisableAsync(Guid id, CancellationToken cancellationToken)
        {
            var sbu = await _sbuRepository.GetByIdAsync(id, cancellationToken);
            if (sbu == null) throw new BusinessException(ErrorCodes.NotFound, "Sbu.NotFound");
            sbu.Disable();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task SetSbuEnableAsync(Guid id, CancellationToken cancellationToken)
        {
            var sbu = await _sbuRepository.GetByIdAsync(id, cancellationToken);
            if (sbu == null) throw new BusinessException(ErrorCodes.NotFound, "Sbu.NotFound");
            sbu.Enable();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateNameAsync(UpdateSbuNameDto updateSbuName, CancellationToken cancellationToken)
        {
            var sbu = await _sbuRepository.GetByIdAsync(updateSbuName.Id, cancellationToken);
            if (sbu == null) throw new BusinessException(ErrorCodes.NotFound, "Sbu.NotFound");
            sbu.ChangeName(updateSbuName.Name);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            var sbu = await _sbuRepository.GetByIdAsync(id, cancellationToken);
            if (sbu == null) throw new BusinessException(ErrorCodes.NotFound, "Sbu.NotFound");
            sbu.MarkDeleted();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task RestoreAsync(Guid id, CancellationToken cancellationToken)
        {
            var sbu = await _sbuRepository.GetByIdIncludeDeletedAsync(id, cancellationToken);
            if (sbu == null) throw new BusinessException(ErrorCodes.NotFound, "Sbu.NotFound");
            if (!sbu.IsDeleted) throw new BusinessException(ErrorCodes.Conflict, "Sbu.NotDeleted");
            sbu.Restore();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<SbuDetailDto>> GetAllEnabledSbusAsync(CancellationToken cancellationToken)
        {
            var sbus = await _sbuRepository.GetAllEnabledSbusAsync(cancellationToken);
            return sbus.Select(f => new SbuDetailDto
            {
                Id = f.Id,
                Code = f.Code,
                Name = f.Name,
                IsEnabled = f.IsEnabled,
                CreatedAtUtc = f.CreatedAtUtc,
                UpdatedAtUtc = f.UpdatedAtUtc
            }).ToList();
        }
    }
}
