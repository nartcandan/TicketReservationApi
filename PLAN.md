# Ticket Reservation API — Project Plan

A REST API where admins create events and users reserve and cancel seats for events with limited capacity.

The project is built to practise ASP.NET Core Web API, EF Core, authentication, business rules and **concurrency** handling end to end.

---

## 1. Scope

### MVP
- Registration / login (JWT)
- Roles: `Admin`, `User`
- Event CRUD (Admin)
- Creating and cancelling reservations (User)
- Filtering and pagination of the event list
- Protection against concurrent reservation requests

### Deferred
- [ ] Reservations with seat numbers
- [ ] Waiting list (notify the next person when a seat frees up)
- [ ] Email notifications
- [ ] Payment simulation
- [ ] Caching (event list)
- [ ] Rate limiting
- [ ] Refresh tokens
- [ ] Full Docker deployment + CI (GitHub Actions), including MSSQL and Testcontainers

> **Model decision:** in the MVP, events are **capacity-based** (e.g. a concert for 100 people). Seat selection is deferred because it significantly expands the scope.

---

## 2. Roles

| Role | Permissions |
|---|---|
| **Admin** | Create, update and cancel events; view an event's reservations |
| **User** | List events; make reservations; view and cancel their own reservations |

- Registration always creates a `User` account.
- The admin account is **seeded** at application startup (credentials from configuration).

---

## 3. Business Rules

Rule IDs (K1–K9) are referenced in code comments and returned in the `rule` field of error responses.

| # | Rule | Error |
|---|---|---|
| K1 | A reservation cannot be made when the event is full. | 409 |
| K2 | A user cannot have more than one **active** reservation for the same event. | 409 |
| K3 | Past or cancelled events cannot be reserved. | 422 |
| K4 | A reservation cannot be cancelled less than 24 hours before the event starts. | 422 |
| K5 | Cancelling a reservation frees up its seat. | — |
| K6 | An admin cannot reduce capacity below the current number of reservations. | 422 |
| K7 | Users can only view and cancel their own reservations. Someone else's reservation is treated as non-existent. | 404 |
| K8 | When an admin cancels an event, all of its active reservations become `Cancelled`. | — |
| K9 | Cancelled or already started events cannot be updated; `StartsAt` cannot be moved into the past. | 422 / 400 |

Each rule gets at least one unit test at the service layer. Time-dependent rules (K3, K4, K9) are tested through `TimeProvider`.

---

## 4. Data Model

```
User
- Id
- Email (unique)
- PasswordHash
- FullName
- Role (Admin / User)
- CreatedAt

Event
- Id
- Title
- Description
- Venue
- StartsAt
- Capacity
- ReservedCount
- Status (Active / Cancelled)
- RowVersion        ← optimistic concurrency
- CreatedAt

Reservation
- Id
- EventId  → Event
- UserId   → User
- Status (Active / Cancelled)
- CreatedAt
- CancelledAt (nullable)
```

**Relationships**
- User 1 — N Reservation
- Event 1 — N Reservation

**Constraints**
- `User.Email` unique
- `Reservation (EventId, UserId)` → **filtered unique index** (`WHERE [Status] = 'Active'`) — database-level guarantee for K2
- `Event.Capacity > 0`, `Event.ReservedCount >= 0`, `Event.ReservedCount <= Event.Capacity` (check constraints) — database-level guarantee for K1 and K6

**Notes**
- Enums are stored as **strings** in the database (for the filtered index and readability).
- All timestamps are stored in **UTC** (`DateTimeOffset`).
- Reservations are never deleted; they are set to `Status = Cancelled` (soft cancel), so history is preserved.

---

## 5. Endpoints

### Auth
| Method | Path | Access | Description |
|---|---|---|---|
| POST | `/api/auth/register` | Anonymous | Register a user |
| POST | `/api/auth/login` | Anonymous | Returns a JWT |

### Events
| Method | Path | Access | Description |
|---|---|---|---|
| GET | `/api/events` | Anonymous | List (filtering + pagination) |
| GET | `/api/events/{id}` | Anonymous | Details |
| POST | `/api/events` | Admin | Create |
| PUT | `/api/events/{id}` | Admin | Update (K6, K9) |
| PATCH | `/api/events/{id}/cancel` | Admin | Cancel (K8) |
| GET | `/api/events/{id}/reservations` | Admin | The event's reservations |

List query parameters: `from`, `to`, `search`, `onlyAvailable`, `page` (default 1), `pageSize` (default 20, max 100)

Paged response format:
```json
{ "items": [], "page": 1, "pageSize": 20, "totalCount": 0 }
```

### Reservations
| Method | Path | Access | Description |
|---|---|---|---|
| POST | `/api/reservations` | User | Make a reservation (K1, K2, K3) |
| GET | `/api/reservations/me` | User | My reservations |
| PATCH | `/api/reservations/{id}/cancel` | User | Cancel (K4, K5, K7) |

### Status code contract
| Code | Meaning |
|---|---|
| 200 / 201 / 204 | Success |
| 400 | Validation error |
| 401 | Not authenticated |
| 403 | Not authorized (wrong role) |
| 404 | Resource not found (including resources owned by someone else) |
| 409 | Conflict (event full, reservation already exists, concurrency conflict, unique index violation) |
| 422 | Business rule violation |

All error responses use the **ProblemDetails** format.

---

## 6. Technology and Architecture

- **.NET 10**, controller-based Web API
- **EF Core** + **MSSQL** (local SQL Server 2022 Developer for now; Docker later)
- **FluentValidation**
- **JWT** authentication, role-based authorization; passwords hashed with `PasswordHasher<User>`
- Global exception handling (`IExceptionHandler` + ProblemDetails)
  - `DbUpdateConcurrencyException` → 409
  - Unique index violation (`SqlException` 2601 / 2627) → 409
- **xUnit** + **NSubstitute**; integration tests with `WebApplicationFactory` and a test database on local SQL Server
- OpenAPI + **Scalar** UI (with JWT support)

**Layers**
```
src/
  TicketReservationApi.Api             → Controllers, middleware, DI setup
  TicketReservationApi.Application     → Services, DTOs, validators, business rules
  TicketReservationApi.Infrastructure  → DbContext, entity configurations, migrations
  TicketReservationApi.Domain          → Entities, enums
tests/
  TicketReservationApi.UnitTests
  TicketReservationApi.IntegrationTests
```

---

## 7. Concurrency Experiment

**Scenario:** 10 simultaneous reservation requests for an event with a capacity of 1.
**Expected:** 1 × `201 Created`, 9 × `409 Conflict`; in the database `ReservedCount = 1` and exactly 1 active reservation.

**Steps**
1. Write unprotected service code; add a temporary `Task.Delay` between the check and the save.
2. Write an integration test that sends 10 parallel requests with `Task.WhenAll` (real MSSQL — do not use the InMemory provider).
3. Observe the problem: **overbooking** and **lost updates**.
4. Apply the fixes:
   - Optimistic concurrency with `Event.RowVersion` → `DbUpdateConcurrencyException` → 409
   - Alternative: atomic `WHERE ReservedCount < Capacity` update with `ExecuteUpdateAsync` + transaction
   - Guarantee K2 with a filtered unique index on `Reservation`
   - `ReservedCount <= Capacity` check constraint as the last line of defence
5. Run the same test again and verify the expected result.
6. **Additional scenario:** 10 simultaneous requests for an event with a capacity of 100. With optimistic concurrency, requests get 409 even though seats are available (false conflicts); with the atomic update, all succeed. Compare the two approaches.
7. Remove the `Task.Delay`.
8. Document the results in the "Concurrency" section of the README.

---

## 8. Development Order

- [x] **1. Setup** — Solution and layers, local MSSQL, DbContext, `Event` entity, first migration
- [x] **2. Event CRUD** — DTOs, FluentValidation, global error handling, OpenAPI + Scalar
- [x] **3. Reservations** — `User` (with seeded test users) and `Reservation` entities, relationships; create and cancel (no auth, `UserId` from the request body)
- [x] **4. Concurrency experiment** — Section 7; the integration test project is set up here
- [x] **5. Auth** — Register / login, JWT, roles, admin seed; `UserId` now comes from the token
- [x] **6. Business rules and queries** — Remaining rules (K3–K9, implemented alongside steps 2–3), filtering, pagination
- [ ] **7. Tests and documentation** — Unit tests (K1–K9), remaining integration tests, README

---

## 9. Decision Log

| Date | Decision | Rationale |
|---|---|---|
| 2026-11-02 | Capacity-based model | Keep the MVP small; seat selection later |
| 2026-11-02 | Reservations are cancelled, never deleted | Preserve history |
| 2026-11-02 | Concurrency experiment before auth | Tests don't need tokens for different users |
| 2026-11-02 | Local SQL Server 2022 instead of Docker | Docker not used for now; real MSSQL behaviour is kept |
| 2026-11-02 | `User` entity in step 3 (seeded) | The `Reservation.UserId` foreign key needs the table before auth exists |
| 2026-11-02 | K7 violations return 404 | Don't reveal that someone else's resource exists |
| 2026-11-02 | Cancelling an event cancels its active reservations (K8) | No inconsistent "active" reservations left behind |
| 2026-11-02 | Enums stored as strings | Filtered index `WHERE [Status] = 'Active'` and readability |
| 2026-11-02 | `ReservedCount <= Capacity` check constraint | Database-level guarantee for K1 / K6 |
| 2026-11-02 | Time abstracted with `TimeProvider` | Deterministic tests for time-dependent rules |
| 2026-11-02 | Admin is seeded; registration creates only `User` | Prevent privilege escalation |
| 2026-11-02 | NSubstitute, Scalar, `PasswordHasher<User>` | Minimal dependencies; full Identity is unnecessary |
| 2026-11-02 | Reservations use an atomic `UPDATE … WHERE ReservedCount < Capacity` + filtered unique index | Optimistic concurrency is correct but produces false conflicts even when seats are free (experiment result, see README) |
| 2026-11-02 | Reservation cancellation also uses a conditional `UPDATE … WHERE Status = 'Active'` | A seat is never released twice by concurrent cancellations |
| 2026-11-02 | `RowVersion` kept for admin operations | A stale admin update must not overwrite `ReservedCount` |
| 2026-11-02 | PLAN.md translated to English | Consistent with the README and code for the public repository |
| 2026-11-02 | Event list returns past and cancelled events unless `onlyAvailable=true` | One endpoint serves both browsing and booking; each item carries its `status` |
| 2026-11-02 | `from` / `to` are inclusive; out-of-range `page` / `pageSize` return 400 instead of being clamped | Predictable behaviour; clients learn about mistakes instead of getting silently different results |
