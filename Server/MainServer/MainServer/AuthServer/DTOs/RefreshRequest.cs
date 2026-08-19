using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MainServer.AuthServer.DTOs
{
    public record RefreshRequest(string _refreshToken);
}