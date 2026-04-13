# PM_Agent

ASP.NET Core solution scaffold for an AI Agent focused on project management planning.

## Project structure

- `src/PMAgent.Api`: HTTP API with planning endpoint.
- `src/PMAgent.Application`: Use-case contracts and request/response models.
- `src/PMAgent.Domain`: Domain layer placeholder for core entities.
- `src/PMAgent.Infrastructure`: Service implementations and DI wiring.
- `tests/PMAgent.Tests`: xUnit tests.

## Run the API

```bash
dotnet run --project src/PMAgent.Api
```

## Test

```bash
dotnet test PMAgent.slnx
```

## Sample request

`POST /api/planning/next-actions`

```json
{
	"projectName": "PM Agent",
	"goal": "Build MVP for project managers",
	"constraints": ["2 month deadline", "small team"],
	"teamMembers": ["Alice", "Bob"]
}
```