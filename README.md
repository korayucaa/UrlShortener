# URL Shortener API

A URL shortening REST API built with **ASP.NET Core 8**, **PostgreSQL**, **Redis**, and **Docker Compose**.

## Features

- Create short URLs through a REST API
- Redirect short codes to original URLs
- Track URL click counts
- Store URL data in PostgreSQL using Entity Framework Core
- Cache redirect lookups with Redis
- Apply database migrations at application startup
- Run the API and its dependencies with Docker Compose

## Technologies

- C# / ASP.NET Core 8
- Entity Framework Core
- PostgreSQL
- Redis
- Docker / Docker Compose
- Swagger / OpenAPI

## Getting Started

### Prerequisites

- Docker Desktop
- Git

### Setup

1. Clone the repository:

   ```bash
   git clone https://github.com/korayucaa/UrlShortener.git
   cd UrlShortener
   ```

2. Create your local environment file from the example:

   **PowerShell:**

   ```powershell
   Copy-Item .env.example .env
   ```

3. Open `.env` and set a local PostgreSQL password.

4. Start the application:

   ```bash
   docker compose up --build -d
   ```

5. Open Swagger in your browser:

   http://localhost:8080/swagger

## API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/Urls` | Create a short URL |
| GET | `/api/Urls` | List URLs, if enabled in the current implementation |
| GET | `/{shortCode}` | Redirect to the original URL |

## Example Request

```json
{
  "originalUrl": "https://www.udemy.com/"
}
```

## Environment Variables

Configure these values in your local `.env` file:

| Variable | Description |
|---|---|
| `POSTGRES_DB` | PostgreSQL database name |
| `POSTGRES_USER` | PostgreSQL username |
| `POSTGRES_PASSWORD` | Local PostgreSQL password |

## Stopping the Application

```bash
docker compose down
```

The PostgreSQL data volume is preserved when using this command. To delete the database volume as well, use `docker compose down -v` — this permanently removes the stored database data.

## License

No license specified yet.
