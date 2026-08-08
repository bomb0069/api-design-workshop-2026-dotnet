# Lab 08-01: GraphQL

Build a GraphQL API in ASP.NET Core using the HotChocolate library with a products catalog backed by PostgreSQL.

## Learning Objectives

- Build a GraphQL API in .NET with HotChocolate
- Define schemas with types, queries, and mutations
- Implement resolvers that interact with a database
- Compare GraphQL vs REST approaches
- Use the built-in GraphQL IDE (Banana Cake Pop) for interactive exploration

## Getting Started

```bash
docker compose up --build
```

To run locally without Docker (requires a PostgreSQL instance on `localhost:5432`):

```bash
dotnet run
```

## Access

- **GraphQL IDE (Banana Cake Pop)**: http://localhost:8080/graphql
- **REST endpoint** (for comparison): http://localhost:8080/api/products

Banana Cake Pop is the interactive IDE that ships with HotChocolate (replacing the Go lab's GraphQL Playground). Open `/graphql` in your browser to write and execute queries.

## Example Queries

Paste these into the GraphQL IDE or send them via curl.

### List all products

```graphql
{
  products {
    id
    name
    price
    category
  }
}
```

### Get only names and prices (no over-fetching)

```graphql
{
  products {
    name
    price
  }
}
```

### Filter by category

```graphql
{
  products(category: "electronics") {
    id
    name
    price
  }
}
```

### Filter by price range

```graphql
{
  products(minPrice: 20, maxPrice: 50) {
    name
    price
    category
  }
}
```

### Get a single product

```graphql
{
  product(id: 1) {
    id
    name
    price
    category
  }
}
```

### List categories with counts

```graphql
{
  categories {
    name
    count
  }
}
```

### Combine multiple queries in one request

```graphql
{
  electronics: products(category: "electronics") {
    name
    price
  }
  books: products(category: "books") {
    name
    price
  }
  categories {
    name
    count
  }
}
```

## Example Mutations

### Create a product

```graphql
mutation {
  createProduct(name: "New Item", price: 29.99, category: "electronics") {
    id
    name
    price
    category
  }
}
```

### Update a product

```graphql
mutation {
  updateProduct(id: 1, price: 1199.99) {
    id
    name
    price
  }
}
```

### Delete a product

```graphql
mutation {
  deleteProduct(id: 1)
}
```

## Using curl

You can also interact with the GraphQL API using curl:

```bash
# Query all products
curl -X POST http://localhost:8080/graphql \
  -H "Content-Type: application/json" \
  -d '{"query":"{ products { id name price category } }"}'

# Query with filter
curl -X POST http://localhost:8080/graphql \
  -H "Content-Type: application/json" \
  -d '{"query":"{ products(category: \"electronics\") { id name price } }"}'

# Create a product
curl -X POST http://localhost:8080/graphql \
  -H "Content-Type: application/json" \
  -d '{"query":"mutation { createProduct(name: \"Headphones\", price: 79.99, category: \"electronics\") { id name price } }"}'

# Compare with REST endpoint
curl http://localhost:8080/api/products
```

## REST vs GraphQL Comparison

| Feature | REST | GraphQL |
|---------|------|---------|
| Endpoints | Multiple (`/products`, `/products/:id`, `/categories`) | Single (`/graphql`) |
| Data fetching | Fixed response structure | Client specifies exact fields needed |
| Over-fetching | Common (returns all fields) | Eliminated (request only what you need) |
| Under-fetching | Requires multiple requests | Combine queries in one request |
| Filtering | Query parameters (`?category=books`) | Arguments (`category: "books"`) |
| Documentation | Requires OpenAPI/Swagger | Self-documenting schema (introspection) |
| Caching | HTTP caching (GET) | More complex (POST-based) |
| File upload | Native support | Requires extensions |
| Learning curve | Lower | Higher |

## Code Walkthrough

### Schema Types (`Models.cs`)

With HotChocolate's code-first approach, GraphQL types are inferred from C# types. Property names become camelCase field names automatically:

```csharp
public record Product
{
    public int Id { get; init; }          // id: Int
    public string Name { get; init; }     // name: String
    public double Price { get; init; }    // price: Float
    public string Category { get; init; } // category: String
}
```

### Query Fields (`Query.cs`)

Queries define what clients can read. Each resolver method becomes a field (`GetProducts` -> `products`); method parameters become field arguments:

```csharp
public class Query
{
    [GraphQLDescription("List all products")]
    public async Task<List<Product>> GetProducts(
        [Service] NpgsqlDataSource db,
        string? category = null,
        double? minPrice = null,
        double? maxPrice = null)
    {
        // build SQL with optional filters, query database, return results
    }
}
```

### Mutation Fields (`Mutation.cs`)

Mutations define write operations. Non-nullable C# parameters become required (non-null) GraphQL arguments:

```csharp
public class Mutation
{
    [GraphQLDescription("Create a new product")]
    public async Task<Product> CreateProduct(
        [Service] NpgsqlDataSource db,
        string name,        // name: String!
        double price,       // price: Float!
        string category)    // category: String!
    {
        // INSERT ... RETURNING and return the created product
    }
}
```

### Wiring It Up (`Program.cs`)

```csharp
builder.Services
    .AddGraphQLServer()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>();

app.MapGraphQL("/graphql");
```

`[Service]` injects registered services (like the Npgsql data source) into resolvers, so resolvers stay plain methods that fetch or modify data.

## Exercises

1. **Add a search query**: Create a `search` query field that searches products by name using PostgreSQL `ILIKE` for case-insensitive partial matching. Example: `{ search(term: "laptop") { id name price } }`.

2. **Add pagination support**: Add `limit` and `offset` arguments to the `products` query to support pagination. Example: `{ products(limit: 5, offset: 0) { id name } }`.

3. **Add a reviews type**: Create a `Review` type with fields like `id`, `productId`, `rating`, `comment`. Add a `reviews` field to the `Product` type (a `[UseParent]` or type-extension resolver) to fetch related reviews (one-to-many relationship). This requires creating a new database table and nested resolvers.

4. **Add input validation**: Validate mutation inputs in the resolvers. For example, ensure `price > 0`, `name` is not empty after trimming whitespace, and `category` is one of the allowed values. Throw `GraphQLException` with descriptive error messages.

## Key Concepts

| Concept | Description |
|---------|-------------|
| **Schema** | Defines the structure of the API: types, queries, and mutations |
| **Types** | Define the shape of data objects (like `Product` and `Category`) |
| **Queries** | Read operations; analogous to GET in REST |
| **Mutations** | Write operations; analogous to POST/PUT/DELETE in REST |
| **Resolvers** | Functions that fetch or modify data for each field |
| **Over-fetching** | REST problem where the API returns more data than needed |
| **Under-fetching** | REST problem where multiple requests are needed to get related data |
| **Introspection** | GraphQL APIs are self-documenting; clients can query the schema itself |
| **Banana Cake Pop** | Interactive IDE for writing and testing GraphQL queries, built into HotChocolate |

## Cleanup

```bash
docker compose down -v
```
