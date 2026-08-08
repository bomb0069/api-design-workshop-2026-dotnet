// Endpoint handlers: token issuance, role-shaped reads, payment creation,
// and the deliberately-broken leaky endpoint.
using System.Text.Json;

public static class Handlers
{
    // POST /auth/token — DEV-ONLY convenience so the lab can demo three roles
    // without a user database. Real systems derive the role from the
    // authenticated identity; they never hand out roles on request.
    public static async Task<IResult> IssueToken(HttpRequest request)
    {
        TokenRequest? input;
        try
        {
            input = await request.ReadFromJsonAsync<TokenRequest>();
        }
        catch (JsonException)
        {
            input = null;
        }
        if (input is null)
            return Results.Json(new ErrorResponse("Invalid request body"), statusCode: 400);

        var role = input.Role ?? "";
        if (role != Auth.RolePublic && role != Auth.RoleInternal && role != Auth.RoleAdmin)
            return Results.Json(
                new ErrorResponse("Role must be one of: public, internal, admin"), statusCode: 400);

        var username = string.IsNullOrEmpty(input.Username) ? "demo" : input.Username;
        var token = Auth.IssueToken(99, username, role);
        return Results.Json(new TokenResponse(token, 86400, role));
    }

    // GET /users/{id} — ONE endpoint, THREE response shapes depending on role.
    public static IResult GetUser(string id, HttpRequest request)
    {
        var (role, error) = Auth.ResolveRole(request);
        if (error is not null)
            return error;

        if (!int.TryParse(id, out var userId))
            return Results.Json(new ErrorResponse("Invalid ID"), statusCode: 400);

        var user = Store.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return Results.Json(new ErrorResponse("User not found"), statusCode: 404);

        return role switch
        {
            Auth.RoleAdmin => Results.Json(new UserAdminDto(
                user.Id, user.Username, user.FullName, user.Email, user.Phone,
                user.CitizenId, user.CreditScore, user.InternalNotes)),
            Auth.RoleInternal => Results.Json(new UserInternalDto(
                user.Id, user.Username, user.FullName, user.Email, user.Phone)),
            _ => Results.Json(new UserPublicDto(
                user.Id, user.Username, user.FullName,
                Masking.MaskEmail(user.Email), Masking.MaskPhone(user.Phone)))
        };
    }

    // GET /payments/{id} — same pattern for card data.
    public static IResult GetPayment(string id, HttpRequest request)
    {
        var (role, error) = Auth.ResolveRole(request);
        if (error is not null)
            return error;

        if (!int.TryParse(id, out var paymentId))
            return Results.Json(new ErrorResponse("Invalid ID"), statusCode: 400);

        var payment = Store.Payments.FirstOrDefault(p => p.Id == paymentId);
        if (payment is null)
            return Results.Json(new ErrorResponse("Payment not found"), statusCode: 404);

        return role switch
        {
            Auth.RoleAdmin => Results.Json(new PaymentAdminDto(
                payment.Id, payment.UserId, payment.CardNumber, payment.CardHolder,
                payment.Amount, payment.Currency, payment.Status, payment.InternalNotes)),
            Auth.RoleInternal => Results.Json(new PaymentInternalDto(
                payment.Id, payment.UserId, Masking.MaskCard(payment.CardNumber),
                payment.CardHolder, payment.Amount, payment.Currency, payment.Status)),
            _ => Results.Json(new PaymentPublicDto(
                payment.Id, payment.UserId, payment.Amount, payment.Currency, payment.Status))
        };
    }

    // POST /payments — accepts a card_number in the body so the log-scrubbing
    // middleware has something real to redact. The response masks the card:
    // never echo a full PAN back, not even to the client that just sent it.
    public static async Task<IResult> CreatePayment(HttpRequest request)
    {
        PaymentInput? input;
        try
        {
            input = await request.ReadFromJsonAsync<PaymentInput>();
        }
        catch (JsonException)
        {
            input = null;
        }
        if (input is null)
            return Results.Json(new ErrorResponse("Invalid request body"), statusCode: 400);

        if (string.IsNullOrEmpty(input.CardNumber))
            return Results.Json(new ErrorResponse("card_number is required"), statusCode: 400);
        if (input.Amount <= 0)
            return Results.Json(new ErrorResponse("amount must be greater than 0"), statusCode: 400);

        PaymentEntity payment;
        lock (Store.Payments)
        {
            payment = new PaymentEntity
            {
                Id = Store.Payments.Count == 0 ? 1 : Store.Payments.Max(p => p.Id) + 1,
                UserId = input.UserId,
                CardNumber = input.CardNumber,
                CardHolder = input.CardHolder ?? "",
                Amount = input.Amount,
                Currency = string.IsNullOrEmpty(input.Currency) ? "THB" : input.Currency,
                Status = "pending",
                InternalNotes = ""
            };
            Store.Payments.Add(payment);
        }

        return Results.Json(new PaymentCreatedDto(
            payment.Id, payment.UserId, Masking.MaskCard(payment.CardNumber),
            payment.Amount, payment.Currency, payment.Status), statusCode: 201);
    }

    // GET /leaky/users/{id} — DELIBERATELY BROKEN. Serializes the raw entity,
    // leaking password_hash, citizen_id, credit_score, and internal_notes to
    // ANY caller. This is the "find the leak" exercise target — see README.
    public static IResult LeakyGetUser(string id)
    {
        if (!int.TryParse(id, out var userId))
            return Results.Json(new ErrorResponse("Invalid ID"), statusCode: 400);

        var user = Store.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return Results.Json(new ErrorResponse("User not found"), statusCode: 404);

        // BUG (on purpose): returning the entity instead of a DTO.
        return Results.Json(user);
    }
}
