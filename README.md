# Ticket Reservation API

A REST API where admins create events with a fixed capacity and users reserve and cancel seats.
Built with ASP.NET Core (.NET 10), EF Core and SQL Server to practise authentication,
business rules and, above all, **concurrency handling**: no overbooking, even when many users try to
reserve the last seat at the same moment ([see the experiment](#concurrency)).

[PLAN.md](PLAN.md) contains the full scope, data model, business rules and a decision log explaining
why things are built the way they are.

## Contents

- [Features](#features)
- [Tech stack and architecture](#tech-stack-and-architecture)
- [Running locally](#running-locally)
- [API](#api)
- [Business rules](#business-rules)
- [Concurrency](#concurrency)
- [Tests](#tests)
- [Not in scope (yet)](#not-in-scope-yet)

## Features

- JWT authentication with `Admin` and `User` roles
- Event management for admins: create, update, cancel (cancelling also cancels its reservations)
- Reservations for users: reserve, list own reservations, cancel up to 24 hours before the event
- Public event list with date range, text search, availability filter and pagination
- Business rules enforced in code **and** in the database (check constraints, filtered unique index)
- Safe under concurrent requests: atomic conditional updates instead of check-then-act
- Consistent errors in [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) format with the violated rule id
- OpenAPI document and Scalar UI with JWT support

## Tech stack and architecture

.NET 10 · ASP.NET Core (controllers) · EF Core 10 · SQL Server · FluentValidation · JWT bearer ·
OpenAPI + Scalar · xUnit · `WebApplicationFactory` · `FakeTimeProvider`

```
src/
  TicketReservationApi.Api             Controllers, auth setup, global exception handler, OpenAPI
  TicketReservationApi.Application     Services, DTOs, validators, business rules
  TicketReservationApi.Infrastructure  DbContext, entity configurations, migrations, JWT issuing, seeding
  TicketReservationApi.Domain          Entities and enums
tests/
  TicketReservationApi.UnitTests         Business rules and validators, no database
  TicketReservationApi.IntegrationTests  Full HTTP pipeline against a real SQL Server database
```

Dependencies point inwards: `Api → Application → Domain`, and `Infrastructure` implements the interfaces
defined in `Application` (`IAppDbContext`, `IAccessTokenGenerator`). Time is read through `TimeProvider`
everywhere, so time-dependent rules are testable.

## Running locally

Requirements: .NET 10 SDK and a local SQL Server instance reachable as `.` with Windows authentication
(connection string in `src/TicketReservationApi.Api/appsettings.json`).

Secrets are not stored in the repository. Set them once with user-secrets (the JWT signing key must be at
least 32 bytes; the app refuses to start otherwise):

```bash
cd src/TicketReservationApi.Api
dotnet user-secrets set "Jwt:SigningKey" "<random string of 32+ bytes>"
dotnet user-secrets set "Admin:Email" "admin@ticketreservation.local"
dotnet user-secrets set "Admin:Password" "<admin password>"
cd ../..
```

```bash
dotnet tool restore
dotnet ef database update -p src/TicketReservationApi.Infrastructure -s src/TicketReservationApi.Api
dotnet run --project src/TicketReservationApi.Api
```

API reference (Scalar): http://localhost:5133/scalar/v1

In Development, three test users are seeded: `ali@test.local`, `ayse@test.local`, `can@test.local`, all with
the password `Passw0rd!`. [`TicketReservationApi.http`](src/TicketReservationApi.Api/TicketReservationApi.http)
contains ready-made requests that log in and reuse the tokens (Visual Studio / VS Code REST Client).

## API

| Method | Path | Access | Description |
|---|---|---|---|
| POST | `/api/auth/register` | Anonymous | Create a `User` account |
| POST | `/api/auth/login` | Anonymous | Returns `{ token, expiresAt }` |
| GET | `/api/events` | Anonymous | List events (filters and paging below) |
| GET | `/api/events/{id}` | Anonymous | Event details |
| POST | `/api/events` | Admin | Create an event |
| PUT | `/api/events/{id}` | Admin | Update an event |
| PATCH | `/api/events/{id}/cancel` | Admin | Cancel an event and its reservations |
| GET | `/api/events/{id}/reservations` | Admin | Reservations of an event |
| POST | `/api/reservations` | User | Reserve a seat: `{ "eventId": "…" }` |
| GET | `/api/reservations/{id}` | User | One of my reservations |
| GET | `/api/reservations/me` | User | My reservations |
| PATCH | `/api/reservations/{id}/cancel` | User | Cancel my reservation |

Send the token as `Authorization: Bearer <token>`. Registration always creates a `User`; the `Admin`
account is created at startup from the `Admin:*` settings.

### Listing events

`GET /api/events` is public and paged. All parameters are optional:

| Parameter | Description |
|---|---|
| `from`, `to` | Events starting within this range (inclusive), e.g. `2026-12-01T00:00:00Z`. `from` defaults to now |
| `search` | Case-insensitive match on title or venue |
| `onlyAvailable` | `true` → only events that can be reserved now: active, not started, not full |
| `page`, `pageSize` | Default `1` and `20`; `pageSize` is at most `100` |

Without filters, the list shows upcoming events, including cancelled and fully booked ones (each item has
`status` and `availableSeats`). Past events appear only when `from` is set to an earlier time.
Results are ordered by start time. Invalid values return `400`.

```json
{
  "items": [ { "id": "…", "title": "…", "startsAt": "…", "availableSeats": 12, "status": "Active", "…": "…" } ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 42
}
```

### Errors

All errors use ProblemDetails. Business rule violations include the rule id from the table below:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict.",
  "status": 409,
  "detail": "Event is fully booked.",
  "rule": "K1"
}
```

| Status | Meaning |
|---|---|
| 400 | Validation error (`errors` lists each invalid field) |
| 401 / 403 | Not authenticated / wrong role |
| 404 | Not found, including other users' reservations |
| 409 | Conflict: event full, duplicate reservation, email taken, concurrent modification |
| 422 | Business rule violation |

## Business rules

| # | Rule | Status |
|---|---|---|
| K1 | A full event cannot be reserved | 409 |
| K2 | One active reservation per user and event | 409 |
| K3 | Past or cancelled events cannot be reserved | 422 |
| K4 | Reservations cannot be cancelled less than 24 hours before the event | 422 |
| K5 | Cancelling a reservation frees its seat | — |
| K6 | Capacity cannot be reduced below the number of reservations | 422 |
| K7 | Users only see and cancel their own reservations (others' are reported as not found) | 404 |
| K8 | Cancelling an event cancels all its active reservations | — |
| K9 | Cancelled or started events cannot be updated; an event cannot be moved into the past | 422 / 400 |

## Concurrency

**Scenario:** many users try to reserve the same event at the same moment. Every request first checks
"is there a free seat?" and "does this user already have a reservation?", then saves. If two requests
pass the checks before either one saves, both succeed: the classic *check-then-act* race.

The experiment lives in [`ConcurrencyTests`](tests/TicketReservationApi.IntegrationTests/ConcurrencyTests.cs).
Each test fires 10 parallel HTTP requests against a real SQL Server database (EF Core's InMemory provider
has no transactions or locking, so it cannot reproduce the problem). It was run in three stages; the commits
are kept in history so each stage can be checked out.

| Scenario | Expected | 1. No protection | 2. Optimistic (RowVersion) | 3. Atomic UPDATE + unique index |
|---|---|---|---|---|
| Capacity 1, 10 different users | 1×201, 9×409 | ❌ 10×201, 10 reservations, `ReservedCount = 1` | ✅ | ✅ |
| Capacity 100, same user 10× | 1×201, 9×409 | ❌ 10×201, 10 active reservations for one user | ✅ | ✅ |
| Capacity 100, 10 different users | 10×201 | ❌ 10×201 but `ReservedCount = 1` | ❌ 1×201, 9×409 | ✅ |

### 1. No protection: overbooking and lost updates

To reproduce the race, the `RowVersion` concurrency token was disabled and a `Task.Delay(200)` was placed
between the checks and `SaveChanges`. Every request read `ReservedCount = 0`, passed the checks and wrote `1`:

- **Overbooking:** 10 reservations for a single seat.
- **Lost update:** `ReservedCount` ended at 1 instead of 10; nine increments overwrote each other.
- **K2 broken:** one user ended up with 10 active reservations for the same event.

The `ReservedCount <= Capacity` check constraint did not help: every writer wrote `1`, which is a valid value.
The overbooking is in the reservation rows, not in the counter.

### 2. Optimistic concurrency with `RowVersion`

With the token enabled, EF Core adds `WHERE RowVersion = @original` to the `UPDATE` of the event row.
The first request wins; the others affect zero rows, get a `DbUpdateConcurrencyException` and are mapped to
**409 Conflict**.

This is **correct**, but it causes **false conflicts**: with 100 free seats, 9 of 10 requests still failed,
because every reservation updates the same event row. Clients would need retry logic, and under load most
requests would be wasted.

### 3. Atomic conditional UPDATE + filtered unique index (final solution)

The capacity check and the increment are done in a single statement inside a transaction:

```sql
UPDATE Events SET ReservedCount = ReservedCount + 1
WHERE Id = @id AND Status = 'Active' AND ReservedCount < Capacity
```

- If it affects 0 rows, the event is full → **409** (K1).
- SQL Server's row lock serializes concurrent increments; requests wait briefly instead of failing,
  so no false conflicts.
- The reservation row is inserted in the same transaction. A filtered unique index on
  `(EventId, UserId) WHERE Status = 'Active'` guarantees K2. A concurrent duplicate fails on insert
  (SQL error 2601), the transaction rolls back and the service turns the error into the same
  **409 `K2`** response as the early check, so clients see one answer regardless of timing.
- Cancellation uses the same idea: `UPDATE Reservations SET Status = 'Cancelled' WHERE Id = @id AND Status = 'Active'`.
  Only one of several concurrent cancels affects a row, so the seat is released exactly once.

The earlier checks in the service are kept for clear error messages in the common, non-concurrent case.
The database statements are what actually guarantee the rules.

`RowVersion` remains on `Event` for admin operations (update, cancel): those use tracked entities, and any
concurrent reservation changes the row version, so a stale admin update gets a 409 instead of silently
overwriting `ReservedCount`.

## Tests

```bash
dotnet test
```

The integration tests need the local SQL Server; they create (and recreate on every run) a separate
`TicketReservation_Tests` database. The unit tests need nothing.

| Project | What it covers |
|---|---|
| **UnitTests** | `EventRules`, `ReservationRules` (K1, K3, K4, K6–K9) and validators, with a fixed clock (`FakeTimeProvider`). Rules are plain functions over entities, so no database or mocks are needed. |
| **IntegrationTests** | The full HTTP pipeline with real JWTs against SQL Server: every rule end to end with its status code and rule id (`BusinessRuleTests`), authentication and roles (`AuthTests`), the event list (`EventListTests`) and the concurrency scenarios (`ConcurrencyTests`). |

K2 and K5 are enforced by the database (unique index, atomic updates), so they are covered by integration
tests only.

## Not in scope (yet)

Seat numbers, waiting lists, email notifications, payments, caching, rate limiting, refresh tokens,
Docker and CI. See [PLAN.md](PLAN.md#1-scope).
