using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MusicLibrary.Api.Controllers;

public abstract class MusicLibraryControllerBase : ControllerBase
{
    protected ActionResult IdentityFailure(IEnumerable<IdentityError> errors)
    {
        foreach (var error in errors) ModelState.AddModelError(error.Code, error.Description);
        return ValidationProblem(ModelState);
    }
}
