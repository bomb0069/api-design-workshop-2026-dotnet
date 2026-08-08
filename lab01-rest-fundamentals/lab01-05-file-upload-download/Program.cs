using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;

const string bucketName = "uploads";

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=workshop;Username=postgres;Password=postgres";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// MinIO speaks the S3 protocol, so we talk to it with the AWS S3 client.
// ForcePathStyle makes the client use http://host:9000/bucket/key URLs,
// which is what MinIO expects.
var minioEndpoint = Environment.GetEnvironmentVariable("MINIO_ENDPOINT") ?? "localhost:9000";
var storage = new AmazonS3Client(
    new BasicAWSCredentials("minioadmin", "minioadmin"),
    new AmazonS3Config
    {
        ServiceURL = $"http://{minioEndpoint}",
        ForcePathStyle = true,
        AuthenticationRegion = "us-east-1",
    });
builder.Services.AddSingleton<IAmazonS3>(storage);

var app = builder.Build();

// Ensure the files table exists on startup (like createTable in the Go version).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.ExecuteSqlRaw(@"CREATE TABLE IF NOT EXISTS files (
        id SERIAL PRIMARY KEY,
        filename TEXT NOT NULL,
        content_type TEXT NOT NULL,
        size BIGINT NOT NULL,
        object_key TEXT NOT NULL,
        uploaded_at TIMESTAMP DEFAULT NOW()
    )");
}

// Ensure the uploads bucket exists.
try
{
    await storage.PutBucketAsync(bucketName);
}
catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
{
    // Bucket already exists - nothing to do.
}

// POST /upload - upload a file (multipart/form-data, field name "file")
app.MapPost("/upload", async (HttpRequest request, AppDbContext db, IAmazonS3 s3) =>
{
    IFormFile? file = null;
    try
    {
        var form = await request.ReadFormAsync();
        file = form.Files["file"];
    }
    catch (InvalidDataException) { }
    catch (InvalidOperationException) { }

    if (file is null)
    {
        return Results.Json(new { error = "File is required. Use form field 'file'" }, statusCode: 400);
    }

    var ext = Path.GetExtension(file.FileName);
    var unixNano = (DateTime.UtcNow.Ticks - DateTime.UnixEpoch.Ticks) * 100;
    var objectKey = $"{unixNano}{ext}";

    var contentType = string.IsNullOrEmpty(file.ContentType)
        ? "application/octet-stream"
        : file.ContentType;

    try
    {
        await using var stream = file.OpenReadStream();
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = contentType,
        });
    }
    catch
    {
        return Results.Json(new { error = "Failed to upload file" }, statusCode: 500);
    }

    var meta = new FileMetadata
    {
        Filename = file.FileName,
        ContentType = contentType,
        Size = file.Length,
        ObjectKey = objectKey,
    };

    try
    {
        db.Files.Add(meta);
        await db.SaveChangesAsync();
    }
    catch
    {
        return Results.Json(new { error = "Failed to save metadata" }, statusCode: 500);
    }

    return Results.Json(meta, statusCode: 201);
});

// GET /files - list all files (newest first)
app.MapGet("/files", async (AppDbContext db) =>
{
    try
    {
        var files = await db.Files.OrderByDescending(f => f.UploadedAt).ToListAsync();
        return Results.Json(files);
    }
    catch
    {
        return Results.Json(new { error = "Internal server error" }, statusCode: 500);
    }
});

// GET /files/{id} - get file metadata
app.MapGet("/files/{id}", async (string id, AppDbContext db) =>
{
    int.TryParse(id, out var fileId);
    var meta = await db.Files.FindAsync(fileId);
    if (meta is null)
    {
        return Results.Json(new { error = "File not found" }, statusCode: 404);
    }
    return Results.Json(meta);
});

// GET /files/{id}/download - download the file content
app.MapGet("/files/{id}/download", async (string id, AppDbContext db, IAmazonS3 s3, HttpResponse response) =>
{
    int.TryParse(id, out var fileId);
    var meta = await db.Files.FindAsync(fileId);
    if (meta is null)
    {
        return Results.Json(new { error = "File not found" }, statusCode: 404);
    }

    try
    {
        using var obj = await s3.GetObjectAsync(bucketName, meta.ObjectKey);
        response.ContentType = meta.ContentType;
        response.Headers.ContentDisposition = $"attachment; filename=\"{meta.Filename}\"";
        await obj.ResponseStream.CopyToAsync(response.Body);
        return Results.Empty;
    }
    catch
    {
        if (response.HasStarted)
        {
            return Results.Empty;
        }
        return Results.Json(new { error = "Failed to retrieve file" }, statusCode: 500);
    }
});

// DELETE /files/{id} - delete a file
app.MapDelete("/files/{id}", async (string id, AppDbContext db, IAmazonS3 s3) =>
{
    int.TryParse(id, out var fileId);
    var meta = await db.Files.FindAsync(fileId);
    if (meta is null)
    {
        return Results.Json(new { error = "File not found" }, statusCode: 404);
    }

    try
    {
        await s3.DeleteObjectAsync(bucketName, meta.ObjectKey);
    }
    catch
    {
        // Like the Go version, storage removal errors are ignored.
    }

    db.Files.Remove(meta);
    await db.SaveChangesAsync();

    return Results.NoContent();
});

Console.WriteLine("Server starting on :8080");
app.Run("http://0.0.0.0:8080");
