using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;
using ZX.Platform.Identity.Application.Factories.Dtos;
using ZX.Platform.Identity.Application.Persistence;
using ZX.Platform.Identity.Domain.Entities;

namespace ZX.Platform.Identity.Application.Factories
{
    public class FactoryService : IFactoryService
    {
        private readonly IFactoryRepository _factoryRepository;
        private readonly IUnitOfWork _unitOfWork;

        public FactoryService(IFactoryRepository factoryRepository, IUnitOfWork unitOfWork)
        {
            _factoryRepository = factoryRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<Guid> CreateAsync(CreateFactoryDto createFactory, CancellationToken cancellationToken)
        {
            var code = createFactory.Code.Trim().ToUpperInvariant();
            var existingFactory = await _factoryRepository.GetByCodeAsync(code, cancellationToken);
            if (existingFactory != null)
            {
                if (existingFactory.IsDeleted)
                    throw new BusinessException(
                        ErrorCodes.Conflict,
                        "Factory.CodeDeleted",
                        new
                        {
                            Reason = "FactoryCodeDeleted",
                            FactoryId = existingFactory.Id
                        });

                if (!existingFactory.IsEnabled)
                    throw new BusinessException(
                        ErrorCodes.Conflict,
                        "Factory.Disabled",
                        new
                        {
                            Reason = "FactoryDisabled",
                            FactoryId = existingFactory.Id
                        });

                throw new BusinessException(
                    ErrorCodes.Conflict,
                    "Factory.CodeExists"
                   );
            }

            var factory = Factory.Create(createFactory.Code, createFactory.Name);
            _factoryRepository.Add(factory);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return factory.Id;
        }

        public async Task<FactoryDetailDto> GetByIdAsync(Guid guid, CancellationToken cancellationToken)
        {
            var data = await _factoryRepository.GetByIdAsync(guid, cancellationToken);
            if (data == null) throw new BusinessException(ErrorCodes.NotFound, "Factory.NotFound");
            return new FactoryDetailDto
            {
                Id = data.Id,
                Code = data.Code,
                Name = data.Name,
                IsEnabled = data.IsEnabled,
                CreatedAtUtc = data.CreatedAtUtc,
                UpdatedAtUtc = data.UpdatedAtUtc
            };
        }

        public async Task<List<FactoryDetailDto>> GetAllAsync(CancellationToken cancellationToken)
        {
            var factories = await _factoryRepository.GetAllAsync(cancellationToken);
            return factories.Select(f => new FactoryDetailDto
            {
                Id = f.Id,
                Code = f.Code,
                Name = f.Name,
                IsEnabled = f.IsEnabled,
                CreatedAtUtc = f.CreatedAtUtc,
                UpdatedAtUtc = f.UpdatedAtUtc
            }).ToList();
        }

        public async Task SetFactoryDisableAsync(Guid id, CancellationToken cancellationToken)
        {
            var factory = await _factoryRepository.GetByIdAsync(id, cancellationToken);
            if (factory == null) throw new BusinessException(ErrorCodes.NotFound, "Factory.NotFound");
            factory.Disable();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task SetFactoryEnableAsync(Guid id, CancellationToken cancellationToken)
        {
            var factory = await _factoryRepository.GetByIdAsync(id, cancellationToken);
            if (factory == null) throw new BusinessException(ErrorCodes.NotFound, "Factory.NotFound");
            factory.Enable();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateNameAsync(UpdateFactoryNameDto updateFactoryName, CancellationToken cancellationToken)
        {
            var factory = await _factoryRepository.GetByIdAsync(updateFactoryName.Id, cancellationToken);
            if (factory == null) throw new BusinessException(ErrorCodes.NotFound, "Factory.NotFound");
            factory.ChangeName(updateFactoryName.Name);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            var factory = await _factoryRepository.GetByIdAsync(id, cancellationToken);
            if (factory == null) throw new BusinessException(ErrorCodes.NotFound, "Factory.NotFound");
            factory.MarkDeleted();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task RestoreAsync(Guid id, CancellationToken cancellationToken)
        {
            var factory = await _factoryRepository.GetByIdIncludeDeletedAsync(id, cancellationToken);
            if (factory == null) throw new BusinessException(ErrorCodes.NotFound, "Factory.NotFound");
            if (!factory.IsDeleted) throw new BusinessException(ErrorCodes.Conflict, "Factory.NotDeleted");
            factory.Restore();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<List<FactoryDetailDto>> GetAllEnabledFactoriesAsync(CancellationToken cancellationToken)
        {
            var factories = await _factoryRepository.GetAllEnabledFactoriesAsync(cancellationToken);
            return factories.Select(f => new FactoryDetailDto
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
