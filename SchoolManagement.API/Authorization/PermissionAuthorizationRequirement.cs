using Microsoft.AspNetCore.Authorization;

namespace SchoolManagement.API.Authorization;

public class PermissionAuthorizationRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionAuthorizationRequirement(string permission)
    {
        Permission = permission;
    }
}

