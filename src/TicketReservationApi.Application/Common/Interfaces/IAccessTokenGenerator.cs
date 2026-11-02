using TicketReservationApi.Application.Auth;
using TicketReservationApi.Domain.Entities;

namespace TicketReservationApi.Application.Common.Interfaces;

public interface IAccessTokenGenerator
{
    AccessToken Generate(User user);
}
