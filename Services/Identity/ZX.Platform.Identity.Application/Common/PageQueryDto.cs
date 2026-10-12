using System.ComponentModel.DataAnnotations;
using ZX.Platform.Identity.Application.Errors;
using ZX.Platform.Identity.Application.Exceptions;

namespace ZX.Platform.Identity.Application.Common
{
    public class PageQueryDto
    {
        [Range(1, int.MaxValue, ErrorMessage = "Validation.PageRange")]
        public int Page { get; set; } = 1;

        [Range(1, 10000, ErrorMessage = "Validation.PageSizeRange")]
        public int PageSize { get; set; } = 20;

        public int GetSkip()
        {
            var skip = ((long)Page - 1) * PageSize;
            if (Page < 1 || PageSize < 1 || skip > int.MaxValue)
            {
                throw new BusinessException(
                    ErrorCodes.ValidationFailed,
                    "Validation.PaginationOverflow");
            }
            return (int)skip;
        }
    }
}
