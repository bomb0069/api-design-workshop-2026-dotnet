# Lab 01-04: CRUD with Database

In this lab, we replace the in-memory store from Lab 01-03 with a real **PostgreSQL** database. The API endpoints remain identical, but data now persists across restarts.

## Learning Objectives

- Connect an ASP.NET Core application to PostgreSQL using **Entity Framework Core** and the **Npgsql** provider
- Query and modify data with LINQ (`Where`, `OrderBy`, `Find`, `ExecuteDelete`)
- Understand how EF Core uses **parameterized queries** under the hood to prevent SQL injection
- Orchestrate multiple services with **Docker Compose**
- Configure **database health checks** so the API waits for PostgreSQL to be ready

## Getting Started

Start both the API and database with a single command:

```bash
docker compose up --build
```

Docker Compose will:

1. Start a PostgreSQL 16 container
2. Wait until PostgreSQL passes its health check (`pg_isready`)
3. Build and start the .NET API, which connects to PostgreSQL and auto-creates the `todos` table

The API is available at `http://localhost:8080`.

## Test with curl

These are the same endpoints as Lab 01-03 -- the only difference is that data is now stored in PostgreSQL.

**Create a todo:**

```bash
curl -s -X POST http://localhost:8080/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Buy groceries"}' | jq
```

**List all todos:**

```bash
curl -s http://localhost:8080/todos | jq
```

**Get a single todo:**

```bash
curl -s http://localhost:8080/todos/1 | jq
```

**Update a todo:**

```bash
curl -s -X PUT http://localhost:8080/todos/1 \
  -H "Content-Type: application/json" \
  -d '{"completed": true}' | jq
```

**Delete a todo:**

```bash
curl -s -X DELETE http://localhost:8080/todos/1 -w "\nHTTP Status: %{http_code}\n"
```

## Code Walkthrough

### Entity Framework Core + Npgsql

EF Core is .NET's object-relational mapper (ORM). The `Npgsql.EntityFrameworkCore.PostgreSQL` package is the PostgreSQL provider for EF Core. It handles connection pooling, SQL generation, and type mapping.

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
```

### The DbContext

```csharp
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<Todo> Todos => Set<Todo>();
}
```

The `DbContext` is the gateway to the database. Each `DbSet<T>` property maps to a table. Handlers receive an `AppDbContext` instance through dependency injection.

### The Entity

```csharp
[Table("todos")]
public class Todo
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("completed")]
    public bool Completed { get; set; }
}
```

The `[Table]` and `[Column]` attributes map the class to the same `todos` table and lowercase column names that the Go version creates. The integer `[Key]` becomes an auto-incrementing identity column -- the equivalent of `SERIAL` in raw SQL.

### Connection String

The connection string is read from the `DATABASE_URL` environment variable, with a localhost fallback:

```
Host=localhost;Database=workshop;Username=postgres;Password=postgres
```

In Docker Compose, the hostname is the service name (`db`), so the API uses:

```
Host=db;Database=workshop;Username=postgres;Password=postgres
```

(Go's `lib/pq` uses a `postgres://` URI; Npgsql uses this key-value format. Same information, different syntax.)

### Creating the Table on Startup

On startup, the application runs `CREATE TABLE IF NOT EXISTS` (via `db.Database.ExecuteSqlRaw`) to ensure the `todos` table exists -- exactly like the Go version's `createTable`. This is a simple approach suitable for development. In production, you would use EF Core migrations (`dotnet ef migrations`).

### Parameterized Queries

EF Core translates LINQ expressions into SQL with bound parameters -- user input is never concatenated into the SQL string, which prevents SQL injection:

```csharp
var todo = await db.Todos.FindAsync(todoId);
// -> SELECT ... FROM todos WHERE id = @p0

await db.Todos.Where(t => t.Id == todoId).ExecuteDeleteAsync();
// -> DELETE FROM todos WHERE id = @p0
```

### Not Found Handling

`FindAsync` returns `null` when no row matches, and `ExecuteDeleteAsync` returns the number of rows affected. We check both to return a proper 404 response:

```csharp
if (todo is null)
{
    return Results.Json(new { error = "Todo not found" }, statusCode: 404);
}
```

This plays the role of `sql.ErrNoRows` / `RowsAffected()` in the Go version.

## Comparing with Lab 01-03

| Aspect | Lab 01-03 (In-Memory) | Lab 01-04 (Database) |
|---|---|---|
| Storage | Dictionary + lock | PostgreSQL table |
| Persistence | Lost on restart | Survives restarts |
| ID Generation | Manual counter | Identity column (auto-increment) |
| Concurrency | `lock` statement | Database handles it |
| Infrastructure | Single app | Docker Compose (API + DB) |
| Not Found | Dictionary lookup | `FindAsync` returns `null` |

## Exercises

1. **Add a `created_at` column** -- Add a `DateTime CreatedAt` property with `[Column("created_at")]` and a default of `NOW()` (configure `HasDefaultValueSql("NOW()")` in `OnModelCreating`). Return it in API responses.

2. **Add a `description` column** -- Add an optional `string? Description` property. Update the create and update handlers to accept and return it.

3. **Add a seed data script** -- Create a `seed.sql` file that inserts sample todos, and mount it into the PostgreSQL container at `/docker-entrypoint-initdb.d/` so it runs on first startup.

4. **Connect with psql** -- Use the PostgreSQL CLI to inspect your data directly:
   ```bash
   docker compose exec db psql -U postgres workshop
   ```
   Try running `SELECT * FROM todos;` and `\d todos` to see the table schema.

## Key Concepts

### Entity Framework Core

EF Core provides a high-level interface for SQL databases. It manages connection pooling automatically -- you register the `DbContext` once and inject it wherever you need database access. LINQ queries are translated to SQL at runtime.

### Parameterized Queries

Never build SQL strings by concatenating user input. EF Core parameterizes every value automatically. If you ever drop down to raw SQL, use `FromSql` / `ExecuteSql` with interpolated parameters, which are also bound safely.

### Docker Compose Services

Docker Compose lets you define multi-container applications in a single YAML file. Each service gets its own container, network alias, and configuration. Services can reference each other by name (e.g., the API connects to `db:5432`).

### Database Health Checks

The `healthcheck` configuration tells Docker Compose how to determine if a container is ready. The `depends_on` condition `service_healthy` ensures the API does not start until PostgreSQL is accepting connections.

## Cleanup

Stop and remove all containers and the database volume:

```bash
docker compose down -v
```

The `-v` flag removes the `pgdata` volume, which deletes all stored data. Omit `-v` if you want to keep your data for next time.
