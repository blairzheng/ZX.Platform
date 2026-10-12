namespace ZX.Platform.Identity.Application.Users.Dtos
{
    public class UserPageDto
    {
        public List<UserDetailDto> List { get; set; } = [];

        public int Total { get; set; }
    }
}
