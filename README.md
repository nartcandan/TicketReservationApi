# Ticket Reservation API

A REST API where admins create events with a fixed capacity and users reserve and cancel seats.
Built with ASP.NET Core (.NET 10), EF Core and SQL Server to practise authentication,
business rules and, above all, **concurrency handling**.

See [PLAN.md](PLAN.md) for scope, business rules (K1–K9), endpoints and the decision log.

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

### Authentication

- `POST /api/auth/register` always creates a `User`. The `Admin` account is created at startup from the
  `Admin:*` settings.
- `POST /api/auth/login` returns a JWT (`{ token, expiresAt }`). Send it as `Authorization: Bearer <token>`.
- In Development, three test users are seeded: `ali@test.local`, `ayse@test.local`, `can@test.local`,
  all with the password `Passw0rd!`.
- [`TicketReservationApi.http`](src/TicketReservationApi.Api/TicketReservationApi.http) logs in and reuses
  the tokens automatically (Visual Studio / VS Code REST Client).

Integration tests create and use a separate `TicketReservation_Tests` database:

```bash
dotnet test
```

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
  (SQL error 2601), the transaction rolls back and the API returns **409**.
- Cancellation uses the same idea: `UPDATE Reservations SET Status = 'Cancelled' WHERE Id = @id AND Status = 'Active'`.
  Only one of several concurrent cancels affects a row, so the seat is released exactly once.

The earlier checks in the service are kept for clear error messages in the common, non-concurrent case.
The database statements are what actually guarantee the rules.

`RowVersion` remains on `Event` for admin operations (update, cancel): those use tracked entities, and any
concurrent reservation changes the row version, so a stale admin update gets a 409 instead of silently
overwriting `ReservedCount`.
