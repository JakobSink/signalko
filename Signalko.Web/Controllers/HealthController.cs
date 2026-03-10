// HealthController.cs – enostaven "ping" za preverjanje, da API živi.

using Microsoft.AspNetCore.Mvc;

namespace Signalko.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { ok = true, at = DateTime.UtcNow });
}
