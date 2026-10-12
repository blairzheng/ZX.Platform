using ZX.Platform.Identity.Domain.Auditing;
using ZX.Platform.Identity.Domain.Exceptions;

namespace ZX.Platform.Identity.Domain.Entities
{
    public sealed class Factory : SoftDeletableEntity
    {
        private Factory()
        {
        }

        private Factory(
            Guid id,
            string code,
            string name)
        {
            Id = id;
            Code = code;
            Name = name;
            IsEnabled = true;
        }

        /// <summary>
        /// 厂区唯一标识。
        /// </summary>
        public Guid Id { get; private set; }

        /// <summary>
        /// 厂区编码，例如 VNK、VNY、F。
        /// 创建后原则上不允许修改。
        /// </summary>
        public string Code { get; private set; } = string.Empty;

        /// <summary>
        /// 厂区显示名称。
        /// </summary>
        public string Name { get; private set; } = string.Empty;

        /// <summary>
        /// 厂区是否启用。
        /// </summary>
        public bool IsEnabled { get; private set; }

        /// <summary>
        /// 创建并初始化厂区实体，不负责保存到数据库。
        /// </summary>
        public static Factory Create(string code, string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            return new Factory(
                Guid.NewGuid(),
                code.Trim().ToUpperInvariant(),
                name.Trim());
        }

        /// <summary>
        /// 修改厂区显示名称。
        /// </summary>
        public void ChangeName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Name = name.Trim();
        }

        /// <summary>
        /// 启用厂区。
        /// </summary>
        public void Enable()
        {
            if (IsDeleted)
                throw new DomainException("Factory.RestoreBeforeEnable");
            IsEnabled = true;
        }

        /// <summary>
        /// 禁用厂区。
        /// 禁用后不再允许用户选择或访问该厂区。
        /// </summary>
        public void Disable()
        {
            IsEnabled = false;
        }

        protected override void OnDeleted()
        {
            IsEnabled = false;
        }

        protected override void OnRestored()
        {
            IsEnabled = true;
        }
    }
}
