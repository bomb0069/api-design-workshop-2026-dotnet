# Lab 02-07: Pagination and Filtering

Build a REST API that supports pagination, filtering, and sorting for a product catalog backed by PostgreSQL.

## Learning Objectives

- Implement pagination with `page` and `limit` query parameters
- Filter results by query parameters (category, price range, stock status)
- Sort results with configurable fields and order
- Return response metadata (current page, page size, total items, total pages)

## Getting Started

```bash
docker compose up --build
```

The API will be available at `http://localhost:8080`. The database is automatically seeded with 20 sample products across four categories: electronics, books, clothing, and food.

## API Endpoints

### List Products

```
GET /products
```

#### Query Parameters

| Parameter   | Type    | Default | Description                          |
|-------------|---------|---------|--------------------------------------|
| `page`      | int     | 1       | Page number                          |
| `limit`     | int     | 10      | Items per page (max 100)             |
| `category`  | string  | —       | Filter by category                   |
| `in_stock`  | bool    | —       | Filter by stock availability         |
| `min_price` | float   | —       | Minimum price filter                 |
| `max_price` | float   | —       | Maximum price filter                 |
| `sort`      | string  | id      | Sort field: id, name, price, category|
| `order`     | string  | asc     | Sort order: asc, desc                |

### Get Product

```
GET /products/{id}
```

## Test Examples

**Default listing** (page 1, 10 items):
```bash
curl "http://localhost:8080/products"
```

**Pagination** (page 2, 5 items per page):
```bash
curl "http://localhost:8080/products?page=2&limit=5"
```

**Filter by category**:
```bash
curl "http://localhost:8080/products?category=electronics"
```

**Filter by price range**:
```bash
curl "http://localhost:8080/products?min_price=20&max_price=50"
```

**Filter by stock status**:
```bash
curl "http://localhost:8080/products?in_stock=true"
```

**Sort by price descending**:
```bash
curl "http://localhost:8080/products?sort=price&order=desc"
```

**Combined filters** (books, sorted by price ascending, first page of 3):
```bash
curl "http://localhost:8080/products?category=books&sort=price&order=asc&page=1&limit=3"
```

## Response Structure

All list responses use the `PaginatedResponse` structure:

```json
{
  "data": [
    {
      "id": 1,
      "name": "Laptop Pro 15",
      "price": 1299.99,
      "category": "electronics",
      "in_stock": true
    }
  ],
  "metadata": {
    "current_page": 1,
    "page_size": 10,
    "total_items": 20,
    "total_pages": 2
  }
}
```

The `metadata` object tells the client everything it needs to build pagination controls:

- `current_page` — which page was returned
- `page_size` — how many items per page
- `total_items` — total matching items across all pages
- `total_pages` — total number of pages available

## Code Walkthrough

### Query Parameter Parsing

The handler reads pagination, filter, and sort parameters from the query string using `request.Query`. Default values are applied when parameters are missing or invalid:

```csharp
_ = int.TryParse(query["page"], out var page);
if (page < 1)
{
    page = 1;
}
_ = int.TryParse(query["limit"], out var limit);
if (limit < 1 || limit > 100)
{
    limit = 10;
}
```

### Dynamic WHERE Clause

Filters are applied by building a dynamic WHERE clause with parameterized queries to prevent SQL injection. Each filter appends a condition and adds a named parameter:

```csharp
var where = "WHERE 1=1";
var parameters = new List<NpgsqlParameter>();
var argIdx = 1;

if (category != "")
{
    where += $" AND category = @p{argIdx}";
    parameters.Add(new NpgsqlParameter($"p{argIdx}", category));
    argIdx++;
}
```

### Sort Validation

The sort field is validated against a whitelist to prevent SQL injection through the ORDER BY clause:

```csharp
var validSortFields = new HashSet<string> { "id", "name", "price", "category" };
if (!validSortFields.Contains(sortField))
{
    // return 400 Bad Request
}
```

### Pagination Math

The total number of pages is calculated from total items and page size, and the offset is derived from the current page:

```csharp
var offset = (page - 1) * limit;
var totalPages = (int)Math.Ceiling(totalItems / (double)limit);
```

## Exercises

1. **Add search**: Add a `search` query parameter that performs `ILIKE` matching on the product name. For example, `?search=pro` should return products whose name contains "pro" (case-insensitive).

2. **Cursor-based pagination**: Implement cursor-based pagination as an alternative to offset-based pagination. Use `?cursor=<lastId>&limit=10` where the cursor is the ID of the last item from the previous page. Compare the trade-offs with offset-based pagination.

3. **Link headers**: Add `Link` headers following [RFC 5988](https://tools.ietf.org/html/rfc5988) for `next`, `prev`, `first`, and `last` page navigation. Example: `Link: <http://localhost:8080/products?page=2&limit=10>; rel="next"`.

4. **Stats endpoint**: Add a `GET /products/stats` endpoint that returns aggregate statistics per category: minimum price, maximum price, average price, and product count.

## Key Concepts

### Offset Pagination

Offset-based pagination uses `LIMIT` and `OFFSET` in SQL. It is simple to implement and allows jumping to any page. However, it can be slow on large datasets because the database must scan and discard all rows before the offset. It is also susceptible to issues when data is inserted or deleted between page requests, which can cause items to be skipped or duplicated.

### Query Parameters

Query parameters are the standard way to pass filtering, sorting, and pagination options in REST APIs. They keep the URL clean and make the API self-documenting. Parsing them safely requires validation and sensible defaults.

### Sort Validation

Never interpolate user input directly into SQL `ORDER BY` clauses. Always validate against a whitelist of allowed column names. The sort order should be restricted to `asc` or `desc` only.

### Response Metadata

Including pagination metadata in the response lets clients build UI controls (page numbers, next/previous buttons) without making additional requests. The metadata should include the current page, page size, total items, and total pages.

## Cleanup

```bash
docker compose down -v
```
