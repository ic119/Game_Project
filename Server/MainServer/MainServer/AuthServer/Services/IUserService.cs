using MainServer.AuthServer.DTOs;
using MainServer.AuthServer.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MainServer.AuthServer.Services
{
    public interface IUserService
    {
        Task<UserResponse> RegisterAsync(RegisterRequest request);
        Task<User?> GetByIdAsync(long id);
    }
}
