# Requirements Document

## Introduction

This document defines the requirements for scaffolding the Nornis .NET solution. The scaffolding establishes the foundational project structure, build pipeline, containerization, and health endpoint needed before any feature development begins. The goal is a solution that builds cleanly, passes tests, enforces formatting, and provides containerized deployable services.

## Glossary

- **Solution**: The .NET solution file (Nornis.sln) that groups all source and test projects
- **Source_Project**: A .NET project under the src/ directory containing production code
- **Test_Project**: An NUnit test project under the tests/ directory that validates a corresponding Source_Project
- **Layer_Dependency_Rules**: The architectural constraint that Domain has no infrastructure dependencies, Application depends on Domain, Infrastructure depends on Application and Domain, and presentation projects (Web, Api, Worker) depend on Application and Infrastructure
- **Health_Endpoint**: An anonymous HTTP GET endpoint at /health that returns service status for Kubernetes liveness and readiness probes
- **CI_Workflow**: A GitHub Actions workflow that restores, builds, tests, and checks formatting on every pull request
- **EditorConfig**: A .editorconfig file that defines code formatting rules enforced by the build and CI pipeline
- **Dockerfile**: A multi-stage container build file that produces a minimal runtime image for a deployable service

## Requirements

### Requirement 1: Solution Structure

**User Story:** As a developer, I want a .NET solution with clearly separated projects following clean architecture layers, so that I can develop features with proper dependency boundaries from the start.

#### Acceptance Criteria

1. THE Solution SHALL contain the following source projects: Nornis.Web, Nornis.Api, Nornis.Worker, Nornis.Application, Nornis.Domain, Nornis.Infrastructure, Nornis.Shared
2. THE Solution SHALL contain the following test projects: Nornis.Web.Tests, Nornis.Api.Tests, Nornis.Worker.Tests, Nornis.Application.Tests, Nornis.Domain.Tests, Nornis.Infrastructure.Tests, Nornis.Shared.Tests
3. THE Solution SHALL place all source projects under the src/ directory
4. THE Solution SHALL place all test projects under the tests/ directory
5. WHEN dotnet build is executed against the Solution, THE Solution SHALL compile without errors or warnings treated as errors
6. THE Solution SHALL enforce the following project dependency rules: Nornis.Domain SHALL NOT reference any other source project; Nornis.Application SHALL reference only Nornis.Domain and Nornis.Shared; Nornis.Infrastructure SHALL reference only Nornis.Application, Nornis.Domain, and Nornis.Shared; Nornis.Web, Nornis.Api, and Nornis.Worker SHALL NOT reference each other
7. THE Solution SHALL contain a single solution file (Nornis.sln) located in the repository root directory
8. Each test project SHALL reference its corresponding source project (e.g., Nornis.Domain.Tests references Nornis.Domain)

### Requirement 2: Layer Dependency Rules

**User Story:** As an architect, I want project references to enforce clean architecture boundaries, so that domain logic remains decoupled from infrastructure concerns.

#### Acceptance Criteria

1. THE Nornis.Domain project SHALL have zero references to Nornis.Infrastructure, Nornis.Api, Nornis.Web, Nornis.Worker, or any EF Core, Azure SDK, Auth0, MudBlazor, DataDog, or Azure OpenAI packages
2. THE Nornis.Application project SHALL reference Nornis.Domain and Nornis.Shared only
3. THE Nornis.Infrastructure project SHALL reference Nornis.Application, Nornis.Domain, and Nornis.Shared only
4. THE Nornis.Api project SHALL reference Nornis.Application, Nornis.Infrastructure, and Nornis.Shared only
5. THE Nornis.Web project SHALL reference Nornis.Application, Nornis.Infrastructure, and Nornis.Shared only
6. THE Nornis.Worker project SHALL reference Nornis.Application, Nornis.Infrastructure, and Nornis.Shared only
7. THE Nornis.Shared project SHALL have zero references to any other Nornis project

### Requirement 3: Test Project Configuration

**User Story:** As a developer, I want every source project to have a corresponding NUnit test project with proper references, so that I can write tests for any class immediately.

#### Acceptance Criteria

1. THE Test_Project SHALL reference the NUnit framework package, NUnit3TestAdapter package, and Microsoft.NET.Test.Sdk package
2. THE Test_Project SHALL reference the corresponding Source_Project it tests, following the naming convention Nornis.<Layer>.Tests for each Nornis.<Layer> source project (Nornis.Web.Tests, Nornis.Api.Tests, Nornis.Worker.Tests, Nornis.Application.Tests, Nornis.Domain.Tests, Nornis.Infrastructure.Tests, Nornis.Shared.Tests)
3. WHEN dotnet test is executed against the Solution, THE Solution SHALL discover and run tests in all 7 test projects and report a passing result with zero test failures
4. THE Test_Project SHALL contain at least one test method decorated with the NUnit [Test] attribute that asserts a known-true condition and passes without requiring external dependencies

### Requirement 4: EditorConfig and Formatting

**User Story:** As a developer, I want consistent code formatting enforced by tooling, so that code reviews focus on logic rather than style.

#### Acceptance Criteria

1. THE Solution SHALL include a root .editorconfig file that defines C# formatting rules
2. THE EditorConfig SHALL configure indent_style as space, indent_size as 4, end_of_line as crlf, and charset as utf-8-bom
3. THE EditorConfig SHALL configure C# file-scoped namespaces, system usings sorted first, and expression-bodied member preferences
4. WHEN dotnet format is executed with the --verify-no-changes flag, THE Solution SHALL report no formatting violations for scaffolded code

### Requirement 5: Dockerfiles for Deployable Services

**User Story:** As a DevOps engineer, I want Dockerfiles for each deployable service, so that I can build container images for Kubernetes deployment.

#### Acceptance Criteria

1. THE Solution SHALL include a Dockerfile at src/Nornis.Api/Dockerfile for the Nornis.Api project
2. THE Solution SHALL include a Dockerfile at src/Nornis.Web/Dockerfile for the Nornis.Web project
3. THE Solution SHALL include a Dockerfile at src/Nornis.Worker/Dockerfile for the Nornis.Worker project
4. THE Dockerfile SHALL use multi-stage builds with a separate build stage and runtime stage
5. THE Nornis.Api and Nornis.Web Dockerfiles SHALL use the official .NET SDK image for the build stage and the ASP.NET Core runtime image for the runtime stage. THE Nornis.Worker Dockerfile SHALL use the official .NET SDK image for the build stage and the base .NET runtime image for the runtime stage.
6. THE Dockerfile SHALL produce a container that runs as a non-root user
7. THE Dockerfile SHALL include version metadata labels for org.opencontainers.image.source and org.opencontainers.image.revision that accept values via build arguments
8. THE Nornis.Api and Nornis.Web Dockerfiles SHALL expose port 8080
9. THE Dockerfile SHALL use the solution root as the build context to support multi-project restore

### Requirement 6: GitHub Actions CI Workflow

**User Story:** As a developer, I want a CI pipeline that validates every pull request builds, tests pass, and formatting is correct, so that broken code does not merge.

#### Acceptance Criteria

1. THE CI_Workflow SHALL trigger on pull requests targeting the main branch
2. THE CI_Workflow SHALL trigger on pushes to the main branch
3. THE CI_Workflow SHALL execute steps in the following order: dotnet restore, dotnet build, dotnet test, and dotnet format --verify-no-changes
4. THE CI_Workflow SHALL execute a formatting check using dotnet format with the --verify-no-changes flag
5. IF dotnet build fails, THEN THE CI_Workflow SHALL mark the workflow run as failed and skip all subsequent steps
6. IF dotnet test fails, THEN THE CI_Workflow SHALL mark the workflow run as failed
7. IF the formatting check fails, THEN THE CI_Workflow SHALL mark the workflow run as failed
8. THE CI_Workflow SHALL specify a pinned .NET SDK version so that builds are deterministic across runs

### Requirement 7: Health Endpoint

**User Story:** As a DevOps engineer, I want the API to expose a health endpoint, so that Kubernetes can perform liveness and readiness checks.

#### Acceptance Criteria

1. THE Nornis.Api SHALL expose an HTTP GET endpoint at the path /health
2. WHILE the service is able to accept and process requests, THE Health_Endpoint SHALL return HTTP status code 200 with a JSON response body containing a "status" field set to "Healthy"
3. THE Health_Endpoint SHALL be accessible without authentication
4. THE Health_Endpoint SHALL return a response with Content-Type application/json
5. IF the service is unable to process requests, THEN THE Health_Endpoint SHALL return HTTP status code 503 with a JSON response body containing a "status" field set to "Unhealthy"
6. THE Health_Endpoint SHALL return a response within 5 seconds

### Requirement 8: Project SDK and Framework Configuration

**User Story:** As a developer, I want all projects to target a consistent .NET version with nullable reference types enabled, so that the codebase uses modern C# features from the start.

#### Acceptance Criteria

1. THE Nornis.Web, Nornis.Api, Nornis.Worker, Nornis.Application, Nornis.Domain, Nornis.Infrastructure, and Nornis.Shared projects SHALL each target net8.0
2. THE Nornis.Web, Nornis.Api, Nornis.Worker, Nornis.Application, Nornis.Domain, Nornis.Infrastructure, and Nornis.Shared projects SHALL each enable nullable reference types
3. THE Nornis.Web, Nornis.Api, Nornis.Worker, Nornis.Application, Nornis.Domain, Nornis.Infrastructure, and Nornis.Shared projects SHALL each enable implicit usings
4. THE Nornis.Web project SHALL use the Microsoft.NET.Sdk.Web SDK
5. THE Nornis.Api project SHALL use the Microsoft.NET.Sdk.Web SDK
6. THE Nornis.Worker project SHALL use the Microsoft.NET.Sdk.Worker SDK
7. THE Nornis.Application project SHALL use the Microsoft.NET.Sdk SDK
8. THE Nornis.Domain project SHALL use the Microsoft.NET.Sdk SDK
9. THE Nornis.Infrastructure project SHALL use the Microsoft.NET.Sdk SDK
10. THE Nornis.Shared project SHALL use the Microsoft.NET.Sdk SDK
11. THE Nornis.Web.Tests, Nornis.Api.Tests, Nornis.Worker.Tests, Nornis.Application.Tests, Nornis.Domain.Tests, Nornis.Infrastructure.Tests, and Nornis.Shared.Tests projects SHALL each target net8.0 with nullable reference types and implicit usings enabled
