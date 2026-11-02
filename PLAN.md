# Bilet Rezervasyon API — Proje Planı

Admin'in etkinlik oluşturduğu, kullanıcıların kontenjanlı etkinliklere rezervasyon yapıp iptal edebildiği bir REST API.

Bu proje; ASP.NET Core Web API, EF Core, kimlik doğrulama, iş kuralları ve **eşzamanlılık (concurrency)** yönetimi konularını uçtan uca uygulamak amacıyla geliştirilmektedir.

---

## 1. Kapsam

### MVP
- Kayıt / giriş (JWT)
- Roller: `Admin`, `User`
- Etkinlik CRUD (Admin)
- Rezervasyon oluşturma ve iptal (User)
- Etkinlik listesinde filtreleme ve sayfalama
- Eşzamanlı rezervasyon isteklerine karşı koruma

### Sonraya bırakılanlar
- [ ] Koltuk numaralı rezervasyon
- [ ] Bekleme listesi (kontenjan açılınca sıradakine bildirim)
- [ ] E-posta bildirimi
- [ ] Ödeme simülasyonu
- [ ] Caching (etkinlik listesi)
- [ ] Rate limiting
- [ ] Refresh token
- [ ] Docker ile tam deploy + CI (GitHub Actions) — MSSQL ve Testcontainers dahil

> **Model kararı:** MVP'de etkinlikler **kontenjan bazlıdır** (ör. 100 kişilik konser). Koltuk seçimi kapsamı büyüttüğü için sonraya bırakıldı.

---

## 2. Roller

| Rol | Yetkiler |
|---|---|
| **Admin** | Etkinlik oluşturma, güncelleme, iptal etme; bir etkinliğin rezervasyon listesini görme |
| **User** | Etkinlikleri listeleme; rezervasyon yapma; kendi rezervasyonlarını görme ve iptal etme |

- Register her zaman `User` rolünde hesap açar.
- Admin hesabı uygulama başlangıcında **seed** ile oluşturulur (bilgiler konfigürasyondan).

---

## 3. İş Kuralları

| # | Kural | Hata |
|---|---|---|
| K1 | Kontenjan doluysa rezervasyon yapılamaz. | 409 |
| K2 | Bir kullanıcı aynı etkinliğe birden fazla **aktif** rezervasyon yapamaz. | 409 |
| K3 | Geçmiş veya iptal edilmiş etkinliğe rezervasyon yapılamaz. | 422 |
| K4 | Etkinliğe 24 saatten az kaldıysa rezervasyon iptal edilemez. | 422 |
| K5 | Rezervasyon iptal edildiğinde kontenjan geri açılır. | — |
| K6 | Admin, kapasiteyi mevcut rezervasyon sayısının altına düşüremez. | 422 |
| K7 | Kullanıcı yalnızca kendi rezervasyonlarını görebilir ve iptal edebilir. Başkasının rezervasyonu "yok" kabul edilir. | 404 |
| K8 | Admin etkinliği iptal ettiğinde o etkinliğin tüm aktif rezervasyonları `Cancelled` olur. | — |
| K9 | İptal edilmiş veya başlamış etkinlik güncellenemez; `StartsAt` geçmişe çekilemez. | 422 / 400 |

Her kural için servis katmanında en az bir unit test yazılacaktır. Zamana bağlı kurallar (K3, K4, K9) `TimeProvider` üzerinden test edilir.

---

## 4. Veri Modeli

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

**İlişkiler**
- User 1 — N Reservation
- Event 1 — N Reservation

**Kısıtlar**
- `User.Email` unique
- `Reservation (EventId, UserId)` → **filtered unique index** (`WHERE [Status] = 'Active'`) — K2'nin veritabanı seviyesindeki garantisi
- `Event.Capacity > 0`, `Event.ReservedCount >= 0`, `Event.ReservedCount <= Event.Capacity` (check constraint) — K1 ve K6'nın veritabanı seviyesindeki garantisi

**Notlar**
- Enum'lar veritabanında **string** olarak saklanır (filtered index ve okunabilirlik için).
- Tüm zamanlar **UTC** (`DateTimeOffset`) tutulur.
- Rezervasyonlar silinmez, `Status = Cancelled` yapılır (soft cancel). Geçmiş kayıtlar korunur.

---

## 5. Endpoint'ler

### Auth
| Metot | Yol | Yetki | Açıklama |
|---|---|---|---|
| POST | `/api/auth/register` | Anonim | Kullanıcı kaydı |
| POST | `/api/auth/login` | Anonim | JWT döner |

### Events
| Metot | Yol | Yetki | Açıklama |
|---|---|---|---|
| GET | `/api/events` | Anonim | Liste (filtre + sayfalama) |
| GET | `/api/events/{id}` | Anonim | Detay |
| POST | `/api/events` | Admin | Oluştur |
| PUT | `/api/events/{id}` | Admin | Güncelle (K6, K9) |
| PATCH | `/api/events/{id}/cancel` | Admin | İptal et (K8) |
| GET | `/api/events/{id}/reservations` | Admin | Etkinliğin rezervasyonları |

Liste sorgu parametreleri: `from`, `to`, `search`, `onlyAvailable`, `page` (varsayılan 1), `pageSize` (varsayılan 20, en fazla 100)

Sayfalı yanıt formatı:
```json
{ "items": [], "page": 1, "pageSize": 20, "totalCount": 0 }
```

### Reservations
| Metot | Yol | Yetki | Açıklama |
|---|---|---|---|
| POST | `/api/reservations` | User | Rezervasyon yap (K1, K2, K3) |
| GET | `/api/reservations/me` | User | Kendi rezervasyonlarım |
| PATCH | `/api/reservations/{id}/cancel` | User | İptal et (K4, K5, K7) |

### Status code sözleşmesi
| Kod | Anlam |
|---|---|
| 200 / 201 / 204 | Başarılı |
| 400 | Validasyon hatası |
| 401 | Kimlik doğrulanmadı |
| 403 | Yetki yok (rol uygun değil) |
| 404 | Kaynak bulunamadı (başkasına ait kaynaklar dahil) |
| 409 | Çakışma (kontenjan dolu, zaten rezervasyon var, concurrency hatası, unique index ihlali) |
| 422 | İş kuralı ihlali |

Tüm hata yanıtları **ProblemDetails** formatında döner.

---

## 6. Teknoloji ve Mimari

- **.NET 10**, Controller-based Web API
- **EF Core** + **MSSQL** (şimdilik yerel SQL Server 2022 Developer; Docker sonraya)
- **FluentValidation**
- **JWT** authentication, role-based authorization; şifreler `PasswordHasher<User>` ile hash'lenir
- Global exception handling (`IExceptionHandler` + ProblemDetails)
  - `DbUpdateConcurrencyException` → 409
  - Unique index ihlali (`SqlException` 2601 / 2627) → 409
- **xUnit** + **NSubstitute**; integration testler `WebApplicationFactory` + yerel SQL Server'da test veritabanı
- OpenAPI + **Scalar** UI (JWT desteğiyle)

**Katmanlar**
```
src/
  TicketReservationApi.Api             → Controllers, middleware, DI kurulumu
  TicketReservationApi.Application     → Servisler, DTO'lar, validator'lar, iş kuralları
  TicketReservationApi.Infrastructure  → DbContext, entity konfigürasyonları, migration'lar
  TicketReservationApi.Domain          → Entity'ler, enum'lar
tests/
  TicketReservationApi.UnitTests
  TicketReservationApi.IntegrationTests
```

---

## 7. Eşzamanlılık (Concurrency) Deneyi

**Senaryo:** Kontenjanı 1 olan etkinliğe aynı anda 10 rezervasyon isteği.
**Beklenen:** 1 × `201 Created`, 9 × `409 Conflict`; veritabanında `ReservedCount = 1` ve 1 aktif rezervasyon.

**Adımlar**
1. Korumasız servis kodu yaz; kontrol ile kayıt arasına geçici `Task.Delay` ekle.
2. `Task.WhenAll` ile 10 paralel istek gönderen integration test yaz (gerçek MSSQL — InMemory provider kullanma).
3. Problemi gözlemle: **overbooking** ve **lost update**.
4. Çözümleri uygula:
   - `Event.RowVersion` ile optimistic concurrency → `DbUpdateConcurrencyException` → 409
   - Alternatif: `ExecuteUpdateAsync` ile atomik `WHERE ReservedCount < Capacity` güncellemesi + transaction
   - `Reservation` filtered unique index ile K2 garantisi
   - `ReservedCount <= Capacity` check constraint'i son savunma hattı olarak
5. Aynı testi tekrar çalıştır, beklenen sonucu doğrula.
6. **Ek senaryo:** Kontenjanı 100 olan etkinliğe 10 eşzamanlı istek. Optimistic concurrency'de yer olmasına rağmen istekler 409 alır (false conflict); atomik güncellemede hepsi başarılı olur. İki yaklaşımı karşılaştır.
7. `Task.Delay`'i kaldır.
8. Sonuçları README'de "Concurrency" bölümünde belgele.

---

## 8. Geliştirme Sırası

- [x] **1. Kurulum** — Solution ve katmanlar, yerel MSSQL, DbContext, `Event` entity'si, ilk migration
- [x] **2. Event CRUD** — DTO'lar, FluentValidation, global hata yönetimi, OpenAPI + Scalar
- [x] **3. Rezervasyon** — `User` (seed'li test kullanıcıları) ve `Reservation` entity'leri, ilişkiler; oluşturma ve iptal (auth yok, `UserId` body'den)
- [x] **4. Concurrency deneyi** — Bölüm 7; integration test projesi burada kurulur
- [ ] **5. Auth** — Register / login, JWT, roller, admin seed; `UserId` artık token'dan
- [ ] **6. İş kuralları ve sorgular** — Kalan kurallar (K3–K9), filtreleme, sayfalama
- [ ] **7. Test ve dokümantasyon** — Unit testler (K1–K9), kalan integration testler, README

---

## 9. Kararlar Günlüğü

| Tarih | Karar | Gerekçe |
|---|---|---|
| 2026-11-02 | Kontenjan bazlı model | MVP'yi küçük tutmak; koltuk seçimi sonraya |
| 2026-11-02 | Rezervasyonlar silinmez, iptal edilir | Geçmişin korunması |
| 2026-11-02 | Concurrency deneyi auth'tan önce | Testte farklı kullanıcı token'ları gerektirmemesi |
| 2026-11-02 | Docker yerine yerel SQL Server 2022 | Docker şimdilik kullanılmıyor; gerçek MSSQL davranışı korunuyor |
| 2026-11-02 | `User` entity'si 3. adımda (seed'li) | `Reservation.UserId` FK'si için tablo auth'tan önce gerekli |
| 2026-11-02 | K7 ihlali 404 döner | Başkasının kaynağının varlığını sızdırmamak |
| 2026-11-02 | Etkinlik iptali aktif rezervasyonları iptal eder (K8) | Tutarsız "aktif" rezervasyon kalmaması |
| 2026-11-02 | Enum'lar string saklanır | Filtered index `WHERE [Status] = 'Active'` ve okunabilirlik |
| 2026-11-02 | `ReservedCount <= Capacity` check constraint | K1 / K6 için veritabanı garantisi |
| 2026-11-02 | `TimeProvider` ile zaman soyutlaması | Zamana bağlı kuralların deterministik testi |
| 2026-11-02 | Admin seed ile oluşturulur, register yalnızca `User` | Yetki yükseltmeyi engellemek |
| 2026-11-02 | NSubstitute, Scalar, `PasswordHasher<User>` | Sade bağımlılıklar; tam Identity gereksiz |
| 2026-11-02 | Rezervasyonda atomik `UPDATE … WHERE ReservedCount < Capacity` + filtered unique index | Optimistic concurrency doğru ama boş yer varken de false conflict üretiyor (deney sonucu, README) |
| 2026-11-02 | Rezervasyon iptali de koşullu `UPDATE … WHERE Status = 'Active'` | Eşzamanlı iptallerde koltuğun iki kez serbest bırakılmaması |
| 2026-11-02 | `RowVersion` admin işlemleri için korunur | Eski veriyle yapılan admin güncellemesi `ReservedCount`'u ezmesin |
