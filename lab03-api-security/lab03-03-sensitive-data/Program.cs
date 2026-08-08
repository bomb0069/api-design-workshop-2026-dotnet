// Sensitive Data Handling: masking, field-level security by role, and
// keeping secrets out of logs.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Every request passes through the scrubber — sensitive body fields are
// [REDACTED] before the audit line is written.
app.UseMiddleware<LogScrubbingMiddleware>();

// Dev-only token endpoint (see README — real systems don't hand out roles).
app.MapPost("/auth/token", Handlers.IssueToken);

// The SAME endpoints return different shapes per role (public/internal/admin).
app.MapGet("/users/{id}", Handlers.GetUser);
app.MapGet("/payments/{id}", Handlers.GetPayment);
app.MapPost("/payments", Handlers.CreatePayment);

// Deliberately broken — the "find the leak" exercise target.
app.MapGet("/leaky/users/{id}", Handlers.LeakyGetUser);

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

Console.WriteLine("Server starting on :8080");
// APP_URL lets local dev pick another port; Docker uses the 8080 default.
app.Run(Environment.GetEnvironmentVariable("APP_URL") ?? "http://0.0.0.0:8080");
