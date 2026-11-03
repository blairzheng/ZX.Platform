using ZX.Platform.Identity.Domain.Auditing;
using ZX.Platform.Identity.Domain.Exceptions;

namespace ZX.Platform.Identity.Domain.Entities
{
    public sealed class Sbu : SoftDeletableEntity
    {
        private Sbu()
        {
        }

        private Sbu(
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
        /// SBU唯一标识。
        /// </summary>
        public Guid Id { get; private set; }

        /// <summary>
        /// SBU编码，例如 VNK、VNY、F。
        /// 创建后原则上不允许修改。
        /// </summary>
        public string Code { get; private set; } = string.Empty;

        /// <summary>
        /// SBU显示名称。
        /// </summary>
        public string Name { get; private set; } = string.Empty;

        /// <summary>
        /// SBU是否启用。
        /// </summary>
        public bool IsEnabled { get; private set; }

        /// <summary>
        /// 创建并初始化SBU实体，不负责保存到数据库。
        /// </summary>
        public static Sbu Create(string code, string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            return new Sbu(
                Guid.NewGuid(),
                code.Trim().ToUpperInvariant(),
                name.Trim());
        }

        /// <summary>
        /// 修改SBU显示名称。
        /// </summary>
        public void ChangeName(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            Name = name.Trim();
        }

        /// <summary>
        /// 启用SBU。
        /// </summary>
        public void Enable()
        {
            if (IsDeleted)
                throw new DomainException("Sbu.RestoreBeforeEnable");
            IsEnabled = true;
        }

        /// <summary>
        /// 禁用SBU。
        /// 禁用后不再允许用户选择或访问该SBU。
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
