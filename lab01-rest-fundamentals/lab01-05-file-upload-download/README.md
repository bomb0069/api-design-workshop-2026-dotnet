# Lab 01-05: File Upload & Download

Build a REST API that handles file uploads and downloads using MinIO as S3-compatible object storage, with file metadata tracked in PostgreSQL.

## Learning Objectives

- Handle `multipart/form-data` file uploads
- Store files in S3-compatible storage (MinIO)
- Track file metadata in a database
- Serve files for download
- Use `Content-Disposition` headers to control download behavior

## Getting Started

Start all services with Docker Compose:

```bash
docker compose up --build
```

## Access Points

| Service       | URL                          | Credentials            |
|---------------|------------------------------|------------------------|
| API           | http://localhost:8080        | -                      |
| MinIO Console | http://localhost:9001        | minioadmin / minioadmin |

## API Endpoints

| Method | Path                    | Description          |
|--------|-------------------------|----------------------|
| POST   | `/upload`               | Upload a file        |
| GET    | `/files`                | List all files       |
| GET    | `/files/{id}`           | Get file metadata    |
| GET    | `/files/{id}/download`  | Download a file      |
| DELETE | `/files/{id}`           | Delete a file        |

## Testing the API

First, create a test file:

```bash
echo "Hello, World!" > test.txt
```

### Upload a file

```bash
curl -X POST http://localhost:8080/upload -F "file=@test.txt"
```

### List all files

```bash
curl http://localhost:8080/files
```

### Get file metadata

```bash
curl http://localhost:8080/files/1
```

### Download a file

```bash
curl http://localhost:8080/files/1/download -o downloaded.txt
```

### Delete a file

```bash
curl -X DELETE http://localhost:8080/files/1
```

## Code Walkthrough

### Multipart Form Parsing

The upload handler calls `request.ReadFormAsync()` to parse the incoming multipart data, then retrieves the uploaded file with `form.Files["file"]`. The resulting `IFormFile` exposes the original filename (`FileName`), size (`Length`), content type (`ContentType`), and a stream of the file's content (`OpenReadStream()`). If the field is missing, the API returns `400 Bad Request` with `{"error": "File is required. Use form field 'file'"}`.

### S3 Client

MinIO speaks the S3 protocol, so the application talks to it with the standard AWS S3 client (`AWSSDK.S3`), pointed at the MinIO endpoint with static credentials:

```csharp
var storage = new AmazonS3Client(
    new BasicAWSCredentials("minioadmin", "minioadmin"),
    new AmazonS3Config
    {
        ServiceURL = $"http://{minioEndpoint}",
        ForcePathStyle = true,
        AuthenticationRegion = "us-east-1",
    });
```

`ForcePathStyle` makes the client use `http://host:9000/bucket/key` URLs, which is what MinIO expects. On startup, the application creates the `uploads` bucket with `PutBucketAsync`, ignoring the "already exists" error on subsequent runs.

### PutObject / GetObject

- **PutObjectAsync**: Streams the uploaded file directly to MinIO with the detected content type. A unique object key is generated using a nanosecond timestamp plus the original file extension.
- **GetObjectAsync**: Retrieves the file from MinIO by its object key. The returned `ResponseStream` is copied directly to the HTTP response body, so the file is streamed to the client without buffering it in memory.

### Content-Disposition

The download endpoint sets the `Content-Disposition` header to `attachment; filename="original-name.ext"`. This tells the browser to download the file with its original filename rather than displaying it inline.

### File Metadata in PostgreSQL

Metadata is stored via EF Core in a `files` table with snake_case columns (`content_type`, `object_key`, `uploaded_at`). The `uploaded_at` column has a database default of `NOW()`, so PostgreSQL stamps the upload time on insert. `[JsonPropertyName]` attributes keep the JSON keys in snake_case, matching the API contract:

```json
{
  "id": 1,
  "filename": "test.txt",
  "content_type": "text/plain",
  "size": 14,
  "object_key": "1754700000000000000.txt",
  "uploaded_at": "2026-08-09T10:30:00.000000Z"
}
```

## Exercises

1. **File type validation** -- Add validation to only allow image uploads (jpg, png, gif). Return a `400 Bad Request` for disallowed file types.

2. **Max file size limit** -- Add a maximum file size limit (e.g., 5MB). Return `413 Payload Too Large` if the uploaded file exceeds the limit.

3. **Image thumbnail generation** -- On upload, if the file is an image, generate a thumbnail version and store it alongside the original in MinIO.

4. **Presigned URLs** -- Add an endpoint that returns presigned URLs for direct upload/download from MinIO, bypassing the API server for the actual file transfer.

## Key Concepts

- **Multipart Form Data**: The encoding type used for file uploads in HTTP. The `multipart/form-data` content type allows sending binary files alongside text fields in a single request.

- **Object Storage**: A storage architecture that manages data as objects (file + metadata) rather than as a file hierarchy. MinIO provides an S3-compatible API for object storage.

- **Content-Disposition**: An HTTP header that indicates whether content should be displayed inline in the browser or treated as a downloadable attachment with a suggested filename.

- **File Metadata**: Information about the file (name, size, content type, storage location) stored in a relational database, separate from the file content itself in object storage.

## Cleanup

Stop and remove all containers, networks, and volumes:

```bash
docker compose down -v
```
