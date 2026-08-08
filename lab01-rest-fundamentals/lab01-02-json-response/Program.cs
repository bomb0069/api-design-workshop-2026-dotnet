var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var books = new List<Book>
{
    new(Id: 1, Title: "The Go Programming Language", Author: "Alan Donovan", Year: 2015),
    new(Id: 2, Title: "Go in Action", Author: "William Kennedy", Year: 2015),
    new(Id: 3, Title: "Learning Go", Author: "Jon Bodner", Year: 2021),
};

// app.Map (without a method constraint) matches all HTTP methods,
// like Go's http.HandleFunc on the default mux.
app.Map("/books", () => Results.Json(books));
app.Map("/books/count", () => Results.Json(new { count = books.Count }));
app.Map("/health", () => Results.Json(new { status = "ok" }));

Console.WriteLine("Server starting on :8080");
app.Run("http://0.0.0.0:8080");

// The record's property names are serialized as camelCase JSON keys:
// Id -> "id", Title -> "title", Author -> "author", Year -> "year"
public record Book(int Id, string Title, string Author, int Year);
