# Implementation Plan: Project Scaffolding

## Overview

Scaffold the Nornis .NET solution with all source projects, test projects, shared build configuration, formatting rules, Dockerfiles, CI workflow, and health endpoint. Each task builds incrementally so the solution compiles and passes tests at every checkpoint.

## Tasks

- [x] 1. Create solution file and shared build configuration
  - [x] 1.1 Create Nornis.sln, Directory.Build.props, and global.json at the repository root
    - Create `Nornis.sln` solution file in the repository root
    - Create `Directory.Build.props` with TargetFramework net8.0, Nullable enable, ImplicitUsings enable, TreatWarningsAsErrors true
    - Create `global.json` pinning SDK version to 8.0.x for deterministic builds
    - _Requirements: 1.7, 8.1, 8.2, 8.3_

  - [x] 1.2 Create .editorconfig at the repository root
    - Configure indent_style space, indent_size 4, end_of_line crlf, charset utf-8-bom
    - Configure C# file-scoped namespaces, system usings sorted first, expression-bodied member preferences
    - _Requirements: 4.1, 4.2, 4.3_

- [x] 2. Create domain and shared library projects
  - [x] 2.1 Create Nornis.Domain class library project
    - Create `src/Nornis.Domain/Nornis.Domain.csproj` using Microsoft.NET.Sdk
    - Ensure zero project references (domain has no dependencies on other Nornis projects)
    - Add project to Nornis.sln
    - _Requirements: 1.1, 1.3, 2.1, 8.8_

  - [x] 2.2 Create Nornis.Shared class library project
    - Create `src/Nornis.Shared/Nornis.Shared.csproj` using Microsoft.NET.Sdk
    - Ensure zero project references (shared has no dependencies on other Nornis projects)
    - Add project to Nornis.sln
    - _Requirements: 1.1, 1.3, 2.7, 8.10_

- [x] 3. Create application and infrastructure projects
  - [x] 3.1 Create Nornis.Application class library project
    - Create `src/Nornis.Application/Nornis.Application.csproj` using Microsoft.NET.Sdk
    - Add ProjectReference to Nornis.Domain and Nornis.Shared only
    - Add project to Nornis.sln
    - _Requirements: 1.1, 1.3, 2.2, 8.7_

  - [x] 3.2 Create Nornis.Infrastructure class library project
    - Create `src/Nornis.Infrastructure/Nornis.Infrastructure.csproj` using Microsoft.NET.Sdk
    - Add ProjectReference to Nornis.Application, Nornis.Domain, and Nornis.Shared only
    - Add project to Nornis.sln
    - _Requirements: 1.1, 1.3, 2.3, 8.9_

- [x] 4. Create deployable service projects
  - [x] 4.1 Create Nornis.Api web project with health endpoint
    - Create `src/Nornis.Api/Nornis.Api.csproj` using Microsoft.NET.Sdk.Web
    - Add ProjectReference to Nornis.Application, Nornis.Infrastructure, and Nornis.Shared
    - Implement Program.cs with health check middleware mapped to GET /health
    - Health endpoint returns 200 with `{"status":"Healthy"}` when healthy, 503 with `{"status":"Unhealthy"}` when unhealthy
    - Health endpoint uses Content-Type application/json, requires no authentication (AllowAnonymous)
    - Add custom ResponseWriter for JSON formatting
    - Add project to Nornis.sln
    - _Requirements: 1.1, 1.3, 2.4, 7.1, 7.2, 7.3, 7.4, 7.5, 7.6, 8.5_

  - [x] 4.2 Create Nornis.Web Blazor project
    - Create `src/Nornis.Web/Nornis.Web.csproj` using Microsoft.NET.Sdk.Web
    - Add ProjectReference to Nornis.Application, Nornis.Infrastructure, and Nornis.Shared
    - Implement minimal Program.cs for Blazor Web App
    - Add project to Nornis.sln
    - _Requirements: 1.1, 1.3, 2.5, 8.4_

  - [x] 4.3 Create Nornis.Worker background service project
    - Create `src/Nornis.Worker/Nornis.Worker.csproj` using Microsoft.NET.Sdk.Worker
    - Add ProjectReference to Nornis.Application, Nornis.Infrastructure, and Nornis.Shared
    - Implement minimal Program.cs with Worker service host
    - Add project to Nornis.sln
    - _Requirements: 1.1, 1.3, 2.6, 8.6_

- [x] 5. Checkpoint - Verify solution builds
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Create test projects
  - [x] 6.1 Create Nornis.Domain.Tests project
    - Create `tests/Nornis.Domain.Tests/Nornis.Domain.Tests.csproj` targeting net8.0
    - Add NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk package references
    - Add ProjectReference to Nornis.Domain
    - Add placeholder sanity test class with one passing [Test] method
    - Add project to Nornis.sln
    - _Requirements: 1.2, 1.4, 3.1, 3.2, 3.3, 3.4, 8.11_

  - [x] 6.2 Create Nornis.Shared.Tests project
    - Create `tests/Nornis.Shared.Tests/Nornis.Shared.Tests.csproj` targeting net8.0
    - Add NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk package references
    - Add ProjectReference to Nornis.Shared
    - Add placeholder sanity test class with one passing [Test] method
    - Add project to Nornis.sln
    - _Requirements: 1.2, 1.4, 3.1, 3.2, 3.3, 3.4, 8.11_

  - [x] 6.3 Create Nornis.Application.Tests project
    - Create `tests/Nornis.Application.Tests/Nornis.Application.Tests.csproj` targeting net8.0
    - Add NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk package references
    - Add ProjectReference to Nornis.Application
    - Add placeholder sanity test class with one passing [Test] method
    - Add project to Nornis.sln
    - _Requirements: 1.2, 1.4, 3.1, 3.2, 3.3, 3.4, 8.11_

  - [x] 6.4 Create Nornis.Infrastructure.Tests project
    - Create `tests/Nornis.Infrastructure.Tests/Nornis.Infrastructure.Tests.csproj` targeting net8.0
    - Add NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk package references
    - Add ProjectReference to Nornis.Infrastructure
    - Add placeholder sanity test class with one passing [Test] method
    - Add project to Nornis.sln
    - _Requirements: 1.2, 1.4, 3.1, 3.2, 3.3, 3.4, 8.11_

  - [x] 6.5 Create Nornis.Api.Tests project
    - Create `tests/Nornis.Api.Tests/Nornis.Api.Tests.csproj` targeting net8.0
    - Add NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk package references
    - Add ProjectReference to Nornis.Api
    - Add placeholder sanity test class with one passing [Test] method
    - Add project to Nornis.sln
    - _Requirements: 1.2, 1.4, 3.1, 3.2, 3.3, 3.4, 8.11_

  - [x] 6.6 Create Nornis.Web.Tests project
    - Create `tests/Nornis.Web.Tests/Nornis.Web.Tests.csproj` targeting net8.0
    - Add NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk package references
    - Add ProjectReference to Nornis.Web
    - Add placeholder sanity test class with one passing [Test] method
    - Add project to Nornis.sln
    - _Requirements: 1.2, 1.4, 3.1, 3.2, 3.3, 3.4, 8.11_

  - [x] 6.7 Create Nornis.Worker.Tests project
    - Create `tests/Nornis.Worker.Tests/Nornis.Worker.Tests.csproj` targeting net8.0
    - Add NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk package references
    - Add ProjectReference to Nornis.Worker
    - Add placeholder sanity test class with one passing [Test] method
    - Add project to Nornis.sln
    - _Requirements: 1.2, 1.4, 3.1, 3.2, 3.3, 3.4, 8.11_

- [x] 7. Checkpoint - Verify build and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Create Dockerfiles for deployable services
  - [x] 8.1 Create Dockerfile for Nornis.Api
    - Create `src/Nornis.Api/Dockerfile` with multi-stage build
    - Use mcr.microsoft.com/dotnet/sdk:8.0 for build stage
    - Use mcr.microsoft.com/dotnet/aspnet:8.0 for runtime stage
    - Copy all .csproj files for restore, then copy full source for publish
    - Run as non-root user (appuser)
    - Expose port 8080
    - Accept IMAGE_SOURCE and IMAGE_REVISION build arguments for OCI labels
    - Use solution root as build context
    - _Requirements: 5.1, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9_

  - [x] 8.2 Create Dockerfile for Nornis.Web
    - Create `src/Nornis.Web/Dockerfile` with multi-stage build
    - Use mcr.microsoft.com/dotnet/sdk:8.0 for build stage
    - Use mcr.microsoft.com/dotnet/aspnet:8.0 for runtime stage
    - Copy all .csproj files for restore, then copy full source for publish
    - Run as non-root user (appuser)
    - Expose port 8080
    - Accept IMAGE_SOURCE and IMAGE_REVISION build arguments for OCI labels
    - Use solution root as build context
    - _Requirements: 5.2, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9_

  - [x] 8.3 Create Dockerfile for Nornis.Worker
    - Create `src/Nornis.Worker/Dockerfile` with multi-stage build
    - Use mcr.microsoft.com/dotnet/sdk:8.0 for build stage
    - Use mcr.microsoft.com/dotnet/runtime:8.0 for runtime stage (not aspnet)
    - Copy all .csproj files for restore, then copy full source for publish
    - Run as non-root user (appuser)
    - Do NOT expose ports (worker does not serve HTTP)
    - Accept IMAGE_SOURCE and IMAGE_REVISION build arguments for OCI labels
    - Use solution root as build context
    - _Requirements: 5.3, 5.4, 5.5, 5.6, 5.7, 5.9_

- [x] 9. Create GitHub Actions CI workflow
  - [x] 9.1 Create .github/workflows/ci.yml
    - Trigger on pull requests targeting main and pushes to main
    - Setup .NET SDK with pinned version 8.0.x
    - Execute steps in order: checkout, setup-dotnet, dotnet restore, dotnet build --no-restore, dotnet test --no-build, dotnet format --verify-no-changes
    - Fail the workflow if any step fails (subsequent steps are skipped on failure)
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7, 6.8_

- [x] 10. Final checkpoint - Verify formatting compliance
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at natural break points
- The design has no Correctness Properties section, so property-based tests are not applicable for this scaffolding feature
- Unit tests are placeholder sanity checks; meaningful tests will be added as feature code is developed
- Dockerfiles use solution-root context so they must be built with `docker build -f src/Nornis.Api/Dockerfile .` from the repo root

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.2"] },
    { "id": 1, "tasks": ["2.1", "2.2"] },
    { "id": 2, "tasks": ["3.1", "3.2"] },
    { "id": 3, "tasks": ["4.1", "4.2", "4.3"] },
    { "id": 4, "tasks": ["6.1", "6.2", "6.3", "6.4", "6.5", "6.6", "6.7"] },
    { "id": 5, "tasks": ["8.1", "8.2", "8.3", "9.1"] }
  ]
}
```
