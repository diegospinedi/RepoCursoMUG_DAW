# AGENTS.md — project context

> **DAW template.** Fill in the `[...]` with what is true of YOUR project and delete what does not
> apply. This file describes **the project**; **the process** is DAW's job (phases, gates, when to
> test, when to commit). Do not mix the two: process rules written here compete with the pipeline's.
>
> It is **tool-agnostic on purpose**: Claude Code reads it through the import in `CLAUDE.md`, Codex
> CLI, Copilot CLI, Cursor and OpenCode read it directly, and Gemini CLI gets it through
> `GEMINI.md`. The same file serves whichever tool you open the repo with — which is the point:
> porting the pipeline to another tool must not mean rewriting what your project is.

---

## Language

**Always respond in the language the user writes in.** Write every artifact you produce — PRDs,
specs, ADRs, reports, commit messages, status lines — in that same language, regardless of the
language these instructions are written in.

If this project has a fixed working language, state it here and use it instead:

> Working language: Spanish

---

## What this project is

Aplicación web para Óptica Sistema: presupuestos con catálogo de artículos y precio de venta calculado.
Desde un presupuesto en estado Final emite factura electrónica a consumidor final vía web services de ARCA (WSFEv1).

**Reference PRD:** `PRD.md`

---

## Stack

- .NET 10 (ASP.NET Core Web API) + EF Core 10
- SQLite como base de datos local (archivo, sin servidor)
- React 19 + TypeScript + Vite, sobre Node 22
- Integración ARCA WSFEv1 aislada en su propio módulo (RF-27, cambios normativos)
- Install: npm ci en web/ y e2e/ (npm ≥ 10.9.8)
- Build/typecheck: dotnet build Optica.sln · web: npm run build
- Tests: dotnet test Optica.sln · web: npm run test:coverage · e2e: npm run test:harness
---

## Architecture conventions

Use a modular monolith organized into explicit layers.
Use a single deployable application and SQLite as the initial relational database.
Organize code by responsibility and maintain clear module boundaries.
Do not introduce microservices, distributed infrastructure or additional databases without explicit approval.
Keep the architecture simple and proportional to the project's requirements.
Layers and Responsibilities
Presentation Layer
Handle HTTP requests, routing, input validation and response serialization.
Use controllers that delegate business operations to application use cases.
Do not implement business rules or execute SQL directly.
Translate application errors into appropriate transport responses.
Application Layer
Implement use cases and coordinate business operations.
Define transaction boundaries and orchestration.
Depend on domain abstractions rather than concrete infrastructure implementations.
Avoid coupling application logic to HTTP frameworks or SQLite APIs.
Domain Layer
Contain business entities, value objects, domain services and business rules.
Define repository contracts where persistence abstractions are required.
Remain independent of databases, web frameworks and infrastructure details.
Enforce invariants regardless of which interface invokes the domain.
Infrastructure Layer
Implement repository contracts and database access.
Encapsulate SQLite connections, SQL queries and persistence mapping.
Manage schema migrations and database initialization.
Keep database-specific code out of the domain and presentation layers.
Dependency Rules
Presentation may depend on Application.
Application may depend on Domain.
Infrastructure may implement interfaces defined by Domain or Application.
Domain must not depend on Application, Presentation or Infrastructure.
Wire concrete implementations through a single composition root.
Avoid circular dependencies between modules.
Database: SQLite
Use SQLite as the initial persistence engine.
Store database files in a configurable location outside the source tree.
Use parameterized queries for all data values.
Enable foreign key enforcement on database connections.
Use transactions for operations that must succeed or fail atomically.
Implement schema changes through versioned migrations.
Define backup and restore procedures appropriate to the deployment.
Do not assume SQLite supports concurrent write workloads like a client-server database.
Evaluate a database migration only when concrete requirements justify it.
Testing Strategy
Unit-test domain rules without requiring a database.
Test application use cases using controlled repository implementations.
Integration-test SQLite repositories against a temporary test database.
Use isolated test data and clean up resources after each test.
Add end-to-end tests for critical user workflows.
Change Management
Follow the existing architecture before introducing new patterns.
Update tests and documentation when architectural behavior changes.
Avoid unrelated refactors.
Document significant architectural decisions and their trade-offs.
Report any change that would violate the dependency rules.
---

## Code conventions

---

## Domain glossary

---

## What NOT to do in this project

- **No modificar registros cerrados.** Un presupuesto en estado Final no se edita ni vuelve a Borrador (RF-08, RF-67), y una factura con CAE no se modifica ni se elimina (RF-32). No agregues endpoints, migraciones ni "fixes" que permitan eso. La base lo refuerza con triggers de SQLite (migraciones `CierrePresupuestos` y `FacturasInmutablesYBusqueda`): no los borres. Para cargar datos de prueba, el presupuesto se graba en Borrador y después pasa a Final, como en la app.
- **No quitar la leyenda de comprobante simulado.** Con `Arca:Entorno = Simulado`, el PDF de la factura dice "COMPROBANTE SIMULADO — SIN VALIDEZ FISCAL": su CAE no existe en ARCA.
- **No copiar la base con una copia de archivo común.** SQLite está en modo WAL: los cambios recientes pueden estar en `optica.db-wal`. Un backup (RNF-02) tiene que usar `VACUUM INTO` o la API de backup de SQLite.
- **No emitir contra ARCA producción.** Todo desarrollo y prueba va contra el entorno de homologación. No apuntes a producción ni uses el certificado productivo para probar; un comprobante autorizado por error no se puede anular desde el sistema (las notas de crédito están fuera de alcance).
- **No ampliar el alcance.** Quedan afuera de esta versión: facturas A, notas de crédito/débito y anulaciones, envío automático por email o WhatsApp, ABM de clientes y módulo de usuarios con roles. Si algo parece necesitarlos, preguntá antes de implementarlo.



> ℹ️ **What does NOT belong in this file, because DAW provides it:** the order work happens in, when
> the spec gets written, when tests run, when to commit, what it takes to move between phases. All
> of that lives in `.daw/` and applies on its own.

<!-- BEGIN DAW (managed by DAW — do not edit by hand) -->
# DAW — Dilux Agentic Workflow

This repo uses **DAW**: an agent-driven development pipeline with the phases
`CLASSIFY → DEFINE → PLAN → CODE → VERIFY → RELEASE`.

Before answering, read `.daw/orchestrator.md` and run its Boot Sequence. It is a strict state
machine: it decides what you are allowed to do based on the phase recorded in `.daw-state.json`.

The project's own context — stack, architecture, domain — is elsewhere in this file. It lives here,
in `AGENTS.md`, and not in any one tool's file, on purpose: it is tool-agnostic and comes along
unchanged when the pipeline is ported to another agent.
<!-- END DAW -->
