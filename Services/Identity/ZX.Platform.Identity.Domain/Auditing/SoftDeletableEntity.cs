namespace ZX.Platform.Identity.Domain.Auditing
{
    public abstract class SoftDeletableEntity : AuditableEntity
    {
        /// <summary>
        /// 是否已经被逻辑删除。
        /// </summary>
        public bool IsDeleted { get; private set; }

        /// <summary>
        /// 将实体标记为逻辑删除。
        /// </summary>
        public void MarkDeleted()
        {
            if (IsDeleted)
                return;

            IsDeleted = true;
            OnDeleted();
        }

        /// <summary>
        /// 恢复被逻辑删除的实体。
        /// </summary>
        public void Restore()
        {
            if (!IsDeleted)
                return;

            IsDeleted = false;
            OnRestored();
        }

        /// <summary>
        /// 实体被逻辑删除后执行的附加操作。
        /// </summary>
        protected abstract void OnDeleted();

        /// <summary>
        /// 实体恢复后执行的附加操作。
        /// </summary>
        protected abstract void OnRestored();
    }
}
