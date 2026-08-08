using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/changes")]
public class ChangesController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var changes = new[]
        {
            new { change = "Add optional query parameter 'sort'", classification = "safe", explanation = "New optional params don't affect existing clients" },
            new { change = "Add 'created_at' field to response", classification = "safe", explanation = "Additive response fields are backward compatible" },
            new { change = "Remove 'legacy_id' field from response", classification = "breaking", explanation = "Clients may depend on this field" },
            new { change = "Rename 'name' to 'title' in response", classification = "breaking", explanation = "Field rename breaks all existing clients" },
            new { change = "Change 'price' from number to string", classification = "breaking", explanation = "Type change breaks deserialization" },
            new { change = "Add new endpoint POST /api/v1/orders", classification = "safe", explanation = "New endpoints don't affect existing ones" },
            new { change = "Add new enum value 'premium' to 'tier'", classification = "context-dependent", explanation = "Safe if clients handle unknown values; breaking if they use exhaustive switch" },
            new { change = "Change error response from string to object", classification = "context-dependent", explanation = "Safe if clients only check HTTP status; breaking if they parse error body" },
            new { change = "Make 'email' field required (was optional)", classification = "breaking", explanation = "Existing requests without email will now fail" },
            new { change = "Change pagination from offset to cursor", classification = "breaking", explanation = "Completely changes how clients navigate results" },
        };
        return Ok(changes);
    }
}
