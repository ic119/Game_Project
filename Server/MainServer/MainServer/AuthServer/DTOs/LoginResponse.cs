using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MainServer.AuthServer.DTOs
{
    public record LoginResponse(string _accessToken, string _refreshToken, UserResponse _user);
}