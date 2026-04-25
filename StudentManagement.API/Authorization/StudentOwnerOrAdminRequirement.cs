using Microsoft.AspNetCore.Authorization;

namespace StudentManagement.API.Authorization
{
    public class StudentOwnerOrAdminRequirement : IAuthorizationRequirement
    {
    }
}
