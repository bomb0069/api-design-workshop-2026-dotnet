using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/lifecycle")]
public class LifecycleController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            versions = new[]
            {
                new { version = "v1", stage = "deprecated", releasedAt = "2025-01-01", description = "Original API. Deprecated since 2026-03-01. Sunset: 2026-09-01." },
                new { version = "v2", stage = "current", releasedAt = "2026-01-01", description = "Current version. Enhanced responses with envelope, description, tags." },
            },
            policy = new
            {
                minimumSupportWindow = "12 months from release",
                deprecationNotice = "6 months before sunset",
                sunsetTrigger = "Traffic below 1% OR support window expires"
            }
        });
    }
}
