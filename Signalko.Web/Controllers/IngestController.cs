using Microsoft.AspNetCore.Mvc;
using Signalko.Web.Services;

namespace Signalko.Web.Controllers;

// 🇸🇮 Majhen kontroler za branje/menjavo profila ingestanja
[ApiController]
[Route("api/ingest")]
public class IngestController : ControllerBase
{
    private readonly IngestProfileState _state;

    public IngestController(IngestProfileState state) => _state = state;

    [HttpGet("profile")]
    public ActionResult<object> GetProfile()
        => Ok(new { profile = _state.Current.ToString(), minGapSeconds = _state.CurrentMinGap.TotalSeconds });

    [HttpPost("profile/{profile}")]
    public IActionResult SetProfile(string profile)
    {
        if (!Enum.TryParse<IngestProfile>(profile, true, out var p))
            return BadRequest(new { error = "Neznan profil. Dovojeni: normal, inventory, loans" });

        _state.Current = p;
        return Ok(new { ok = true, profile = p.ToString(), minGapSeconds = _state.CurrentMinGap.TotalSeconds });
    }
}
