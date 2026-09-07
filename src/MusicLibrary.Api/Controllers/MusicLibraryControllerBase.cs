using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MusicLibrary.Api.Controllers;

public abstract class MusicLibraryControllerBase : ControllerBase
{
    protected Guid CurrentUserId
    {
        get
        {
            var value = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("The authenticated token does not contain a user identifier.");
            return Guid.Parse(value);
        }
    }

    protected ActionResult IdentityFailure(IEnumerable<IdentityError> errors)
    {
        foreach (var error in errors) ModelState.AddModelError(error.Code, error.Description);
        return ValidationProblem(ModelState);
    }
}
