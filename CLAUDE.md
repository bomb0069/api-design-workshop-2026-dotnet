# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

API Design Workshop — .NET Edition. A hands-on learning path for API design using ASP.NET Core on .NET 8, starting from simple RESTful APIs and progressively introducing more advanced API technologies. This repo is the C#/.NET sibling of the Go-based API Design Workshop; lab numbering, endpoints, ports, and behavior mirror the Go edition.

## Structure

Labs use `labXX-YY-topic-name` naming where XX = group number, YY = sub-lab number. Each group folder (`labXX-group-name/`) contains sub-lab folders. Each sub-lab is a self-contained .NET 8 project (csproj at the sub-lab root, or in per-app subfolders like `server/`/`client/` or `publisher/`/`consumer/` for multi-app labs) with its own `docker-compose.yml`.

## Running a Lab

```bash
cd labXX-group-name/labXX-YY-topic
docker compose up --build
# or locally:
dotnet run
```

## Conventions

- Target framework: `net8.0` for all projects
- Minimal APIs for simple labs; MVC controllers where the lesson benefits (e.g., API versioning)
- Multi-stage Dockerfiles: build on `mcr.microsoft.com/dotnet/sdk:8.0`, run on `mcr.microsoft.com/dotnet/aspnet:8.0`
- Each lab is self-contained with its own `docker-compose.yml`
- JSON property names, routes, ports, and status codes must match the Go edition of the same lab
- Lab numbering (`labXX`) defines the learning order; topic slugs are kebab-case
